using System;
using System.IO;
using System.Management.Automation;
using System.Xml.Linq;
using PowerShell.ObjectRehydrator.Serialization;

namespace PowerShell.ObjectRehydrator.Cmdlets
{
    [Cmdlet(VerbsData.Save, "XmlObject")]
    public sealed class SaveXmlObjectCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [AllowNull]
        public object InputObject { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }

        protected override void ProcessRecord()
        {
            string target = Path;

            if (string.IsNullOrEmpty(target))
            {
                var ps = PSObject.AsPSObject(InputObject);
                var pathProp = ps.Properties["Path"];
                if (pathProp != null)
                    target = pathProp.Value?.ToString();
            }

            if (string.IsNullOrEmpty(target))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException("No target path. Pass -Path or give the object a .Path property."),
                    "PathMissing", ErrorCategory.InvalidArgument, InputObject));
                return;
            }

            string resolved = GetUnresolvedProviderPathFromPSPath(target);
            EnsureObjectsFile(resolved);

            XElement node = InstanceSerializer.ToObjectElement(InputObject);

            XDocument doc = XDocument.Load(resolved);
            doc.Root.Add(node);
            doc.Save(resolved);

            string typeName = (string)node.Attribute("type") ?? (string)node.Attribute("name") ?? "object";
            WriteVerbose($"Appended <object type=\"{typeName}\"> to {resolved}");
        }

        private static void EnsureObjectsFile(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
                new XDocument(new XElement("objects")).Save(path);
        }
    }
}
