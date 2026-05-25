using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI модификации оружия в runtime-инвентаре юнита и на панели «земля».
/// Данные меняются через <see cref="ItemModificationUtility"/>; 3D-модель оружия пока не синхронизируется.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-90)]
public sealed class RuntimeInventoryModificationCoordinator : MonoBehaviour
{
	#region Static Access
	private static RuntimeInventoryModificationCoordinator s_Instance;

	public static RuntimeInventoryModificationCoordinator Instance => s_Instance;
	#endregion

	#region Serialized Fields
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[SerializeField] private RtsUnitSelectionManager m_SelectionManager;
	#endregion

	#region Private Fields
	private RuntimeInventoryModificationUiState m_ModificationUiState;
	private readonly List<ItemModificationSlotDescriptor> m_ModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<ItemModificationSlotDescriptor> m_VisibleModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<WeaponSlotBinding> m_WeaponSlotBindingBuffer = new List<WeaponSlotBinding>(8);
	private Coroutine m_DeferredInlineRefreshCoroutine;
	#endregion

	private readonly struct WeaponSlotBinding
	{
		public readonly InventoryPanelView Panel;
		public readonly InventorySlotView SlotView;
		public readonly InventorySlotRuntimeData WeaponData;
		public readonly bool IsMainHand;
		public readonly int BagIndex;
		public readonly bool IsGroundSlot;
		public readonly int GroundSlotIndex;

		public WeaponSlotBinding(
			InventoryPanelView _panel,
			InventorySlotView _slotView,
			InventorySlotRuntimeData _weaponData,
			bool _isMainHand,
			int _bagIndex,
			bool _isGroundSlot = false,
			int _groundSlotIndex = -1)
		{
			Panel = _panel;
			SlotView = _slotView;
			WeaponData = _weaponData;
			IsMainHand = _isMainHand;
			BagIndex = _bagIndex;
			IsGroundSlot = _isGroundSlot;
			GroundSlotIndex = _groundSlotIndex;
		}
	}

	#region Public Properties
	public InventoryPanelView CharacterPanel
	{
		get
		{
			EnsureRuntimeReferences();
			return m_SelectionManager != null ? m_SelectionManager.CharacterInventoryPanel : null;
		}
	}

	public InventoryPanelView GroundPanel
	{
		get
		{
			EnsureRuntimeReferences();
			return m_SelectionManager != null ? m_SelectionManager.GroundPanel : null;
		}
	}

	private CharacterInventory ActiveInventory
	{
		get
		{
			EnsureRuntimeReferences();
			if (m_InventoryBindings != null && m_InventoryBindings.ActiveCharacterInventory != null)
				return m_InventoryBindings.ActiveCharacterInventory;

			return m_SelectionManager != null
				? m_SelectionManager.TryGetActiveCharacterInventoryForUi()
				: null;
		}
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		EnsureRuntimeReferences();
		RuntimeModificationOutsideClick.EnsureOn(this);
		RuntimeUiDestroyQueue.EnsureOn(this);
	}

	private void OnEnable()
	{
		EnsureRuntimeReferences();
		RuntimeInventoryModificationDragContext.Changed += HandleModificationDragContextChanged;
	}

	private void OnDisable()
	{
		RuntimeInventoryModificationDragContext.Changed -= HandleModificationDragContextChanged;
		RuntimeModificationSlotDrag.CleanupActiveDragVisual();
		RuntimeInventoryModificationDragContext.ResetAfterDrag();
		m_ModificationUiState = default;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Methods
	public bool TryExpandModificationPanel(InventorySlotView _slot)
	{
		if (TryResolveGroundSlot(_slot, out int groundSlotIndex))
		{
			if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
				return false;

			if (m_ModificationUiState.MatchesGround(groundSlotIndex) && m_ModificationUiState.ExpandEmptySlots)
				return true;

			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(groundSlotIndex, _expandEmptySlots: true);
			RebuildInlineModificationRows();
			return true;
		}

		if (!TryResolveCharacterSlot(_slot, out bool isMainHand, out int bagIndex))
			return false;

		CharacterInventory inventory = ActiveInventory;
		if (inventory == null || !inventory.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (!ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
			return false;

		if (m_ModificationUiState.MatchesCharacter(isMainHand, bagIndex) && m_ModificationUiState.ExpandEmptySlots)
			return true;

		m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(isMainHand, bagIndex, _expandEmptySlots: true);
		RebuildInlineModificationRows();
		return true;
	}

	public bool TryCollapseEmptyModificationSlotsForSlot(InventorySlotView _slot)
	{
		if (TryResolveGroundSlot(_slot, out int groundSlotIndex))
		{
			if (!m_ModificationUiState.MatchesGround(groundSlotIndex) || !m_ModificationUiState.ExpandEmptySlots)
				return false;

			CollapseEmptyModificationSlots();
			return true;
		}

		if (!TryResolveCharacterSlot(_slot, out bool isMainHand, out int bagIndex))
			return false;

		if (!m_ModificationUiState.MatchesCharacter(isMainHand, bagIndex) || !m_ModificationUiState.ExpandEmptySlots)
			return false;

		CollapseEmptyModificationSlots();
		return true;
	}

	public bool TryToggleModificationPanel(InventorySlotView _slot)
	{
		if (TryResolveGroundSlot(_slot, out int groundSlotIndex))
		{
			if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
				return false;

			if (m_ModificationUiState.MatchesGround(groundSlotIndex))
				m_ModificationUiState.ExpandEmptySlots = !m_ModificationUiState.ExpandEmptySlots;
			else
				m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(groundSlotIndex, _expandEmptySlots: true);

			RebuildInlineModificationRows();
			return true;
		}

		if (!TryResolveCharacterSlot(_slot, out bool isMainHand, out int bagIndex))
			return false;

		CharacterInventory inventory = ActiveInventory;
		if (inventory == null || !inventory.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (!ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
			return false;

		if (m_ModificationUiState.MatchesCharacter(isMainHand, bagIndex))
			m_ModificationUiState.ExpandEmptySlots = !m_ModificationUiState.ExpandEmptySlots;
		else
			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(isMainHand, bagIndex, _expandEmptySlots: true);

		RebuildInlineModificationRows();
		return true;
	}

	public bool HasExpandedEmptyModificationSlots()
	{
		return m_ModificationUiState.HasSelection && m_ModificationUiState.ExpandEmptySlots;
	}

	public bool TryGetModificationWeaponSlot(out InventorySlotRuntimeData _weaponSlot)
	{
		_weaponSlot = default;
		if (!m_ModificationUiState.HasSelection)
			return false;

		if (m_ModificationUiState.IsGroundSlot)
		{
			if (!TryGetGroundWeaponSlot(m_ModificationUiState.GroundSlotIndex, out _, out _weaponSlot))
				return false;

			return !_weaponSlot.IsEmpty && ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition);
		}

		CharacterInventory inventory = ActiveInventory;
		if (inventory == null)
			return false;

		if (!inventory.TryGetInventorySlot(m_ModificationUiState.IsMainHand, m_ModificationUiState.BagIndex, out _weaponSlot))
			return false;

		return !_weaponSlot.IsEmpty && ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition);
	}

	public bool ShouldHighlightCompatibleWithModificationWeapon(InventorySlotRuntimeData _candidate)
	{
		if (_candidate.IsEmpty)
			return false;

		return HasExpandedEmptyModificationSlots() &&
		       TryGetModificationWeaponSlot(out InventorySlotRuntimeData weaponSlot) &&
		       ItemModificationUtility.IsCompatibleWithWeapon(weaponSlot, _candidate);
	}

	public void RefreshModificationCompatibilityHighlights()
	{
		RefreshPanelHighlights(CharacterPanel);
		RefreshPanelHighlights(GroundPanel);
	}

	public void CollapseEmptyModificationSlots()
	{
		if (!m_ModificationUiState.HasSelection || !m_ModificationUiState.ExpandEmptySlots)
			return;

		m_ModificationUiState.ExpandEmptySlots = false;
		RebuildInlineModificationRows();
	}

	public void ClearModificationUiSelection()
	{
		m_ModificationUiState = default;
		ClearAllModificationVisuals();
		RebuildInlineModificationRows();
	}

	/// <summary>Убрать inline-ряды модификации с обеих панелей без пересборки из данных.</summary>
	public void ClearAllModificationVisuals()
	{
		m_ModificationUiState = default;
		if (CharacterPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(CharacterPanel);
		if (GroundPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(GroundPanel);
		CharacterPanel?.RebuildContentLayout();
		GroundPanel?.RebuildContentLayout();
		RefreshModificationCompatibilityHighlights();
	}

	/// <summary>Перестроить inline-ряды установленных модов (и раскрытых пустых слотов) на панелях персонажа и «земля».</summary>
	public void RefreshInlineModificationRows()
	{
		RebuildInlineModificationRows();
	}

	/// <summary>Безопасно после drag/drop: Destroy и rebuild mod-UI на следующем кадре.</summary>
	public void ScheduleRefreshInlineModificationRowsAfterDrag()
	{
		if (!isActiveAndEnabled)
		{
			RebuildInlineModificationRows();
			return;
		}

		if (m_DeferredInlineRefreshCoroutine != null)
			return;

		m_DeferredInlineRefreshCoroutine = StartCoroutine(CoRefreshInlineModificationRowsNextFrame());
	}

	/// <summary>После удаления предмета с панели «земля» (выход из зоны, подбор).</summary>
	public void NotifyGroundListingRemoved()
	{
		if (m_ModificationUiState.IsGroundSlot)
			m_ModificationUiState = default;

		ScheduleRefreshInlineModificationRowsAfterDrag();
	}

	public bool TryInstallModificationFromDrag(
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		bool _weaponIsOnGroundPanel = false,
		int _weaponGroundSlotIndex = -1)
	{
		RuntimeInventoryModificationDragPayload payload = RuntimeInventoryModificationDragContext.Current;
		if (!payload.HasItem)
			return false;

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.ModificationSlot)
			return false;

		InventorySlotRuntimeData weaponSlot;
		if (_weaponIsOnGroundPanel)
		{
			if (!TryGetGroundWeaponSlot(_weaponGroundSlotIndex, out _, out weaponSlot))
				return false;
		}
		else
		{
			if (ActiveInventory == null)
				return false;

			if (!ActiveInventory.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out weaponSlot))
				return false;
		}

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.CharacterBag)
		{
			if (ActiveInventory == null || payload.SlotIndex < 0)
				return false;
			if (!_weaponIsOnGroundPanel && !_weaponIsMainHand && payload.SlotIndex == _weaponBagIndex)
				return false;
			if (!ActiveInventory.TryGetInventorySlot(_isMainHandEquipmentSlot: false, payload.SlotIndex, out _))
				return false;
		}

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.GroundPanel)
		{
			if (payload.SlotIndex < 0 || GroundPanel == null)
				return false;
			if (payload.SlotIndex >= GroundPanel.Slots.Count)
				return false;
			if (_weaponIsOnGroundPanel && payload.SlotIndex == _weaponGroundSlotIndex)
				return false;
		}

		if (!ItemModificationUtility.CanAcceptItem(_slotDescriptor, weaponSlot, payload.Item))
			return false;

		InventorySlotRuntimeData candidate = MissionPrepInventoryCopyUtility.CloneSlot(payload.Item);
		if (!ItemModificationUtility.TryInstallAtSlot(_slotDescriptor, weaponSlot, candidate, out InventorySlotRuntimeData replacedItem))
			return false;

		int targetBagIndex = _weaponBagIndex;
		if (!TryConsumeModificationDragSource(payload, ref targetBagIndex))
			return false;

		if (_weaponIsOnGroundPanel)
		{
			if (!TryCommitGroundWeaponSlot(_weaponGroundSlotIndex, weaponSlot))
				return false;
		}
		else if (!ActiveInventory.TrySetInventorySlot(_weaponIsMainHand, targetBagIndex, weaponSlot))
			return false;

		if (!replacedItem.IsEmpty)
		{
			if (ActiveInventory != null)
				ActiveInventory.TryAdd(replacedItem);
			else
				GroundPanel?.TryAdd(replacedItem);
		}

		if (!_weaponIsOnGroundPanel && m_ModificationUiState.MatchesCharacter(_weaponIsMainHand, _weaponBagIndex) && !_weaponIsMainHand)
			m_ModificationUiState.BagIndex = targetBagIndex;

		RuntimeInventoryModificationDragContext.NotifyDropConsumed();
		NotifyInventoryMutated();
		return true;
	}

	public bool TryClearModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		bool _addToCharacterBag = true,
		bool _weaponIsOnGroundPanel = false,
		int _weaponGroundSlotIndex = -1)
	{
		InventorySlotRuntimeData weaponSlot;
		if (_weaponIsOnGroundPanel)
		{
			if (!TryGetGroundWeaponSlot(_weaponGroundSlotIndex, out _, out weaponSlot))
				return false;
		}
		else
		{
			CharacterInventory inventory = ActiveInventory;
			if (inventory == null || !inventory.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out weaponSlot))
				return false;
		}

		if (!ItemModificationUtility.TryClearSlot(_slotDescriptor, weaponSlot, out InventorySlotRuntimeData removedItem))
			return false;

		if (_weaponIsOnGroundPanel)
		{
			if (!TryCommitGroundWeaponSlot(_weaponGroundSlotIndex, weaponSlot))
				return false;
		}
		else if (!ActiveInventory.TrySetInventorySlot(_weaponIsMainHand, _weaponBagIndex, weaponSlot))
			return false;

		if (!removedItem.IsEmpty && _addToCharacterBag)
		{
			if (ActiveInventory != null)
				ActiveInventory.TryAdd(removedItem);
			else
				GroundPanel?.TryAdd(removedItem);
		}

		NotifyInventoryMutated();
		return true;
	}

	public bool TryEjectModificationSlotToCharacterBag(RuntimeModificationSlotDrag _drag)
	{
		if (_drag == null)
			return false;

		RuntimeModificationSlotDrag.CleanupActiveDragVisual();
		return TryClearModificationSlot(
			_drag.SlotDescriptor,
			_drag.WeaponIsMainHand,
			_drag.WeaponBagIndex,
			_addToCharacterBag: true,
			_weaponIsOnGroundPanel: _drag.WeaponIsOnGroundPanel,
			_weaponGroundSlotIndex: _drag.WeaponGroundSlotIndex);
	}

	public bool TryEjectModificationSlotToGround(RuntimeModificationSlotDrag _drag)
	{
		if (_drag == null || GroundPanel == null)
			return false;

		InventorySlotRuntimeData weaponSlot;
		if (_drag.WeaponIsOnGroundPanel)
		{
			if (!TryGetGroundWeaponSlot(_drag.WeaponGroundSlotIndex, out _, out weaponSlot))
				return false;
		}
		else
		{
			CharacterInventory inventory = ActiveInventory;
			if (inventory == null ||
			    !inventory.TryGetInventorySlot(_drag.WeaponIsMainHand, _drag.WeaponBagIndex, out weaponSlot))
				return false;
		}

		if (!ItemModificationUtility.TryClearSlot(_drag.SlotDescriptor, weaponSlot, out InventorySlotRuntimeData removedItem))
			return false;

		if (_drag.WeaponIsOnGroundPanel)
		{
			if (!TryCommitGroundWeaponSlot(_drag.WeaponGroundSlotIndex, weaponSlot))
				return false;
		}
		else if (!ActiveInventory.TrySetInventorySlot(_drag.WeaponIsMainHand, _drag.WeaponBagIndex, weaponSlot))
			return false;

		if (removedItem.IsEmpty)
		{
			NotifyInventoryMutated();
			return true;
		}

		InventorySlotRuntimeData forGround = removedItem;
		forGround.WorldSource = null;
		if (!GroundPanel.TryAdd(forGround))
		{
			if (_drag.WeaponIsOnGroundPanel)
			{
				ItemModificationUtility.TryInstallAtSlot(_drag.SlotDescriptor, weaponSlot, removedItem, out _);
				TryCommitGroundWeaponSlot(_drag.WeaponGroundSlotIndex, weaponSlot);
			}
			else
				ActiveInventory?.TryAdd(removedItem);

			NotifyInventoryMutated();
			return false;
		}

		RuntimeModificationSlotDrag.CleanupActiveDragVisual();
		NotifyInventoryMutated();
		return true;
	}

	public bool IsScreenPointOverCharacterPanel(Vector2 _screenPosition, Camera _eventCamera)
	{
		return RuntimeModificationPanelUtility.IsScreenPointOverPanel(CharacterPanel, _screenPosition);
	}

	public bool IsScreenPointOverGroundPanel(Vector2 _screenPosition, Camera _eventCamera)
	{
		return RuntimeModificationPanelUtility.IsScreenPointOverPanel(GroundPanel, _screenPosition);
	}

	public void OnGroundPanelRepopulated()
	{
		EnsureGroundPanelUiHooks();
		RefreshInlineModificationRows();
	}

	/// <summary>Повесить highlight/drag-хуки на новые ячейки «земли» без полного repopulate.</summary>
	public void EnsureGroundPanelUiHooks()
	{
		EnsureRuntimeReferences();
		EnsureGroundPanelComponents();
		RefreshModificationCompatibilityHighlights();
	}

	/// <summary>
	/// После открытия инвентаря или смены selection manager: повесить click/highlight на ячейки unit-префаба.
	/// </summary>
	public void EnsureModificationUiHooks()
	{
		EnsureRuntimeReferences();
		if (CharacterPanel == null)
			return;

		EnsureCharacterPanelComponents();
	}

	public bool TryRepaintCharacterAndGroundPanels(CharacterInventory _inventory = null)
	{
		EnsureRuntimeReferences();

		CharacterInventory inventory = _inventory != null ? _inventory : ActiveInventory;
		InventoryPanelView characterPanel = CharacterPanel;
		if (characterPanel == null)
			return false;

		if (inventory == null)
		{
			ClearAllModificationVisuals();
			characterPanel.ClearAllSlots();
			GroundPanel?.ClearAllSlots();
			GroundPanel?.RebuildContentLayout();
			return true;
		}

		RepaintCharacterAndGroundPanelsInternal(inventory, characterPanel);
		return true;
	}

	public void RepaintCharacterAndGroundPanels()
	{
		TryRepaintCharacterAndGroundPanels();
	}

	private void RepaintCharacterAndGroundPanelsInternal(CharacterInventory _inventory, InventoryPanelView _characterPanel)
	{
		RuntimeInlineModificationBuilder.ClearAllRows(_characterPanel);
		_characterPanel.RepaintFromCharacterInventory(_inventory);
		EnsureCharacterPanelComponents();
		RebuildInlineModificationRows();
		EnsureGroundPanelComponents();
		GroundPanel?.RebuildContentLayout();
		RefreshModificationCompatibilityHighlights();
	}

	public bool TryResolveCharacterSlot(InventorySlotView _slot, out bool _isMainHandEquipmentSlot, out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_bagIndex = -1;

		if (_slot == null || CharacterPanel == null || ActiveInventory == null)
			return false;

		if (!RuntimeModificationPanelUtility.IsSlotOnPanel(_slot, CharacterPanel))
			return false;

		int slotIndex = CharacterPanel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
			return false;

		int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);
		if (slotIndex < lead)
		{
			_isMainHandEquipmentSlot = slotIndex == 0;
			return _isMainHandEquipmentSlot && ActiveInventory.HasMainHandEquipment;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0 && _bagIndex < ActiveInventory.BagCount;
	}

	public bool TryResolveGroundSlot(InventorySlotView _slot, out int _groundSlotIndex)
	{
		_groundSlotIndex = -1;
		if (_slot == null || GroundPanel == null)
			return false;

		if (!RuntimeModificationPanelUtility.IsSlotOnPanel(_slot, GroundPanel))
			return false;

		_groundSlotIndex = GroundPanel.GetInventorySlotListIndex(_slot);
		return _groundSlotIndex >= 0;
	}

	public void TryBeginModificationDragFromCharacterSlot(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModificationItem(_slot.Data))
			return;

		if (!TryResolveCharacterSlot(_slot, out bool isMainHand, out int bagIndex))
			return;

		RuntimeInventoryModificationDragContext.BeginCharacter(_slot.Data, isMainHand, bagIndex, _slot);
	}

	public void TryBeginModificationDragFromGroundSlot(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || GroundPanel == null || !ItemModificationUtility.IsModificationItem(_slot.Data))
			return;

		if (!TryResolveGroundSlot(_slot, out int groundSlotIndex))
			return;

		RuntimeInventoryModificationDragContext.BeginGround(_slot.Data, groundSlotIndex, _slot);
	}

	public void NotifyInventoryMutated()
	{
		CharacterInventory inventory = ActiveInventory;
		if (inventory != null)
			TryRepaintCharacterAndGroundPanels(inventory);
		else
			TryRepaintCharacterAndGroundPanels();
	}
	#endregion

	#region Private Methods
	private void EnsureRuntimeReferences()
	{
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;

		if (m_InventoryBindings != null)
			m_SelectionManager = m_InventoryBindings.SelectionManager;

		if (m_SelectionManager == null)
			m_SelectionManager = RtsUnitSelectionManager.Instance;
	}

	private bool TryConsumeModificationDragSource(RuntimeInventoryModificationDragPayload _payload, ref int _targetBagIndex)
	{
		switch (_payload.SourceKind)
		{
			case RuntimeInventoryModificationDragSourceKind.CharacterBag:
				if (_payload.SlotIndex < 0)
					return false;
				if (!ActiveInventory.TryRemoveInventorySlot(_isMainHandEquipmentSlot: false, _payload.SlotIndex, out _))
					return false;
				if (!_payload.IsMainHand && _payload.SlotIndex < _targetBagIndex)
					_targetBagIndex--;
				return true;

			case RuntimeInventoryModificationDragSourceKind.GroundPanel:
				return TryRemoveGroundSlotAt(_payload.SlotIndex);

			default:
				return true;
		}
	}

	private bool TryRemoveGroundSlotAt(int _groundSlotIndex)
	{
		if (GroundPanel == null)
			return false;

		InventorySlotView slot = RuntimeInventoryModificationDragContext.SourceSlotView;
		if (slot != null && slot.HasItem)
		{
			if (!slot.TryTakeItem(out InventorySlotRuntimeData takenFromDrag))
				return false;

			if (takenFromDrag.WorldSource != null)
				takenFromDrag.WorldSource.OnTransferredToCharacterInventory();

			GroundPanel.NotifyGroundSlotItemTakenAway(slot);
			return true;
		}

		if (_groundSlotIndex < 0 || _groundSlotIndex >= GroundPanel.Slots.Count)
			return false;

		slot = GroundPanel.Slots[_groundSlotIndex];
		if (slot == null || !slot.TryTakeItem(out InventorySlotRuntimeData taken))
			return false;

		if (taken.WorldSource != null)
			taken.WorldSource.OnTransferredToCharacterInventory();

		GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		return true;
	}

	private void EnsureCharacterPanelComponents()
	{
		if (CharacterPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = CharacterPanel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			RuntimeInventoryModificationClick click = slot.GetComponent<RuntimeInventoryModificationClick>();
			if (click == null)
				click = slot.gameObject.AddComponent<RuntimeInventoryModificationClick>();
			click.Bind(this);

			if (slot.GetComponent<InventoryEquipDoubleClick>() == null)
				slot.gameObject.AddComponent<InventoryEquipDoubleClick>();

			RuntimeModificationSlotHighlightView highlight = slot.GetComponent<RuntimeModificationSlotHighlightView>();
			if (highlight == null)
				highlight = slot.gameObject.AddComponent<RuntimeModificationSlotHighlightView>();
			highlight.Bind(this);
		}

		EnsureGroundPanelComponents();
	}

	private void EnsureGroundPanelComponents()
	{
		if (GroundPanel == null)
			return;

		IReadOnlyList<InventorySlotView> groundSlots = GroundPanel.Slots;
		for (int i = 0; i < groundSlots.Count; i++)
		{
			InventorySlotView slot = groundSlots[i];
			if (slot == null)
				continue;

			RuntimeModificationSlotHighlightView highlight = slot.GetComponent<RuntimeModificationSlotHighlightView>();
			if (highlight == null)
				highlight = slot.gameObject.AddComponent<RuntimeModificationSlotHighlightView>();
			highlight.Bind(this);

			RuntimeInventoryModificationClick click = slot.GetComponent<RuntimeInventoryModificationClick>();
			if (click == null)
				click = slot.gameObject.AddComponent<RuntimeInventoryModificationClick>();
			click.Bind(this);
		}
	}

	private void RebuildInlineModificationRows()
	{
		CharacterInventory inventory = ActiveInventory;
		bool canBuildCharacterRows = CharacterPanel != null && inventory != null;

		if (CharacterPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(CharacterPanel);
		if (GroundPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(GroundPanel);

		m_WeaponSlotBindingBuffer.Clear();
		if (canBuildCharacterRows)
			CollectModifiableCharacterWeaponBindings(m_WeaponSlotBindingBuffer);

		CollectModifiableGroundWeaponBindings(m_WeaponSlotBindingBuffer);
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);

		if (m_WeaponSlotBindingBuffer.Count == 0)
		{
			CharacterPanel?.RebuildContentLayout();
			GroundPanel?.RebuildContentLayout();
			RefreshModificationCompatibilityHighlights();
			return;
		}

		for (int i = m_WeaponSlotBindingBuffer.Count - 1; i >= 0; i--)
		{
			WeaponSlotBinding binding = m_WeaponSlotBindingBuffer[i];
			if (binding.Panel == null || binding.SlotView == null || binding.WeaponData.IsEmpty)
				continue;

			BuildVisibleModificationDescriptors(binding, m_VisibleModificationDescriptorBuffer);
			if (m_VisibleModificationDescriptorBuffer.Count == 0)
				continue;

			bool expandEmpty = m_ModificationUiState.ExpandEmptySlots &&
			                   (binding.IsGroundSlot
				                   ? m_ModificationUiState.MatchesGround(binding.GroundSlotIndex)
				                   : m_ModificationUiState.MatchesCharacter(binding.IsMainHand, binding.BagIndex));
			RuntimeInlineModificationBuilder.RebuildWeaponRows(
				binding.Panel,
				this,
				binding.SlotView,
				binding.WeaponData,
				binding.IsMainHand,
				binding.BagIndex,
				binding.IsGroundSlot,
				binding.GroundSlotIndex,
				expandEmpty,
				m_VisibleModificationDescriptorBuffer);
		}

		CharacterPanel?.RebuildContentLayout();
		GroundPanel?.RebuildContentLayout();
		if (CharacterPanel != null)
			RuntimeInlineModificationBuilder.RefreshHighlights(CharacterPanel);
		if (GroundPanel != null)
			RuntimeInlineModificationBuilder.RefreshHighlights(GroundPanel);
		RefreshModificationCompatibilityHighlights();
	}

	private void CollectModifiableCharacterWeaponBindings(List<WeaponSlotBinding> _outBindings)
	{
		CharacterInventory inventory = ActiveInventory;
		if (CharacterPanel == null || inventory == null)
			return;

		IReadOnlyList<InventorySlotView> slots = CharacterPanel.Slots;
		int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);

		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null || !slot.HasItem)
				continue;

			bool isMainHand = i < lead && i == 0;
			int bagIndex = isMainHand ? -1 : i - lead;
			if (!isMainHand && (bagIndex < 0 || bagIndex >= inventory.BagCount))
				continue;

			if (!inventory.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponData))
				continue;

			if (!ItemModificationUtility.IsModifiableWeapon(weaponData.Definition))
				continue;

			_outBindings.Add(new WeaponSlotBinding(CharacterPanel, slot, weaponData, isMainHand, bagIndex));
		}
	}

	private void CollectModifiableGroundWeaponBindings(List<WeaponSlotBinding> _outBindings)
	{
		if (GroundPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = GroundPanel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null || !slot.HasItem)
				continue;

			if (slot.Data.WorldSource != null && !slot.Data.WorldSource.IsListedInGroundUi)
				continue;

			InventorySlotRuntimeData weaponData = MissionPrepInventoryCopyUtility.CloneSlot(slot.Data);
			if (!ItemModificationUtility.IsModifiableWeapon(weaponData.Definition))
				continue;

			_outBindings.Add(new WeaponSlotBinding(GroundPanel, slot, weaponData, false, -1, true, i));
		}
	}

	private void BuildVisibleModificationDescriptors(
		WeaponSlotBinding _binding,
		List<ItemModificationSlotDescriptor> _outVisibleDescriptors)
	{
		bool expandEmpty = m_ModificationUiState.ExpandEmptySlots &&
		                   (_binding.IsGroundSlot
			                   ? m_ModificationUiState.MatchesGround(_binding.GroundSlotIndex)
			                   : m_ModificationUiState.MatchesCharacter(_binding.IsMainHand, _binding.BagIndex));

		ItemModificationUtility.BuildVisibleModificationDescriptors(
			_binding.WeaponData,
			expandEmpty,
			m_ModificationDescriptorBuffer,
			_outVisibleDescriptors);
	}

	private bool TryGetGroundWeaponSlot(int _groundSlotIndex, out InventorySlotView _slotView, out InventorySlotRuntimeData _weaponSlot)
	{
		_slotView = null;
		_weaponSlot = default;

		if (GroundPanel == null || _groundSlotIndex < 0 || _groundSlotIndex >= GroundPanel.Slots.Count)
			return false;

		_slotView = GroundPanel.Slots[_groundSlotIndex];
		if (_slotView == null || !_slotView.HasItem)
			return false;

		_weaponSlot = MissionPrepInventoryCopyUtility.CloneSlot(_slotView.Data);
		return ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition);
	}

	private bool TryCommitGroundWeaponSlot(int _groundSlotIndex, InventorySlotRuntimeData _weaponSlot)
	{
		if (GroundPanel == null || _groundSlotIndex < 0 || _groundSlotIndex >= GroundPanel.Slots.Count)
			return false;

		InventorySlotView slotView = GroundPanel.Slots[_groundSlotIndex];
		if (slotView == null)
			return false;

		InventorySlotRuntimeData committed = _weaponSlot;
		if (slotView.HasItem && slotView.Data.WorldSource != null)
			committed.WorldSource = slotView.Data.WorldSource;

		slotView.SetItem(committed);
		if (committed.WorldSource != null)
			committed.WorldSource.ApplyInventorySlotData(committed);

		return true;
	}

	private void ValidateModificationUiSelection(IReadOnlyList<WeaponSlotBinding> _bindings)
	{
		if (!m_ModificationUiState.HasSelection)
			return;

		if (m_ModificationUiState.IsGroundSlot)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (binding.IsGroundSlot && m_ModificationUiState.MatchesGround(binding.GroundSlotIndex))
					return;
			}

			m_ModificationUiState = default;
			return;
		}

		for (int i = 0; i < _bindings.Count; i++)
		{
			WeaponSlotBinding binding = _bindings[i];
			if (binding.IsGroundSlot)
				continue;

			if (m_ModificationUiState.MatchesCharacter(binding.IsMainHand, binding.BagIndex))
				return;
		}

		m_ModificationUiState = default;
	}

	private void RefreshPanelHighlights(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;

		RuntimeModificationSlotHighlightView[] highlights =
			_panel.GetComponentsInChildren<RuntimeModificationSlotHighlightView>(true);
		for (int i = 0; i < highlights.Length; i++)
		{
			if (highlights[i] != null)
				highlights[i].RefreshHighlight();
		}
	}

	private void HandleModificationDragContextChanged()
	{
		RuntimeInlineModificationBuilder.RefreshHighlights(CharacterPanel);
		RefreshModificationCompatibilityHighlights();
	}

	private IEnumerator CoRefreshInlineModificationRowsNextFrame()
	{
		yield return null;
		m_DeferredInlineRefreshCoroutine = null;
		RebuildInlineModificationRows();
	}
	#endregion
}
