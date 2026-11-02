Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1 -Force

Write-Host "=== Verification Test for GitHub Issues ===" -ForegroundColor Cyan

# Issue #14: Vector2 round-trip
Write-Host "`n[Issue #14] Vector2 round-trip test" -ForegroundColor Yellow
$v2 = [System.Numerics.Vector2]::new(1, 2)
Save-XmlObject -InputObject $v2 -Path ./tests/xml/verify_vector2.xml
$r2 = Import-Xml -Path ./tests/xml/verify_vector2.xml
if ($r2.X -eq 1 -and $r2.Y -eq 2) {
    Write-Host "✓ PASS: Vector2 <$($r2.X), $($r2.Y)>" -ForegroundColor Green
} else {
    Write-Host "✗ FAIL: Vector2 expected <1, 2>, got <$($r2.X), $($r2.Y)>" -ForegroundColor Red
    exit 1
}

# Issue #13: System Enum (DayOfWeek)
Write-Host "`n[Issue #13] System Enum (DayOfWeek) test" -ForegroundColor Yellow
$day = [DayOfWeek]::Friday
Save-XmlObject -InputObject $day -Path ./tests/xml/verify_enum.xml
$rday = Import-Xml -Path ./tests/xml/verify_enum.xml
if ($rday -eq [DayOfWeek]::Friday -and $rday.GetType() -eq [DayOfWeek]) {
    Write-Host "✓ PASS: System enum $rday (type: $($rday.GetType().Name))" -ForegroundColor Green
} else {
    Write-Host "✗ FAIL: Expected Friday (DayOfWeek), got $rday ($($rday.GetType().Name))" -ForegroundColor Red
    exit 1
}

# Issue #13: Typed Enum (byte underlying type)
Write-Host "`n[Issue #13] Typed Enum (byte underlying) test" -ForegroundColor Yellow
enum ByteEnum : byte { Zero = 0; One = 1; Max = 255 }
$bval = [ByteEnum]::Max
Save-XmlObject -InputObject $bval -Path ./tests/xml/verify_typed_enum.xml
$rbval = Import-Xml -Path ./tests/xml/verify_typed_enum.xml
if ($rbval -eq [ByteEnum]::Max -and $rbval.GetType() -eq [ByteEnum]) {
    Write-Host "✓ PASS: Typed enum $rbval (type: $($rbval.GetType().Name))" -ForegroundColor Green
} else {
    Write-Host "✗ FAIL: Expected Max (ByteEnum), got $rbval ($($rbval.GetType().Name))" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== All verification tests passed ===" -ForegroundColor Green
