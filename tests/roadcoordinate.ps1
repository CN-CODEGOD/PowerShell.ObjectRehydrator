class roadcoordinate {
[System.Numerics.Vector3]$roadcoordinate1
[System.Numerics.Vector3]$roadcoordinate2

roadcoordinate ($value){
$this.DoInit($value)
}
roadcoordinate ([System.Numerics.Vector3]$roadcoordinate1,[System.Numerics.Vector3]$roadcoordinate2)
{
   $this.roadcoordinate1=$roadcoordinate1
   $this.roadcoordinate2=$roadcoordinate2
}


### validation
[void]DoInit ([pscustomobject]$pscustomobject){
    $pscustomobjectName= (($pscustomobject)|Get-Member -Type NoteProperty ).Name
       foreach($propertyName in $pscustomobjectName ){

          $this.$Propertyname= $pscustomobject.$Propertyname
       }

}

[void] DoInit (


[hashtable]$properties


)
{
   foreach ($property in $properties.key) {

       $this.$property =$properties.$property
   }
}

}

$pscustomobject =[pscustomObject]@{
	roadcoordinate1=[pscustomobject]@{
		x=1
		y=1
		z=1}
	roadcoordinate2=[pscustomObject]@{
		x=3
		y=1
		z=3
	}
}

[roadcoordinate]::new($pscustomobject)
