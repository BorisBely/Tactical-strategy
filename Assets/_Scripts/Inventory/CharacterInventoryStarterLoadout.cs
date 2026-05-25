using UnityEngine;

/// <summary>
/// Одноразовая (или по контекстному меню) загрузка инвентаря: оружие в руки, заряженные магазины в сумку,
/// опционально коробки патронов. Повесь на того же объекта, что и <see cref="CharacterInventory"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
public sealed class CharacterInventoryStarterLoadout : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private CharacterInventory m_Inventory;

	[Header("Когда применять")]
	[Tooltip("Вызвать ApplyLoadout в Awake при старте сцены (Play Mode).")]
	[SerializeField] private bool m_ApplyOnAwake = true;
	[Tooltip("Очистить инвентарь и снять визуал оружия перед выдачей.")]
	[SerializeField] private bool m_ClearInventoryFirst = true;

	[Header("Оружие")]
	[SerializeField] private ItemDefinition m_WeaponItem;

	[Header("Магазины")]
	[SerializeField] private ItemDefinition m_MagazineItem;
	[SerializeField] private AmmoDefinition m_AmmoForMagazines;
	[Tooltip("Положить столько заряженных магазинов в сумку (не считая магазин, вставляемый в оружие).")]
	[SerializeField, Min(0)] private int m_SpareLoadedMagazinesInBag = 2;
	[Tooltip("Положить столько пустых магазинов в сумку для ручной зарядки.")]
	[SerializeField, Min(0)] private int m_SpareEmptyMagazinesInBag;
	[Tooltip("Вставить один заряженный магазин в оружие при старте.")]
	[SerializeField] private bool m_LoadFirstMagazineIntoWeapon = true;
	[Tooltip("-1 = заполнить по вместимости MagazineDefinition.")]
	[SerializeField] private int m_RoundsPerMagazine = -1;

	[Header("Коробки патронов в сумку")]
	[Tooltip("Каждый элемент — отдельный слот (ItemDefinition с AmmoDefinition, как пачка/коробка).")]
	[SerializeField] private ItemDefinition[] m_AmmoBoxItems;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_ApplyOnAwake && Application.isPlaying)
			ApplyLoadout();
	}
	#endregion

	#region Public Methods
	/// <summary>Собрать инвентарь по полям инспектора. Имеет смысл в Play Mode.</summary>
	public void ApplyLoadout()
	{
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_Inventory == null)
			m_Inventory = GetComponentInParent<CharacterInventory>();
		if (m_Inventory == null)
			m_Inventory = GetComponentInChildren<CharacterInventory>(true);
		if (m_Inventory == null && transform.parent != null)
			m_Inventory = transform.parent.GetComponentInChildren<CharacterInventory>(true);
		if (m_Inventory == null)
		{
			if (m_WeaponItem != null && m_ApplyOnAwake)
				Debug.LogWarning(
					$"{nameof(CharacterInventoryStarterLoadout)}: нет {nameof(CharacterInventory)} на этом объекте, в родителе или среди братьев родителя. Повесь скрипт на корень юнита с инвентарём или укажите ссылку вручную.",
					this);
			return;
		}

		if (m_WeaponItem == null)
		{
			Debug.LogWarning($"{nameof(CharacterInventoryStarterLoadout)}: не назначен Weapon Item.", this);
			return;
		}

		if (!m_WeaponItem.IsEquipment || m_WeaponItem.EquipmentKind != EquipmentKind.Weapon || m_WeaponItem.WeaponDefinition == null)
		{
			Debug.LogWarning($"{nameof(CharacterInventoryStarterLoadout)}: Weapon Item должен быть экипируемым оружием с WeaponDefinition.", this);
			return;
		}

		if (m_ClearInventoryFirst)
			m_Inventory.Clear();

		InventorySlotRuntimeData weaponSlot = InventorySlotRuntimeData.FromDefinition(m_WeaponItem);
		WeaponRuntimeState weaponState = weaponSlot.InstanceState != null ? weaponSlot.InstanceState.WeaponState : null;
		if (weaponState == null)
			return;

		int rounds = ResolveRoundsPerMagazine();
		bool canBuildMags = m_MagazineItem != null && m_MagazineItem.MagazineDefinition != null && m_AmmoForMagazines != null &&
		                    rounds > 0 &&
		                    MagazineCanHoldAmmo(m_MagazineItem.MagazineDefinition, m_AmmoForMagazines);

		if (!canBuildMags && (m_LoadFirstMagazineIntoWeapon || m_SpareLoadedMagazinesInBag > 0))
		{
			Debug.LogWarning(
				$"{nameof(CharacterInventoryStarterLoadout)}: задайте Magazine Item, Ammo For Magazines и корректный калибр; иначе магазины не создаются.",
				this);
		}

		if (canBuildMags && m_LoadFirstMagazineIntoWeapon)
		{
			InventorySlotRuntimeData magSlot = BuildLoadedMagazineSlot(rounds);
			if (!weaponState.TryInsertMagazine(magSlot))
			{
				Debug.LogWarning($"{nameof(CharacterInventoryStarterLoadout)}: магазин не вошёл в оружие (совместимость?). Кладём в сумку.", this);
				m_Inventory.TryAdd(magSlot);
			}
			else
				weaponState.TryChamberRoundFromMagazine();
		}

		if (canBuildMags)
		{
			for (int i = 0; i < m_SpareLoadedMagazinesInBag; i++)
				m_Inventory.TryAdd(BuildLoadedMagazineSlot(rounds));
		}

		if (m_MagazineItem != null && m_MagazineItem.MagazineDefinition != null)
		{
			for (int i = 0; i < m_SpareEmptyMagazinesInBag; i++)
				m_Inventory.TryAdd(InventorySlotRuntimeData.FromDefinition(m_MagazineItem));
		}

		m_Inventory.RestoreAfterFailedDrop(true, weaponSlot);

		if (m_AmmoBoxItems != null)
		{
			for (int i = 0; i < m_AmmoBoxItems.Length; i++)
			{
				ItemDefinition box = m_AmmoBoxItems[i];
				if (box == null || box.AmmoDefinition == null)
					continue;

				m_Inventory.TryAdd(InventorySlotRuntimeData.FromDefinition(box));
			}
		}

		UnitWeaponRuntime weaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (weaponRuntime != null)
			weaponRuntime.RefreshFromEquipment();
	}

#if UNITY_EDITOR
	[ContextMenu("Apply Loadout (Play Mode)")]
	private void ContextMenuApplyLoadout()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning($"{nameof(CharacterInventoryStarterLoadout)}: контекстное меню работает только в Play Mode.", this);
			return;
		}

		ApplyLoadout();
	}
#endif
	#endregion

	#region Private Methods
	private int ResolveRoundsPerMagazine()
	{
		if (m_MagazineItem == null || m_MagazineItem.MagazineDefinition == null)
			return 0;

		int capacity = m_MagazineItem.MagazineDefinition.Capacity;
		if (m_RoundsPerMagazine < 0)
			return capacity;

		return Mathf.Clamp(m_RoundsPerMagazine, 0, capacity);
	}

	private static bool MagazineCanHoldAmmo(MagazineDefinition _magazine, AmmoDefinition _ammo)
	{
		if (_magazine == null || _ammo == null)
			return false;
		if (_magazine.SupportedCaliber == CaliberType.None)
			return true;
		return _ammo.Caliber == _magazine.SupportedCaliber;
	}

	private InventorySlotRuntimeData BuildLoadedMagazineSlot(int _rounds)
	{
		InventorySlotRuntimeData slot = InventorySlotRuntimeData.FromDefinition(m_MagazineItem);
		MagazineRuntimeState magazineState = slot.InstanceState != null ? slot.InstanceState.MagazineState : null;
		MagazineDefinition magazineDefinition = m_MagazineItem.MagazineDefinition;
		if (magazineState != null && magazineDefinition != null)
			magazineState.Configure(magazineDefinition, m_AmmoForMagazines, _rounds);

		return slot;
	}
	#endregion
}
