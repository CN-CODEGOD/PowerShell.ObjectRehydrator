using System;
using System.Management.Automation;
using PowerShell.ObjectRehydrator.Serialization;

namespace PowerShell.ObjectRehydrator.Cmdlets
{
    /// <summary>
    /// Save-XmlInstance
    ///
    /// 方案二入口：反射类实例，**自动生成** XML 片段。
    /// 用户类只需：
    ///
    ///     [object] save() { save $this }
    ///
    /// 本 cmdlet 即被调用的全局 <c>save</c>（通过 psd1 的别名 save 暴露）。
    ///
    /// 输出为 <c>&lt;object name="类型名"&gt;...</c> 字符串（不含 id）。
    /// </summary>
    [Cmdlet(VerbsData.Save, "XmlInstance")]
    [Alias("save")]
    [OutputType(typeof(string))]
    public sealed class SaveXmlInstanceCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [ValidateNotNull]
        public PSObject InputObject { get; set; }

        [Parameter]
        public SwitchParameter AsXElement { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                if (AsXElement)
                {
                    WriteObject(InstanceSerializer.ToObjectElement(InputObject));
                }
                else
                {
                    WriteObject(InstanceSerializer.Save(InputObject));
                }
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
