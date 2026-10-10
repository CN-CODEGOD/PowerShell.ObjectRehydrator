class exampleInstance{

[string]$name
[int]$num
[scriptblock]$scriptblock

[void]Doinit(){
$this.name="testInstance"
$this.num=123
$this.scriptblock={write-host 1}
}


exampleInstance (){
$this.Doinit()
	}
}	

	[exampleInstance]::new()
