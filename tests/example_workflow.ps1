class workflow  {
    [string]$name 
    [scriptblock]$scriptblock
    [string]$result
    [string]$desc


    
        
       [void]Doinit([pscustomObject]$pscustomObject){
$pscustomObjectName=(($pscustomObject)|Get-Member -type NoteProperty).Name 
foreach($propertyName in $pscustomObjectName  ){
    $this.$propertyName = $pscustomObject.$propertyName
}

       }

workflow([pscustomobject]$pscustomObject){
    $this.Doinit($pscustomObject)


    }

    [string]Excute(){
    $output=$this.scriptblock.Invoke()
    return $output.ToString()

    }
}
$pscustomobject=[pscustomobject]@{

	name="exampleWorkflow"
	scriptblock={write 123}
	desc="a example workflow Instance for tesing the Powershell.ObjectRehydrator"}


$workflow=[workflow]::new($pscustomObject)
$workflow
