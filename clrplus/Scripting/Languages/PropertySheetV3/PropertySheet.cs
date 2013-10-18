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

namespace ClrPlus.Scripting.Languages.PropertySheetV3 {
    using System.IO;
    using Core.Extensions;
    using Languages.PropertySheet;
    using Mapping;
    using Platform;

    public class PropertySheet : ObjectNode {
        internal string FullPath;

        internal PropertySheet(RootPropertySheet root)
            : base(root) {
            // used by imported sheets to bind themselves to the right root object.
        }

        public override View CurrentView {
            get {
                return Parent.CurrentView;
            }
        }

        public override RootPropertySheet Root {
            get {
                return Parent as RootPropertySheet;
            }
        }

        internal PropertySheet() {
            // called only by RootPropertySheet.
        }

        public virtual void ImportFile(string filename) {
            Root.ImportFile(filename);
        }

        public virtual void ImportText(string propertySheetText, string originalFilename) {
            Root.ImportText(propertySheetText, originalFilename);
        }

        public virtual void ParseFile(string filename) {
            FullPath = filename.GetFullPath();

            if(!File.Exists(FullPath)) {
                throw new FileNotFoundException("Can't find property sheet file '{0}'".format(filename), FullPath);
            }

            ParseText(File.ReadAllText(FullPath), FullPath);
        }

        public virtual void ParseText(string propertySheetText, string originalFilename) {
            FullPath = originalFilename;
            new PropertySheetParser(PropertySheetTokenizer.Tokenize(propertySheetText, TokenizerVersion.V3), this, originalFilename).Parse();
        }

        public void SaveFile(string filename = null) {
            filename = filename ?? FullPath;
            File.WriteAllText(filename, GetPropertySheetSource());
        }

        public string GetPropertySheetSource() {
            return "";
        }


    }
}