using UnityEngine;

/// <summary>
/// TEMP: кладёт в инвентарь машины все турельные предметы для проверки системы.
/// После теста удали этот компонент с машины (или весь скрипт).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(46)]
public sealed class VehicleTurretTempDebugLoadout : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleInventory m_Inventory;
	[Tooltip("Если true — заполняет инвентарь при Awake. Выключи после проверки.")]
	[SerializeField] private bool m_ApplyOnAwake = true;
	[SerializeField] private ItemDefinition m_M2Browning;
	[SerializeField] private ItemDefinition m_Mk19;
	[SerializeField] private ItemDefinition m_FrontalShield;
	[SerializeField] private ItemDefinition m_SurroundShield;
	[SerializeField] private ItemDefinition m_M2Box;
	[SerializeField] private ItemDefinition m_Mk19Box;
	[SerializeField, Min(0)] private int m_M2BoxCount = 3;
	[SerializeField, Min(0)] private int m_Mk19BoxCount = 1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_ApplyOnAwake)
			Apply();
	}
	#endregion

	#region Public Methods
	[ContextMenu("TEMP/Apply All Turret Test Items")]
	public void Apply()
	{
		if (m_Inventory == null)
			TryGetComponent(out m_Inventory);
		if (m_Inventory == null)
		{
			Debug.LogWarning($"[TEMP Turret Loadout] {name}: VehicleInventory не найден.", this);
			return;
		}

		ResolveItemRefs();
		m_Inventory.SetExchangeModificationAllowed(true);

		TryEquip(m_M2Browning, VehicleEquipmentSlotId.TurretWeapon);
		TryEquip(m_FrontalShield, VehicleEquipmentSlotId.FrontalShield);
		TryEquip(m_SurroundShield, VehicleEquipmentSlotId.SurroundShield);
		EnsureBagCount(m_Mk19, 1);
		EnsureBagCount(m_M2Box, m_M2BoxCount);
		EnsureBagCount(m_Mk19Box, m_Mk19BoxCount);

		m_Inventory.SetExchangeModificationAllowed(false);
	}

	[ContextMenu("TEMP/Clear Vehicle Inventory")]
	public void ClearInventory()
	{
		if (m_Inventory == null)
			TryGetComponent(out m_Inventory);
		if (m_Inventory == null)
			return;

		m_Inventory.SetExchangeModificationAllowed(true);
		while (m_Inventory.HasSurroundShield)
			m_Inventory.TryUnequipToBag(VehicleEquipmentSlotId.SurroundShield);
		while (m_Inventory.HasFrontalShield)
			m_Inventory.TryUnequipToBag(VehicleEquipmentSlotId.FrontalShield);
		while (m_Inventory.HasTurretWeapon)
			m_Inventory.TryUnequipToBag(VehicleEquipmentSlotId.TurretWeapon);

		while (m_Inventory.BagCount > 0)
			m_Inventory.TryRemoveBagAt(0, out _);

		m_Inventory.SetExchangeModificationAllowed(false);
		Debug.Log($"[TEMP Turret Loadout] {name}: инвентарь машины очищен.", this);
	}
	#endregion

	#region Private Methods
	private void ResolveItemRefs()
	{
		TurretContentCatalog catalog = TurretContentCatalog.Get();
		if (catalog == null)
		{
			Debug.LogError(
				"[TEMP Turret Loadout] не найден Resources/Turret/TurretContentCatalog.asset",
				this);
			return;
		}

		if (m_M2Browning == null)
			m_M2Browning = catalog.M2Browning;
		if (m_Mk19 == null)
			m_Mk19 = catalog.Mk19;
		if (m_FrontalShield == null)
			m_FrontalShield = catalog.FrontalShield;
		if (m_SurroundShield == null)
			m_SurroundShield = catalog.SurroundShield;
		if (m_M2Box == null)
			m_M2Box = catalog.M2MagazineBox;
		if (m_Mk19Box == null)
			m_Mk19Box = catalog.Mk19MagazineBox;
	}

	private void TryEquip(ItemDefinition _item, VehicleEquipmentSlotId _slot)
	{
		if (_item == null)
		{
			Debug.LogWarning($"[TEMP Turret Loadout] нет ItemDefinition для слота {_slot}.", this);
			return;
		}

		InventorySlotRuntimeData existing = m_Inventory.GetEquipmentSlot(_slot);
		if (!existing.IsEmpty && existing.Definition == _item)
			return;

		if (!existing.IsEmpty)
			m_Inventory.TryUnequipToBag(_slot);

		if (!m_Inventory.TryEquipExternal(InventorySlotRuntimeData.FromDefinition(_item), _slot))
			Debug.LogWarning($"[TEMP Turret Loadout] не удалось экипировать {_item.name} в {_slot}.", this);
	}

	private void EnsureBagCount(ItemDefinition _item, int _count)
	{
		if (_item == null || _count <= 0)
			return;

		int existing = 0;
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			if (m_Inventory.BagItems[i].Definition == _item)
				existing++;
		}

		for (int i = existing; i < _count; i++)
		{
			InventorySlotRuntimeData slot = InventorySlotRuntimeData.FromDefinition(_item);
			TryFillMagazineSlot(ref slot);
			if (!m_Inventory.TryAdd(slot))
			{
				Debug.LogWarning(
					$"[TEMP Turret Loadout] не удалось добавить {_item.name} ({i + 1}/{_count}) — лимит веса багажа.",
					this);
				break;
			}
		}
	}

	private static void TryFillMagazineSlot(ref InventorySlotRuntimeData _slot)
	{
		if (_slot.Definition == null || _slot.Definition.MagazineDefinition == null)
			return;

		MagazineRuntimeState magState = _slot.InstanceState != null ? _slot.InstanceState.MagazineState : null;
		if (magState == null)
			return;

		TurretContentCatalog catalog = TurretContentCatalog.Get();
		AmmoDefinition ammo = null;
		if (_slot.Definition.MagazineDefinition.SupportedCaliber == CaliberType.TwelvePointSevenByNinetyNine)
			ammo = catalog != null ? catalog.Ammo127 : null;
		else if (_slot.Definition.MagazineDefinition.SupportedCaliber == CaliberType.FortyByFiftyThree)
			ammo = catalog != null ? catalog.Ammo40 : null;

		if (ammo == null)
			return;

		magState.Configure(
			_slot.Definition.MagazineDefinition,
			ammo,
			_slot.Definition.MagazineDefinition.Capacity);
	}
	#endregion
}
