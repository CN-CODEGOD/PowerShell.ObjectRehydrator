# ObjectRehydrator v0.2.0 Release

## Quick Start

1. Copy the `ObjectRehydrator.dll` and `ObjectRehydrator.psd1` files to your module directory
2. Import the module:
   ```powershell
   Import-Module ./ObjectRehydrator.psd1
   ```

## What's Fixed

### Vector2 / Vector3 round-trip
- Fixed field-based types (Vector2.X/Y, Vector3.X/Y/Z) being zeroed out during deserialization
- Root cause: `Populate()` only used `GetProperty()`, but Vector members are public **fields**

### Null support
- `Save-XmlObject -InputObject $null` now works (added `[AllowNull()]` to cmdlet parameters)
- Null values serialize to `<object name="null" type="null" null="true"/>` and rehydrate correctly

### System Enum & typed Enum
- `[DayOfWeek]::Friday` and custom enums with explicit underlying types (e.g., `enum X : byte`) round-trip correctly

### Generic collections
- `Dictionary<K,V>`, `List<T>`, `Queue<T>`, `HashSet<T>`, `Stack<T>`, `LinkedList<T>`
- `SortedDictionary<K,V>`, `ConcurrentDictionary<K,V>`

### Other types
- DateTime, Guid, Version, TimeSpan, ScriptBlock, PSCustomObject, Hashtable, ArrayList

## Test Results
All 20/20 tests passing in `test.required.ps1`

## Example Usage

```powershell
# Vector2
$v = [System.Numerics.Vector2]::new(1, 2)
Save-XmlObject $v -path vector2.xml
$result = Import-Xml -path vector2.xml
# $result is <1, 2>

# Enum
$day = [DayOfWeek]::Friday
Save-XmlObject $day -path enum.xml
$result = Import-Xml -path enum.xml
# $result is Friday

# Null
Save-XmlObject $null -path null.xml
$result = Import-Xml -path null.xml
# $result is $null
```
