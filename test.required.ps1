beforeAll {
$module = "$PSScriptRoot/dist/ObjectRehydrator/ObjectRehydrator.psd1"
import-module $module
   . ./tests/example_class.ps1

    . ./tests/example_workflow.ps1
    

}


describe "test Environment"{

	context "test if the environment was in fixed with develop"{

it "test the module is loaded"{
(get-command import-xml)|should -not -benullorEmpty
	}

it "test pester Version"{

$version =(get-module -name pester).version.Major
$version |should -BeGreaterOrEqual 6
	}

it 'test [workflow] is loaded'{

([workflow]).name|should -be "workflow"

	}

it "test [exampleInstance] is loaded"{

([exampleInstance]).name|should -be "exampleInstance"

	}
	}
}
describe "run through" {


beforeEach {
$roadcoordinate =./test/roadcoordinate.ps1
$vector= 


$array = (1, 2, 3)
        $string = "123"
        $int = 123
        $hashtable = @{
            1 = 1
            2 = 2
            3 = 3
        }
        $scriptblock = { write "123" }


	$exampleInstance=[exampleInstance]::new()

        $pscustomObject =[pscustomobject]@{
            name        = "example_workflow"
            scriptblock = { write 123 }
            desc        = "example workflow"
        }

        $workflow = [workflow]::new($pscustomObject)
    }

    context "测试powershell 常见的弱类" {

        it 'test save /import array' {
            $array = (1, 2, 3)
            save-xmlobject -inputObject $array -path tests/xml/array.xml
            $rehydrated_object = import-xml tests/xml/array.xml
            
            
	    $rehydrated_object |should -be (1,2,3)
        }

        it 'test save/import string' {
            $string = "123"
            save-xmlobject -inputObject $string -path tests/xml/string.xml
            $rehydrated_object= import-xml tests/xml/string.xml
            
            $rehydrated_object| should -beoftype ([string])
        }

        it "test save/import int" {
            $int = 123
            save-xmlobject -inputObject $int -path tests/xml/int.xml
            $rehydrated_object = import-xml -path tests/xml/int.xml
	     $rehydrated_object |should -beoftype ([int])

	}

        it 'test save/import hashtable' {
            $hashtable = @{
                1 = 1
                2 = 2
            }
            save-xmlobject -inputObject $hashtable -path tests/xml/hashtable.xml
            $rehydrated_object = import-xml tests/xml/hashtable.xml
            
            $rehydrated_object| should -beoftype ([hashtable])
        }
    }



    
    context "测试强类" {

        it "test with a workflow instance" {
		#verify the object 
		$workflow.name |should -not -benullorEmpty
		$workflow |should -beoftype ([workflow])

            save-xmlobject -inputObject $workflow -path tests/xml/workflow.xml
            $rehydrated_object = import-xml tests/xml/workflow.xml
            
            $rehydrated_object |should -beoftype ([workflow])

            $workflow | should -not -be $null
	    
        }

       it "test with a exampleInstance"{
       # verify the object 
       $ExampleInstance |should -not -benullOrEmpty

$exampleInstance|should -beoftype ([exampleInstance])
save-xmlobject -inputObject $exampleInstance -path tests/xml/exampleInstance.xml
$rehydrated_object = import-xml tests/xml/exampleInstance.xml
  $rehydrated_object |should -beoftype ([exampleInstance])

	       }
     	
    }





# 以上的都修好，如果出现复发请重新参考旧代码，并分析出错的原因


  context "测试已经知道的bug" {

        it "test the known bug of saving the scriptblock object" {
            $scriptblock = { write-host 1 }
            save-xmlobject -inputObject $scriptblock -path tests/xml/scriptblock.xml
        }

	it "test the roadInstance was initilize successfully" {
	
		}
    }



    afterAll {
        # 只清理本测试生成的 xml，不要整个删除 tests 目录
        rm -rf tests/xml/ 
        mkdir tests/xml     
    }
}
