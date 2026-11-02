#
# Module manifest for module 'ObjectRehydrator'
#
# A binary PowerShell module (compiled DLL) that serializes PowerShell class
# instances to XML and rehydrates them back into real, typed instances.
#
# Load:  Import-Module ./ObjectRehydrator.psd1
#

@{
    # 二进制模块根 DLL（由 dotnet build 产出后放到与本 psd1 同级目录）
    RootModule        = 'ObjectRehydrator.dll'

    ModuleVersion     = '0.2.0'
    GUID              = 'b424f0d2-5b4e-4451-b446-e76d6dc71fae'

    Author            = 'cn_codegod'
    CompanyName       = 'Unknown'
    Copyright         = '(c) cn_codegod. All rights reserved.'

    Description       = 'Serialize PowerShell class instances to XML and rehydrate them back into real, typed instances.'

    # PowerShell 7.x（二进制模块依赖 SDK 7.4.x）
    PowerShellVersion = '7.4'

    # 需要与模块一同加载的依赖程序集（发布时随 psd1 一起拷贝）
    RequiredAssemblies = @()

    # 从 DLL 导出的 cmdlet
    CmdletsToExport   = @(
        'Import-Xml',
        'Save-XmlObject',
        'Export-XmlObject',
        'Save-XmlInstance'
    )

    FunctionsToExport = @()
    VariablesToExport = @()

    # 方案二：暴露全局 `save`（类锁点方法 `save(){ save $this }` 调用）
    AliasesToExport   = @('save')

    PrivateData = @{
        PSData = @{
            Tags       = @('XML', 'Serialization', 'Rehydrate', 'Class')
            ProjectUri = 'https://github.com/CN-CODEGOD/import-xml'
            ReleaseNotes = 'Initial C# port. See /root/workspace/PowerShell.ObjectRehydrator/plan.md'
        }
    }
}
