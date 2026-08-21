using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Роль оружия на CQB-арене: уникальные классы имеют приоритет над серийными клонами.
/// </summary>
public enum CombatTestWeaponRole
{
	Sniper = 0,
	Marksman = 1,
	MachineGun = 2,
	Rifle = 3,
	Carbine = 4,
	Shotgun = 5,
	LightMachineGun = 6
}

/// <summary>
/// Комплект оружия + магазины/патроны для одной точки спавна арены.
/// </summary>
[Serializable]
public sealed class CombatTestArenaWeaponKit
{
	#region Private Fields
	[SerializeField] private CombatTestWeaponRole m_Role;
	[SerializeField] private string m_DisplayName;
	[SerializeField] private ItemDefinition m_Weapon;
	[SerializeField] private ItemDefinition m_Magazine;
	[SerializeField] private AmmoDefinition m_Ammo;
	[SerializeField] private ItemDefinition m_AmmoBox;
	[SerializeField, Min(0)] private int m_MagazineCount;
	[SerializeField, Min(0)] private int m_AmmoBoxCount;
	#endregion

	#region Public Properties
	public CombatTestWeaponRole Role => m_Role;
	public string DisplayName => m_DisplayName;
	public ItemDefinition Weapon => m_Weapon;
	public ItemDefinition Magazine => m_Magazine;
	public AmmoDefinition Ammo => m_Ammo;
	public ItemDefinition AmmoBox => m_AmmoBox;
	public int MagazineCount => m_MagazineCount;
	public int AmmoBoxCount => m_AmmoBoxCount;
	public bool IsValid => m_Weapon != null;
	#endregion

	#region Constructors
	public CombatTestArenaWeaponKit()
	{
	}

	public CombatTestArenaWeaponKit(
		CombatTestWeaponRole _role,
		string _displayName,
		ItemDefinition _weapon,
		ItemDefinition _magazine,
		AmmoDefinition _ammo,
		ItemDefinition _ammoBox,
		int _magazineCount,
		int _ammoBoxCount)
	{
		m_Role = _role;
		m_DisplayName = _displayName;
		m_Weapon = _weapon;
		m_Magazine = _magazine;
		m_Ammo = _ammo;
		m_AmmoBox = _ammoBox;
		m_MagazineCount = Mathf.Max(0, _magazineCount);
		m_AmmoBoxCount = Mathf.Max(0, _ammoBoxCount);
	}
	#endregion

	#region Public Methods
	public ItemDefinition[] BuildBagItems(ItemDefinition _ifak, int _ifakCount)
	{
		int ifakCount = _ifak != null ? Mathf.Max(0, _ifakCount) : 0;
		int magCount = m_Magazine != null ? m_MagazineCount : 0;
		int ammoCount = m_AmmoBox != null ? m_AmmoBoxCount : 0;
		var items = new List<ItemDefinition>(ifakCount + magCount + ammoCount);

		for (int i = 0; i < ifakCount; i++)
			items.Add(_ifak);

		for (int i = 0; i < magCount; i++)
			items.Add(m_Magazine);

		for (int i = 0; i < ammoCount; i++)
			items.Add(m_AmmoBox);

		return items.ToArray();
	}

	/// <summary>
	/// Сначала раскладывает уникальные классы по случайным слотам, остаток добивает случайными серийными комплектами.
	/// </summary>
	public static CombatTestArenaWeaponKit[] PickForSlotCount(
		CombatTestArenaWeaponKit[] _unique,
		CombatTestArenaWeaponKit[] _fill,
		int _slotCount)
	{
		if (_slotCount <= 0)
			return Array.Empty<CombatTestArenaWeaponKit>();

		List<CombatTestArenaWeaponKit> unique = CollectValid(_unique);
		List<CombatTestArenaWeaponKit> fill = CollectValid(_fill);
		var result = new CombatTestArenaWeaponKit[_slotCount];

		Shuffle(unique);
		int uniqueCount = Mathf.Min(unique.Count, _slotCount);
		int[] slotOrder = CreateShuffledIndices(_slotCount);
		for (int i = 0; i < uniqueCount; i++)
			result[slotOrder[i]] = unique[i];

		List<CombatTestArenaWeaponKit> residual = fill.Count > 0 ? fill : unique;
		if (residual.Count == 0)
			return Array.Empty<CombatTestArenaWeaponKit>();

		for (int i = uniqueCount; i < _slotCount; i++)
			result[slotOrder[i]] = residual[UnityEngine.Random.Range(0, residual.Count)];

		return result;
	}
	#endregion

	#region Private Methods
	private static List<CombatTestArenaWeaponKit> CollectValid(CombatTestArenaWeaponKit[] _source)
	{
		var list = new List<CombatTestArenaWeaponKit>();
		if (_source == null)
			return list;

		for (int i = 0; i < _source.Length; i++)
		{
			CombatTestArenaWeaponKit kit = _source[i];
			if (kit != null && kit.IsValid)
				list.Add(kit);
		}

		return list;
	}

	private static int[] CreateShuffledIndices(int _count)
	{
		int[] indices = new int[_count];
		for (int i = 0; i < _count; i++)
			indices[i] = i;
		Shuffle(indices);
		return indices;
	}

	private static void Shuffle<T>(IList<T> _items)
	{
		for (int i = _items.Count - 1; i > 0; i--)
		{
			int swapIndex = UnityEngine.Random.Range(0, i + 1);
			T temporary = _items[i];
			_items[i] = _items[swapIndex];
			_items[swapIndex] = temporary;
		}
	}
	#endregion
}
