#requires -Version 7.4
<#
  demo.class.ps1 — 方案二的示例类
  =================================

  使用 .NET 静态方法调用代替 cmdlet，绕过 PowerShell class 内
  模块 cmdlet 解析限制。

  旧（手写结构）：
      [object] save() { ... 一大坨 XElement::new ... }

  新（固定锚点 + .NET 静态调用）：
      [object] save() {
          [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
      }
#>

class DemoNested {
    [DemoInner]$inner
    [string]$text

    [object] save() {
        return [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
    }

    [void] DoInit([pscustomobject]$o) {
        foreach ($n in ($o | Get-Member -Type NoteProperty).Name) {
            $this.$n = $o.$n
        }
    }
    DemoNested([pscustomobject]$o) { $this.DoInit($o) }
    DemoNested() {}
}

class DemoInner {
    [string]$text
    [string]$name

    [void] DoInit([pscustomobject]$o) {
        foreach ($n in ($o | Get-Member -Type NoteProperty).Name) {
            $this.$n = $o.$n
        }
    }
    DemoInner([pscustomobject]$o) { $this.DoInit($o) }
    DemoInner() {}
}

class DemoHash {
    [hashtable]$hash
    [string]$text
    [object] save() {
        return [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
    }
}

class DemoArray {
    [array]$array
    [string]$text
    [object] save() {
        return [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
    }
}

class DemoScriptBlock {
    [scriptblock]$scriptblock
    [string]$text
    [object] save() {
        return [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
    }
}
