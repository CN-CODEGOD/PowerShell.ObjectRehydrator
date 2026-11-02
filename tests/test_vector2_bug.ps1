Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1 -Force

# Test the exact scenario from the bug report: save-XmlObject followed by Import-Xml
$v = [System.Numerics.Vector2]::new(1, 2)
Write-Host "Original Vector2: $v"
Write-Host "X=$($v.X) Y=$($v.Y)"

# Save using Save-XmlObject
Save-XmlObject -InputObject $v -Path ./tests/xml/vector2_bug.xml

# Read back the XML
$xml = Get-Content ./tests/xml/vector2_bug.xml -Raw
Write-Host "`nSaved XML:"
Write-Host $xml

# Import using Import-Xml
$rehydrated = Import-Xml -Path ./tests/xml/vector2_bug.xml
Write-Host "`nRehydrated Vector2: $rehydrated"
Write-Host "X=$($rehydrated.X) Y=$($rehydrated.Y)"

# Verify
if ($rehydrated.X -eq 1 -and $rehydrated.Y -eq 2) {
    Write-Host "`n✓ PASS: Vector2 round-trip successful" -ForegroundColor Green
} else {
    Write-Host "`n✗ FAIL: Vector2 corrupted during round-trip" -ForegroundColor Red
    exit 1
}
