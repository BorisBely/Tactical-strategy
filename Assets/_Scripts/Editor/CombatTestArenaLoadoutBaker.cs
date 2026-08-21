#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fills <see cref="CombatTestArenaSpawner"/> with polygon-style weapon kits:
/// player US/M-line, enemy Mosin/SVD/PKM + AK series, magazines/ammo matched to caliber.
/// </summary>
public static class CombatTestArenaLoadoutBaker
{
	#region Constants
	private const string c_Inv = "Assets/GameData/Inventory/";
	private const string c_M4 = c_Inv + "M4/";
	private const string c_Ak = c_Inv + "AK/";
	private const string c_Stand = c_Inv + "Standalone/";
	private const string c_Ammo = "Assets/GameData/Shooting/";
	#endregion

	#region Public Methods
	public static bool Apply(CombatTestArenaSpawner _spawner)
	{
		if (_spawner == null)
			return false;

		ItemDefinition backpack = LoadItem(c_Inv + "Backpacks/Item_Backpack_1.asset");
		ItemDefinition ifak = LoadItem(c_Inv + "Medical/Item_IFAK.asset");
		ItemDefinition[] grenades =
		{
			LoadItem(c_Inv + "Grenades/Item_Grenade_Frag_01.asset"),
			LoadItem(c_Inv + "Grenades/Item_Grenade_F1.asset"),
			LoadItem(c_Inv + "Grenades/Item_Grenade_RGD5.asset"),
			LoadItem(c_Inv + "Grenades/Item_Grenade_Flash_01.asset"),
			LoadItem(c_Inv + "Grenades/Item_Grenade_Smoke_01.asset")
		};
		ItemDefinition[] helmets =
		{
			LoadItem(c_Inv + "Helmets/Item_Helmet_1_Kevlar.asset"),
			LoadItem(c_Inv + "Helmets/Item_Helmet_2_Kevlar_Mod.asset"),
			LoadItem(c_Inv + "Helmets/Item_Helmet_3_Tactical.asset"),
			LoadItem(c_Inv + "Helmets/Item_Helmet_4_Crew.asset")
		};

		AmmoDefinition ammo556 = LoadAmmo(c_Ammo + "Ammo_556x45mmNATO.asset");
		AmmoDefinition ammo762Nato = LoadAmmo(c_Ammo + "Ammo_762x51mmNATO.asset");
		AmmoDefinition ammo12g = LoadAmmo(c_Ammo + "Ammo_12Gauge.asset");
		AmmoDefinition ammo762x39 = LoadAmmo(c_Ammo + "Ammo_762x39mm.asset");
		AmmoDefinition ammo545 = LoadAmmo(c_Ammo + "Ammo_545x39mm.asset");
		AmmoDefinition ammo54r = LoadAmmo(c_Ammo + "Ammo_762x54mmR.asset");

		ItemDefinition magM4_30 = LoadItem(c_M4 + "Item_Mag_M4_556_30.asset");
		ItemDefinition magM4_20 = LoadItem(c_M4 + "Item_Mag_M4_556_20.asset");
		ItemDefinition magM249 = LoadItem(c_Stand + "Item_Mag_M249_556_200.asset");
		ItemDefinition magSniper = LoadItem(c_Stand + "Item_Mag_Sniper_762x51_10.asset");
		ItemDefinition magMosin = LoadItem(c_Stand + "Item_Mag_Mosin_762_54R_5.asset");
		ItemDefinition magSvd = LoadItem(c_Stand + "Item_Mag_SVD_762_54R_10.asset");
		ItemDefinition magPkm = LoadItem(c_Stand + "Item_Mag_PKM_762_54R_100.asset");
		ItemDefinition magAk762 = LoadItem(c_Ak + "Item_Mag_AK_762_30.asset");
		ItemDefinition magAk545 = LoadItem(c_Ak + "Item_Mag_AK_545_30.asset");
		ItemDefinition magRpk762 = LoadItem(c_Ak + "Item_Mag_AK_762_75.asset");
		ItemDefinition magRpk545 = LoadItem(c_Ak + "Item_Mag_AK_545_45.asset");

		ItemDefinition box556 = LoadItem(c_Inv + "Item_Loot_AmmoBox_556NATO.asset");
		ItemDefinition box762Nato = LoadItem(c_Stand + "Item_Loot_AmmoBox_762x51.asset");
		ItemDefinition box12g = LoadItem(c_Stand + "Item_Loot_AmmoBox_12Gauge.asset");
		ItemDefinition box762x39 = LoadItem(c_Inv + "Item_Loot_AmmoBox_762x39.asset");
		ItemDefinition box545 = LoadItem(c_Inv + "Item_Loot_AmmoBox_545x39.asset");
		ItemDefinition box54r = LoadItem(c_Stand + "Item_Loot_AmmoBox_762x54R.asset");

		CombatTestArenaWeaponKit[] playerUnique =
		{
			Kit(CombatTestWeaponRole.Sniper, "Sniper762", c_Stand + "Item_Weapon_Sniper762x51.asset", magSniper, ammo762Nato, box762Nato, 4, 2),
			Kit(CombatTestWeaponRole.MachineGun, "M249", c_Stand + "Item_Weapon_M249.asset", magM249, ammo556, box556, 3, 2),
			Kit(CombatTestWeaponRole.Rifle, "M4", c_M4 + "Item_Weapon_M4_ModA_1.asset", magM4_30, ammo556, box556, 10, 2),
			Kit(CombatTestWeaponRole.Shotgun, "Benelli", c_Stand + "Item_Weapon_BenelliM4.asset", null, ammo12g, box12g, 0, 6),
			Kit(CombatTestWeaponRole.Marksman, "MK12", c_M4 + "Item_Weapon_MK12.asset", magM4_20, ammo556, box556, 8, 2)
		};
		CombatTestArenaWeaponKit[] playerFill =
		{
			Kit(CombatTestWeaponRole.Rifle, "M4_ModA2", c_M4 + "Item_Weapon_M4_ModA_2.asset", magM4_30, ammo556, box556, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "M16A", c_M4 + "Item_Weapon_M16A_ModA_1.asset", magM4_30, ammo556, box556, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "M16A4", c_M4 + "Item_Weapon_M16A4_ModA_2.asset", magM4_30, ammo556, box556, 10, 2),
			Kit(CombatTestWeaponRole.Carbine, "MK18", c_M4 + "Item_Weapon_MK18.asset", magM4_30, ammo556, box556, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "M4", c_M4 + "Item_Weapon_M4_ModA_1.asset", magM4_30, ammo556, box556, 10, 2)
		};

		CombatTestArenaWeaponKit[] enemyUnique =
		{
			Kit(CombatTestWeaponRole.Sniper, "Mosin", c_Stand + "Item_Weapon_Mosin.asset", magMosin, ammo54r, box54r, 4, 2),
			Kit(CombatTestWeaponRole.Marksman, "SVD", c_Stand + "Item_Weapon_SVD.asset", magSvd, ammo54r, box54r, 6, 2),
			Kit(CombatTestWeaponRole.MachineGun, "PKM", c_Stand + "Item_Weapon_PKM.asset", magPkm, ammo54r, box54r, 3, 2)
		};
		CombatTestArenaWeaponKit[] enemyFill =
		{
			Kit(CombatTestWeaponRole.Rifle, "AK47", c_Ak + "Item_Weapon_AK47.asset", magAk762, ammo762x39, box762x39, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "AK47_1", c_Ak + "Item_Weapon_AK47_1.asset", magAk762, ammo762x39, box762x39, 10, 2),
			Kit(CombatTestWeaponRole.Carbine, "AK47S", c_Ak + "Item_Weapon_AK47S.asset", magAk762, ammo762x39, box762x39, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "AK47MOD1", c_Ak + "Item_Weapon_AK47MOD1.asset", magAk762, ammo762x39, box762x39, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "AK74", c_Ak + "Item_Weapon_AK74.asset", magAk545, ammo545, box545, 10, 2),
			Kit(CombatTestWeaponRole.Rifle, "AK74MOD1", c_Ak + "Item_Weapon_AK74MOD1.asset", magAk545, ammo545, box545, 10, 2),
			Kit(CombatTestWeaponRole.Carbine, "AK74U", c_Ak + "Item_Weapon_AK74U.asset", magAk545, ammo545, box545, 10, 2),
			Kit(CombatTestWeaponRole.Carbine, "AK74UMOD1", c_Ak + "Item_Weapon_AK74UMOD1.asset", magAk545, ammo545, box545, 10, 2),
			Kit(CombatTestWeaponRole.LightMachineGun, "RPK47", c_Ak + "Item_Weapon_RPK47.asset", magRpk762, ammo762x39, box762x39, 4, 2),
			Kit(CombatTestWeaponRole.LightMachineGun, "RPK47MOD1", c_Ak + "Item_Weapon_RPK47MOD1.asset", magRpk762, ammo762x39, box762x39, 4, 2),
			Kit(CombatTestWeaponRole.LightMachineGun, "RPK74", c_Ak + "Item_Weapon_RPK74.asset", magRpk545, ammo545, box545, 4, 2),
			Kit(CombatTestWeaponRole.LightMachineGun, "RPK74MOD1", c_Ak + "Item_Weapon_RPK74MOD1.asset", magRpk545, ammo545, box545, 4, 2)
		};

		_spawner.AssignLoadoutCatalog(
			backpack,
			ifak,
			FilterNull(grenades),
			FilterNull(helmets),
			FilterInvalid(playerUnique),
			FilterInvalid(playerFill),
			FilterInvalid(enemyUnique),
			FilterInvalid(enemyFill));
		EditorUtility.SetDirty(_spawner);
		return _spawner.PlayerUniqueKitCount >= 4 &&
		       _spawner.PlayerFillKitCount >= 4 &&
		       _spawner.EnemyUniqueKitCount >= 3 &&
		       _spawner.EnemyFillKitCount >= 8 &&
		       _spawner.GrenadeTypeCount >= 5 &&
		       _spawner.PlayerHelmetCount >= 3 &&
		       backpack != null &&
		       ifak != null;
	}
	#endregion

	#region Private Methods
	private static CombatTestArenaWeaponKit Kit(
		CombatTestWeaponRole _role,
		string _displayName,
		string _weaponPath,
		ItemDefinition _magazine,
		AmmoDefinition _ammo,
		ItemDefinition _ammoBox,
		int _magazineCount,
		int _ammoBoxCount)
	{
		return new CombatTestArenaWeaponKit(
			_role,
			_displayName,
			LoadItem(_weaponPath),
			_magazine,
			_ammo,
			_ammoBox,
			_magazineCount,
			_ammoBoxCount);
	}

	private static ItemDefinition LoadItem(string _path)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_path);
		if (item == null)
			Debug.LogError("[CombatTestArenaLoadoutBaker] Missing item: " + _path);
		return item;
	}

	private static AmmoDefinition LoadAmmo(string _path)
	{
		AmmoDefinition ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(_path);
		if (ammo == null)
			Debug.LogError("[CombatTestArenaLoadoutBaker] Missing ammo: " + _path);
		return ammo;
	}

	private static ItemDefinition[] FilterNull(ItemDefinition[] _items)
	{
		var list = new List<ItemDefinition>(_items.Length);
		for (int i = 0; i < _items.Length; i++)
		{
			if (_items[i] != null)
				list.Add(_items[i]);
		}

		return list.ToArray();
	}

	private static CombatTestArenaWeaponKit[] FilterInvalid(CombatTestArenaWeaponKit[] _kits)
	{
		var list = new List<CombatTestArenaWeaponKit>(_kits.Length);
		for (int i = 0; i < _kits.Length; i++)
		{
			if (_kits[i] != null && _kits[i].IsValid)
				list.Add(_kits[i]);
		}

		return list.ToArray();
	}
	#endregion
}
#endif
