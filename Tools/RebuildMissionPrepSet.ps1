$inventoryRoot = 'd:\Unity project\My project 001\Assets\GameData\Inventory'
$setPath = 'd:\Unity project\My project 001\Assets\GameData\Inventory\M4\MissionPrepM4AvailableEquipmentSet.asset'

$items = Get-ChildItem -Path $inventoryRoot -Recurse -Filter 'Item_*.asset' | ForEach-Object {
    $meta = Join-Path $_.DirectoryName ($_.BaseName + '.asset.meta')
    $guidLine = Select-String -Path $meta -Pattern '^guid: (\w+)' | Select-Object -First 1
    $guid = $guidLine.Matches[0].Groups[1].Value
    [PSCustomObject]@{ Name = $_.BaseName; Guid = $guid }
}

function Get-SortOrder([string]$name) {
    if ($name -match 'Weapon') { return 0 }
    if ($name -match 'Helmet') { return 1 }
    if ($name -match 'Backpack') { return 2 }
    if ($name -match 'Attachment') { return 3 }
    if ($name -match 'Mag') { return 4 }
    if ($name -match 'Ammo|Loot') { return 5 }
    if ($name -match 'Grenade') { return 6 }
    return 7
}

$sorted = $items | Sort-Object @{ Expression = { Get-SortOrder $_.Name } }, Name
$lines = $sorted | ForEach-Object { "  - {fileID: 11400000, guid: $($_.Guid), type: 2}" }
$content = Get-Content $setPath -Raw
$newBlock = "  m_Items:`r`n" + ($lines -join "`r`n")
$content = [regex]::Replace($content, '(?ms)  m_Items:.*?(?=  m_MagazineAmmo:)', ($newBlock + "`r`n"))
Set-Content -Path $setPath -Value $content -Encoding utf8 -NoNewline

Write-Output "Rebuilt mission prep with $($sorted.Count) items"
$required = @(
    'a273772e13ab49a2b0d63be14fe7ffe1',
    'a2d29810a14d409ba268cd3ccfd79931',
    'a3a7722bed9645e6ba70a6702cd7c1c2',
    'd5689e04549543b4b00cf6df84ba7d29'
)
foreach ($guid in $required) {
    if ($content -match $guid) { Write-Output "Found $guid" }
    else { Write-Output "MISSING $guid"; exit 1 }
}
