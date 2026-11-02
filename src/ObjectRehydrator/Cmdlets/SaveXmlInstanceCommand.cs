using System;
using System.Management.Automation;
using PowerShell.ObjectRehydrator.Serialization;

namespace PowerShell.ObjectRehydrator.Cmdlets
{
    [Cmdlet(VerbsData.Save, "XmlInstance")]
    [Alias("save")]
    [OutputType(typeof(string))]
    public sealed class SaveXmlInstanceCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [AllowNull]
        public object InputObject { get; set; }

        [Parameter]
        public SwitchParameter AsXElement { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                if (AsXElement)
                    WriteObject(InstanceSerializer.ToObjectElement(InputObject));
                else
                    WriteObject(InstanceSerializer.Save(InputObject));
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "SerializeInstanceFailed",
                    ErrorCategory.InvalidData, InputObject));
            }
        }
    }
}
