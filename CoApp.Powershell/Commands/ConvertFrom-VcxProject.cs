using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoApp.Powershell.Commands {
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Xml;
    using System.Xml.Linq;
    using ClrPlus.Core.Collections;
    using ClrPlus.Core.DynamicXml;
    using ClrPlus.Core.Exceptions;
    using ClrPlus.Core.Extensions;
    using ClrPlus.Core.Tasks;
    using ClrPlus.Platform;
    using ClrPlus.Platform.Process;
    using ClrPlus.Powershell.Core;
    using ClrPlus.Powershell.Rest.Commands;
    using ClrPlus.Scripting.MsBuild.Building;
    using ClrPlus.Scripting.MsBuild.Packaging;
    using ClrPlus.Scripting.MsBuild.Utility;
    using Microsoft.Build.Construction;
    using Microsoft.Build.Evaluation;
    using System.Text.RegularExpressions;

    [Cmdlet(AllVerbs.ConvertFrom, "VcxProject")]
    public class ConvertFromVcxProject : RestableCmdlet<ConvertFromVcxProject> {
        [Parameter(HelpMessage = "Original VcxProject file (.vcxproj)", Mandatory = true, Position = 0)]
        public string SourceFile { get; set; }

        [Parameter(HelpMessage = "Output VcxProject file (.vcxproj)", Mandatory = true, Position = 1)]
        public string OutputFile { get; set; }

        [Parameter(HelpMessage = "Replace value in script", Position = 2)]
        public Hashtable Overrides { get; set; }

        [Parameter(HelpMessage = "Overwrite the destination file")]
        public SwitchParameter Force;

        protected override void ProcessRecord() {
            if(Remote) {
                ProcessRecordViaRest();
                return;
            }

            System.Environment.CurrentDirectory = (SessionState.PSVariable.GetValue("pwd") ?? "").ToString();

            var replacements =Overrides != null ? Overrides.Keys.Cast<object>().ToDictionary(k => new Regex(k.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase), k => Overrides[k].ToString()) : new Dictionary<Regex, string>();

            using (dynamic ps = Runspace.DefaultRunspace.Dynamic()) {
                Project project = null;
                string tmpFile  = null;

                try {
                    SourceFile = SourceFile.GetFullPath();
                    OutputFile = OutputFile.GetFullPath();

                    if (!File.Exists(SourceFile)) {
                        throw new ClrPlusException("Source file '{0}' does not exist.".format(SourceFile));
                    }

                    if (!SourceFile.EndsWith(".vcxproj", StringComparison.InvariantCultureIgnoreCase)) {
                        throw new ClrPlusException("Source file '{0}' does not have a .vcxproj extension.".format(SourceFile));
                    }

                    if(Force && File.Exists(OutputFile)) {
                        OutputFile.TryHardToDelete();
                    }
                    
                    if (File.Exists(OutputFile)) {
#if DEBUG
                        OutputFile.TryHardToDelete();
#else
                        throw new ClrPlusException("Destination file '{0}' already exists.".format(OutputFile));
#endif
                    }

                    var text = System.IO.File.ReadAllText(SourceFile);
                    text = text.Replace("xmlns", "notxmlns");

                    var doc = XElement.Parse(text);

                    var elements = from e in doc.Descendants("Import") where ((string)e.Attribute("Project")).IndexOf("VCTargetsPath") > -1 select e;
                    elements.Remove();
                    elements = from e in doc.Descendants("Import") where ((string)e.Attribute("Project")).ToLower().IndexOf("microsoft.cpp") > -1 select e;
                    elements.Remove();

                    text = doc.ToString().Replace("notxmlns", "xmlns");
                     tmpFile = SourceFile + ".tmp";

                    File.WriteAllText(tmpFile, text);

                    var msbuild = Path.Combine(EnvironmentUtility.DotNetFrameworkFolder, "msbuild.exe");

                    var proc = AsyncProcess.Start(
                        new ProcessStartInfo(msbuild, "/pp /p:Configuration=Debug;Platform=Win32 {0}".format(tmpFile)) {
                            WindowStyle = ProcessWindowStyle.Normal,
                        });
                    proc.WaitForExit();

                    // get the processed script.
                    text = proc.StandardOutput.Aggregate((c, e) => c + "\r\n" + e);
                    text = text.Replace("xmlns", "notxmlns");

                    // now we can maniuplate the whole source document
                    doc = XElement.Parse(text);

                    // use our cmdlet to make the project file.
                    ps.NewVCProject(OutputFile).Wait();

                    var outputFolder = Path.GetDirectoryName(OutputFile);
                    var originalProjectFolder = Path.GetDirectoryName(SourceFile);
                    Environment.CurrentDirectory = originalProjectFolder;

                    project = new Project(OutputFile);

                    doc.CopyItemsToProject(project, outputFolder, "ClCompile", "C Source Files");
                    doc.CopyItemsToProject(project, outputFolder, "ResourceCompile", "Resource Files");

                    using (var local = CurrentTask.Local.Events) {
                        var propertiesReferenced = new Queue<string>();
                        var propertiesFinished = new HashSet<string>();

                        local.Events += new ProjectExtensions.PropertyReferenced(name => {
                            if (!propertiesReferenced.Contains(name) && !propertiesFinished.Contains(name)) {
                                propertiesReferenced.Enqueue(name);
                            }
                        });

                        local.Events += new ProjectExtensions.CustomReplacement(value=> {
                            foreach (var rx in replacements.Keys) {
                                value = rx.Replace(value, replacements[rx]);
                            }
                            return value;
                        });

                        var configurations = doc.GetConfigurations().ToArray();

                        // remove the common one from the peers
                        var conditionedConfigurations = configurations.Where(each => each.Condition.Is()).ToArray();

                        // common stuff
                        configurations.Where(each => string.IsNullOrEmpty(each.Condition)).ProcessConfiguration(project, "", outputFolder);


                        conditionedConfigurations.ProcessConfiguration(project, "", outputFolder);

                        conditionedConfigurations.Where(each => each.IsDebug).ProcessConfiguration(project, "$(IS_DEBUG)", outputFolder);
                        conditionedConfigurations.Where(each => each.IsRelease).ProcessConfiguration(project, "$(IS_RELEASE)", outputFolder);

                        conditionedConfigurations.Where(each => each.IsStatic).ProcessConfiguration(project, "$(IS_STATIC) Or $(IS_LTCG)", outputFolder);
                        conditionedConfigurations.Where(each => each.IsDynamic).ProcessConfiguration(project, "$(IS_DYNAMIC)", outputFolder);

                        // conditionedConfigurations.Where(each => !each.IsStatic && each.IsLibrary).ProcessConfiguration(project, "$(IS_DYNAMIC) And $(IS_LIBRARY)", outputFolder);
                        // conditionedConfigurations.Where(each => !each.IsDynamic && each.IsLibrary).ProcessConfiguration(project, "($(IS_STATIC) Or $(IS_LTCG)) And $(IS_LIBRARY)", outputFolder);

                        conditionedConfigurations.Where(each => each.IsLibrary).ProcessConfiguration(project, "", outputFolder);
                        conditionedConfigurations.Where(each => each.IsApplication).ProcessConfiguration(project, "", outputFolder);

                        // if this is an app, set the configuration type.
                        if (conditionedConfigurations.Any(each => each.IsApplication)) {
                            var pgpe = project.FindOrCreatePropertyGroup("ConfigurationSettings");
                            pgpe.Properties.FirstOrDefault(each => each.Name == "ConfigurationType").Value = "Application";
                        }

                        // now do the referenced variables
                        
                        while (propertiesReferenced.Any()) {
                            var propRefd = propertiesReferenced.Dequeue();
                            propertiesFinished.Add(propRefd);

                            configurations.Where(each => string.IsNullOrEmpty(each.Condition)).ProcessReferenceVariables(project, "", propRefd);


                            conditionedConfigurations.ProcessReferenceVariables(project, "", propRefd);

                            conditionedConfigurations.Where(each => each.IsDebug).ProcessReferenceVariables(project, "$(IS_DEBUG)", propRefd);
                            conditionedConfigurations.Where(each => each.IsRelease).ProcessReferenceVariables(project, "$(IS_RELEASE)", propRefd);

                            conditionedConfigurations.Where(each => each.IsStatic).ProcessReferenceVariables(project, "$(IS_STATIC) Or $(IS_LTCG)", propRefd);
                            conditionedConfigurations.Where(each => each.IsDynamic).ProcessReferenceVariables(project, "$(IS_DYNAMIC)", propRefd);

                            conditionedConfigurations.Where(each => each.IsLibrary).ProcessReferenceVariables(project, "", propRefd);
                            conditionedConfigurations.Where(each => each.IsApplication).ProcessReferenceVariables(project, "", propRefd);

                           

                        }
                        // more likely, we'd like the customsettings to be in the opposite order.
                        var customSettings = project.FindOrCreatePropertyGroup("CustomSettings");
                        var xml = customSettings.XmlElement();
                        var nodes = xml.ChildNodes.Cast<XmlElement>().Reverse().ToArray();
                        customSettings.RemoveAllChildren();

                        foreach(var i in nodes) {
                            xml.AppendChild(i);
                        }

                    }

                    project.Save();
                } finally {
                    if (project != null && ProjectCollection.GlobalProjectCollection.LoadedProjects.Contains(project)) {
                        ProjectCollection.GlobalProjectCollection.UnloadProject(project);
                    }

#if DEBUG
#else
                    tmpFile.TryHardToDelete();
#endif
                }
            }

        }

    }

    public static class ProjectExtensions {
        public static ProjectItemGroupElement FindOrCreateItemGroup(this Project project, string label) {
            var result = project.Xml.ItemGroups.FirstOrDefault(each => each.Label == label);
            if (result == null) {
                result = project.Xml.AddItemGroup();
                result.Label = label;
            }
            return result;
        }

        public static ProjectPropertyGroupElement FindOrCreatePropertyGroup(this Project project, string label) {
            var result = project.Xml.PropertyGroups.FirstOrDefault(each => each.Label == label);
            if(result == null) {
                result = project.Xml.AddPropertyGroup();
                result.Label = label;
            }
            return result;
        }

        public static ProjectPropertyElement FindOrCreatePropertyByCondition(this ProjectPropertyGroupElement pgpe, string name, string condition) {
            var result = pgpe.Properties.FirstOrDefault(each => each.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && each.Condition == condition);
            if (result == null) {
                result = pgpe.AddProperty(name,"");
                result.Condition = condition;
            }
            return result;
        }

        public static UniquePathPropertyList FindOrCreatePathPropertyListByCondition(this ProjectPropertyGroupElement pgpe, string name, string condition) {
            var result = pgpe.Properties.FirstOrDefault(each => each.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && each.Condition == condition);
            if(result == null) {
                result = pgpe.AddProperty(name, "");
                result.Condition = condition;
            }
            return new UniquePathPropertyList(() => result.Value, (s) => result.Value = s);
        }

        public static UniqueStringPropertyList FindOrCreatePropertyListByCondition(this ProjectPropertyGroupElement pgpe, string name, string condition) {
            var result = pgpe.Properties.FirstOrDefault(each => each.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && each.Condition == condition);
            if(result == null) {
                result = pgpe.AddProperty(name, "");
                result.Condition = condition;
            }
            return new UniqueStringPropertyList(() => result.Value, (s) => result.Value = s);
        }


        public static void CopyItemsToProject(this XElement doc, Project project, string outputFolder, string itemType, string itemGroupLabel) {
            // grab all the clcompile lines
            var items = doc.Descendants(itemType).Where(each => each.Parent.Name == "ItemGroup").ToArray();

            var solo = items.Where(each => string.IsNullOrEmpty(each.Parent.AttributeValue("Condition"))).ToArray();
            var grouped = items.Where(each => each.Parent.AttributeValue("Condition").Is() ).ToArray();

            var groups = grouped.Select(each => each.Parent.AttributeValue("Condition")).Distinct().ToArray();

            // handle the lone files
            project.FindOrCreateItemGroup(itemGroupLabel).AddItemsToGroup(solo, itemType, outputFolder);

            // handle the files that are in conditioned groups.
            var index = 1;
            foreach (var grp in groups) {
                var group = grp;
                project.FindOrCreateItemGroup("{0} ({1})".format(itemGroupLabel, index++)).AddItemsToGroup(grouped.Where(each => each.Parent.AttributeValue("Condition") == group), itemType, outputFolder);
            }
        }

        public static void AddItemsToGroup(this ProjectItemGroupElement group, IEnumerable<XElement> items, string itemType, string outputFolder) {
            foreach (var each in items) {
                var itemPath = each.AttributeValue("Include");
                var newItemPath = outputFolder.RelativePathTo(itemPath.GetFullPath());

                var item = group.AddItem(itemType, newItemPath);
                var condition = each.AttributeValue("Condition");
                if (condition.Is()) {
                    condition = condition.Replace(itemPath, newItemPath, StringComparison.InvariantCultureIgnoreCase);
                    item.Condition = condition;
                }
            }
        }

        public static IEnumerable<Configuration> GetConfigurations(this XElement project) {
            var projectConfigurations = project.Descendants("ProjectConfiguration");
            var propertyGroups = project.Elements("PropertyGroup").Where(each => each.AttributeValue("Label") == "Configuration").ToArray();
            var pgConditions = propertyGroups.Select(each => each.AttributeValue("Condition")).UnionSingleItem("").Distinct().ToArray();
            var conditions = project.Descendants().Where(each => each.AttributeValue("Condition") != null).Select(each => each.AttributeValue("Condition")).Distinct().ToArray();
            var unmatchedConditions = conditions.Where(each => !pgConditions.ContainsIgnoreCase(each)).ToArray();
            if (unmatchedConditions.Length > 0) {
                foreach (var c in unmatchedConditions) {
                    Event<Warning>.Raise("", @"Unmatched condition (""{0}"") in project ", c);
                }
            }

            return pgConditions.Select(condition => new Configuration(project,condition));
        }

        public static void AddChildren(this XDictionary<string, string> propertyDictionary, IEnumerable<XElement> children) {
            foreach (var child in children) {
                var val = child.Value;
                var key = child.LocalName();

                if (propertyDictionary.ContainsKey(key)) {
                    var cur = propertyDictionary[key];
                    val = val.Replace("%({0})".format(key), cur);
                }
                val = Event<CustomReplacement>.Raise(val);

                propertyDictionary.Add(key, val);
            }
        }

        public static void ProcessPropertyList(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, ProjectPropertyGroupElement pgpe, string outputPropertyName, string inputCategory, string inputPropertyName) {
            UniqueStringPropertyList propertyList = null;
            var masterProperty = pgpe.FindOrCreatePropertyListByCondition(outputPropertyName, "");

            var commonValues = configurations.GetCommonValues(inputCategory, inputPropertyName).ToArray();
            if(commonValues.Length > 0) {
                foreach(var cv in commonValues) {
                    var i = cv;
                    i= Event<CustomReplacement>.Raise(i);
                    FindReferencedProperties(i);
                    if(!masterProperty.Contains(i)) {
                        if(propertyList == null) {
                            propertyList = (pgpe.FindOrCreatePropertyListByCondition(outputPropertyName, conditionToApply));
                            if(conditionToApply.Is()) {
                                propertyList.Add("$({0})".format(outputPropertyName));
                            }
                        }
                        propertyList.Add(i);
                    }
                }
            }
        }

        static private Regex _propRx = new Regex(@"\$\((\w*)");
        public delegate void PropertyReferenced(string propertyName);
        public delegate string CustomReplacement(string someValue);


        private static void FindReferencedProperties(this string value) {
            foreach(Match match in _propRx.Matches(value)) {
                Event<PropertyReferenced>.Raise( match.Groups[1].Value );
            }
        }

        public static void ProcessPathPropertyList(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, ProjectPropertyGroupElement pgpe, string outputPropertyName, string inputCategory, string inputPropertyName, string outputFolder) {
            UniquePathPropertyList propertyList = null;
            var masterProperty = pgpe.FindOrCreatePathPropertyListByCondition(outputPropertyName, "");

            var commonValues = configurations.GetCommonValues(inputCategory, inputPropertyName).ToArray();
            if(commonValues.Length > 0) {
                foreach(var i in commonValues) {
                    var pth = i;
                    if (pth.IndexOf("$(") == -1) {
                        pth = outputFolder.RelativePathTo(pth.GetFullPath());
                    }
                    if(pth.Equals("$(OutDir)", StringComparison.InvariantCultureIgnoreCase)) {
                        continue;
                    }

                    pth = Event<CustomReplacement>.Raise(pth);

                    FindReferencedProperties(pth);

                    if(!masterProperty.Contains(pth)) {
                        
                        if(propertyList == null) {
                            propertyList = (pgpe.FindOrCreatePathPropertyListByCondition(outputPropertyName, conditionToApply));
                            if(conditionToApply.Is()) {
                                propertyList.Add("$({0})".format(outputPropertyName));
                            }
                        }
                        propertyList.Add(pth);
                    }
                }
            }
        }

        public static void ProcessProperty(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, ProjectPropertyGroupElement pgpe, string outputPropertyName, string inputCategory, string inputPropertyName) {
            ProjectPropertyElement masterProperty = null;

            var value = configurations.GetCommonValue(inputCategory, inputPropertyName);
            if(value.Is()) {
                value = Event<CustomReplacement>.Raise(value);
                FindReferencedProperties(value);
                if(pgpe.FindOrCreatePropertyByCondition(outputPropertyName, "").Value != value) {
                    pgpe.FindOrCreatePropertyByCondition(outputPropertyName, conditionToApply).Value = value;
                }
            }
        }

        public static void ProcessPathProperty(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, ProjectPropertyGroupElement pgpe, string outputPropertyName, string inputCategory, string inputPropertyName, string outputFolder) {
            var masterProperty = pgpe.FindOrCreatePropertyByCondition(outputPropertyName, "");

            var pth = configurations.GetCommonValue(inputCategory, inputPropertyName);
            if(pth.Is()) {
                pth = Event<CustomReplacement>.Raise(pth);
                FindReferencedProperties(pth);
                if(pth.IndexOf("$(") == -1) {
                    pth = outputFolder.RelativePathTo(pth.GetFullPath());
                }
                if (pth.Equals("$(OutDir)", StringComparison.InvariantCultureIgnoreCase)) {
                    return;
                }
                   
                if(masterProperty.Value != pth) {
                    pgpe.FindOrCreatePropertyByCondition(outputPropertyName, conditionToApply).Value = pth;
                }
            }
        }

        public static void ProcessConfiguration(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, string outputFolder) {
            
            var pgpe = project.FindOrCreatePropertyGroup("ConfigurationSettings");
            var cfgs = configurations.ToArray();

            cfgs.ProcessPathPropertyList(project, conditionToApply, pgpe, "IncludeDirectories", "ClCompile", "AdditionalIncludeDirectories", outputFolder);
            cfgs.ProcessPropertyList(project, conditionToApply, pgpe, "Defines", "ClCompile", "PreprocessorDefinitions");

            cfgs.ProcessPathPropertyList(project, conditionToApply, pgpe, "LibraryDirectories", "Link", "AdditionalLibraryDirectories", outputFolder);
            cfgs.ProcessPropertyList(project, conditionToApply, pgpe, "Libraries", "Link", "AdditionalDependencies");

            cfgs.ProcessPathProperty(project, conditionToApply, pgpe, "ModuleDefinitionFile", "Link", "ModuleDefinitionFile", outputFolder);


            cfgs.ProcessProperty( project, conditionToApply, pgpe, "PreBuild", "PreBuildEvent", "Command");
            cfgs.ProcessProperty(project, conditionToApply, pgpe, "PostBuild", "PostBuildEvent", "Command");
            cfgs.ProcessProperty(project, conditionToApply, pgpe, "PreLink", "PreLinkEvent", "Command");
        }

        public static void ProcessReferenceVariables(this IEnumerable<Configuration> configurations, Project project, string conditionToApply, string variable) {
            var pgpe = project.FindOrCreatePropertyGroup("CustomSettings");
            var cfgs = configurations.ToArray();
            cfgs.ProcessProperty(project, conditionToApply, pgpe, variable, "Properties",variable);
        }

        public static IEnumerable<string> GetCommonValues(this IEnumerable<Configuration> configurations, string propertyCollection, string property) {
            var values = configurations.Select(each => (each.Props.GetOrAdd(propertyCollection, () => new XDictionary<string, string>())[property] ?? string.Empty).Split(';')).ToArray();
            if (values.Length > 0) {
                foreach (var val in values[0]) {
                    if(val.IsNullOrEmpty() || val.StartsWith("%(") ) {
                        continue;
                    }
                    bool ok = true;

                    foreach (var set in values) {
                        if (!set.ContainsIgnoreCase(val)) {
                            ok = false;
                        }
                    }

                    if (ok) {
                        yield return val;
                    }
                }
            }
        }

        public static string GetCommonValue(this IEnumerable<Configuration> configurations, string propertyCollection, string property) {
            var values = configurations.Select(each => (each.Props.GetOrAdd(propertyCollection, () => new XDictionary<string, string>())[property] ?? string.Empty)).ToArray();
            if (values.Length > 0) {
                if (values.All(each => each.Equals(values.FirstOrDefault(), StringComparison.InvariantCultureIgnoreCase))) {
                    return values.FirstOrDefault();
                }
            }
            return null;
        }

    }

    public class Configuration {
        public Configuration(XElement project, string condition) {
            Condition = condition.ToLower();

            // process property groups
            foreach(var propertyGroup in project.Elements("PropertyGroup").Where(each => Condition.Equals( each.AttributeValue("Condition") , StringComparison.InvariantCultureIgnoreCase) || (string.IsNullOrEmpty(condition) && each.AttributeValue("Condition").IsNullOrEmpty()))) {
                // properties.
                foreach(var d in propertyGroup.Descendants()) {

                    switch (d.LocalName()) {
                        case "ConfigurationType"  :
                            switch(d.Value.ToLower()) {
                                case "application":
                                    IsApplication = true;
                                    break;

                                case "dynamiclibrary":
                                    IsLibrary = true;
                                    IsDynamic = true;
                                    break;

                                case "staticlibrary":
                                    IsLibrary = true;
                                    IsStatic = true;
                                    break;
                            } 

                            break;

                            // all other properties, just record them for later.
                        default:
                            Props.GetOrAdd("Properties", () => new XDictionary<string, string>()).AddOrSet(d.LocalName(), d.Value);
                            break;
                    }
                }
            }

            foreach(var idg in project.Elements("ItemDefinitionGroup").Where(each => Condition.Equals(each.AttributeValue("Condition"), StringComparison.InvariantCultureIgnoreCase) || (string.IsNullOrEmpty(condition) && each.AttributeValue("Condition").IsNullOrEmpty()))) {
                foreach (var child in idg.Elements()) {
                    Props.GetOrAdd( child.LocalName(), () => new XDictionary<string,string>()).AddChildren( child.Elements() );
                }
            }
          
        }

        public string Condition;
        public XDictionary<string, XDictionary<string, string>> Props = new XDictionary<string, XDictionary<string, string>>();

        public bool IsDebug {
            get {
                return Condition.Contains("debug") || Condition.Contains("dbg");
            }
        }

        public bool IsRelease {
            get {
                return !IsDebug;
            }
        }

        public bool IsX64 {
            get {
                return Condition.Contains("x64") || Condition.Contains("amd64");
            }
        }

        public bool IsX86 {
            get {
                return Condition.Contains("x86") || Condition.Contains("win32");
            }
        }

        public bool IsArm {
            get {
                return Condition.Contains("arm");
            }
        }

        public bool IsItanium{
            get {
                return Condition.Contains("itanium");
            }
        }

        public bool IsDynamic { get; set; }
        public bool IsStatic { get; set; }
        public bool IsLibrary { get; set;}
        public bool IsApplication { get; set; }
    }
}
