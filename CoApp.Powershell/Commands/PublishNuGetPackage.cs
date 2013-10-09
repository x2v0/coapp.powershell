//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2010-2012 Garrett Serack and CoApp Contributors. 
//     Contributors can be discovered using the 'git log' command.
//     All rights reserved.
// </copyright>
// <license>
//     The software is licensed under the Apache 2.0 License (the "License")
//     You may not use the software except in compliance with the License. 
// </license>
//-----------------------------------------------------------------------

namespace CoApp.Powershell.Commands {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Packaging;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using ClrPlus.Core.Exceptions;
    using ClrPlus.Core.Extensions;
    using ClrPlus.Core.Tasks;
    using ClrPlus.Core.Utility;
    using ClrPlus.Platform;
    using ClrPlus.Platform.Process;
    using ClrPlus.Powershell.Core;
    using ClrPlus.Powershell.Rest.Commands;
    using Microsoft.Build.Tasks;
    using Microsoft.SqlServer.Server;
    using Error = ClrPlus.Core.Tasks.Error;
    using Warning = ClrPlus.Core.Tasks.Warning;
    using Message = ClrPlus.Core.Tasks.Message;

    internal static class CmdletUtility {
        internal static string EtcPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "etc");

        static CmdletUtility() {
            try {
                var asmDir = Path.GetDirectoryName(typeof (WriteNuGetPackage).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir)) {
                    var path = Environment.GetEnvironmentVariable("path");

                    if (string.IsNullOrEmpty(path) || !path.Contains(asmDir)) {
                        Environment.SetEnvironmentVariable("path", path + ";" + asmDir + ";" + asmDir + "\\etc");
                    }
                }
            } catch (Exception e) {
                Console.WriteLine("EXC: {0}/{1}", e.Message, e.StackTrace);
            }

            AssemblyResolver.Initialize();
        }
    }
    
    [Cmdlet(AllVerbs.Publish, "NuGetPackage")]
    public class PublishNuGetPackage : RestableCmdlet<PublishNuGetPackage> {

        static PublishNuGetPackage() {
            // ensure that the etc folder is added to the path.
            var x = CmdletUtility.EtcPath;
        }

        private Executable _nugetExe;

        private static Regex rx = new Regex(@"(?<name>.*?)(?<variant>\.overlay-.*?)?(?<version>[\.\d]+).nupkg");

        [Parameter(HelpMessage = "Package files to publish",Mandatory = true, Position = 0)]
        public string[] Packages { get; set;}

        [Parameter(HelpMessage = "The API key for the server.", Mandatory = false, Position = 1)]
        public string ApiKey { get; set; }

        [Parameter(HelpMessage = @"The NuGet configuration file. If not specified, file %AppData%\NuGet\NuGet.config is used as configuration file.")]
        public string ConfigFile { get; set; }

        [Parameter(HelpMessage = "Specifies the timeout for pushing to a server in seconds. Defaults to 300 seconds (5 minutes).")]
        public int? Timeout { get; set; }

        [Parameter(HelpMessage = "Specifies the server URL. If not specified, nuget.org is used unless DefaultPushSource config value is set in the NuGet config file.")]
        public string Source { get; set; }

        [Parameter(HelpMessage = "Display this amount of details in the output: normal, quiet, detailed.")]
        public string Verbosity { get; set; }

        [Parameter(HelpMessage = "Doesn't automatically upload .overlay- .nupkg files")]
        public SwitchParameter IgnoreOverlay { get; set; }

        [Parameter(HelpMessage = "Doesn't automatically unlist .overlay- .nupkg files")]
        public SwitchParameter DontUnlistOverlay { get; set; }

        [Parameter(HelpMessage = "Don't parallelize the uploads")]
        public SwitchParameter Slow {get; set;}

        internal class PackageIdentity {
            internal PackageIdentity(string path) {
                FullPath = path.GetFullPath();
                Folder = Path.GetDirectoryName(FullPath);
                Filename = Path.GetFileName(FullPath);
                var peices = rx.Match(Filename);
                ValidName = peices.Success;
                if (ValidName) {
                    BaseName = peices.GetValue("name");
                    Variant = peices.GetValue("variant");
                    Version = peices.GetValue("version");
                }
                Name = IsOverlay ? "{0}{1}".format(BaseName, Variant) : BaseName;
            }

            public string FullPath {get; set;}
            public string Folder {get; set;}
            public string Filename { get; set; }
            public string Name {get; set;}
            public string BaseName {get; set;}
            public string Version { get; set; }
            public string Variant { get; set; }
            public bool ValidName {get; set;}
            public bool IsOverlay { get {
                return Variant.Is();
            }}

            public IEnumerable<PackageIdentity> OverlayPackages {
                get {
                    return Directory.EnumerateFiles(Folder, "{0}.overlay-*{1}.nupkg".format(BaseName, Version), SearchOption.TopDirectoryOnly).Select(overlayFile => new PackageIdentity(overlayFile));
                }
            }
        }

        protected override void ProcessRecord() {
            if (Remote) {
                ProcessRecordViaRest();
                return;
            }

            System.Environment.CurrentDirectory = (SessionState.PSVariable.GetValue("pwd") ?? "").ToString();
            _nugetExe = new Executable("nuget.exe");

            using (var local = LocalEventSource) {
                var pkgPaths = new List<string>();
                foreach (var pkg in Packages) {
                    ProviderInfo packagePathProviderInfo;
                    pkgPaths.AddRange(SessionState.Path.GetResolvedProviderPathFromPSPath(pkg, out packagePathProviderInfo));
                }

                if (pkgPaths.Count == 0) {
                    if (Event<Error>.Raise("PublishNuGetPackage", "No packages found.")) {
                        throw new ClrPlusException("Fatal Error.");
                    }
                }

                var tasks = new Dictionary<Task<IEnumerable<string>>,PackageIdentity>();

                foreach (var file in pkgPaths) {
                    var extension = Path.GetExtension(file) ?? "";
                    if (extension.ToLower() != ".nupkg") {
                        Event<Warning>.Raise("PublishNuGetPackage", "Skipping unknown file ('{0}') with unrecognized extension ('{1}')", file, extension);
                        continue;
                    }
                    var pid = new PackageIdentity(file);

                    if (!pid.ValidName) {
                        Event<Error>.Raise("PublishNuGetPackage", "Unable to parse nuget package name '{0}'.", pid.Filename);
                        continue;
                    }

                    if (!pid.IsOverlay && !IgnoreOverlay) {
                        // check for overlay packages for this package 
                        foreach (var overlay in pid.OverlayPackages) {
                            var overlayPkg = overlay;
                            Event<Verbose>.Raise("PublishNuGetPackage", "Pushing overlay file {0}.", overlay.Name );
                            tasks.Add(NuPush(overlayPkg), overlayPkg);
                        }
                    }

                    Event<Verbose>.Raise("PublishNuGetPackage", "Pushing package file {0}.", file);
                    tasks.Add(NuPush(pid), pid);
                }

                float total = tasks.Count;
                float n = 1;

                WriteProgress(new ProgressRecord(1, "Uploading {0} packages to server ".format(total), "Starting") { PercentComplete = 0 });


                while (tasks.Any()) {
                    var tsks = tasks.Keys.ToArray();
                    Task.WaitAny(tsks);
                    foreach (var t in tsks) {
                        if (t.IsCompleted) {
                            var pid = tasks[t];
                            foreach (var r in t.Result.ToArray()) {
                                Host.UI.WriteLine(" > " + r);
                            }
                            tasks.Remove(t);
                            
                            WriteProgress(new ProgressRecord(1, "Uploading {0} packages to server ".format(total), "Completed package '{0}' [{1}/{2}]".format( pid.Name,n,total )) { PercentComplete =(int) ((n/total) * 100) });
                            n++;
                        }
                    }
                }

                 WriteProgress(new ProgressRecord(1, "Uploaded {0} packages to server ".format(total), "Complete") { PercentComplete = 100 });
            }
        }

        private static Mutex mut = new Mutex();

        internal Task<IEnumerable<string>>  NuPush(PackageIdentity package) {
            return Task<IEnumerable<string>>.Factory.StartNew(() => {
                if (Slow) {
                    mut.WaitOne();
                }
                var results = NuPushImpl(package.FullPath);
                if (!DontUnlistOverlay && package.IsOverlay) {
                    results = results.Concat(NuUnlistImpl(package.Name, package.Version.Trim('.')));
                }
                results = results.ToArray();
                if (Slow) {
                    mut.ReleaseMutex();
                }
                return results;
            }, TaskCreationOptions.LongRunning);
           
        }

        public IEnumerable<string> NuPushImpl(string path) {
            var cmdline = "";
            if (!string.IsNullOrEmpty(Source)) {
                cmdline = cmdline + @" -Source ""{0}""".format(Source);
            }

            if (!string.IsNullOrEmpty(ApiKey)) {
                cmdline = cmdline + @" -ApiKey ""{0}""".format(ApiKey);
            }

            if (Timeout != null && Timeout != 0) {
                cmdline = cmdline + @" -Timeout {0}".format(Timeout);
            }

            if (!string.IsNullOrEmpty(ConfigFile)) {
                cmdline = cmdline + @" -ConfigFile ""{0}""".format(ConfigFile);
            }

            if (!string.IsNullOrEmpty(Verbosity)) {
                cmdline = cmdline + @" -Verbosity {0}".format(ConfigFile);
            }

            cmdline = @"push ""{0}"" ".format(path) + cmdline;

            var process = _nugetExe.Exec( cmdline );

            foreach (var txt in process.StandardOutput) {
                if (!string.IsNullOrEmpty(txt)) {
                    yield return txt;
                }
            }

            if (process.ExitCode != 0) {
                foreach (var txt in process.StandardError) {
                    if (!string.IsNullOrEmpty(txt)) {
                        yield return txt;
                    }
                }
            }
        }

        public IEnumerable<string> NuUnlistImpl(string name, string version) {
            var cmdline = "";
            if (!string.IsNullOrEmpty(Source)) {
                cmdline = cmdline + @" -Source ""{0}""".format(Source);
            }

            if (!string.IsNullOrEmpty(ApiKey)) {
                cmdline = cmdline + @" -ApiKey ""{0}""".format(ApiKey);
            }

            if (!string.IsNullOrEmpty(ConfigFile)) {
                cmdline = cmdline + @" -ConfigFile ""{0}""".format(ConfigFile);
            }

            if (!string.IsNullOrEmpty(Verbosity)) {
                cmdline = cmdline + @" -Verbosity {0}".format(ConfigFile);
            }

            cmdline = @"delete ""{0}"" {1} -NonInteractive ".format(name,version) + cmdline;

            var process = _nugetExe.Exec(cmdline);

            foreach (var txt in process.StandardOutput) {
                if (!string.IsNullOrEmpty(txt)) {
                    yield return txt;
                }
            }

            if (process.ExitCode != 0) {
                foreach (var txt in process.StandardError) {
                    if (!string.IsNullOrEmpty(txt)) {
                        yield return txt;
                    }
                }
            }
        }
    }
}