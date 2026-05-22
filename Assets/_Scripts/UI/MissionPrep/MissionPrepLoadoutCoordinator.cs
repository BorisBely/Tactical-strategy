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
	private readonly List<InventorySlotRuntimeData> m_AvailableSlotBuffer = new List<InventorySlotRuntimeData>();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		EnsureSharedPresetStore();
		m_EditingPresetCatalogIndex = Mathf.Max(0, m_DefaultEditingPresetIndex);
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

		int containerIndex = m_PresetInventoryPanel.GetInventorySlotContainerIndex(_slot);
		if (containerIndex < 0)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		if (containerIndex < lead)
		{
			_isMainHandEquipmentSlot = containerIndex == 0;
			return _isMainHandEquipmentSlot && !snapshot.MainHandEquipment.IsEmpty;
		}

		_bagIndex = containerIndex - lead;
		return _bagIndex >= 0 && _bagIndex < snapshot.BagCount;
	}

	public bool TryEditingPresetInventoryDoubleClick(InventorySlotView _slot)
	{
		if (!TryResolveInventorySlot(_slot, out bool isMainHand, out int bagIndex) || m_SharedPresetStore == null)
			return false;

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

	public void RepaintInventoryPanel()
	{
		if (m_PresetInventoryPanel == null)
			return;

		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null)
		{
			m_PresetInventoryPanel.ClearAllSlots();
			return;
		}

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		m_PresetInventoryPanel.RepaintFromPresetSnapshot(snapshot);
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
	#endregion
}
