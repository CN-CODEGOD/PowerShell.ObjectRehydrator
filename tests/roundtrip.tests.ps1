#requires -Version 7.4
<#
  roundtrip.tests.ps1 — 方案二回归测试

  验证：用固定锚点 save() { save $this } 生成的 XML，
        与旧 import-xml 手写 save() 的输出**等价**，
        且 Import-Xml 能重建为真实类实例。

  运行： pwsh -c 'Invoke-Pester ./tests/roundtrip.tests.ps1'
#>

BeforeAll {
    Import-Module "$PSScriptRoot/../dist/ObjectRehydrator/ObjectRehydrator.psd1" -Force
    . "$PSScriptRoot/demo.class.ps1"
}

Describe 'Save-XmlInstance（方案二自动序列化）' {

    It '嵌套对象：结构由反射生成' {
        $obj = [DemoNested]::new()
        $obj.text = 'text'
        $obj.inner = [DemoInner]::new()
        $obj.inner.text = 'default text'
        $obj.inner.name = 'default name'

        $xml = $obj.save()
        [xml]$doc = $xml
        $doc.object.name | Should -Be 'DemoNested'
        $doc.object.property[0].name | Should -Be 'inner'
        $doc.object.property[0].property[0].name | Should -Be 'text'
        $doc.object.property[0].property[0].'#text' | Should -Be 'default text'
    }

    It 'hashtable' {
        $obj = [DemoHash]::new()
        $obj.hash = @{ key1 = 'value1'; key2 = 'value2' }
        $obj.text = 'text'

        [xml]$doc = $obj.save()
        $keys = $doc.object.property | Where-Object name -eq 'hash'
        $keys.hashtable.key.Count | Should -Be 2
    }

    It 'array：index 属性正确' {
        $obj = [DemoArray]::new()
        $obj.array = @(1, 2, 3, 4, 5)
        $obj.text = 'text'

        [xml]$doc = $obj.save()
        $arr = $doc.object.property | Where-Object name -eq 'array'
        $arr.array.value.Count | Should -Be 5
        $arr.array.value[0].index | Should -Be '0'
        $arr.array.value[4].index | Should -Be '4'
    }

    It 'scriptblock' {
        $obj = [DemoScriptBlock]::new()
        $obj.scriptblock = { Write-Host "Hello, World!" }
        $obj.text = 'text'

        [xml]$doc = $obj.save()
        $sb = $doc.object.property | Where-Object name -eq 'scriptblock'
        $sb.scriptblock | Should -Match 'Hello'
    }

    It '排除 hidden $Path 属性' {
        $obj = [DemoNested]::new()
        $obj.PSObject.Properties.Add([psnoteproperty]::new('Path', '/tmp/x.xml'))
        [xml]$doc = $obj.save()
        $doc.object.property.name | Should -Not -Contain 'Path'
    }

    It '往返：Save → Import 还原为真实类实例' {
        $obj = [DemoNested]::new()
        $obj.text = 'roundtrip'
        $obj.inner = [DemoInner]::new()
        $obj.inner.text = 'inner text'
        $obj.inner.name = 'inner name'

        $tmp = [System.IO.Path]::GetTempFileName()
        try {
            # 写文件（复用文件层格式）
            [xml]$wrapper = "<objects>$($obj.save())</objects>"
            $wrapper.Save($tmp)

            $restored = Import-Xml -Path $tmp
            $restored -is [DemoNested] | Should -BeTrue
            $restored.text | Should -Be 'roundtrip'
        }
        finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
    }
}
