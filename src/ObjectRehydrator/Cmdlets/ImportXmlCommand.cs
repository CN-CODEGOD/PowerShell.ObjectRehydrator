using System;
using System.IO;
using System.Management.Automation;
using System.Xml.Linq;
using PowerShell.ObjectRehydrator.Rehydration;

namespace PowerShell.ObjectRehydrator.Cmdlets
{
    [Cmdlet(VerbsData.Import, "Xml")]
    [OutputType(typeof(object))]
    public sealed class ImportXmlCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }

        protected override void ProcessRecord()
        {
            string resolved = GetUnresolvedProviderPathFromPSPath(Path);

            if (!File.Exists(resolved))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"XML file not found: {resolved}"),
                    "XmlFileNotFound", ErrorCategory.ObjectNotFound, resolved));
                return;
            }

            XDocument doc = XDocument.Load(resolved);
            if (doc.Root == null) return;

            var typeResolver = new TypeResolver(this.SessionState);
            var rehydrator = new Rehydration.ObjectRehydrator(typeResolver);

            foreach (var objectElement in doc.Root.Elements("object"))
            {
                object instance;
                try
                {
                    instance = rehydrator.Rehydrate(objectElement);
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "RehydrateFailed",
                        ErrorCategory.InvalidData, objectElement));
                    continue;
                }

                if (instance != null)
                    WriteObject(instance);
            }
        }
    }
}
