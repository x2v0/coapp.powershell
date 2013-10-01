namespace CoApp.NuGetNativeMSBuildTasks {
    using System;
    using Microsoft.Build.Framework;

    public class CheckRuntimeLibrary : MsBuildTaskBase {
        [Required]
        public string RuntimeLibrary {get; set;}

        [Required]
        public string ExpectedRuntimeLibrary { get; set; }

        [Required]
        public string LibraryName { get; set; }

        // [Required]
        // public string Configuration { get; set; }

        public override bool Execute() {
            // if they say that the expected runtime library is to be ignored, that's ok.
            if (string.IsNullOrEmpty(ExpectedRuntimeLibrary) || string.IsNullOrEmpty(RuntimeLibrary) || ExpectedRuntimeLibrary.Equals("none",StringComparison.InvariantCultureIgnoreCase)) {
                return true;
            }

            if (!RuntimeLibrary.Equals(ExpectedRuntimeLibrary, StringComparison.InvariantCultureIgnoreCase)) { //is being used matching configuration '{2}'. This
                Log.LogWarning("This project is compiling with the RuntimeLibrary '{0}' and the included package '{1}' is expecting the runtime library to be '{2}'",RuntimeLibrary, LibraryName, ExpectedRuntimeLibrary);
                Log.LogWarning("If this project fails to link correctly, you may need to change the RuntimeLibrary linkage to match ( likely, '{0}')", ExpectedRuntimeLibrary);
            }

            return true;
        }
    }
}