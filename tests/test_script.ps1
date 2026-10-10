 

param(
$path,$inputObject)
import-module "/root/workspace/PowerShell.ObjectRehydrator/dist/ObjectRehydrator/ObjectRehydrator.psd1" 

if (test-path $path){

rm $path
	}

save-xmlObject -inputObject $inputObject -path $path

$Rehydrated_object=import-xml -path $path

if ($rehydrated_object.gettype() -eq $inputObject.gettype()){

return $true
	}

	else{



@{
rehydrated_object=	@{
object =$rehydrated_object
type=$rehydrated_object.gettype()}
inputObject=@{
Object=$inputObject
type=$inputObject.gettype()
}
	}
}
		
