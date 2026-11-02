Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1 -Force

$v = [System.Numerics.Vector2]::new(1, 2)
Write-Host "Original: $v"
Write-Host "X=$($v.X) Y=$($v.Y)"

Save-XmlObject -InputObject $v -Path ./tests/xml/vector2_test.xml
$xml = Get-Content ./tests/xml/vector2_test.xml -Raw
Write-Host "XML:"
Write-Host $xml

$rehydrated = Import-Xml -Path ./tests/xml/vector2_test.xml
Write-Host "`nRehydrated: $rehydrated"
Write-Host "X=$($rehydrated.X) Y=$($rehydrated.Y)"
