//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2010-2013 Garrett Serack and CoApp Contributors. 
//     Contributors can be discovered using the 'git log' command.
//     All rights reserved.
// </copyright>
// <license>
//     The software is licensed under the Apache 2.0 License (the "License")
//     You may not use the software except in compliance with the License. 
// </license>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NativeExtensions
{
    using NuGet;

    public class CustomPackagePathResolver : DefaultPackagePathResolver {
        public string OverlayDirectory {get; set;}

        public CustomPackagePathResolver(string path) : base(path) {
        }

        public CustomPackagePathResolver(IFileSystem fileSystem) : base(fileSystem) {
        }

        public CustomPackagePathResolver(string path, bool useSideBySidePaths) : base(path, useSideBySidePaths) {
        }

        public CustomPackagePathResolver(IFileSystem fileSystem, bool useSideBySidePaths) : base(fileSystem, useSideBySidePaths) {
        }

        public override string GetPackageDirectory(IPackage package) {
            return GetPackageDirectory(package.Id, package.Version);
        }

        public override string GetPackageDirectory(string packageId, SemanticVersion version) {
            return OverlayDirectory;
        }
    }
}
