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

namespace NativeExtensions {
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.IO;
    using System.Linq;
    using NuGet;
    using NuGet.Commands;
    using NuGet.Common;

    [Export(typeof(ICommand))]
    [Command("Overlay", "Overlays a package into an existing directory")]
    public class OverlayCommand : InstallCommand {

        [Option("Target Package Directory to overlay into")]
        public string OverlayPackageDirectory {get; set;}
        
        [Option("Filter packages by Pivot keywords")]
        public string Pivots { get; set; }

        [Option("Just list the packages instead of installing them.")]
        public bool List { get; set; }

        public override void ExecuteCommand() {
            if (string.IsNullOrEmpty(Pivots)) {
                // if they didn't specify any pivot keywords, just install the package they asked for.
                base.ExecuteCommand();
                return;
            }
            
            // otherwise, they have specified at least one pivot, and therefore we should just grab the pivot list 
            // and install everything that we completely match in the pivot list.

            var pivotListPath = Path.Combine(OverlayPackageDirectory, @"build\native\pivot-list.txt");
            if (!File.Exists(pivotListPath)) {
                throw new Exception(string.Format("Can't fine pivot list at '{0}'", pivotListPath));
            }

            var allPivots = File.ReadAllLines(pivotListPath);

            IEnumerable<string> keywords = Pivots.Split(new char[] {' ',','} , StringSplitOptions.RemoveEmptyEntries);
            if (this.Arguments.Count > 0) {
                keywords = keywords.Union(this.Arguments);
            }
            keywords = keywords.Select(each => each.ToLower()).ToArray();


            IEnumerable<string> selectedPivots = keywords.All(each => each == "all") ? allPivots : (from p in allPivots let piv = p.ToLower() where keywords.All(piv.Contains) select p);

            // just looking?
            if (List) {
                this.Console.WriteLine("Overlay packages found:");
                foreach (var overlayPivot in selectedPivots) {
                    this.Console.WriteLine("   {0}", overlayPivot);
                }
                return;
            } 

            // otherwise, just install each one.
            Pivots = null;

            foreach (var overlayPivot in selectedPivots) {
                try {
                    Arguments.Clear();
                    Arguments.Add(overlayPivot);

                    ExecuteCommand();
                } catch (Exception e) {
                    Console.WriteLine("{0}/{1}/{2}", e.GetType().Name, e.Message, e.StackTrace );
                }
            }
        }

        protected override IPackageManager CreatePackageManager(IFileSystem packagesFolderFileSystem) {
            var sourceRepository = GetRepository();
            var pathResolver = new CustomPackagePathResolver(packagesFolderFileSystem, true) {
                OverlayDirectory = OverlayPackageDirectory
            };
            return new PackageManager(sourceRepository, pathResolver, packagesFolderFileSystem, new LocalPackageRepository(pathResolver, packagesFolderFileSystem)) { Logger = base.Console };
        }

        protected virtual IPackageRepository GetRepository() {
            var aggregateRepository = AggregateRepositoryHelper.CreateAggregateRepositoryFromSources(RepositoryFactory, SourceProvider, (IEnumerable<string>)Source);
            var failingRepositories = aggregateRepository.IgnoreFailingRepositories;
            if (!NoCache)
                aggregateRepository = new AggregateRepository((IEnumerable<IPackageRepository>)new IPackageRepository[2] {
                    CacheRepository,
                    (IPackageRepository)aggregateRepository
                }) {
                    IgnoreFailingRepositories = failingRepositories
                };
            aggregateRepository.Logger = (ILogger)Console;
            return (IPackageRepository)aggregateRepository;
        }

    }
}