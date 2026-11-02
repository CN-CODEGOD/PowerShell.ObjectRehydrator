using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml.Linq;
using PowerShell.ObjectRehydrator.Serialization;

namespace PowerShell.ObjectRehydrator.Cmdlets
{
    [Cmdlet(VerbsData.Export, "XmlObject")]
    public sealed class ExportXmlObjectCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [AllowNull]
        public object InputObject { get; set; }

        [Parameter(Mandatory = true, Position = 1)]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }

        private XElement _root;
        private string _resolved;

        protected override void BeginProcessing()
        {
            _resolved = GetUnresolvedProviderPathFromPSPath(Path);
            var dir = System.IO.Path.GetDirectoryName(_resolved);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _root = new XElement("objects");
        }

        protected override void ProcessRecord()
        {
            XElement node = InstanceSerializer.ToObjectElement(InputObject);
            _root.Add(node);
        }

        protected override void EndProcessing()
        {
            new XDocument(_root).Save(_resolved);
            WriteVerbose($"Wrote {_root.Elements("object").Count()} object(s) to {_resolved}");
        }
    }
}
