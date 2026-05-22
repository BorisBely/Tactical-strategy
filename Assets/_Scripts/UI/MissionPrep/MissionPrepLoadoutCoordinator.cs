using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Редактирование общих пресетов каталога и опциональный превью на выбранном юните.
/// Панель инвентаря работает по <see cref="EditingPresetCatalogIndex"/>, без обязательного выбора юнита.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepLoadoutCoordinator : MonoBehaviour
{
	#region Static Access
	private static MissionPrepLoadoutCoordinator s_Instance;

	public static MissionPrepLoadoutCoordinator Instance => s_Instance;
	#endregion

	#region Serialized Fields
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[SerializeField] private MissionPrepSharedPresetStore m_SharedPresetStore;
	[SerializeField] private InventoryPanelView m_PresetInventoryPanel;
	[SerializeField] private InventoryPanelView m_AvailableEquipmentPanel;
	[SerializeField] private MissionPrepAvailableEquipmentCatalog m_AvailableEquipmentCatalog;
	[SerializeField, Min(0)] private int m_DefaultEditingPresetIndex;
	#endregion

	#region Private Fields
	private MissionPrepUnitPresetState m_BoundPresetState;
	private CharacterInventory m_BoundInventory;
	private int m_EditingPresetCatalogIndex;
	private MissionPrepModificationUiState m_ModificationUiState;
	private readonly List<InventorySlotRuntimeData> m_AvailableSlotBuffer = new List<InventorySlotRuntimeData>();
	private readonly List<ItemModificationSlotDescriptor> m_ModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<ItemModificationSlotDescriptor> m_VisibleModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<WeaponSlotBinding> m_WeaponSlotBindingBuffer = new List<WeaponSlotBinding>(8);
	#endregion

	private readonly struct WeaponSlotBinding
	{
		public readonly InventorySlotView SlotView;
		public readonly InventorySlotRuntimeData WeaponData;
		public readonly bool IsMainHand;
		public readonly int BagIndex;
		public readonly int ListIndex;

		public WeaponSlotBinding(
			InventorySlotView _slotView,
			InventorySlotRuntimeData _weaponData,
			bool _isMainHand,
			int _bagIndex,
			int _listIndex)
		{
			SlotView = _slotView;
			WeaponData = _weaponData;
			IsMainHand = _isMainHand;
			BagIndex = _bagIndex;
			ListIndex = _listIndex;
		}
	}

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		EnsureSharedPresetStore();
		m_EditingPresetCatalogIndex = Mathf.Max(0, m_DefaultEditingPresetIndex);
		MissionPrepModificationOutsideClick.EnsureOn(this);
	}

	private void OnEnable()
	{
		MissionPrepModificationDragContext.Changed += HandleModificationDragContextChanged;
	}

	private void OnDisable()
	{
		MissionPrepModificationDragContext.Changed -= HandleModificationDragContextChanged;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Properties
	public InventoryPanelView PresetInventoryPanel => m_PresetInventoryPanel;
	public InventoryPanelView AvailableEquipmentPanel => m_AvailableEquipmentPanel;
	public CharacterInventory BoundInventory => m_BoundInventory;
	public MissionPrepUnitPresetState BoundPresetState => m_BoundPresetState;
	public bool HasBoundUnit => m_BoundPresetState != null;
	public int EditingPresetCatalogIndex => m_EditingPresetCatalogIndex;
	#endregion

	#region Public Methods
	public void Configure(
		MissionPrepEquipmentPresetCatalog _catalog,
		InventoryPanelView _presetInventoryPanel,
		InventoryPanelView _availableInventoryPanel = null,
		MissionPrepAvailableEquipmentCatalog _availableCatalog = null)
	{
		if (_catalog != null)
			m_PresetCatalog = _catalog;

		if (_presetInventoryPanel != null)
			m_PresetInventoryPanel = _presetInventoryPanel;

		if (_availableInventoryPanel != null)
			m_AvailableEquipmentPanel = _availableInventoryPanel;

		if (_availableCatalog != null)
			m_AvailableEquipmentCatalog = _availableCatalog;

		EnsureSharedPresetStore();
		MissionPrepPresetInventoryDropZone.EnsureOnPresetPanel(m_PresetInventoryPanel, this);
		MissionPrepAvailableEquipmentDropZone.EnsureOnAvailablePanel(m_AvailableEquipmentPanel, this);
	}

	/// <summary>Показать и подготовить редактирование пресета каталога (без выбора юнита).</summary>
	public void BeginEditingPresets(int _initialPresetIndex = -1)
	{
		MissionPrepPresetInventoryDropZone.EnsureOnPresetPanel(m_PresetInventoryPanel, this);
		MissionPrepAvailableEquipmentDropZone.EnsureOnAvailablePanel(m_AvailableEquipmentPanel, this);
		EnsureSharedPresetStoreInitialized();

		int index = _initialPresetIndex >= 0 ? _initialPresetIndex : m_EditingPresetCatalogIndex;
		m_EditingPresetCatalogIndex = m_PresetCatalog != null
			? m_PresetCatalog.ClampPresetIndex(index)
			: Mathf.Max(0, index);

		m_SharedPresetStore.EnsureSnapshotDefaultsFromCatalog(m_EditingPresetCatalogIndex, m_PresetCatalog);
		RepaintInventoryPanel();
		RepaintAvailableEquipmentPanel();
	}

	public void SetEditingPresetCatalogIndex(int _presetIndex)
	{
		int clamped = m_PresetCatalog != null
			? m_PresetCatalog.ClampPresetIndex(_presetIndex)
			: Mathf.Max(0, _presetIndex);

		m_EditingPresetCatalogIndex = clamped;
		m_SharedPresetStore?.EnsureSnapshotDefaultsFromCatalog(clamped, m_PresetCatalog);
		ClearModificationUiSelection();
		RepaintInventoryPanel();
	}

	/// <summary>Выбор юнита: превью его назначенного пресета на модели; панель остаётся на редактируемом пресете.</summary>
	public void BindUnit(GameObject _unitRoot)
	{
		m_BoundPresetState = _unitRoot != null
			? MissionPrepUnitPresetState.GetOrCreate(_unitRoot, 0)
			: null;

		m_BoundInventory = _unitRoot != null
			? _unitRoot.GetComponentInChildren<CharacterInventory>(true)
			: null;

		EnsureSharedPresetStoreInitialized();

		if (m_BoundPresetState != null)
		{
			m_EditingPresetCatalogIndex = m_PresetCatalog != null
				? m_PresetCatalog.ClampPresetIndex(m_BoundPresetState.PresetCatalogIndex)
				: m_BoundPresetState.PresetCatalogIndex;

			m_BoundPresetState.EnsureSnapshotDefaultsFromCatalog(m_EditingPresetCatalogIndex, m_PresetCatalog);
			ApplyUnitAssignedPresetToRuntime();
			RepaintInventoryPanel();
		}

		RepaintAvailableEquipmentPanel();
	}

	public void ClearUnitBinding()
	{
		m_BoundPresetState = null;
		m_BoundInventory = null;
		RepaintInventoryPanel();
		RepaintAvailableEquipmentPanel();
	}

	/// <summary>Смена пресета в дропдауне: общий снимок + назначение выбранному юниту (если есть).</summary>
	public void SwitchToPreset(int _newPresetIndex)
	{
		int clamped = m_PresetCatalog != null
			? m_PresetCatalog.ClampPresetIndex(_newPresetIndex)
			: Mathf.Max(0, _newPresetIndex);

		m_EditingPresetCatalogIndex = clamped;
		m_SharedPresetStore?.EnsureSnapshotDefaultsFromCatalog(clamped, m_PresetCatalog);
		ClearModificationUiSelection();

		if (m_BoundPresetState != null)
		{
			int presetCount = GetPresetSlotCount();
			if (m_BoundInventory != null)
				m_BoundPresetState.ChangeActivePresetIndex(clamped, m_BoundInventory, presetCount);
			else
				m_BoundPresetState.SetActivePresetIndex(clamped, presetCount);

			ApplyUnitAssignedPresetToRuntime();
		}

		RepaintInventoryPanel();
	}

	public void SetActivePresetArmor(int _armorIndex)
	{
		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null)
			return;

		int clamped = m_PresetCatalog != null
			? m_PresetCatalog.ClampArmorIndex(_armorIndex)
			: Mathf.Clamp(_armorIndex, 0, MissionPrepUnitArmorVisualController.ArmorVariantCount - 1);

		m_SharedPresetStore.SetArmorForPreset(m_EditingPresetCatalogIndex, clamped);
		PropagatePresetToAllUnits(m_EditingPresetCatalogIndex, _refreshBoundUnitRuntime: true);
	}

	public void NotifyInventoryMutated()
	{
		PropagatePresetToAllUnits(m_EditingPresetCatalogIndex, _refreshBoundUnitRuntime: true);
		RepaintInventoryPanel();
	}

	public void PropagatePresetToAllUnitsWithCatalogIndex(int _presetIndex)
	{
		PropagatePresetToAllUnits(_presetIndex, _refreshBoundUnitRuntime: true);
	}

	public void RepaintAvailableEquipmentPanel()
	{
		if (m_AvailableEquipmentPanel == null || !m_AvailableEquipmentPanel.IsConfiguredForDynamicRepaint)
			return;

		if (m_AvailableEquipmentCatalog == null)
		{
			m_AvailableEquipmentPanel.ClearAllSlots();
			return;
		}

		m_AvailableEquipmentCatalog.BuildSlotList(m_AvailableSlotBuffer);
		m_AvailableEquipmentPanel.RepaintFromSlotList(m_AvailableSlotBuffer);
	}

	public bool TryTransferAvailableSlotToPreset(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || m_SharedPresetStore == null)
			return false;

		if (!IsAvailableEquipmentSlot(_slot))
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		InventorySlotRuntimeData clone = MissionPrepInventoryCopyUtility.CloneSlot(_slot.Data);
		if (clone.IsEmpty || !snapshot.TryAddToBag(clone))
			return false;

		NotifyInventoryMutated();
		return true;
	}

	public bool TryAcceptAvailableDrag(MissionPrepAvailableToPresetDrag _drag)
	{
		if (_drag == null)
			return false;

		InventorySlotView slot = _drag.SlotView;
		if (slot == null || !slot.HasItem)
			return false;

		if (!TryTransferAvailableSlotToPreset(slot))
			return false;

		_drag.NotifyDropAccepted();
		return true;
	}

	/// <summary>Удалить предмет из снимка пресета (слот оружия или сумки).</summary>
	public bool TryRemovePresetInventorySlot(bool _isMainHandEquipmentSlot, int _bagIndex)
	{
		if (m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		bool changed = _isMainHandEquipmentSlot
			? snapshot.TryClearMainHand()
			: snapshot.TryRemoveBagItemAt(_bagIndex);

		if (!changed)
			return false;

		NotifyInventoryMutated();
		return true;
	}

	/// <summary>Курсор над панелью инвентаря пресета (для fallback при EndDrag).</summary>
	public bool IsScreenPointOverPresetInventoryPanel(Vector2 _screenPosition, Camera _eventCamera)
	{
		if (m_PresetInventoryPanel == null)
			return false;

		Transform panelRoot = m_PresetInventoryPanel.transform;
		var results = new List<RaycastResult>();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};

		EventSystem.current.RaycastAll(pointerData, results);
		for (int i = 0; i < results.Count; i++)
		{
			Transform hit = results[i].gameObject.transform;
			if (hit == panelRoot || hit.IsChildOf(panelRoot))
				return true;
		}

		return false;
	}

	/// <summary>Курсор над панелью доступного снаряжения (для fallback при EndDrag).</summary>
	public bool IsScreenPointOverAvailableEquipmentPanel(Vector2 _screenPosition, Camera _eventCamera)
	{
		if (m_AvailableEquipmentPanel == null)
			return false;

		Transform panelRoot = m_AvailableEquipmentPanel.transform;
		var results = new List<RaycastResult>();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};

		EventSystem.current.RaycastAll(pointerData, results);
		for (int i = 0; i < results.Count; i++)
		{
			Transform hit = results[i].gameObject.transform;
			if (hit == panelRoot || hit.IsChildOf(panelRoot))
				return true;
		}

		return false;
	}

	public bool TryResolveInventorySlot(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_bagIndex = -1;

		if (_slot == null || m_PresetInventoryPanel == null || m_SharedPresetStore == null)
			return false;

		int slotIndex = m_PresetInventoryPanel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		if (slotIndex < lead)
		{
			_isMainHandEquipmentSlot = slotIndex == 0;
			return _isMainHandEquipmentSlot && !snapshot.MainHandEquipment.IsEmpty;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0 && _bagIndex < snapshot.BagCount;
	}

	public bool TryEditingPresetInventoryDoubleClick(InventorySlotView _slot)
	{
		if (!TryResolveInventorySlot(_slot, out bool isMainHand, out int bagIndex) || m_SharedPresetStore == null)
			return false;

		ClearModificationUiSelection();
		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		bool changed;

		if (isMainHand)
			changed = snapshot.TryUnequipMainHandToBag();
		else
			changed = snapshot.TryMoveBagItemToMainHand(bagIndex);

		if (!changed)
			return false;

		NotifyInventoryMutated();
		return true;
	}

	public bool TryToggleModificationPanel(InventorySlotView _slot)
	{
		if (!TryResolveInventorySlot(_slot, out bool isMainHand, out int bagIndex) || m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (!ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
			return false;

		if (m_ModificationUiState.Matches(isMainHand, bagIndex))
			m_ModificationUiState.ExpandEmptySlots = !m_ModificationUiState.ExpandEmptySlots;
		else
			m_ModificationUiState = MissionPrepModificationUiState.CreateSelection(isMainHand, bagIndex, _expandEmptySlots: true);

		RebuildInlineModificationRows();
		return true;
	}

	public bool HasExpandedEmptyModificationSlots()
	{
		return m_ModificationUiState.HasSelection && m_ModificationUiState.ExpandEmptySlots;
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
		MissionPrepInlineModificationBuilder.ClearAllRows(m_PresetInventoryPanel);
	}

	public void CloseModificationPanel()
	{
		ClearModificationUiSelection();
	}

	public bool TryInstallModificationFromDrag(ItemModificationSlotDescriptor _slotDescriptor, bool _weaponIsMainHand, int _weaponBagIndex)
	{
		MissionPrepModificationDragPayload payload = MissionPrepModificationDragContext.Current;
		if (!payload.HasItem || m_SharedPresetStore == null)
			return false;

		if (payload.SourceKind == MissionPrepModificationDragSourceKind.ModificationSlot)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (payload.SourceKind == MissionPrepModificationDragSourceKind.PresetBag)
		{
			if (payload.PresetBagIndex < 0)
				return false;

			if (!_weaponIsMainHand && payload.PresetBagIndex == _weaponBagIndex)
				return false;

			if (!snapshot.TryGetInventorySlot(_isMainHandEquipmentSlot: false, payload.PresetBagIndex, out _))
				return false;
		}

		if (!ItemModificationUtility.CanAcceptItem(_slotDescriptor, weaponSlot, payload.Item))
			return false;

		InventorySlotRuntimeData candidate = MissionPrepInventoryCopyUtility.CloneSlot(payload.Item);
		if (!ItemModificationUtility.TryInstallAtSlot(_slotDescriptor, weaponSlot, candidate, out InventorySlotRuntimeData replacedItem))
			return false;

		int targetBagIndex = _weaponBagIndex;
		if (payload.SourceKind == MissionPrepModificationDragSourceKind.PresetBag)
		{
			if (!snapshot.TryRemoveInventorySlot(_isMainHandEquipmentSlot: false, payload.PresetBagIndex, out _))
				return false;

			if (!_weaponIsMainHand && payload.PresetBagIndex < targetBagIndex)
				targetBagIndex--;
		}

		if (!snapshot.TrySetInventorySlot(_weaponIsMainHand, targetBagIndex, weaponSlot))
			return false;

		if (!replacedItem.IsEmpty)
			snapshot.TryAddToBag(replacedItem);

		MissionPrepModificationDragContext.NotifyDropConsumed();
		NotifyInventoryMutated();
		return true;
	}

	public bool TryClearModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		bool _addToBag = true)
	{
		if (m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (!ItemModificationUtility.TryClearSlot(_slotDescriptor, weaponSlot, out InventorySlotRuntimeData removedItem))
			return false;

		if (!removedItem.IsEmpty && _addToBag)
			snapshot.TryAddToBag(removedItem);

		snapshot.TrySetInventorySlot(_weaponIsMainHand, _weaponBagIndex, weaponSlot);
		NotifyInventoryMutated();
		return true;
	}

	public bool TryEjectModificationSlotToPreset(MissionPrepModificationSlotDrag _drag)
	{
		if (_drag == null)
			return false;

		MissionPrepModificationSlotDrag.CleanupActiveDragVisual();
		return TryClearModificationSlot(
			_drag.SlotDescriptor,
			_drag.WeaponIsMainHand,
			_drag.WeaponBagIndex,
			_addToBag: true);
	}

	public bool TryEjectModificationSlotToAvailable(MissionPrepModificationSlotDrag _drag)
	{
		if (_drag == null)
			return false;

		MissionPrepModificationSlotDrag.CleanupActiveDragVisual();
		return TryClearModificationSlot(
			_drag.SlotDescriptor,
			_drag.WeaponIsMainHand,
			_drag.WeaponBagIndex,
			_addToBag: false);
	}

	public void RepaintInventoryPanel()
	{
		if (m_PresetInventoryPanel == null)
			return;

		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null)
		{
			ClearModificationUiSelection();
			m_PresetInventoryPanel.ClearAllSlots();
			return;
		}

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		m_PresetInventoryPanel.RepaintFromPresetSnapshot(snapshot);
		EnsureModificationClickHandlers();
		RebuildInlineModificationRows();
	}

	public bool TryGetActivePresetLabel(out string _label)
	{
		return TryGetPresetLabel(m_EditingPresetCatalogIndex, out _label);
	}

	public bool TryGetPresetLabelForUnit(MissionPrepUnitPresetState _unit, out string _label)
	{
		_label = string.Empty;
		if (_unit == null)
			return false;

		return TryGetPresetLabel(_unit.PresetCatalogIndex, out _label);
	}

	public bool TryGetPresetLabel(int _presetIndex, out string _label)
	{
		_label = string.Empty;

		if (m_PresetCatalog == null)
			return false;

		_label = m_PresetCatalog.GetPresetLabel(m_PresetCatalog.ClampPresetIndex(_presetIndex));
		return !string.IsNullOrEmpty(_label);
	}

	public int GetActivePresetArmorIndex()
	{
		return m_SharedPresetStore != null
			? m_SharedPresetStore.GetArmorForPreset(m_EditingPresetCatalogIndex)
			: 0;
	}
	#endregion

	#region Private Methods
	private void EnsureSharedPresetStore()
	{
		if (m_SharedPresetStore == null)
			m_SharedPresetStore = MissionPrepSharedPresetStore.GetOrCreate(this);
	}

	private void EnsureSharedPresetStoreInitialized()
	{
		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null)
			return;

		m_SharedPresetStore.EnsurePresetSnapshots(GetPresetSlotCount());
		m_SharedPresetStore.EnsureDefaultsFromCatalog(m_PresetCatalog);
	}

	private void PropagatePresetToAllUnits(int _presetIndex, bool _refreshBoundUnitRuntime)
	{
		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null)
			return;

		MissionPrepUnitPresetState[] units = FindObjectsByType<MissionPrepUnitPresetState>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None);
		for (int i = 0; i < units.Length; i++)
		{
			MissionPrepUnitPresetState unit = units[i];
			if (unit == null || unit.PresetCatalogIndex != _presetIndex)
				continue;

			bool isBoundUnit = m_BoundPresetState != null && unit == m_BoundPresetState;
			if (isBoundUnit && !_refreshBoundUnitRuntime)
				continue;

			CharacterInventory inventory = unit.GetComponentInChildren<CharacterInventory>(true);
			if (inventory != null)
			{
				m_SharedPresetStore.ApplyPresetToInventory(_presetIndex, inventory);
				UnitWeaponRuntime weaponRuntime = inventory.GetComponentInChildren<UnitWeaponRuntime>(true);
				if (weaponRuntime != null)
					weaponRuntime.RefreshFromEquipment();
			}

			int armorIndex = m_SharedPresetStore.GetArmorForPreset(_presetIndex);
			MissionPrepUnitArmorVisualController visual =
				MissionPrepUnitArmorVisualController.GetOrCreate(unit.gameObject, armorIndex);
			visual.ApplyArmorVisual(armorIndex);
		}
	}

	private int GetPresetSlotCount()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.PresetCount;

		return 2;
	}

	private void ApplyUnitAssignedPresetToRuntime()
	{
		if (m_BoundPresetState == null)
			return;

		if (m_BoundInventory != null)
		{
			m_BoundPresetState.ApplyActivePresetToRuntime(m_BoundInventory);
			UnitWeaponRuntime weaponRuntime = m_BoundInventory.GetComponentInChildren<UnitWeaponRuntime>(true);
			if (weaponRuntime != null)
				weaponRuntime.RefreshFromEquipment();
		}

		GameObject unitRoot = m_BoundPresetState.gameObject;
		int armorIndex = m_BoundPresetState.ActivePresetArmorIndex;
		MissionPrepUnitArmorVisualController visual = MissionPrepUnitArmorVisualController.GetOrCreate(unitRoot, armorIndex);
		visual.ApplyArmorVisual(armorIndex);
	}

	private bool IsAvailableEquipmentSlot(InventorySlotView _slot)
	{
		if (_slot == null || m_AvailableEquipmentPanel == null)
			return false;

		if (IsSlotOnPanel(_slot, m_AvailableEquipmentPanel))
			return true;

		// Во время drag ячейка на root canvas, не под панелью.
		if (!_slot.TryGetComponent(out MissionPrepAvailableToPresetDrag drag))
			return false;

		return drag.SourceAvailablePanel == m_AvailableEquipmentPanel;
	}

	private static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_slot == null || _panel == null)
			return false;

		return _slot.GetComponentInParent<InventoryPanelView>() == _panel;
	}

	private void EnsureModificationClickHandlers()
	{
		if (m_PresetInventoryPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			MissionPrepInventoryModificationClick click = slot.GetComponent<MissionPrepInventoryModificationClick>();
			if (click == null)
				click = slot.gameObject.AddComponent<MissionPrepInventoryModificationClick>();

			click.Bind(this);
		}
	}

	private void RebuildInlineModificationRows()
	{
		if (m_PresetInventoryPanel == null || m_SharedPresetStore == null)
			return;

		MissionPrepInlineModificationBuilder.ClearAllRows(m_PresetInventoryPanel);
		CollectModifiableWeaponBindings(m_WeaponSlotBindingBuffer);
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);
		if (m_WeaponSlotBindingBuffer.Count == 0)
		{
			m_PresetInventoryPanel.RebuildContentLayout();
			return;
		}

		for (int i = m_WeaponSlotBindingBuffer.Count - 1; i >= 0; i--)
		{
			WeaponSlotBinding binding = m_WeaponSlotBindingBuffer[i];
			if (binding.SlotView == null || binding.WeaponData.IsEmpty)
				continue;

			BuildVisibleModificationDescriptors(binding, m_VisibleModificationDescriptorBuffer);
			if (m_VisibleModificationDescriptorBuffer.Count == 0)
				continue;

			bool expandEmpty = m_ModificationUiState.Matches(binding.IsMainHand, binding.BagIndex) &&
			                   m_ModificationUiState.ExpandEmptySlots;
			MissionPrepInlineModificationBuilder.RebuildWeaponRows(
				m_PresetInventoryPanel,
				this,
				binding.SlotView,
				binding.WeaponData,
				binding.IsMainHand,
				binding.BagIndex,
				expandEmpty,
				m_VisibleModificationDescriptorBuffer);
		}

		m_PresetInventoryPanel.RebuildContentLayout();
		MissionPrepInlineModificationBuilder.RefreshHighlights(m_PresetInventoryPanel);
	}

	private void CollectModifiableWeaponBindings(List<WeaponSlotBinding> _outBindings)
	{
		_outBindings.Clear();
		if (m_PresetInventoryPanel == null || m_SharedPresetStore == null)
			return;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);

		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null || !slot.HasItem)
				continue;

			bool isMainHand = i < lead && i == 0;
			int bagIndex = isMainHand ? -1 : i - lead;
			if (!isMainHand && (bagIndex < 0 || bagIndex >= snapshot.BagCount))
				continue;

			if (!snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponData))
				continue;

			if (!ItemModificationUtility.IsModifiableWeapon(weaponData.Definition))
				continue;

			_outBindings.Add(new WeaponSlotBinding(slot, weaponData, isMainHand, bagIndex, i));
		}
	}

	private void BuildVisibleModificationDescriptors(
		WeaponSlotBinding _binding,
		List<ItemModificationSlotDescriptor> _outVisibleDescriptors)
	{
		_outVisibleDescriptors.Clear();
		ItemModificationUtility.BuildSlotDescriptors(_binding.WeaponData.Definition, m_ModificationDescriptorBuffer);

		bool expandEmpty = m_ModificationUiState.Matches(_binding.IsMainHand, _binding.BagIndex) &&
		                   m_ModificationUiState.ExpandEmptySlots;

		for (int i = 0; i < m_ModificationDescriptorBuffer.Count; i++)
		{
			ItemModificationSlotDescriptor descriptor = m_ModificationDescriptorBuffer[i];
			bool hasInstalledItem = ItemModificationUtility.TryGetInstalledItem(
				descriptor,
				_binding.WeaponData,
				out _);

			if (hasInstalledItem || expandEmpty)
				_outVisibleDescriptors.Add(descriptor);
		}
	}

	private void ValidateModificationUiSelection(IReadOnlyList<WeaponSlotBinding> _bindings)
	{
		if (!m_ModificationUiState.HasSelection)
			return;

		for (int i = 0; i < _bindings.Count; i++)
		{
			WeaponSlotBinding binding = _bindings[i];
			if (m_ModificationUiState.Matches(binding.IsMainHand, binding.BagIndex))
				return;
		}

		m_ModificationUiState = default;
	}

	private void HandleModificationDragContextChanged()
	{
		MissionPrepInlineModificationBuilder.RefreshHighlights(m_PresetInventoryPanel);
	}
	#endregion
}
