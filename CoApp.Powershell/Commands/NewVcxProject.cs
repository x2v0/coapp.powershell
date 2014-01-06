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
    using System.Collections;
    using System.IO;
    using System.Management.Automation;
    using ClrPlus.Platform;
    using ClrPlus.Powershell.Core;

    [Cmdlet(AllVerbs.New, "VcxProject")]
    public class NewVcxProject : BaseCmdlet {
        [Parameter(HelpMessage = "VCXProjfile to create (.vcxproj)", Mandatory = true, Position = 0)]
        public string OutputFile {get; set;}

        [Parameter(HelpMessage = "Import project information from existing .vcxproj file")]
        public string ImportFrom {get; set;}

        [Parameter(HelpMessage = "Overwrite the destination file if it exists")]
        public SwitchParameter Force {get; set;}

        [Parameter(HelpMessage = "Replace value in script")]
        public Hashtable Overrides {get; set;}

        protected override void ProcessRecord() {
#if USING_RESTABLE_CMDLET
            if (Remote) {
                ProcessRecordViaRest();
                return;
            }
#endif 
            using (new PushDirectory((Path.Combine(SessionState.Drive.Current.Root, SessionState.Drive.Current.CurrentLocation)))) {
            }
        }
    }
}