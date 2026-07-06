#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Собирает полный список ItemDefinition для панели «доступное снаряжение» на экране предмиссии.
/// </summary>
public static class MissionPrepAvailableEquipmentBaker
{
	private const string c_InventoryRoot = "Assets/GameData/Inventory";
	private const string c_SetAssetPath = "Assets/GameData/Inventory/M4/MissionPrepM4AvailableEquipmentSet.asset";
	private const string c_DefaultMagazineAmmoPath = "Assets/GameData/Shooting/Ammo_762x39mm.asset";
	private const string c_MagazineAmmo556Path = "Assets/GameData/Shooting/Ammo_556x45mmNATO.asset";
	private const string c_MagazineAmmo762Path = "Assets/GameData/Shooting/Ammo_762x39mm.asset";
	private const string c_MagazineAmmo545Path = "Assets/GameData/Shooting/Ammo_545x39mm.asset";

	[MenuItem("Polygone/Mission Prep/Rebuild Available Equipment Set")]
	public static void RebuildAvailableEquipmentSet()
	{
		MissionPrepAvailableEquipmentItemSet set =
			AssetDatabase.LoadAssetAtPath<MissionPrepAvailableEquipmentItemSet>(c_SetAssetPath);
		if (set == null)
		{
			Debug.LogError($"Missing asset: {c_SetAssetPath}");
			return;
		}

		ItemDefinition[] items = LoadAllInventoryItems();
		AmmoDefinition magazineAmmo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(c_DefaultMagazineAmmoPath);
		AmmoDefinition magazineAmmo556 = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(c_MagazineAmmo556Path);
		AmmoDefinition magazineAmmo762 = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(c_MagazineAmmo762Path);
		AmmoDefinition magazineAmmo545 = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(c_MagazineAmmo545Path);

		var so = new SerializedObject(set);
		SerializedProperty array = so.FindProperty("m_Items");
		array.arraySize = items.Length;
		for (int i = 0; i < items.Length; i++)
			array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];

		so.FindProperty("m_MagazineAmmo").objectReferenceValue = magazineAmmo;
		so.FindProperty("m_MagazineAmmo556").objectReferenceValue = magazineAmmo556;
		so.FindProperty("m_MagazineAmmo762").objectReferenceValue = magazineAmmo762;
		so.FindProperty("m_MagazineAmmo545").objectReferenceValue = magazineAmmo545;
		so.FindProperty("m_RoundsPerMagazine").intValue = -1;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(set);
		AssetDatabase.SaveAssets();

		Debug.Log($"Mission prep available equipment set rebuilt: {items.Length} items.");
	}

	private static ItemDefinition[] LoadAllInventoryItems()
	{
		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { c_InventoryRoot });
		var list = new List<ItemDefinition>(guids.Length);

		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			if (!path.Contains("/Item_"))
				continue;

			var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
			if (item != null)
				list.Add(item);
		}

		list.Sort(CompareItems);
		return list.ToArray();
	}

	private static int CompareItems(ItemDefinition _a, ItemDefinition _b)
	{
		int orderA = GetSortOrder(_a);
		int orderB = GetSortOrder(_b);
		if (orderA != orderB)
			return orderA.CompareTo(orderB);

		return string.Compare(_a.name, _b.name, System.StringComparison.Ordinal);
	}

	private static int GetSortOrder(ItemDefinition _item)
	{
		if (_item == null)
			return 99;

		if (_item.IsEquipment && _item.EquipmentKind == EquipmentKind.Helmet)
			return 1;
		if (_item.IsEquipment && _item.EquipmentKind == EquipmentKind.Backpack)
			return 2;

		string name = _item.name ?? string.Empty;
		if (name.Contains("Weapon"))
			return 0;
		if (name.Contains("Helmet"))
			return 1;
		if (name.Contains("Backpack"))
			return 2;
		if (name.Contains("Attachment"))
			return 3;
		if (name.Contains("Mag"))
			return 4;
		if (name.Contains("Ammo") || name.Contains("Loot"))
			return 5;
		if (_item.IsGrenade || name.Contains("Grenade"))
			return 6;

		return 7;
	}
}
#endif
