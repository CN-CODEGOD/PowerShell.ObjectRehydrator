Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1 -Force

# Define a typed enum with byte underlying type
enum ByteEnum : byte {
    Zero = 0
    One = 1
    Two = 2
    Max = 255
}

# Test typed enum
$val = [ByteEnum]::Max
Write-Host "Original typed enum: $val (type: $($val.GetType().FullName), underlying: $([System.Enum]::GetUnderlyingType($val.GetType()).FullName))"

Save-XmlObject -InputObject $val -Path ./tests/xml/typed_enum_test.xml
$xml = Get-Content ./tests/xml/typed_enum_test.xml -Raw
Write-Host "`nSaved XML:"
Write-Host $xml

$rehydrated = Import-Xml -Path ./tests/xml/typed_enum_test.xml
Write-Host "`nRehydrated: $rehydrated (type: $($rehydrated.GetType().FullName))"

if ($rehydrated -eq [ByteEnum]::Max) {
    Write-Host "✓ PASS: Typed enum round-trip successful" -ForegroundColor Green
} else {
    Write-Host "✗ FAIL: Expected Max, got $rehydrated" -ForegroundColor Red
}
