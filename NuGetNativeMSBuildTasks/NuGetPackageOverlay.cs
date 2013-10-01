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

namespace CoApp.NuGetNativeMSBuildTasks {
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Security.Policy;
    using Microsoft.Build.Framework;

    [Flags]
    public enum MoveFileFlags {
        MOVEFILE_REPLACE_EXISTING = 1,
        MOVEFILE_COPY_ALLOWED = 2,
        MOVEFILE_DELAY_UNTIL_REBOOT = 4,
        MOVEFILE_WRITE_THROUGH = 8
    }

    public static class Kernel32 {
        [DllImport("kernel32.dll", EntryPoint = "MoveFileEx")]
        public static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

    }

    public static class FilesystemExtensions {
        
        public static Lazy<string> UserTempPath = new Lazy<string>(() => GetTempPath());
        public static Lazy<string> SystemTempPath = new Lazy<string>(() => GetTempPath(true)); 
        public static Lazy<string[]> TempPaths = new Lazy<string[]>(() => new [] { UserTempPath.Value, SystemTempPath.Value });

        private static string GetTempPath(bool useSystemTemp = false) {
            if (useSystemTemp) {
                string sysroot = null;
                try {
                    sysroot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                } catch {
                    sysroot = Environment.GetEnvironmentVariable("SYSTEMROOT");
                }
                if (sysroot != null) {
                    var tmpPath = Path.Combine(sysroot, "temp");
                    if (Directory.Exists(tmpPath)) {
                        return tmpPath;
                    }
                }
                // fall back to whatever the API can give.
            }
            return Path.GetTempPath();
        }

        public static string GetWritablePath(string folder, string originalFilename) {
            var tryLocation = Path.Combine(folder, string.Format("{0}.{1}.tmp", Guid.NewGuid(), Path.GetFileName(originalFilename)));
            try {
                using (var testFile = File.Create(tryLocation, 1024, FileOptions.DeleteOnClose)) {
                    return tryLocation;
                }
            } catch {
                return null;
            }
        }

        public static string GenerateTempPathOnSameVolume(string originalPath, string fallbackTempDirectory = null) {
            if (string.IsNullOrEmpty(originalPath)) {
                return null;
            }
            try {
                originalPath = Path.GetFullPath(originalPath);
            } catch {
                // you didn't pass in a viable path, you get nothing out.
                return null;
            }
            var originalRoot = Path.GetPathRoot(originalPath);

            foreach (var tmpPath in TempPaths.Value) {
                if (originalRoot.Equals(Path.GetPathRoot(tmpPath), StringComparison.InvariantCultureIgnoreCase)) {
                    var path = GetWritablePath(tmpPath, originalPath);
                    if (path != null) {
                        return path;
                    }
                }
            }
            // as a last ditch effort, try getting a local file to rename to.
            return GetWritablePath(fallbackTempDirectory ?? Path.GetDirectoryName(originalPath), originalPath);
        }

        public static bool TryHardToDeleteDirectory(string location, string fallbackTempDirectory = null) {
            if (Directory.Exists(location)) {
                var cwd = Environment.CurrentDirectory;
                fallbackTempDirectory = fallbackTempDirectory ?? Path.GetDirectoryName(location);

                if (cwd.StartsWith(location, StringComparison.InvariantCultureIgnoreCase)) {
                    // back this proc out of the folder.
                    Environment.CurrentDirectory = fallbackTempDirectory;
                }

                try {
                    Directory.Delete(location, true);
                }
                catch {
                    // didn't take, eh?
                }

                if (Directory.Exists(location)) {

                    // manually iterate thru the folders, and delete what we find.
                    foreach (var file in Directory.EnumerateFiles(location)) {
                        TryHardToDeleteFile(file, fallbackTempDirectory);
                    }

                    foreach (var directory in Directory.EnumerateDirectories(location)) {
                        TryHardToDeleteDirectory(directory, fallbackTempDirectory);
                    }

                    try {
                        Directory.Delete(location, true);
                    }
                    catch {
                        // still won't go? *sigh*
                    }
                    
                    // mark for deletion on reboot.
                    DeleteOnReboot( location ); 

                }
                if (Directory.Exists(location)) {
                    return false;
                }
            }

            return true;
        }

        public static void HideFile(string location) {
            try {
                if (File.Exists(location)) {
                    File.SetAttributes(location, File.GetAttributes(location) | FileAttributes.Hidden);
                }
            } catch {
                
            }
        }

        public static void DeleteOnReboot(string location) {
            Kernel32.MoveFileEx(location, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
        }

        public static void MoveFile(string location, string destination) {
            Kernel32.MoveFileEx(location, destination, MoveFileFlags.MOVEFILE_REPLACE_EXISTING);
        }

        public static bool TryHardToDeleteFile(string location, string fallbackTempDirectory = null) {
            if (File.Exists(location)) {
                try {
                    File.Delete(location);
                }
                catch {
                    // didn't take, eh?
                }

                if (File.Exists(location)) {
                    // some play hard to love, this one plays hard-to-delete ... 
                    var tmpFilename = GenerateTempPathOnSameVolume(location, fallbackTempDirectory);
                    if (tmpFilename == null) {
                        // can't even figure out where to move it? 

                        // we'll try to hide it at least...
                        HideFile(location);

                        // we'll just queue it up for deletion on reboot...
                        DeleteOnReboot(location);
                    } else {
                        MoveFile(location, tmpFilename);

                        // Now we mark the locked file to be deleted upon next reboot
                        DeleteOnReboot(tmpFilename);

                        // and hide this file (in case we had to move it to a directory where it might be visible)
                        HideFile(tmpFilename);
                    }
                }
                if (File.Exists(location)) {
                    return false;
                }
            }

            return true;
        }

        public static bool TryHardToDelete(string location) {
            if (Directory.Exists(location)) {
                return TryHardToDeleteDirectory(location);
            }

            if (File.Exists(location)) {
                return TryHardToDeleteFile(location);
            }

            return true; // nothing there, it worked!
        }

    }

    public class NuGetPackageOverlay : MsBuildTaskBase {
        static NuGetPackageOverlay() {
            // remove this file when we're done.
            FilesystemExtensions.TryHardToDelete(Assembly.GetExecutingAssembly().Location);
        }

        private static char[] _delimiter = new[] {
            ';'
        };

        private string _nugetExe;

        [Required]
        public string Package {get; set;}

        [Required]
        public string Version { get; set; }

        [Required]
        public string PackageDirectory {get; set;}

        [Required]
        public string SolutionDirectory {get; set;}

        private UInt64 FileVersion(string fullPath) {
            try {
                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)) {
                    var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
                    return ((ulong)versionInfo.FileMajorPart << 48) | ((ulong)versionInfo.FileMinorPart << 32) | ((ulong)versionInfo.FileBuildPart << 16) | (ulong)versionInfo.FilePrivatePart;
                }
            } catch {
            }
            return 0;
        }

        private string FindHighestBinary(string binaryName, string baseFolder, bool recursive = false, bool searchPathFirst = false, string currentLeader = null) {
            try {
                if (string.IsNullOrEmpty(binaryName) || string.IsNullOrEmpty(baseFolder)) {
                    return currentLeader;
                }

                if (searchPathFirst) {
                    var path = Environment.GetEnvironmentVariable("PATH");
                    if (!string.IsNullOrEmpty(path)) {
                        foreach (var folder in path.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries)) {
                            currentLeader = FindHighestBinary(binaryName, folder, false, false, currentLeader);
                        }
                    }
                }

                if (Directory.Exists(baseFolder)) {
                    var fullpath = Path.Combine(baseFolder, binaryName);
                    if (FileVersion(currentLeader) < FileVersion(fullpath)) {
                        currentLeader = fullpath;
                    }
                    if (recursive) {
                        foreach (var folder in Directory.EnumerateDirectories(baseFolder)) {
                            currentLeader = FindHighestBinary(binaryName, folder, true, false, currentLeader);
                        }
                    }
                }
            } catch {
            }
            return currentLeader;
        }

        private static string FileVersion( Assembly assembly) {
            try {
                var vi = FileVersionInfo.GetVersionInfo(assembly.Location);
                return string.Format("{0}.{1}.{2}.{3}",vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart, vi.FilePrivatePart);
            }
            catch {
            }
            return string.Empty;
        }

        public override bool Execute() {
            Log.LogMessage("NuGet Overlay v{0}",FileVersion(Assembly.GetExecutingAssembly()));

            // ensure that the PackageDirectory exists
            if (string.IsNullOrEmpty(PackageDirectory)) {
                Log.LogError("PackageDirectory attribute is null or empty.");
                return false;
            }

            PackageDirectory = Path.GetFullPath(PackageDirectory);

            if (!Directory.Exists(PackageDirectory)) {
                Log.LogError("PackageDirectory '{0}' does not exist.", PackageDirectory);
                return false;
            }

            // is the overlay package installed?
            var expectedFile = Path.Combine(PackageDirectory, string.Format("{0}.{1}.nupkg", Package, Version ));

            if (File.Exists(expectedFile)) {
                // yes, return success.
                return true;
            }
            var nugetExtensionsPath = Environment.GetEnvironmentVariable("NUGET_EXTENSIONS_PATH");
            try {
                // nope, not yet.

                if (string.IsNullOrEmpty(SolutionDirectory) || !Directory.Exists(SolutionDirectory)) {
                    try {
                        SolutionDirectory = Path.GetFullPath(Path.Combine(PackageDirectory, "../.."));
                    } catch {
                        SolutionDirectory = PackageDirectory;
                    }
                }

                // find the highest version nuget.exe (either on the PATH, or in the directory somewhere.)
                _nugetExe = FindHighestBinary("nuget.exe", SolutionDirectory, true, true);
                if (string.IsNullOrEmpty(_nugetExe)) {
                    _nugetExe = FindHighestBinary("nuget.exe", PackageDirectory, true, true);
                    if (string.IsNullOrEmpty(_nugetExe)) {
                        Log.LogError("Unable to find nuget exe in PATH, SolutionDirectory ('{0}') or PackageDirectory ('{1}') ", SolutionDirectory, PackageDirectory);
                        return false;
                    }
                }

                // find the highest version of the CoApp.NuGetNativeExtensions.dll file (somewhere in there too)
                var extension = FindHighestBinary("CoApp.NuGetNativeExtensions.dll", SolutionDirectory, true);
                if (string.IsNullOrEmpty(extension)) {
                    extension = FindHighestBinary("CoApp.NuGetNativeExtensions.dll", PackageDirectory, true);
                    if (string.IsNullOrEmpty(extension)) {
                        Log.LogError("Unable to find CoApp.NuGetNativeExtensions.dll in SolutionDirectory ('{0}') or PackageDirectory ('{1}') ", SolutionDirectory, PackageDirectory);
                        return false;
                    }
                }

                var location = Path.GetDirectoryName(extension);

                if (!string.IsNullOrEmpty(nugetExtensionsPath)) {
                    var paths = nugetExtensionsPath.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries);
                    paths = new[] {
                        location
                    }.Union(paths, StringComparer.CurrentCultureIgnoreCase).ToArray();
                    location = paths.Aggregate(string.Empty, (c, e) => c + ";" + e);
                }
                Environment.SetEnvironmentVariable("NUGET_EXTENSIONS_PATH", location);

                // setup environment to include this assembly's folder in the NUGET_EXTENSIONS_PATH

                // call nuget overlay <pkg> -overlaydirectory <dir>
                if (!NuGetOverlay(Package,Version, PackageDirectory)) {
                    return false;
                }


                // is the overlay package installed?
                if (!File.Exists(expectedFile)) {
                    Log.LogWarning("Overlay Package '{0} v{1}' installed correctly, but the nupkg file '{2}' is not in the expected location.", Package, Version, expectedFile);
                }

                return true;
            } finally {
                Environment.SetEnvironmentVariable("NUGET_EXTENSIONS_PATH", nugetExtensionsPath);
            }
            // return false;
        }

        private bool NuGetOverlay(string package, string version, string packageDirectory) {
            var process = AsyncProcess.Start(_nugetExe, string.Format("overlay {0} -Version {1} -OverlayPackageDirectory {2}", package,version , packageDirectory));
            foreach (var txt in process.StandardOutput) {
                if (!string.IsNullOrEmpty(txt)) {
                    Log.LogMessage("NuGet:{0}",txt);
                }
            }

            if (process.ExitCode != 0) {
                foreach (var txt in process.StandardError) {
                    if (!string.IsNullOrEmpty(txt)) {
                        Log.LogError("NuGet:{0}", txt);
                    }
                }
                return false;
            }
            return true;
        }
    }
}