param(
$path,$inputObject)


if (test-path $path){

rm $path
        }

export-clixml -inputObject $inputObject -path $path

$Rehydrated_object=import-clixml -path $path

if ($rehydrated_object.gettype() -eq $inputObject.gettype()){

return $true
        }

        else{



@{
rehydrated_object=      @{
object =$rehydrated_object
type=$rehydrated_object.gettype()}
inputObject=@{
Object=$inputObject
type=$inputObject.gettype()
}
        }
}

