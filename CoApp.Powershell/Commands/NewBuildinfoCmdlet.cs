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
    using System.IO;
    using System.Management.Automation;
    using ClrPlus.Platform;
    using ClrPlus.Powershell.Core;
    using ClrPlus.Powershell.Rest.Commands;

    [Cmdlet(AllVerbs.New, "Buildinfo")]
    public class NewBuildinfoCmdlet : RestableCmdlet<NewBuildinfoCmdlet> {
        [Parameter(HelpMessage = "Projects to include in .buildinfo (either .vcxproj or .buildinfo files)", Mandatory = true, Position = 0)]
        public string[] SourceFiles {get; set;}

        [Parameter(HelpMessage = "Output .buildinfo file")]
        public string OutputFile {get; set;}

        [Parameter(HelpMessage = "Overwrite the destination file if it exists")]
        public SwitchParameter Force {get; set;}

        protected override void ProcessRecord() {
            if (Remote) {
                ProcessRecordViaRest();
                return;
            }

            using (new PushDirectory((Path.Combine(SessionState.Drive.Current.Root, SessionState.Drive.Current.CurrentLocation)))) {
                
            }
        }
    }
}