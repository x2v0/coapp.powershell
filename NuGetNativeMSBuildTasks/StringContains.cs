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
    using System.Linq;
    using Microsoft.Build.Framework;

    public class StringContains : MsBuildTaskBase {

        [Required]
        public string Text { get; set; }
        [Required]
        public string Library { get; set; }
        [Required]
        public string Value { get; set; }

        [Output]
        public string Result { get; set; }

        public override bool Execute() {
            Result = ((Text ?? "").Split(';').Contains(Library) ) ? Value : String.Empty;
            return true;
        }
    }
}