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

namespace ClrPlus.Core.Utility {
    using System;
    using System.Reflection;

    public static class AssemblyResolver {
        private static bool _initialized;
        static AssemblyResolver() {
            Initialize();
        } 

        public static void Initialize() {
            if (!_initialized) {
                _initialized = true;
                AppDomain.CurrentDomain.AssemblyResolve += Resolver;
            }
        }
         
        public static Assembly Resolver(object sender, ResolveEventArgs args) {
            var match = "Assemblies." + new AssemblyName(args.Name).Name;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    foreach (var resource in assembly.GetManifestResourceNames()) {
                        if (resource.Equals(match, StringComparison.InvariantCultureIgnoreCase)) {
                            using (var stream = assembly.GetManifestResourceStream(resource)) {
                                var assemblyData = new Byte[stream.Length];
                                stream.Read(assemblyData, 0, assemblyData.Length);
                                return Assembly.Load(assemblyData);
                            }
                        }
                    }
                } catch {
                    
                }
            }
            return null;
        }
    }
}