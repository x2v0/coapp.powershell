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
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Packaging;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using ClrPlus.Core.Exceptions;
    using ClrPlus.Core.Extensions;
    using ClrPlus.Core.Tasks;
    using ClrPlus.Powershell.Core;
    using ClrPlus.Powershell.Rest.Commands;

    [Cmdlet(AllVerbs.Publish, "NuGetPackage")]
    public class PublishNuGetPackage : RestableCmdlet<PublishNuGetPackage> {

        [Parameter(HelpMessage = "Package files to publish",Mandatory = true, Position = 0)]
        public string[] Packages { get; set;}

        [Parameter(HelpMessage = "The API key for the server.")]
        public string ApiKey { get; set; }

        [Parameter(HelpMessage = @"The NuGet configuration file. If not specified, file %AppData%\NuGet\NuGet.config is used as configuration file.")]
        public string ConfigFile { get; set; }

        [Parameter(HelpMessage = "Specifies the timeout for pushing to a server in seconds. Defaults to 300 seconds (5 minutes).")]
        public int? Timeout { get; set; }

        [Parameter(HelpMessage = "Specifies the server URL. If not specified, https://nuget.gw.symbolsource.org/Public/NuGet is used.")]
        public string SymbolRepository { get; set; }

        [Parameter(HelpMessage = "Specifies the symbol server URL. If not specified, nuget.org is used unless DefaultPushSource config value is set in the NuGet config file")]
        public string Repository { get; set; }

        [Parameter(HelpMessage = "Doesn't automatically upload .redist .nupkg files")]
        public SwitchParameter IgnoreRedist { get; set; }

        [Parameter(HelpMessage = "Doesn't automatically upload .symbols .nupkg files to symbolsource.org")]
        public SwitchParameter IgnoreSymbols { get; set; }

        protected override void ProcessRecord() {
            if (Remote) {
                ProcessRecordViaRest();
                return;
            }
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

                foreach (var file in pkgPaths) {
                    var extension = Path.GetExtension(file) ?? "";
                    if (extension.ToLower() != ".nupkg") {
                        Event<Warning>.Raise("PublishNuGetPackage", "Skipping unknown file ('{0}') with unrecognized extension ('{1}')", file, extension);
                        continue;
                    }

                    var folder = Path.GetDirectoryName(file);
                    var name = Path.GetFileName(file);
                    var justname = Path.GetFileNameWithoutExtension(file);
                    string redist = null;
                    string symbols = null;


                    var bits = name.Split('.');
                    for (int i = 1; i < bits.Length; i++) {
                        if (!IgnoreRedist) {
                            var fname = string.Format("{0}.redist.{1}", bits.Take(i).Aggregate((c, e) => c + "." + e), i < (bits.Length) ? bits.Skip(i).Aggregate((c, e) => c + "." + e) : "");
                            var fullPath = Path.Combine(folder, fname);
                            if (File.Exists(fullPath)) {
                                redist = fullPath;
                            }
                        }

                        if (!IgnoreRedist) {
                            var fname = string.Format("{0}.symbols.{1}", bits.Take(i).Aggregate((c, e) => c + "." + e), i < (bits.Length) ? bits.Skip(i).Aggregate((c, e) => c + "." + e) : "");
                            var fullPath = Path.Combine(folder, fname);
                            if (File.Exists(fullPath)) {
                                symbols = fullPath;
                            }
                        }
                    }

                    if (!IgnoreRedist && redist == null) {
                        Event<Warning>.Raise("PublishNuGetPackage", "No redist package found for ('{0}').", file);
                    }

                    if(!IgnoreSymbols&& symbols == null) {
                        Event<Warning>.Raise("PublishNuGetPackage", "No symbols package found for ('{0}').", file);
                    }

                    Event<Verbose>.Raise("PublishNuGetPackage", "Pushing package file {0}.", file);
                    NuPush(file, Repository);

                    if (!IgnoreRedist && redist != null) {
                        Event<Verbose>.Raise("PublishNuGetPackage", "Pushing redist file {0}.", redist);
                        NuPush(redist, Repository);
                    }

                    if (!IgnoreSymbols && symbols != null ) {
                        Event<Verbose>.Raise("PublishNuGetPackage", "Pushing symbols file {0}.", symbols);
                        NuPush(symbols, SymbolRepository.Is() ? SymbolRepository : "https://nuget.gw.symbolsource.org/Public/NuGet");
                    }
                }
            }
        }

        public void NuPush(string path, string repository) {
            using (dynamic ps = Runspace.DefaultRunspace.Dynamic()) {
                var cmdline = "";
                if (!string.IsNullOrEmpty(repository)) {
                    cmdline = cmdline + @" -Source '{0}'".format(repository);
                }

                if (!string.IsNullOrEmpty(ApiKey)) {
                    cmdline = cmdline + @" -ApiKey '{0}'".format(ApiKey);
                }

                if (Timeout != null && Timeout != 0) {
                    cmdline = cmdline + @" -Timeout '{0}'".format(Timeout);
                }

                if (!string.IsNullOrEmpty(ConfigFile)) {
                    cmdline = cmdline + @" -ConfigFile '{0}'".format(ConfigFile);
                }

                var results = ps.InvokeExpression(@"nuget.exe push '{0}' {1} 2>&1".format(path, cmdline));
                bool lastIsBlank = false;
                foreach (var r in results) {
                    string s = r.ToString();
                    if (string.IsNullOrWhiteSpace(s)) {
                        if (lastIsBlank) {
                            continue;
                        }
                        lastIsBlank = true;
                    } else {
                        /*
                        if (s.IndexOf("Issue: Assembly outside lib folder") > -1) {
                            continue;
                        }
                        if(s.IndexOf("folder and hence it won't be added as reference when the package is installed into a project") > -1) {
                            continue;
                        }
                        if (s.IndexOf("Solution: Move it into the 'lib' folder if it should be referenced") > -1) {
                            continue;
                        }
                        if(s.IndexOf("issue(s) found with package") > -1) {
                            continue;
                        }
                        */
                        lastIsBlank = false;
                    }

                    Event<Message>.Raise(" >", "{0}", s);
                }
            }
        }
    }
}