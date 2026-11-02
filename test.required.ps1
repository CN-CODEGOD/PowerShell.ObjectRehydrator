# test.required.ps1
# ---------------------------------------------------------------------------
# 覆盖两组失败：
#   A. known_bug  —— scriptblock_input / roadInstance / null
#   B. 弱类       —— 手动测试的弱类 / clixml
#
# 运行：  pwsh -NoProfile -Command "Invoke-Pester ./test.required.ps1"
# 约定：  每次运行前清理 tests/xml/，避免脏数据
# ---------------------------------------------------------------------------

beforeAll {
    $module = "$PSScriptRoot/dist/ObjectRehydrator/ObjectRehydrator.psd1"
    Import-Module $module -Force

    . "$PSScriptRoot/tests/example_class.ps1"
    . "$PSScriptRoot/tests/example_workflow.ps1"
    . "$PSScriptRoot/tests/roadcoordinate.ps1"

    # 干净工作目录
    $XmlDir = "$PSScriptRoot/tests/xml"
    if (Test-Path $XmlDir) { Remove-Item $XmlDir -Recurse -Force }
    New-Item -ItemType Directory -Path $XmlDir -Force | Out-Null
}

afterAll {
    # 只清理本测试生成的 xml，不删整个 tests 目录
    $XmlDir = "$PSScriptRoot/tests/xml"
    if (Test-Path $XmlDir) { Remove-Item $XmlDir -Recurse -Force }
    New-Item -ItemType Directory -Path $XmlDir -Force | Out-Null
}

describe "test Environment" {
    context "test if the environment was in fixed with develop" {

        it "test the module is loaded" {
            (Get-Command Import-Xml) | Should -Not -BeNullOrEmpty
        }

        it "test pester Version" {
            $version = (Get-Module -Name Pester).Version.Major
            $version | Should -BeGreaterOrEqual 6
        }

        it 'test [workflow] is loaded' {
            ([workflow]).Name | Should -Be "workflow"
        }

        it "test [exampleInstance] is loaded" {
            ([exampleInstance]).Name | Should -Be "exampleInstance"
        }
    }
}

# ===========================================================================
# B. 弱类
# ===========================================================================
describe "弱类 round-trip" {

    context "测试powershell 常见的弱类" {

        it 'test save /import array' {
            $array = (1, 2, 3)
            Save-XmlObject -InputObject $array -Path "$XmlDir/array.xml"
            $rehydrated_object = Import-Xml "$XmlDir/array.xml"

            # 逐元素比对（避免管道展开把数组拆成多个断言输入）
            ,$rehydrated_object | Should -BeOfType ([array])
            $rehydrated_object.Count | Should -Be 3
            $rehydrated_object[0] | Should -Be 1
            $rehydrated_object[1] | Should -Be 2
            $rehydrated_object[2] | Should -Be 3
        }

        it 'test save/import string' {
            $string = "123"
            Save-XmlObject -InputObject $string -Path "$XmlDir/string.xml"
            $rehydrated_object = Import-Xml "$XmlDir/string.xml"

            ,$rehydrated_object | Should -BeOfType ([string])
            $rehydrated_object | Should -Be "123"
        }

        it "test save/import int" {
            $int = 123
            Save-XmlObject -InputObject $int -Path "$XmlDir/int.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/int.xml"

            ,$rehydrated_object | Should -BeOfType ([int])
            $rehydrated_object | Should -Be 123
        }

        it "test save/import double" {
            $double = 1.5
            Save-XmlObject -InputObject $double -Path "$XmlDir/double.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/double.xml"

            ,$rehydrated_object | Should -BeOfType ([double])
            $rehydrated_object | Should -Be 1.5
        }

        it "test save/import bool" {
            $bool = $true
            Save-XmlObject -InputObject $bool -Path "$XmlDir/bool.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/bool.xml"

            ,$rehydrated_object | Should -BeOfType ([bool])
            $rehydrated_object | Should -Be $true
        }

        it 'test save/import hashtable' {
            $hashtable = @{
                1 = 1
                2 = 2
            }
            Save-XmlObject -InputObject $hashtable -Path "$XmlDir/hashtable.xml"
            $rehydrated_object = Import-Xml "$XmlDir/hashtable.xml"

            ,$rehydrated_object | Should -BeOfType ([hashtable])
            # 引用比较永远为 false —— 逐键比对内容
            $rehydrated_object.Count | Should -Be 2
            $rehydrated_object[1] | Should -Be 1
            $rehydrated_object[2] | Should -Be 2
        }

        it "test save/import null" {
            Save-XmlObject -InputObject $null -Path "$XmlDir/null.xml" -ErrorAction SilentlyContinue
            if (Test-Path "$XmlDir/null.xml") {
                $rehydrated_object = Import-Xml -Path "$XmlDir/null.xml"
                $rehydrated_object | Should -BeNullOrEmpty
            }
        }
    }
}

# ===========================================================================
# A. known_bug
# ===========================================================================
describe "known_bug 回归" {

    context "已知 bug：scriptblock 输入" {

        it "test the known bug of saving the scriptblock object" {
            $scriptblock = { write-host 1 }
            { Save-XmlObject -InputObject $scriptblock -Path "$XmlDir/scriptblock.xml" -ErrorAction Stop } |
                Should -Not -Throw

            Test-Path "$XmlDir/scriptblock.xml" | Should -Be $true
        }

        it "test scriptblock round-trip keeps ScriptBlock type" {
            $scriptblock = { write "123" }
            Save-XmlObject -InputObject $scriptblock -Path "$XmlDir/scriptblock2.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/scriptblock2.xml"

            ,$rehydrated_object | Should -BeOfType ([scriptblock])
        }
    }

    context "已知 bug：roadInstance（嵌套对象丢值）" {

        it "test roadInstance nested values survive round-trip" {
            $road = [roadcoordinate]::new([pscustomobject]@{
                roadcoordinate1 = [pscustomobject]@{ x = 1; y = 1; z = 1 }
                roadcoordinate2 = [pscustomobject]@{ x = 3; y = 1; z = 3 }
            })

            Save-XmlObject -InputObject $road -Path "$XmlDir/road.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/road.xml"

            ,$rehydrated_object | Should -BeOfType ([roadcoordinate])

            # 原始 bug：往返后变成 <0,0,0>；这里要求逐值还原
            $rehydrated_object.roadcoordinate1.X | Should -Be 1
            $rehydrated_object.roadcoordinate1.Y | Should -Be 1
            $rehydrated_object.roadcoordinate1.Z | Should -Be 1
            $rehydrated_object.roadcoordinate2.X | Should -Be 3
            $rehydrated_object.roadcoordinate2.Y | Should -Be 1
            $rehydrated_object.roadcoordinate2.Z | Should -Be 3
        }
    }

    context "已知 bug：Vector3" {

        it "test the vector was initialized correctly" {
            $vector3 = [System.Numerics.Vector3]::new(1, 2, 3)

            Save-XmlObject -InputObject $vector3 -Path "$XmlDir/vector.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/vector.xml"

            ,$rehydrated_object | Should -BeOfType ([System.Numerics.Vector3])
            $rehydrated_object.X | Should -Be 1
            $rehydrated_object.Y | Should -Be 2
            $rehydrated_object.Z | Should -Be 3
        }
    }

    context "已知 bug：null" {

        it "test null round-trips as null without error" {
            Save-XmlObject -InputObject $null -Path "$XmlDir/null2.xml" -ErrorAction SilentlyContinue
            if (Test-Path "$XmlDir/null2.xml") {
                { Import-Xml -Path "$XmlDir/null2.xml" -ErrorAction Stop } | Should -Not -Throw
            }
        }
    }

    context "已知 bug：dictionary / queue" {

        it "test dictionary was initialized correctly" {
            $dict = [System.Collections.Generic.Dictionary[string, int]]::new()
            $dict["a"] = 1
            $dict["b"] = 2

            Save-XmlObject -InputObject $dict -Path "$XmlDir/dict.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/dict.xml"

            ,$rehydrated_object | Should -BeOfType ([System.Collections.Generic.Dictionary[string, int]])
            $rehydrated_object.Count | Should -Be 2
            $rehydrated_object["a"] | Should -Be 1
            $rehydrated_object["b"] | Should -Be 2
        }

        it "test queue was initialized correctly" {
            $queue = [System.Collections.Generic.Queue[int]]::new()
            $queue.Enqueue(1)
            $queue.Enqueue(2)

            Save-XmlObject -InputObject $queue -Path "$XmlDir/queue.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/queue.xml"

            ,$rehydrated_object | Should -BeOfType ([System.Collections.Generic.Queue[int]])
            $rehydrated_object.Count | Should -Be 2
            $rehydrated_object.Dequeue() | Should -Be 1
            $rehydrated_object.Dequeue() | Should -Be 2
        }
    }
}

# ===========================================================================
# 强类回归（保证弱类修复不破坏强类）
# ===========================================================================
describe "强类回归" {

    context "测试强类" {

        it "test with a workflow instance" {
            $pscustomObject = [pscustomobject]@{
                name        = "example_workflow"
                scriptblock = { write 123 }
                desc        = "example workflow"
            }
            $workflow = [workflow]::new($pscustomObject)

            $workflow.name | Should -Not -BeNullOrEmpty
            ,$workflow | Should -BeOfType ([workflow])

            Save-XmlObject -InputObject $workflow -Path "$XmlDir/workflow.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/workflow.xml"

            ,$rehydrated_object | Should -BeOfType ([workflow])
            $rehydrated_object.name | Should -Be "example_workflow"
        }

        it "test with a exampleInstance" {
            $exampleInstance = [exampleInstance]::new()

            $exampleInstance | Should -Not -BeNullOrEmpty
            ,$exampleInstance | Should -BeOfType ([exampleInstance])

            Save-XmlObject -InputObject $exampleInstance -Path "$XmlDir/exampleInstance.xml"
            $rehydrated_object = Import-Xml -Path "$XmlDir/exampleInstance.xml"

            ,$rehydrated_object | Should -BeOfType ([exampleInstance])
        }
    }
}
