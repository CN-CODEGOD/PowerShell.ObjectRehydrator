Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1 -Force

# Test system enum
$day = [DayOfWeek]::Friday
Write-Host "Original enum: $day (type: $($day.GetType().FullName))"

Save-XmlObject -InputObject $day -Path ./tests/xml/enum_test.xml
$xml = Get-Content ./tests/xml/enum_test.xml -Raw
Write-Host "`nSaved XML:"
Write-Host $xml

$rehydrated = Import-Xml -Path ./tests/xml/enum_test.xml
Write-Host "`nRehydrated: $rehydrated (type: $($rehydrated.GetType().FullName))"

if ($rehydrated -eq [DayOfWeek]::Friday) {
    Write-Host "✓ PASS: System enum round-trip successful" -ForegroundColor Green
} else {
    Write-Host "✗ FAIL: Expected Friday, got $rehydrated" -ForegroundColor Red
}
