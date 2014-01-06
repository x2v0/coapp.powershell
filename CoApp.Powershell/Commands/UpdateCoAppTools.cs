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
    using System.Diagnostics;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Net;
    using ClrPlus.Core.Extensions;
    using ClrPlus.Platform;
    using ClrPlus.Powershell.Core;
    using Microsoft.Deployment.WindowsInstaller;

    [Cmdlet(AllVerbs.Update, "CoAppTools")]
    public class UpdateCoAppTools : BaseCmdlet {

        [Parameter(HelpMessage = "If specified, will attempt to kill all the powershell processes after kicking off the installer.")]
        public SwitchParameter KillPowershells;

        [Parameter(HelpMessage = "Get the latest 'beta' build, instead of the latest 'stable' build.")]
        public SwitchParameter Beta;

        [Parameter(HelpMessage = "Get the absolute latest build off the dev box. Warning: This may be broken from time-to-time!")]
        public SwitchParameter Development;

        protected override void ProcessRecord() {
#if USING_RESTABLE_CMDLET
            if(Remote) {
                ProcessRecordViaRest();
                return;
            }
#endif

            var wc = new WebClient();
            var tmp = "coapp.tools.powershell.msi".GenerateTemporaryFilename();
            if(Development) {
                wc.DownloadFile(@"http://downloads.coapp.org/files/Development.CoApp.Tools.Powershell.msi", tmp);
            } else if (Beta) {
                wc.DownloadFile(@"http://downloads.coapp.org/files/Beta.CoApp.Tools.Powershell.msi", tmp);
            } else {
                wc.DownloadFile(@"http://downloads.coapp.org/files/CoApp.Tools.Powershell.msi", tmp);
            }   

            
            FourPartVersion ver = FileVersionInfo.GetVersionInfo(tmp);

            if (ver == 0) {
                using (Database db = new Database(tmp)) {
                    ver = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'") as string;
                }
            }

            FourPartVersion thisVer = this.Assembly().Version();
            if (ver < thisVer) {
                WriteObject("The current version {0} is newer than the version on the web {1}.".format( thisVer, ver));
                return;
            }

            if(ver == thisVer) {
                WriteObject("The current version {0} is the current version.".format(thisVer, ver));
                return;
            }

            WriteObject("The current version {0} will be replaced with the newer than the version from the web {1}.".format(thisVer, ver));

            using (dynamic ps = Runspace.DefaultRunspace.Dynamic()) {
                ps.InvokeExpression(@"msiexec.exe /i ""{0}""".format(tmp));
            }

            if (!KillPowershells) {
                WriteObject("FYI, the installer can't actually update without killing all the powershell tasks.");
                WriteObject("If you are running as admin, you can do this automatically with the -KillPowershells switch on this command.");
            } else {
                using (var ps = Runspace.DefaultRunspace.Dynamic()) {
                    ps.StopProcess(name: "powershell");
                }
            }

        }
    }
}