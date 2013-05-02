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
    using ClrPlus.Core.Extensions;
    using ClrPlus.Powershell.Core;
    using ClrPlus.Powershell.Rest.Commands;

    [Cmdlet(AllVerbs.Show, "CoAppToolsVersion")]
    public class ShowCoAppToolsVersion : RestableCmdlet<ShowCoAppToolsVersion> {
        protected override void ProcessRecord() {
            if (Remote) {
                ProcessRecordViaRest();
                return;
            }
            WriteObject("CoApp Powershell Developer Tools Version: {0}".format(this.Assembly().Version()));
        }
    }
}