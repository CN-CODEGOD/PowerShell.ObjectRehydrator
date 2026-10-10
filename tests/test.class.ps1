using namespace System.Xml.Linq
class testInstance {
    [string]$text
    [string]$name

[void] DoInit([pscustomobject]$pscustomobject){
        $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
        foreach($propertyName in $pscustomobjectName ){
            $this.$propertyName = $pscustomobject.$propertyName
        }
    }




    testInstance($object){
        $this.doinit($object)
    }

}

class nestedInstance {
    [testInstance]$testInstance
    [string]$text
    hidden [string]$Path= "$PSScriptRoot/xml/nestedInstance.xml"


[void]Doinit(){

}

[void] DoInit([pscustomobject]$pscustomobject){
        $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
        foreach($propertyName in $pscustomobjectName ){

           $this.$Propertyname= $pscustomobject.$Propertyname
        }
    }


<#
  nestedInstance() {
        $this.testInstance= [testInstance]::new()
        $this.text = 'text'
    }
#>  
     nestedInstance($object){
        $this.doinit($object)
    }
    [object]save(){
        $content=[xelement]::new("object",[XAttribute]::new("name","nestedInstance"),[XObject[]]@([System.Xml.Linq.XElement]::new("property",[XAttribute]::new("name","testInstance"),[XElement]::new("property",[XAttribute]::new("name","txt"),$this.testInstance.txt),[System.Xml.Linq.XElement]::new("property",[XAttribute]::new("name","name"),$this.testInstance.name)),[System.Xml.Linq.XElement]::new("property",[XAttribute]::new("name","text"),$this.text)))
    
    return $content.ToString()
    }
}

class scriptblockInstance {

    [scriptblock]$scriptblock
    [string]$text
    hidden[string]$Path= "$PSScriptRoot/xml/scriptblockInstance.xml"
    
  [void] DoInit() {
        # Empty DoInit method to init new-object
    }
    [void] DoInit([pscustomobject]$pscustomobject){
        $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
        foreach($propertyName in $pscustomobjectName ){

           $this.$Propertyname= $pscustomobject.$Propertyname
        }
    }
    scriptblockInstance() {
        $this.scriptblock= {Write-Host "Hello, World!"}
        $this.text = 'text'
    }
    scriptblockInstance([pscustomobject]$pscustomobject) {
        $this.doinit($pscustomobject)

    }

   [object] save(){
        $scriptblockXml= [XElement]::new("scriptblock", $this.scriptblock.ToString())
        
        $content=[xelement]::new("object",[XAttribute]::new("name","scriptblockInstance"),[XObject[]]@([XElement]::new("property",[XAttribute]::new("name","scriptblock"),$scriptblockXml),[XElement]::new("property",[XAttribute]::new("name","text"),$this.text)))
        return $content.ToString()   
    }
}

class hashtableInstance {
    [hashtable]$hash
    [string]$text
    hidden[string]$path= "$PSScriptRoot/xml/hashobject.xml"
    <# Define the class. Try constructors, properties, or methods. #>

  [void] DoInit() {
        # Empty DoInit method to init new-object
    }
    [void] DoInit(    [pscustomobject]$pscustomobject){
        $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
        foreach($propertyName in $pscustomobjectName ){

           $this.$Propertyname= $pscustomobject.$Propertyname
        }
    }
    hashtableInstance() {
        $this.hash= @{key1="value1"; key2="value2"}
        $this.text = 'text'
    }
    hashtableInstance([pscustomobject]$pscustomobject){

        $this.doinit($pscustomobject)
    }
   [object] save(){
        $hashproperty= ($this.hash.GetEnumerator()) 
        $hashxml=[xelement]::new("hashtable", ($hashproperty | ForEach-Object { [XElement]::new("key", $_.Value, [XAttribute]::new("name", $_.Key)) }))
        $content=[xelement]::new("object",[XAttribute]::new("name","hashtableInstance"),[XObject[]]@([XElement]::new("property",[XAttribute]::new("name","hash"),($hashxml)),[XElement]::new("property",[XAttribute]::new("name","text"),$this.text)))


        return $content.ToString()
    }
}

# ...existing code...

# ...existing code...

class arrayInstance {
    [array]$array
    [string]$text
    hidden[string]$Path= "$PSScriptRoot/xml/array.xml"
    
  [void] DoInit() {
        # Empty DoInit method to init new-object
    }
    [void] DoInit([pscustomobject]$pscustomobject){
        $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
        foreach($propertyName in $pscustomobjectName ){
            $this.$propertyName = $pscustomobject.$propertyName
        }
    }

    arrayInstance() {
        $this.array= @(1, 2, 3, 4, 5)
        $this.text = 'text'
    }
    arrayInstance([pscustomobject]$pscustomobject) {
        $this.doinit($pscustomobject)
    }

    [object] save() {
        $arrayproperty = [XElement]::new("array", ($this.array | ForEach-Object { [XElement]::new("value", $_,[xattribute]::new("index", [array]::IndexOf($this.array, $_))) }))

     $content = [XElement]::new(
    "object",[xattribute]::new("name","arrayInstance"),
    [XObject[]]@([XElement]::new("property", [XAttribute]::new("name","array"), $arrayproperty),
    
        
        [XElement]::new("property", [XAttribute]::new("name","text"), 'Text')
    )
)
        return $content.ToString()
    }
}

# ...existing code...