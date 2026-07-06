$projectRoot = "d:\Unity project\My project 001"
$shootingRoot = Join-Path $projectRoot "Assets\GameData\Shooting\M4"
$inventoryRoot = Join-Path $projectRoot "Assets\GameData\Inventory\M4"
$lootRoot = Join-Path $projectRoot "Assets\Prefabs\World\Loot\M4\Weapons"

function New-GuidNoDash { return ([guid]::NewGuid().ToString('N')) }

function Write-Meta($path, $guid) {
@"

fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@ | Set-Content -Path $path -Encoding utf8
}

function Write-PrefabMeta($path, $guid) {
@"

fileFormatVersion: 2
guid: $guid
PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@ | Set-Content -Path $path -Encoding utf8
}

function Set-Scalar($content, [string]$propName, $value) {
  $pattern = "(?m)^  ${propName}: .+$"
  $replacement = "  ${propName}: $value"
  if ($content -match $pattern) {
    return [regex]::Replace($content, $pattern, $replacement)
  }
  return $content
}

function Set-BasicOpticSlots($content) {
  $slots = @"
  m_AttachmentSlots:
  - SlotType: 0
    IsRequired: 0
    AnchorChildName: 
  - SlotType: 3
    IsRequired: 0
    AnchorChildName: 
"@
  return [regex]::Replace($content, "(?ms)  m_AttachmentSlots:.*?(?=\r?\n  m_FireRateRpm:)", $slots)
}

function Add-SlotProfile($content, $profileIndex) {
  if ($content -match 'm_AttachmentSlotProfile:') {
    return [regex]::Replace($content, 'm_AttachmentSlotProfile: \d+', "m_AttachmentSlotProfile: $profileIndex")
  }
  return $content -replace '(  m_DefaultFireMode: \d+)', "`$1`r`n  m_AttachmentSlotProfile: $profileIndex"
}

$configs = @(
  @{
    Weapon = 'Weapon_M16A_ModA_1'; Item = 'Item_Weapon_M16A_ModA_1'; Loot = 'Loot_Item_Weapon_M16A_ModA_1'
    TemplateWeapon = 'Weapon_M4_ModA_1.asset'; TemplateItem = 'Item_Weapon_M4_ModA_2.asset'; TemplateLoot = 'Loot_Wep_M4_ModA_2.prefab'
    EquippedGuid = '1468d32f053a67d439cb99b7068e6002'; SlotProfile = 3; BasicOptic = $true
    FireModes = '00000000020000000100000003000000'; DefaultFireMode = 0; FireRate = 600; Aim = 0.35; Reload = 2.30; Range = 125; Disp = 0.80
    Recoil = 0.46; SemiRecoil = 0.83; AutoRecoil = 1.12; Recovery = 3.9; Reliability = 0.84; Price = 3000
    LocKey = 'item.weapon.m16a_moda_1'; Desc = 'M16A rifle with carry handle optic. Longer barrel than M4, no stock slot.'
  },
  @{
    Weapon = 'Weapon_M16A4_ModA_2'; Item = 'Item_Weapon_M16A4_ModA_2'; Loot = 'Loot_Item_Weapon_M16A4_ModA_2'
    TemplateWeapon = 'Weapon_M4_ModA_2.asset'; TemplateItem = 'Item_Weapon_M4_ModA_2.asset'; TemplateLoot = 'Loot_Wep_M4_ModA_2.prefab'
    EquippedGuid = '79ec93845dc3de149a91d1e36426c8c5'; SlotProfile = 4; BasicOptic = $false
    FireModes = '00000000020000000100000003000000'; DefaultFireMode = 0; FireRate = 600; Aim = 0.39; Reload = 2.35; Range = 140; Disp = 0.72
    Recoil = 0.43; SemiRecoil = 0.82; AutoRecoil = 1.06; Recovery = 4.3; Reliability = 0.84; Price = 3100
    LocKey = 'item.weapon.m16a4_moda_2'; Desc = 'M16A4 marksman rifle with railed handguard. Full M4 accessory layout except stock.'
  },
  @{
    Weapon = 'Weapon_MK12'; Item = 'Item_Weapon_MK12'; Loot = 'Loot_Item_Weapon_MK12'
    TemplateWeapon = 'Weapon_M4_ModA_2.asset'; TemplateItem = 'Item_Weapon_M4_ModA_2.asset'; TemplateLoot = 'Loot_Wep_M4_ModA_2.prefab'
    EquippedGuid = '7d8da092eb648234c9a4f55ff735c950'; SlotProfile = 0; BasicOptic = $false
    FireModes = '0000000002000000'; DefaultFireMode = 0; FireRate = 450; Aim = 0.50; Reload = 2.50; Range = 160; Disp = 0.56
    Recoil = 0.38; SemiRecoil = 0.80; AutoRecoil = 1.00; Recovery = 4.8; Reliability = 0.86; Price = 3600
    LocKey = 'item.weapon.mk12'; Desc = 'MK12 Mod 1 DMR. Long-barrel 5.56 marksman rifle with full accessory layout.'
  },
  @{
    Weapon = 'Weapon_MK18'; Item = 'Item_Weapon_MK18'; Loot = 'Loot_Item_Weapon_MK18'
    TemplateWeapon = 'Weapon_M4_ModA_2.asset'; TemplateItem = 'Item_Weapon_M4_ModA_2.asset'; TemplateLoot = 'Loot_Wep_M4_ModA_2.prefab'
    EquippedGuid = '4c4ea1f35ab9c3740bde1195ad4f6a2f'; SlotProfile = 0; BasicOptic = $false
    FireModes = '00000000020000000100000003000000'; DefaultFireMode = 1; FireRate = 700; Aim = 0.20; Reload = 1.95; Range = 60; Disp = 1.18
    Recoil = 0.60; SemiRecoil = 0.88; AutoRecoil = 1.50; Recovery = 3.0; Reliability = 0.82; Price = 2750
    LocKey = 'item.weapon.mk18'; Desc = 'MK18 Mod 1 CQB carbine. Short 5.56 rifle with full M4 tactical slots.'
  }
)

$weaponGuids = @{}
$itemGuids = @{}
$lootGuids = @{}

foreach ($cfg in $configs) {
  $weaponGuid = New-GuidNoDash
  $itemGuid = New-GuidNoDash
  $lootGuid = New-GuidNoDash
  $weaponGuids[$cfg.Weapon] = $weaponGuid
  $itemGuids[$cfg.Item] = $itemGuid
  $lootGuids[$cfg.Loot] = $lootGuid

  $weaponPath = Join-Path $shootingRoot "$($cfg.Weapon).asset"
  $weaponContent = Get-Content (Join-Path $shootingRoot $cfg.TemplateWeapon) -Raw
  $weaponContent = $weaponContent -replace 'm_Name: Weapon_M4_ModA_\d', "m_Name: $($cfg.Weapon)"
  $weaponContent = Add-SlotProfile $weaponContent $cfg.SlotProfile
  if ($cfg.BasicOptic) { $weaponContent = Set-BasicOpticSlots $weaponContent }
  $weaponContent = Set-Scalar $weaponContent 'm_AvailableFireModes' $cfg.FireModes
  $weaponContent = Set-Scalar $weaponContent 'm_DefaultFireMode' $cfg.DefaultFireMode
  $weaponContent = Set-Scalar $weaponContent 'm_FireRateRpm' $cfg.FireRate
  $weaponContent = Set-Scalar $weaponContent 'm_AimTimeSeconds' $cfg.Aim
  $weaponContent = Set-Scalar $weaponContent 'm_ReloadTimeSeconds' $cfg.Reload
  $weaponContent = Set-Scalar $weaponContent 'm_EffectiveRangeMeters' $cfg.Range
  $weaponContent = Set-Scalar $weaponContent 'm_BaseShotDispersion' $cfg.Disp
  $weaponContent = Set-Scalar $weaponContent 'm_RecoilPerShot' $cfg.Recoil
  $weaponContent = Set-Scalar $weaponContent 'm_SemiAutoRecoilMultiplier' $cfg.SemiRecoil
  $weaponContent = Set-Scalar $weaponContent 'm_AutoRecoilMultiplier' $cfg.AutoRecoil
  $weaponContent = Set-Scalar $weaponContent 'm_RecoilRecoveryPerSecond' $cfg.Recovery
  $weaponContent = Set-Scalar $weaponContent 'm_Reliability' $cfg.Reliability
  Set-Content -Path $weaponPath -Value $weaponContent -Encoding utf8 -NoNewline
  Write-Meta "$weaponPath.meta" $weaponGuid

  $itemPath = Join-Path $inventoryRoot "$($cfg.Item).asset"
  $itemContent = Get-Content (Join-Path $inventoryRoot $cfg.TemplateItem) -Raw
  $itemContent = $itemContent -replace 'm_Name: Item_Weapon_M4_ModA_2', "m_Name: $($cfg.Item)"
  $itemContent = Set-Scalar $itemContent 'm_LocalizationKey' $cfg.LocKey
  $itemContent = Set-Scalar $itemContent 'm_Description' $cfg.Desc
  $itemContent = Set-Scalar $itemContent 'm_BasePrice' $cfg.Price
  $itemContent = $itemContent -replace 'guid: cb84465c75661054e8fe816361b69ba7', "guid: $weaponGuid"
  $itemContent = $itemContent -replace 'guid: 6fe54d172f46b9646a2876572f0cb7b2', "guid: $($cfg.EquippedGuid)"
  $itemContent = $itemContent -replace 'guid: 91474cca9c712ee408186cd9d73c6c7e', "guid: $lootGuid"
  Set-Content -Path $itemPath -Value $itemContent -Encoding utf8 -NoNewline
  Write-Meta "$itemPath.meta" $itemGuid

  $lootPath = Join-Path $lootRoot "$($cfg.Loot).prefab"
  $lootContent = Get-Content (Join-Path $lootRoot $cfg.TemplateLoot) -Raw
  $lootContent = $lootContent -replace 'm_Name: Loot_Wep_M4_ModA_2', "m_Name: $($cfg.Loot)"
  $lootContent = $lootContent -replace 'guid: 9caa70c7b86a9e441816f7e1e827f2a0', "guid: $itemGuid"
  $lootContent = $lootContent -replace 'guid: cb84465c75661054e8fe816361b69ba7', "guid: $weaponGuid"
  $lootContent = $lootContent -replace 'guid: 6fe54d172f46b9646a2876572f0cb7b2', "guid: $($cfg.EquippedGuid)"
  Set-Content -Path $lootPath -Value $lootContent -Encoding utf8 -NoNewline
  Write-PrefabMeta "$lootPath.meta" $lootGuid
}

$compatWeapons = @(
  'f5b8c44e3f183b14c9d5550a0440a6d4',
  'cb84465c75661054e8fe816361b69ba7'
) + @($weaponGuids.Values)

$compatBlock = ($compatWeapons | ForEach-Object { "  - {fileID: 11400000, guid: $_, type: 2}" }) -join "`r`n"
foreach ($attPath in @('Attachment_M4_Silencer_556.asset','Attachment_M4_MuzzleBrakeM4.asset')) {
  $full = Join-Path $shootingRoot $attPath
  $att = Get-Content $full -Raw
  $att = [regex]::Replace($att, '(?ms)  m_CompatibleWeapons:.*?(?=  m_CompatibleSlots:)', "  m_CompatibleWeapons:`r`n$compatBlock`r`n")
  Set-Content -Path $full -Value $att -Encoding utf8 -NoNewline
}

Set-Content -Path (Join-Path $projectRoot 'Assets\.m4_platform_weapons_build_marker') -Value 'run' -Encoding ascii
Write-Output "Generated $($configs.Count) M4 platform weapons."
$weaponGuids.GetEnumerator() | ForEach-Object { Write-Output "$($_.Key)=$($_.Value)" }
