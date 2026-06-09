using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

	#region Events
	public event Action ModificationGraphDataChanged;
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
	private MissionPrepRuntimePresetRegistry m_RuntimePresetRegistry;
	private MissionPrepModificationUiState m_ModificationUiState;
	private readonly List<InventorySlotRuntimeData> m_AvailableSlotBuffer = new List<InventorySlotRuntimeData>();
	private readonly List<ItemModificationSlotDescriptor> m_ModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<ItemModificationSlotDescriptor> m_VisibleModificationDescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);
	private readonly List<WeaponSlotBinding> m_WeaponSlotBindingBuffer = new List<WeaponSlotBinding>(8);
	private UnitWeaponReloadController m_SubscribedBoundReloadController;
	private bool m_PendingInlineRefresh;
	private Coroutine m_DeferredInlineRefreshCoroutine;
	private Coroutine m_DeferredForcedExpandedRepaintCoroutine;
	private int m_SuppressOutsideClickUntilFrame = -1;
	private int m_ExpandedWeaponListIndex = -1;
	private bool m_KeepExpandedAfterModificationMutation;
	private bool m_KeepExpandedIsMainHand;
	private int m_KeepExpandedBagIndex = -1;
	private ItemDefinition m_KeepExpandedWeaponDefinition;
	private bool m_KeepExpandedRequiresInstalledModification;
	private InventorySlotRuntimeData m_HoveredModificationPreviewCandidate;
	private InventorySlotRuntimeData m_HoveredWeaponGraphCandidate;
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
		EnsureRuntimePresetRegistry();
		m_EditingPresetCatalogIndex = Mathf.Max(0, m_DefaultEditingPresetIndex);
		MissionPrepModificationOutsideClick.EnsureOn(this);
	}

	private void OnEnable()
	{
		MissionPrepModificationDragContext.Changed += HandleModificationDragContextChanged;
		TrySubscribeBoundUnitReloadCompletionHandler();
	}

	private void OnDisable()
	{
		MissionPrepModificationDragContext.Changed -= HandleModificationDragContextChanged;
		TryUnsubscribeBoundUnitReloadCompletionHandler();
		MissionPrepModificationSlotDrag.CleanupActiveDragVisual();
		MissionPrepModificationDragContext.ResetAfterDrag();
		if (m_DeferredInlineRefreshCoroutine != null)
		{
			StopCoroutine(m_DeferredInlineRefreshCoroutine);
			m_DeferredInlineRefreshCoroutine = null;
		}
		if (m_DeferredForcedExpandedRepaintCoroutine != null)
		{
			StopCoroutine(m_DeferredForcedExpandedRepaintCoroutine);
			m_DeferredForcedExpandedRepaintCoroutine = null;
		}

		ClearForcedExpandedModificationSelection();
		m_ModificationUiState = default;

		if (!Application.isPlaying)
			UiEventSystemTeardownUtility.ReleaseAllPointers();
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
		m_EditingPresetCatalogIndex = ClampPresetCatalogIndex(index);

		m_SharedPresetStore.EnsureSnapshotDefaultsFromCatalog(m_EditingPresetCatalogIndex, m_PresetCatalog);
		RepaintInventoryPanel();
		RepaintAvailableEquipmentPanel();
	}

	public void SetEditingPresetCatalogIndex(int _presetIndex)
	{
		int clamped = ClampPresetCatalogIndex(_presetIndex);

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

		TryUnsubscribeBoundUnitReloadCompletionHandler();
		TrySubscribeBoundUnitReloadCompletionHandler();

		EnsureSharedPresetStoreInitialized();

		if (m_BoundPresetState != null)
		{
			m_EditingPresetCatalogIndex = ClampPresetCatalogIndex(m_BoundPresetState.PresetCatalogIndex);

			m_BoundPresetState.EnsureSnapshotDefaultsFromCatalog(m_EditingPresetCatalogIndex, m_PresetCatalog);
			ApplyUnitAssignedPresetToRuntime();
			SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
			RepaintInventoryPanel();
		}

		RepaintAvailableEquipmentPanel();
	}

	public void ClearUnitBinding()
	{
		TryUnsubscribeBoundUnitReloadCompletionHandler();
		m_BoundPresetState = null;
		m_BoundInventory = null;
		RepaintInventoryPanel();
		RepaintAvailableEquipmentPanel();
	}

	/// <summary>Смена пресета в дропдауне: общий снимок + назначение выбранному юниту (если есть).</summary>
	public void SwitchToPreset(int _newPresetIndex)
	{
		int clamped = ClampPresetCatalogIndex(_newPresetIndex);

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

	public void NotifyInventoryMutated(bool _saveSnapshotFromRuntime = true)
	{
		m_HoveredModificationPreviewCandidate = default;
		m_HoveredWeaponGraphCandidate = default;
		PropagatePresetToAllUnits(
			m_EditingPresetCatalogIndex,
			_refreshBoundUnitRuntime: true,
			_saveSnapshotFromRuntime);

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
		EnsureAvailableEquipmentDragComponents();
		RefreshModificationCompatibilityHighlights();
	}

	private void EnsureAvailableEquipmentDragComponents()
	{
		if (m_AvailableEquipmentPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = m_AvailableEquipmentPanel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			if (slot.GetComponent<MissionPrepAvailableToPresetDrag>() == null)
				slot.gameObject.AddComponent<MissionPrepAvailableToPresetDrag>();

			if (slot.GetComponent<MissionPrepAvailableEquipDoubleClick>() == null)
				slot.gameObject.AddComponent<MissionPrepAvailableEquipDoubleClick>();

			EnsureModificationPreviewHover(slot);
			EnsureWeaponProfileGraphHover(slot);

			MissionPrepAvailableEquipmentSlotHighlightView highlight =
				slot.GetComponent<MissionPrepAvailableEquipmentSlotHighlightView>();
			if (highlight == null)
				highlight = slot.gameObject.AddComponent<MissionPrepAvailableEquipmentSlotHighlightView>();

			highlight.Bind(this);
		}
	}

	public bool TryTransferAvailableSlotToPreset(InventorySlotView _slot)
	{
		if (MissionPrepModificationDragContext.WasDropConsumed ||
		    _slot == null || !_slot.HasItem || m_SharedPresetStore == null)
			return false;

		if (_slot.TryGetComponent(out MissionPrepPresetToAvailableDrag presetDrag) &&
		    presetDrag.IsDraggingFromPreset)
			return false;

		if (!IsAvailableEquipmentSlot(_slot))
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		InventorySlotRuntimeData clone = MissionPrepInventoryCopyUtility.CloneSlot(_slot.Data);
		if (clone.IsEmpty || !snapshot.TryAddToBag(clone))
			return false;

		MissionPrepModificationDragContext.NotifyDropConsumed();
		NotifyInventoryMutated(_saveSnapshotFromRuntime: false);
		return true;
	}

	public bool TryAcceptAvailableDrag(MissionPrepAvailableToPresetDrag _drag)
	{
		if (_drag == null || !_drag.IsDraggingFromAvailable)
			return false;

		InventorySlotView slot = _drag.SlotView;
		if (slot == null || !slot.HasItem)
			return false;

		if (!TryTransferAvailableSlotToPreset(slot))
			return false;

		_drag.NotifyDropAccepted();
		return true;
	}

	/// <summary>Снять оружие из слота экипировки пресета в сумку (drag в область инвентаря).</summary>
	public bool TryUnequipPresetMainHandToBag()
	{
		if (MissionPrepModificationDragContext.WasDropConsumed || m_SharedPresetStore == null)
			return false;

		SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		ItemDefinition weaponDefinition = null;
		ItemInstanceState weaponInstance = null;
		if (!snapshot.MainHandEquipment.IsEmpty)
		{
			weaponDefinition = snapshot.MainHandEquipment.Definition;
			weaponInstance = snapshot.MainHandEquipment.InstanceState;
		}

		if (!snapshot.TryUnequipMainHandToBag())
			return false;

		MissionPrepModificationDragContext.NotifyDropConsumed();
		RemapModificationSelectionAfterWeaponSlotChange(
			weaponDefinition,
			weaponInstance,
			_fromMainHand: true,
			_fromBagIndex: -1);
		NotifyInventoryMutated();
		return true;
	}

	/// <summary>Переместить оружие из сумки пресета в слот экипировки.</summary>
	public bool TryMovePresetBagItemToMainHand(int _bagIndex)
	{
		if (MissionPrepModificationDragContext.WasDropConsumed || m_SharedPresetStore == null)
			return false;

		SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		ItemDefinition weaponDefinition = null;
		ItemInstanceState weaponInstance = null;
		if (_bagIndex >= 0 && _bagIndex < snapshot.BagCount &&
		    snapshot.TryGetInventorySlot(false, _bagIndex, out InventorySlotRuntimeData bagWeapon))
		{
			weaponDefinition = bagWeapon.Definition;
			weaponInstance = bagWeapon.InstanceState;
		}

		if (!snapshot.TryMoveBagItemToMainHand(_bagIndex))
			return false;

		MissionPrepModificationDragContext.NotifyDropConsumed();
		RemapModificationSelectionAfterWeaponSlotChange(
			weaponDefinition,
			weaponInstance,
			_fromMainHand: false,
			_fromBagIndex: _bagIndex);
		NotifyInventoryMutated();
		return true;
	}

	/// <summary>Экипировать копию оружия с панели доступного снаряжения в основную руку пресета.</summary>
	public bool TryEquipAvailableSlotToMainHand(InventorySlotView _slot)
	{
		if (MissionPrepModificationDragContext.WasDropConsumed ||
		    _slot == null || !_slot.HasItem || m_SharedPresetStore == null)
			return false;

		if (!IsAvailableEquipmentSlot(_slot))
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		InventorySlotRuntimeData clone = MissionPrepInventoryCopyUtility.CloneSlot(_slot.Data);
		if (!MissionPrepWeaponEquipUtility.CanEquipToMainHand(clone))
			return false;

		SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
		if (!snapshot.MainHandEquipment.IsEmpty)
			snapshot.TryUnequipMainHandToBag();

		if (!snapshot.TrySetInventorySlot(_isMainHandEquipmentSlot: true, _bagIndex: -1, clone))
			return false;

		MissionPrepModificationDragContext.NotifyDropConsumed();
		ClearModificationUiSelection();
		NotifyInventoryMutated();
		return true;
	}

	/// <summary>Drag внутри инвентаря пресета: снятие оружия из слота экипировки в сумку.</summary>
	public bool TryAcceptPresetInventoryInternalDrag(MissionPrepPresetToAvailableDrag _drag)
	{
		if (_drag == null || !_drag.HasResolvedSlot || !_drag.IsMainHandSlot)
			return false;

		return TryUnequipPresetMainHandToBag();
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

	/// <summary>Курсор над ячейкой экипированного оружия пресета.</summary>
	public bool IsScreenPointOverPresetMainHandSlot(Vector2 _screenPosition, Camera _eventCamera)
	{
		InventorySlotView mainHandSlot = InventorySlotUiUtility.GetMainHandEquipmentSlot(m_PresetInventoryPanel);
		return InventorySlotUiUtility.IsScreenPointOverMainHandEquipmentSlot(
			mainHandSlot, _screenPosition, _eventCamera);
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

		if (!TryResolveInventoryDropTarget(_slot, out _isMainHandEquipmentSlot, out _bagIndex))
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (_isMainHandEquipmentSlot)
			return !snapshot.MainHandEquipment.IsEmpty;

		return _bagIndex >= 0 && _bagIndex < snapshot.BagCount;
	}

	/// <summary>Ячейка UI пресета как цель drop (допускает пустой слот экипировки).</summary>
	public bool TryResolveInventoryDropTarget(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_bagIndex = -1;

		if (_slot == null || m_PresetInventoryPanel == null)
			return false;

		int slotIndex = m_PresetInventoryPanel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
			return false;

		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		if (slotIndex < lead)
		{
			_isMainHandEquipmentSlot = slotIndex == 0;
			return _isMainHandEquipmentSlot;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0;
	}

	public bool TryEditingPresetInventoryDoubleClick(InventorySlotView _slot)
	{
		if (!TryResolveInventorySlot(_slot, out bool isMainHand, out int bagIndex) || m_SharedPresetStore == null)
			return false;

		SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		ItemDefinition weaponDefinition = null;
		ItemInstanceState weaponInstance = null;
		if (snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData clickedWeapon))
		{
			weaponDefinition = clickedWeapon.Definition;
			weaponInstance = clickedWeapon.InstanceState;
		}

		bool changed;

		if (isMainHand)
			changed = snapshot.TryUnequipMainHandToBag();
		else
			changed = snapshot.TryMoveBagItemToMainHand(bagIndex);

		if (!changed)
			return false;

		RemapModificationSelectionAfterWeaponSlotChange(
			weaponDefinition,
			weaponInstance,
			_fromMainHand: isMainHand,
			_fromBagIndex: bagIndex);

		m_ExpandedWeaponListIndex = -1;
		RefreshModificationCompatibilityHighlights();
		NotifyInventoryMutated();
		return true;
	}

	public bool TryToggleModificationPanel(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || m_SharedPresetStore == null)
			return false;

		if (!ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
			return false;

		if (!TryResolveInventoryDropTarget(_slot, out bool isMainHand, out int bagIndex))
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		InventorySlotRuntimeData weaponSlot = _slot.Data;
		if (snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData snapshotSlot) &&
		    !snapshotSlot.IsEmpty)
			weaponSlot = snapshotSlot;

		if (!ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
			return false;

		if (IsSameWeaponAsSelection(weaponSlot.InstanceState, isMainHand, bagIndex))
		{
			if (m_ModificationUiState.IsExpanded)
			{
				m_ExpandedWeaponListIndex = -1;
				SetDisplayState(RuntimeModifiableWeaponDisplayState.Collapsed);
			}
			else
			{
				PreserveExpandedModificationSelection(isMainHand, bagIndex, weaponSlot.InstanceState, _slot);
			}
		}
		else
			PreserveExpandedModificationSelection(isMainHand, bagIndex, weaponSlot.InstanceState, _slot);

		RebuildInlineModificationRows();
		return true;
	}

	public void TryCollapseModificationPanelForDoubleClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || m_SharedPresetStore == null)
			return;

		if (!ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
			return;

		if (!TryResolveInventoryDropTarget(_slot, out bool isMainHand, out int bagIndex))
			return;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		InventorySlotRuntimeData weaponSlot = _slot.Data;
		if (snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData snapshotSlot) &&
		    !snapshotSlot.IsEmpty)
			weaponSlot = snapshotSlot;

		if (!m_ModificationUiState.HasSelection || !m_ModificationUiState.IsExpanded)
			return;

		if (!IsSameWeaponAsSelection(weaponSlot.InstanceState, isMainHand, bagIndex))
			return;

		SetDisplayState(RuntimeModifiableWeaponDisplayState.Collapsed);
	}

	public RuntimeModifiableWeaponDisplayState GetDisplayStateForSlot(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
			return RuntimeModifiableWeaponDisplayState.Collapsed;

		if (!TryResolveInventorySlot(_slot, out bool isMainHand, out int bagIndex) || m_SharedPresetStore == null)
			return RuntimeModifiableWeaponDisplayState.Collapsed;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponSlot))
			weaponSlot = _slot.Data;

		return IsSameWeaponAsSelection(weaponSlot.InstanceState, isMainHand, bagIndex)
			? m_ModificationUiState.DisplayState
			: RuntimeModifiableWeaponDisplayState.Collapsed;
	}

	public bool HasExpandedEmptyModificationSlots()
	{
		return m_ModificationUiState.HasSelection && m_ModificationUiState.IsExpanded;
	}

	public bool ShouldSuppressOutsideClickCollapse()
	{
		return Time.frameCount <= m_SuppressOutsideClickUntilFrame;
	}

	public bool TryGetModificationWeaponSlot(out InventorySlotRuntimeData _weaponSlot)
	{
		_weaponSlot = default;
		if (!m_ModificationUiState.HasSelection || m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(m_ModificationUiState.IsMainHand, m_ModificationUiState.BagIndex, out _weaponSlot))
			return false;

		return !_weaponSlot.IsEmpty && ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition);
	}

	public bool TryGetModificationGraphLoadout(
		out WeaponDefinition _weaponDefinition,
		out WeaponAttachmentDefinition[] _currentAttachments,
		out WeaponAttachmentDefinition[] _previewAttachments)
	{
		return TryGetModificationGraphLoadout(
			out _weaponDefinition,
			out _currentAttachments,
			out _,
			out _previewAttachments);
	}

	public bool TryGetModificationGraphLoadout(
		out WeaponDefinition _weaponDefinition,
		out WeaponAttachmentDefinition[] _currentAttachments,
		out WeaponDefinition _previewWeaponDefinition,
		out WeaponAttachmentDefinition[] _previewAttachments)
	{
		_weaponDefinition = null;
		_currentAttachments = null;
		_previewWeaponDefinition = null;
		_previewAttachments = null;

		bool hasBaseline = TryResolveGraphBaselineWeaponSlot(out InventorySlotRuntimeData baselineSlot) &&
		                   TryExtractWeaponLoadout(baselineSlot, out _weaponDefinition, out _currentAttachments);

		bool hasModulePreview = false;
		InventorySlotRuntimeData previewCandidate = ResolveGraphPreviewCandidate();
		if (!previewCandidate.IsEmpty && ItemModificationUtility.IsAttachmentItem(previewCandidate))
		{
			WeaponAttachmentDefinition candidateAttachment = previewCandidate.Definition != null
				? previewCandidate.Definition.WeaponAttachmentDefinition
				: null;

			if (candidateAttachment != null &&
			    TryResolveGraphModulePreviewWeaponSlot(out InventorySlotRuntimeData modulePreviewWeaponSlot) &&
			    ItemModificationUtility.IsCompatibleWithWeapon(modulePreviewWeaponSlot, previewCandidate) &&
			    TryBuildPreviewAttachments(modulePreviewWeaponSlot, previewCandidate, out WeaponAttachmentDefinition[] modulePreviewAttachments))
			{
				_previewAttachments = modulePreviewAttachments;
				hasModulePreview = true;
				if (TryExtractWeaponLoadout(modulePreviewWeaponSlot, out WeaponDefinition moduleWeaponDefinition, out _) &&
				    (!hasBaseline || !RepresentsSameWeaponInstance(baselineSlot, modulePreviewWeaponSlot)))
					_previewWeaponDefinition = moduleWeaponDefinition;
			}
			else if (candidateAttachment != null && !hasBaseline)
			{
				_previewAttachments = new[] { candidateAttachment };
				hasModulePreview = true;
			}
		}

		if (!hasModulePreview &&
		    !m_HoveredWeaponGraphCandidate.IsEmpty &&
		    TryExtractWeaponLoadout(m_HoveredWeaponGraphCandidate, out WeaponDefinition hoveredWeaponDefinition, out WeaponAttachmentDefinition[] hoveredAttachments) &&
		    (!hasBaseline || !RepresentsSameWeaponInstance(baselineSlot, m_HoveredWeaponGraphCandidate)))
		{
			_previewWeaponDefinition = hoveredWeaponDefinition;
			_previewAttachments = hoveredAttachments;
		}

		if (!PreviewLoadoutDiffersFromCurrent(_weaponDefinition, _currentAttachments, _previewWeaponDefinition, _previewAttachments))
		{
			_previewWeaponDefinition = null;
			_previewAttachments = null;
		}

		return hasBaseline || _previewWeaponDefinition != null || _previewAttachments != null;
	}

	public bool IsAccuracyGraphRecoilPreviewActive
	{
		get
		{
			if (!ItemModificationUtility.IsRecoilGraphPreviewItem(ResolveGraphPreviewCandidate()))
				return false;

			TryGetModificationGraphLoadout(
				out WeaponDefinition weaponDefinition,
				out _,
				out WeaponDefinition previewWeaponDefinition,
				out _);

			return weaponDefinition != null || previewWeaponDefinition != null;
		}
	}

	public void SetHoveredModificationPreviewCandidate(InventorySlotRuntimeData _candidate)
	{
		if (_candidate.IsEmpty || !ItemModificationUtility.IsAttachmentItem(_candidate))
		{
			ResetHoveredModificationPreviewCandidate();
			return;
		}

		bool hasWeaponSlot = TryResolveGraphModulePreviewWeaponSlot(out InventorySlotRuntimeData modulePreviewWeaponSlot);
		bool isCompatible = hasWeaponSlot &&
		                    ItemModificationUtility.IsCompatibleWithWeapon(modulePreviewWeaponSlot, _candidate);
		if (hasWeaponSlot && !isCompatible)
		{
			ResetHoveredModificationPreviewCandidate();
			return;
		}

		m_HoveredModificationPreviewCandidate = _candidate;
		ModificationGraphDataChanged?.Invoke();
	}

	public void SetHoveredWeaponGraphCandidate(InventorySlotRuntimeData _candidate)
	{
		if (_candidate.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(_candidate.Definition))
		{
			ResetHoveredWeaponGraphCandidate();
			return;
		}

		m_HoveredWeaponGraphCandidate = _candidate;
		ModificationGraphDataChanged?.Invoke();
	}

	public void ClearHoveredWeaponGraphCandidate(InventorySlotRuntimeData _candidate)
	{
		if (!m_HoveredWeaponGraphCandidate.IsEmpty &&
		    !_candidate.IsEmpty &&
		    m_HoveredWeaponGraphCandidate.Definition != _candidate.Definition)
			return;

		ResetHoveredWeaponGraphCandidate();
	}

	public void ClearHoveredModificationPreviewCandidate(InventorySlotRuntimeData _candidate)
	{
		if (!m_HoveredModificationPreviewCandidate.IsEmpty &&
		    !_candidate.IsEmpty &&
		    m_HoveredModificationPreviewCandidate.Definition != _candidate.Definition)
			return;

		ResetHoveredModificationPreviewCandidate();
	}

	private void ResetHoveredModificationPreviewCandidate()
	{
		if (m_HoveredModificationPreviewCandidate.IsEmpty)
			return;

		m_HoveredModificationPreviewCandidate = default;
		ModificationGraphDataChanged?.Invoke();
	}

	private void ResetHoveredWeaponGraphCandidate()
	{
		if (m_HoveredWeaponGraphCandidate.IsEmpty)
			return;

		m_HoveredWeaponGraphCandidate = default;
		ModificationGraphDataChanged?.Invoke();
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
		RefreshAvailableEquipmentHighlights();
		RefreshPresetInventoryCompatibilityHighlights();
	}

	public void RefreshAvailableEquipmentHighlights()
	{
		if (m_AvailableEquipmentPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = m_AvailableEquipmentPanel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			MissionPrepAvailableEquipmentSlotHighlightView highlight =
				slot.GetComponent<MissionPrepAvailableEquipmentSlotHighlightView>();
			highlight?.RefreshHighlight();
		}
	}

	public void RefreshPresetInventoryCompatibilityHighlights()
	{
		if (m_PresetInventoryPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		for (int i = 0; i < slots.Count; i++)
		{
			if (lead > 0 && i == 0)
				continue;

			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			MissionPrepPresetInventorySlotHighlightView highlight =
				slot.GetComponent<MissionPrepPresetInventorySlotHighlightView>();
			highlight?.RefreshHighlight();
		}
	}

	public void CollapseEmptyModificationSlots()
	{
		SetDisplayState(RuntimeModifiableWeaponDisplayState.Collapsed);
	}

	public void ClearModificationUiSelection()
	{
		ClearForcedExpandedModificationSelection();
		m_ExpandedWeaponListIndex = -1;
		m_ModificationUiState = default;
		m_HoveredModificationPreviewCandidate = default;
		m_HoveredWeaponGraphCandidate = default;
		MissionPrepInlineModificationBuilder.ClearAllRowsImmediate(m_PresetInventoryPanel);
		RebuildInlineModificationRows();
		ModificationGraphDataChanged?.Invoke();
	}

	public void CloseModificationPanel()
	{
		ClearModificationUiSelection();
	}

	public bool TryInstallModificationFromDrag(ItemModificationSlotDescriptor _slotDescriptor, bool _weaponIsMainHand, int _weaponBagIndex)
	{
		const string context = "MissionPrep.TryInstallModificationFromDrag";
		MissionPrepModificationDragPayload payload = MissionPrepModificationDragContext.Current;
		if (!payload.HasItem || m_SharedPresetStore == null)
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "validate_payload", "no drag item or preset store missing");
			return false;
		}

		if (payload.SourceKind == MissionPrepModificationDragSourceKind.ModificationSlot)
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "validate_source", "cannot install from modification slot via drop");
			return false;
		}

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!TryResolvePresetWeaponSlotForModification(
			    snapshot,
			    _weaponIsMainHand,
			    _weaponBagIndex,
			    _weaponDefinitionHint: null,
			    _slotDescriptor,
			    _requireInstalledModification: false,
			    out bool resolvedIsMainHand,
			    out int resolvedBagIndex,
			    out InventorySlotRuntimeData weaponSlot))
		{
			ItemModificationDiagnostics.LogFlowRejected(
				context,
				"resolve_weapon",
				$"preset weapon not found (mainHand={_weaponIsMainHand}, bagIndex={_weaponBagIndex})");
			return false;
		}

		if (payload.SourceKind == MissionPrepModificationDragSourceKind.PresetBag)
		{
			if (payload.PresetBagIndex < 0)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", "invalid preset bag index");
				return false;
			}

			if (!resolvedIsMainHand && payload.PresetBagIndex == resolvedBagIndex)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", "cannot drag from same bag slot as weapon");
				return false;
			}

			if (!snapshot.TryGetInventorySlot(_isMainHandEquipmentSlot: false, payload.PresetBagIndex, out _))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", $"preset bag slot {payload.PresetBagIndex} not found");
				return false;
			}
		}

		string acceptReason = ItemModificationUtility.ExplainCanAcceptItem(_slotDescriptor, weaponSlot, payload.Item);
		if (!string.Equals(acceptReason, ItemModificationDiagnostics.AcceptedReason, System.StringComparison.Ordinal))
		{
			ItemModificationDiagnostics.LogInstallRejected(
				$"{context} src={payload.SourceKind}",
				_slotDescriptor,
				weaponSlot,
				payload.Item,
				acceptReason);
			return false;
		}

		if (ShouldUseBoundUnitEquippedMagazineReload(_slotDescriptor, _weaponIsMainHand))
			return TryInstallEquippedMagazineFromDragMissionPrep(payload, _slotDescriptor, resolvedIsMainHand, resolvedBagIndex);

		InventorySlotRuntimeData candidate = MissionPrepInventoryCopyUtility.CloneSlot(payload.Item);
		if (!ItemModificationUtility.TryInstallAtSlot(_slotDescriptor, weaponSlot, candidate, out InventorySlotRuntimeData replacedItem))
			return false;

		int targetBagIndex = resolvedBagIndex;
		if (payload.SourceKind == MissionPrepModificationDragSourceKind.PresetBag)
		{
			if (!snapshot.TryRemoveInventorySlot(_isMainHandEquipmentSlot: false, payload.PresetBagIndex, out _))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "consume_source", "failed to remove item from preset bag");
				return false;
			}

			if (!resolvedIsMainHand && payload.PresetBagIndex < targetBagIndex)
				targetBagIndex--;
		}

		if (!snapshot.TrySetInventorySlot(resolvedIsMainHand, targetBagIndex, weaponSlot))
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "commit_weapon", "snapshot.TrySetInventorySlot failed");
			return false;
		}

		if (!replacedItem.IsEmpty)
			snapshot.TryAddToBag(replacedItem);

		m_KeepExpandedRequiresInstalledModification = true;
		ResolveExpandedWeaponSlotAfterMutation(
			weaponSlot.Definition,
			resolvedIsMainHand,
			targetBagIndex,
			weaponSlot,
			_requireInstalledModification: true,
			out bool expandedIsMainHand,
			out int expandedBagIndex);
		TryPreserveExpandedModificationSelectionForWeaponSlot(
			expandedIsMainHand,
			expandedBagIndex,
			weaponSlot.InstanceState);

		MissionPrepModificationDragContext.NotifyDropConsumed();
		NotifyInventoryMutated(_saveSnapshotFromRuntime: false);
		ScheduleForcedExpandedModificationRepaint(expandedIsMainHand, expandedBagIndex);
		ItemModificationDiagnostics.LogInstallAccepted(context, _slotDescriptor, weaponSlot, payload.Item);
		return true;
	}

	public bool TryClearModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		bool _addToBag = true,
		ItemDefinition _weaponDefinitionHint = null)
	{
		const string context = "MissionPrep.TryClearModificationSlot";
		if (m_SharedPresetStore == null)
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "validate_store", "SharedPresetStore is null");
			return false;
		}

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!TryResolvePresetWeaponSlotForModification(
			    snapshot,
			    _weaponIsMainHand,
			    _weaponBagIndex,
			    _weaponDefinitionHint,
			    _slotDescriptor,
			    _requireInstalledModification: true,
			    out bool resolvedIsMainHand,
			    out int resolvedBagIndex,
			    out InventorySlotRuntimeData weaponSlot))
		{
			ItemModificationDiagnostics.LogFlowRejected(
				context,
				"resolve_weapon",
				$"preset weapon not found (mainHand={_weaponIsMainHand}, bagIndex={_weaponBagIndex}, hint={_weaponDefinitionHint?.name ?? "null"})");
			return false;
		}

		if (!ItemModificationUtility.TryGetInstalledItem(_slotDescriptor, weaponSlot, out _))
		{
			ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "slot is empty");
			return false;
		}

		if (ShouldUseBoundUnitEquippedMagazineReload(_slotDescriptor, resolvedIsMainHand))
		{
			WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = _addToBag;
			if (!TryStartEquippedMagazineEjectOnAllPresetUnits(_addToBag))
			{
				WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = true;
				ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "TryStartEquippedMagazineEjectOnAllPresetUnits failed");
				return false;
			}

			return true;
		}

		if (!ItemModificationUtility.TryClearSlot(_slotDescriptor, weaponSlot, out InventorySlotRuntimeData removedItem))
			return false;

		if (!removedItem.IsEmpty && _addToBag)
			snapshot.TryAddToBag(removedItem);

		snapshot.TrySetInventorySlot(resolvedIsMainHand, resolvedBagIndex, weaponSlot);

		m_KeepExpandedRequiresInstalledModification = false;
		ResolveExpandedWeaponSlotAfterMutation(
			weaponSlot.Definition,
			resolvedIsMainHand,
			resolvedBagIndex,
			weaponSlot,
			_requireInstalledModification: false,
			out bool expandedIsMainHand,
			out int expandedBagIndex);
		TryPreserveExpandedModificationSelectionForWeaponSlot(
			expandedIsMainHand,
			expandedBagIndex,
			weaponSlot.InstanceState);

		NotifyInventoryMutated(_saveSnapshotFromRuntime: false);
		ScheduleForcedExpandedModificationRepaint(expandedIsMainHand, expandedBagIndex);
		ItemModificationDiagnostics.LogClearAccepted(context, _slotDescriptor, weaponSlot, removedItem);
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
			_addToBag: true,
			_drag.WeaponDefinitionHint);
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
			_addToBag: false,
			_drag.WeaponDefinitionHint);
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

		int preservedExpandedListIndex = m_ExpandedWeaponListIndex;
		MissionPrepModificationUiState preservedModificationUi = m_ModificationUiState;
		MissionPrepInlineModificationBuilder.ClearAllRowsImmediate(m_PresetInventoryPanel);
		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		m_PresetInventoryPanel.RepaintFromPresetSnapshot(snapshot);
		EnsurePresetInventoryDragComponents();
		EnsureModificationClickHandlers();
		EnsureMainHandEquipmentSlot();
		m_ModificationUiState = preservedModificationUi;
		m_ExpandedWeaponListIndex = preservedExpandedListIndex;
		TryRestoreExpandedModificationSelectionAfterRepaint();
		RebuildInlineModificationRows();
		ModificationGraphDataChanged?.Invoke();
	}

	public void ScheduleRefreshInlineModificationRowsAfterDrag()
	{
		if (!isActiveAndEnabled)
		{
			RebuildInlineModificationRows();
			return;
		}

		m_PendingInlineRefresh = true;
		if (m_DeferredInlineRefreshCoroutine != null)
			return;

		m_DeferredInlineRefreshCoroutine = StartCoroutine(CoRefreshInlineModificationRowsNextFrame());
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
		EnsureRuntimePresetRegistry();

		if (m_RuntimePresetRegistry != null)
		{
			_label = m_RuntimePresetRegistry.GetPresetDisplayName(_presetIndex, m_PresetCatalog);
			return !string.IsNullOrEmpty(_label);
		}

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

	public MissionPrepRuntimePresetRegistry RuntimePresetRegistry => m_RuntimePresetRegistry;

	public int GetPresetSlotCount()
	{
		if (m_RuntimePresetRegistry != null && m_RuntimePresetRegistry.TotalPresetCount > 0)
			return m_RuntimePresetRegistry.TotalPresetCount;

		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.PresetCount;

		return 2;
	}

	public int ClampPresetCatalogIndex(int _index)
	{
		return Mathf.Clamp(_index, 0, Mathf.Max(0, GetPresetSlotCount() - 1));
	}

	public bool TryCreateUserPreset(string _proposedName, out int _newPresetIndex)
	{
		_newPresetIndex = -1;
		EnsureRuntimePresetRegistry();
		EnsureSharedPresetStore();
		if (m_RuntimePresetRegistry == null || m_SharedPresetStore == null)
			return false;

		if (!m_RuntimePresetRegistry.TryCreateUserPreset(_proposedName, m_PresetCatalog, out _newPresetIndex, out _))
			return false;

		m_SharedPresetStore.AddEmptySnapshot();
		SwitchToPreset(_newPresetIndex);
		return true;
	}

	public bool TryRenameUserPreset(int _presetIndex, string _proposedName)
	{
		EnsureRuntimePresetRegistry();
		if (m_RuntimePresetRegistry == null)
			return false;

		return m_RuntimePresetRegistry.TryRenameUserPreset(_presetIndex, _proposedName, m_PresetCatalog, out _);
	}

	public bool TryDeleteUserPreset(int _presetIndex)
	{
		EnsureRuntimePresetRegistry();
		EnsureSharedPresetStore();
		if (m_RuntimePresetRegistry == null || m_SharedPresetStore == null)
			return false;

		if (!m_RuntimePresetRegistry.CanDeletePreset(_presetIndex))
			return false;

		int clamped = Mathf.Clamp(_presetIndex, 0, GetPresetSlotCount() - 1);
		if (m_EditingPresetCatalogIndex == clamped)
		{
			int fallbackIndex = clamped > 0 ? clamped - 1 : 0;
			if (fallbackIndex >= clamped)
				fallbackIndex = Mathf.Min(clamped + 1, GetPresetSlotCount() - 1);

			m_EditingPresetCatalogIndex = fallbackIndex;
		}
		else if (m_EditingPresetCatalogIndex > clamped)
			m_EditingPresetCatalogIndex--;

		if (!m_RuntimePresetRegistry.TryDeleteUserPreset(clamped))
			return false;

		m_SharedPresetStore.RemoveSnapshotAt(clamped);
		AdjustAllUnitsAfterPresetDeletion(clamped);
		ClearModificationUiSelection();

		if (m_BoundPresetState != null)
		{
			int presetCount = GetPresetSlotCount();
			m_BoundPresetState.AdjustPresetCatalogIndexAfterDeletion(clamped);
			m_BoundPresetState.SetActivePresetIndex(m_BoundPresetState.PresetCatalogIndex, presetCount);
			ApplyUnitAssignedPresetToRuntime();
		}

		m_EditingPresetCatalogIndex = ClampPresetCatalogIndex(m_EditingPresetCatalogIndex);

		RepaintInventoryPanel();
		RepaintAvailableEquipmentPanel();
		return true;
	}
	#endregion

	#region Private Methods
	private void EnsureSharedPresetStore()
	{
		if (m_SharedPresetStore == null)
			m_SharedPresetStore = MissionPrepSharedPresetStore.GetOrCreate(this);
	}

	private void EnsureRuntimePresetRegistry()
	{
		if (m_RuntimePresetRegistry == null)
			m_RuntimePresetRegistry = MissionPrepRuntimePresetRegistry.GetOrCreate(this);

		if (m_RuntimePresetRegistry != null)
		{
			int builtInCount = m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0
				? m_PresetCatalog.PresetCount
				: 2;
			m_RuntimePresetRegistry.ConfigureBuiltInPresetCount(builtInCount);
		}
	}

	private void EnsureSharedPresetStoreInitialized()
	{
		EnsureSharedPresetStore();
		EnsureRuntimePresetRegistry();
		if (m_SharedPresetStore == null)
			return;

		m_SharedPresetStore.EnsurePresetSnapshots(GetPresetSlotCount());
		m_SharedPresetStore.EnsureDefaultsFromCatalog(m_PresetCatalog);
	}

	private void AdjustAllUnitsAfterPresetDeletion(int _deletedIndex)
	{
		MissionPrepUnitPresetState[] units = FindObjectsByType<MissionPrepUnitPresetState>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None);
		for (int i = 0; i < units.Length; i++)
		{
			MissionPrepUnitPresetState unit = units[i];
			if (unit == null)
				continue;

			unit.AdjustPresetCatalogIndexAfterDeletion(_deletedIndex);
		}
	}

	private void PropagatePresetToAllUnits(int _presetIndex, bool _refreshBoundUnitRuntime, bool _saveSnapshotFromRuntime = true)
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

				if (isBoundUnit && _refreshBoundUnitRuntime && _presetIndex == m_EditingPresetCatalogIndex && _saveSnapshotFromRuntime)
				{
					m_SharedPresetStore.SavePresetFromRuntime(
						_presetIndex,
						inventory,
						m_SharedPresetStore.GetArmorForPreset(_presetIndex));
				}
			}

			int armorIndex = m_SharedPresetStore.GetArmorForPreset(_presetIndex);
			MissionPrepUnitArmorVisualController visual =
				MissionPrepUnitArmorVisualController.GetOrCreate(unit.gameObject, armorIndex);
			visual.ApplyArmorVisual(armorIndex);
			UnitArmor armor = unit.GetComponent<UnitArmor>() ?? unit.gameObject.AddComponent<UnitArmor>();
			armor.SetArmorFromPresetIndex(armorIndex);
		}
	}

	private bool ShouldUseBoundUnitEquippedMagazineReload(ItemModificationSlotDescriptor _slotDescriptor, bool _weaponIsMainHand)
	{
		return _weaponIsMainHand &&
		       m_BoundInventory != null &&
		       WeaponMagazineModificationApplier.IsMagazineSlot(_slotDescriptor) &&
		       WeaponMagazineModificationApplier.CanStartUiMagazineModification(m_BoundInventory);
	}

	private bool TryInstallEquippedMagazineFromDragMissionPrep(
		MissionPrepModificationDragPayload _payload,
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex)
	{
		if (m_SharedPresetStore == null || m_BoundInventory == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out InventorySlotRuntimeData weaponSlot))
			return false;

		if (!ItemModificationUtility.CanAcceptItem(_slotDescriptor, weaponSlot, _payload.Item))
			return false;

		InventorySlotRuntimeData magazineToInstall = MissionPrepInventoryCopyUtility.CloneSlot(_payload.Item);
		int targetBagIndex = _weaponBagIndex;
		InventorySlotRuntimeData restoredBagItem = default;

		if (_payload.SourceKind == MissionPrepModificationDragSourceKind.PresetBag)
		{
			if (_payload.PresetBagIndex < 0)
				return false;

			if (!snapshot.TryRemoveInventorySlot(_isMainHandEquipmentSlot: false, _payload.PresetBagIndex, out restoredBagItem))
				return false;

			if (!_weaponIsMainHand && _payload.PresetBagIndex < targetBagIndex)
				targetBagIndex--;

			magazineToInstall = MissionPrepInventoryCopyUtility.CloneSlot(restoredBagItem);
		}

		if (!TryStartEquippedMagazineInstallOnAllPresetUnits(magazineToInstall))
		{
			if (!restoredBagItem.IsEmpty)
				snapshot.TryAddToBag(restoredBagItem);

			return false;
		}

		if (snapshot.TryGetInventorySlot(_weaponIsMainHand, targetBagIndex, out InventorySlotRuntimeData installedWeapon))
		{
			TryPreserveExpandedModificationSelectionForWeaponSlot(
				_weaponIsMainHand,
				targetBagIndex,
				installedWeapon.InstanceState);
			ScheduleForcedExpandedModificationRepaint(_weaponIsMainHand, targetBagIndex);
		}

		MissionPrepModificationDragContext.NotifyDropConsumed();
		return true;
	}

	private void TrySubscribeBoundUnitReloadCompletionHandler()
	{
		if (m_BoundInventory == null)
			return;

		if (!WeaponMagazineModificationApplier.TryGetReloadController(m_BoundInventory, out UnitWeaponReloadController reloadController))
			return;

		if (m_SubscribedBoundReloadController == reloadController)
			return;

		TryUnsubscribeBoundUnitReloadCompletionHandler();
		m_SubscribedBoundReloadController = reloadController;
		reloadController.UiMagazineModificationCompleted += HandleBoundUnitUiMagazineModificationCompleted;
	}

	private void TryUnsubscribeBoundUnitReloadCompletionHandler()
	{
		if (m_SubscribedBoundReloadController == null)
			return;

		m_SubscribedBoundReloadController.UiMagazineModificationCompleted -= HandleBoundUnitUiMagazineModificationCompleted;
		m_SubscribedBoundReloadController = null;
	}

	private void HandleBoundUnitUiMagazineModificationCompleted(InventorySlotRuntimeData _ejectedMagazine)
	{
		MissionPrepModificationUiState preservedModificationUi = m_ModificationUiState;
		int preservedExpandedListIndex = m_ExpandedWeaponListIndex;
		WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = true;
		SyncBoundUnitInventoryToSnapshot();
		PropagatePresetToAllUnits(m_EditingPresetCatalogIndex, _refreshBoundUnitRuntime: true, _saveSnapshotFromRuntime: false);
		StartCoroutine(RepaintInventoryPanelAfterMagazineModificationCompleted(
			preservedModificationUi,
			preservedExpandedListIndex));
	}

	private IEnumerator RepaintInventoryPanelAfterMagazineModificationCompleted(
		MissionPrepModificationUiState _preservedModificationUi,
		int _preservedExpandedListIndex)
	{
		yield return null;
		m_ModificationUiState = _preservedModificationUi;
		m_ExpandedWeaponListIndex = _preservedExpandedListIndex;
		RepaintInventoryPanel();
	}

	private bool TryStartEquippedMagazineInstallOnAllPresetUnits(InventorySlotRuntimeData _magazineFromSource)
	{
		if (_magazineFromSource.IsEmpty)
			return false;

		return TryStartEquippedMagazineModificationOnAllPresetUnits(
			(_inventory, _mirrorAnimationOnly, _magazine) =>
				WeaponMagazineModificationApplier.TryStartEquippedMagazineInstall(_inventory, _magazine, _mirrorAnimationOnly),
			_magazineFromSource);
	}

	private bool TryStartEquippedMagazineEjectOnAllPresetUnits(bool _addEjectedMagazineToBag)
	{
		WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = _addEjectedMagazineToBag;
		return TryStartEquippedMagazineModificationOnAllPresetUnits(
			(_inventory, _mirrorAnimationOnly, _) =>
				WeaponMagazineModificationApplier.TryStartEquippedMagazineEject(_inventory, _mirrorAnimationOnly),
			default);
	}

	private bool TryStartEquippedMagazineModificationOnAllPresetUnits(
		Func<CharacterInventory, bool, InventorySlotRuntimeData, bool> _tryStart,
		InventorySlotRuntimeData _magazineFromSource)
	{
		EnsureSharedPresetStore();
		if (m_SharedPresetStore == null || m_BoundInventory == null)
			return false;

		bool startedAuthoritative = false;
		MissionPrepUnitPresetState[] units = FindObjectsByType<MissionPrepUnitPresetState>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None);

		for (int i = 0; i < units.Length; i++)
		{
			MissionPrepUnitPresetState unit = units[i];
			if (unit == null || unit.PresetCatalogIndex != m_EditingPresetCatalogIndex)
				continue;

			CharacterInventory inventory = unit.GetComponentInChildren<CharacterInventory>(true);
			if (inventory == null)
				continue;

			m_SharedPresetStore.ApplyPresetToInventory(m_EditingPresetCatalogIndex, inventory);

			bool isAuthoritative = inventory == m_BoundInventory;
			bool mirrorAnimationOnly = !isAuthoritative;
			InventorySlotRuntimeData magazine = _magazineFromSource.IsEmpty
				? default
				: MissionPrepInventoryCopyUtility.CloneSlot(_magazineFromSource);

			if (!_tryStart(inventory, mirrorAnimationOnly, magazine))
				continue;

			UnitWeaponRuntime weaponRuntime = inventory.GetComponentInChildren<UnitWeaponRuntime>(true);
			if (weaponRuntime != null)
				weaponRuntime.RefreshFromEquipment();

			if (isAuthoritative)
				startedAuthoritative = true;
		}

		return startedAuthoritative;
	}

	private void SyncBoundUnitInventoryToSnapshot()
	{
		SyncBoundUnitInventoryToSnapshotIfEditingSamePreset();
	}

	private void SyncBoundUnitInventoryToSnapshotIfEditingSamePreset()
	{
		if (m_BoundInventory == null || m_SharedPresetStore == null || m_BoundPresetState == null)
			return;

		if (m_BoundPresetState.PresetCatalogIndex != m_EditingPresetCatalogIndex)
			return;

		m_SharedPresetStore.SavePresetFromRuntime(
			m_EditingPresetCatalogIndex,
			m_BoundInventory,
			GetActivePresetArmorIndex());
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
		UnitArmor armor = unitRoot.GetComponent<UnitArmor>() ?? unitRoot.AddComponent<UnitArmor>();
		armor.SetArmorFromPresetIndex(armorIndex);
	}

	private bool IsAvailableEquipmentSlot(InventorySlotView _slot)
	{
		if (_slot == null || m_AvailableEquipmentPanel == null)
			return false;

		if (_slot.TryGetComponent(out MissionPrepPresetToAvailableDrag presetDrag) &&
		    presetDrag.IsDraggingFromPreset)
			return false;

		if (IsSlotOnPanel(_slot, m_AvailableEquipmentPanel))
			return true;

		if (!_slot.TryGetComponent(out MissionPrepAvailableToPresetDrag drag))
			return false;

		return drag.IsDraggingFromAvailable && drag.SourceAvailablePanel == m_AvailableEquipmentPanel;
	}

	private static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_slot == null || _panel == null)
			return false;

		return _slot.GetComponentInParent<InventoryPanelView>() == _panel;
	}

	private void EnsurePresetInventoryDragComponents()
	{
		if (m_PresetInventoryPanel == null)
			return;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null)
				continue;

			if (slot.GetComponent<MissionPrepPresetToAvailableDrag>() == null)
				slot.gameObject.AddComponent<MissionPrepPresetToAvailableDrag>();

			if (slot.GetComponent<MissionPrepAvailableToPresetDrag>() == null)
				slot.gameObject.AddComponent<MissionPrepAvailableToPresetDrag>();

			if (slot.GetComponent<MissionPrepInventoryEquipDoubleClick>() == null)
				slot.gameObject.AddComponent<MissionPrepInventoryEquipDoubleClick>();

			EnsureModificationPreviewHover(slot);
			EnsureWeaponProfileGraphHover(slot);

			bool isMainHandSlot = lead > 0 && i == 0;
			MissionPrepPresetInventorySlotDropView existingDropView =
				slot.GetComponent<MissionPrepPresetInventorySlotDropView>();
			if (isMainHandSlot)
			{
				if (existingDropView != null && Application.isPlaying)
					Destroy(existingDropView);
				continue;
			}

			MissionPrepPresetInventorySlotDropView dropView = existingDropView;
			if (dropView == null)
				dropView = slot.gameObject.AddComponent<MissionPrepPresetInventorySlotDropView>();

			dropView.Bind(this);

			MissionPrepPresetInventorySlotHighlightView highlight =
				slot.GetComponent<MissionPrepPresetInventorySlotHighlightView>();
			if (highlight == null)
				highlight = slot.gameObject.AddComponent<MissionPrepPresetInventorySlotHighlightView>();

			highlight.Bind(this);
		}
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

	private void EnsureMainHandEquipmentSlot()
	{
		if (m_PresetInventoryPanel == null || m_PresetInventoryPanel.LeadingEquipmentSlotCount <= 0)
			return;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		if (slots.Count == 0 || slots[0] == null)
			return;

		MissionPrepMainHandEquipmentSlotView mainHandSlot = slots[0].GetComponent<MissionPrepMainHandEquipmentSlotView>();
		if (mainHandSlot == null)
			mainHandSlot = slots[0].gameObject.AddComponent<MissionPrepMainHandEquipmentSlotView>();

		mainHandSlot.Bind(this);
		EnsureModificationPreviewHover(slots[0]);
		EnsureWeaponProfileGraphHover(slots[0]);
	}

	private void EnsureModificationPreviewHover(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		MissionPrepModificationPreviewHover previewHover = _slot.GetComponent<MissionPrepModificationPreviewHover>();
		if (previewHover == null)
			previewHover = _slot.gameObject.AddComponent<MissionPrepModificationPreviewHover>();
		previewHover.Bind(this);
	}

	private void EnsureWeaponProfileGraphHover(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		MissionPrepWeaponProfileGraphHover weaponHover = _slot.GetComponent<MissionPrepWeaponProfileGraphHover>();
		if (weaponHover == null)
			weaponHover = _slot.gameObject.AddComponent<MissionPrepWeaponProfileGraphHover>();
		weaponHover.Bind(this);
	}

	private bool TryResolveGraphBaselineWeaponSlot(out InventorySlotRuntimeData _weaponSlot)
	{
		_weaponSlot = default;
		if (m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (snapshot.TryGetInventorySlot(true, -1, out InventorySlotRuntimeData mainHand) &&
		    !mainHand.IsEmpty &&
		    ItemModificationUtility.IsModifiableWeapon(mainHand.Definition))
		{
			_weaponSlot = mainHand;
			return true;
		}

		if (TryGetModificationWeaponSlot(out _weaponSlot))
			return true;

		for (int i = 0; i < snapshot.BagCount; i++)
		{
			if (!snapshot.TryGetInventorySlot(false, i, out InventorySlotRuntimeData bagSlot))
				continue;

			if (bagSlot.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(bagSlot.Definition))
				continue;

			_weaponSlot = bagSlot;
			return true;
		}

		return false;
	}

	private bool TryResolveGraphModulePreviewWeaponSlot(out InventorySlotRuntimeData _weaponSlot)
	{
		if (TryGetModificationWeaponSlot(out _weaponSlot))
			return true;

		return TryResolveGraphBaselineWeaponSlot(out _weaponSlot);
	}

	private static bool TryExtractWeaponLoadout(
		InventorySlotRuntimeData _weaponSlot,
		out WeaponDefinition _weaponDefinition,
		out WeaponAttachmentDefinition[] _attachments)
	{
		_weaponDefinition = null;
		_attachments = null;

		if (_weaponSlot.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition))
			return false;

		WeaponRuntimeState weaponState = _weaponSlot.InstanceState != null ? _weaponSlot.InstanceState.WeaponState : null;
		_weaponDefinition = weaponState != null
			? weaponState.WeaponDefinition
			: _weaponSlot.Definition != null
				? _weaponSlot.Definition.WeaponDefinition
				: null;
		if (_weaponDefinition == null)
			return false;

		_attachments = CopyAttachmentArray(weaponState != null ? weaponState.EquippedAttachments : null);
		return true;
	}

	private static bool RepresentsSameWeaponInstance(InventorySlotRuntimeData _a, InventorySlotRuntimeData _b)
	{
		if (_a.IsEmpty || _b.IsEmpty)
			return false;

		if (_a.InstanceState != null && _b.InstanceState != null)
			return ReferenceEquals(_a.InstanceState, _b.InstanceState);

		return _a.Definition == _b.Definition;
	}

	private void RebuildInlineModificationRows()
	{
		m_PendingInlineRefresh = false;
		if (m_PresetInventoryPanel == null || m_SharedPresetStore == null)
			return;

		MissionPrepInlineModificationBuilder.ClearAllRowsImmediate(m_PresetInventoryPanel);
		m_PresetInventoryPanel.RefreshSlotsFromHierarchy();
		CollectModifiableWeaponBindings(m_WeaponSlotBindingBuffer);
		TryRemapExpandedSelectionFromBindings(m_WeaponSlotBindingBuffer);
		TryRestoreExpandedModificationSelectionAfterRepaint();
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);
		if (m_WeaponSlotBindingBuffer.Count == 0)
		{
			m_PresetInventoryPanel.RebuildContentLayout();
			return;
		}

		for (int i = m_WeaponSlotBindingBuffer.Count - 1; i >= 0; i--)
		{
			WeaponSlotBinding binding = m_WeaponSlotBindingBuffer[i];
			if (binding.SlotView == null || !binding.SlotView.HasItem)
				continue;

			InventorySlotRuntimeData weaponData = ResolveWeaponDataForModificationUi(binding);
			if (weaponData.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(weaponData.Definition))
				continue;

			bool expandEmpty = ShouldExpandEmptySlotsForBinding(binding);
			BuildVisibleModificationDescriptors(weaponData, expandEmpty, m_VisibleModificationDescriptorBuffer);
			if (m_VisibleModificationDescriptorBuffer.Count == 0 &&
			    ItemModificationUtility.HasAnyInstalledModification(weaponData))
			{
				ItemModificationUtility.BuildInstalledModificationDescriptors(
					weaponData,
					m_ModificationDescriptorBuffer,
					m_VisibleModificationDescriptorBuffer);
			}

			if (m_VisibleModificationDescriptorBuffer.Count == 0)
				continue;

			MissionPrepInlineModificationBuilder.RebuildWeaponRows(
				m_PresetInventoryPanel,
				this,
				binding.SlotView,
				weaponData,
				binding.IsMainHand,
				binding.BagIndex,
				expandEmpty,
				m_VisibleModificationDescriptorBuffer);
		}

		m_PresetInventoryPanel.RebuildContentLayout();
		MissionPrepInlineModificationBuilder.RefreshHighlights(m_PresetInventoryPanel);
		MissionPrepInlineModificationBuilder.RefreshMainHandSlotHighlights(m_PresetInventoryPanel);
		RefreshModificationCompatibilityHighlights();
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

			InventorySlotRuntimeData weaponData = default;
			if (!TryResolveBindingWeaponData(snapshot, slot.Data, isMainHand, bagIndex, out bool resolvedIsMainHand, out int resolvedBagIndex, out weaponData))
				continue;

			isMainHand = resolvedIsMainHand;
			bagIndex = resolvedBagIndex;

			if (!ItemModificationUtility.IsModifiableWeapon(weaponData.Definition))
				continue;

			_outBindings.Add(new WeaponSlotBinding(slot, weaponData, isMainHand, bagIndex, i));
		}
	}

	private static bool TryResolveBindingWeaponData(
		MissionPrepPresetSnapshot _snapshot,
		InventorySlotRuntimeData _uiWeaponData,
		bool _preferredIsMainHand,
		int _preferredBagIndex,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponData)
	{
		_isMainHand = _preferredIsMainHand;
		_bagIndex = _preferredBagIndex;
		_weaponData = default;

		if (_uiWeaponData.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(_uiWeaponData.Definition))
			return false;

		ItemDefinition weaponDefinition = _uiWeaponData.Definition;
		if (_snapshot.TryGetInventorySlot(_preferredIsMainHand, _preferredBagIndex, out InventorySlotRuntimeData atPreferred) &&
		    !atPreferred.IsEmpty &&
		    atPreferred.Definition == weaponDefinition)
		{
			_weaponData = atPreferred;
			return true;
		}

		if (TryFindSnapshotSlotByWeaponDefinition(_snapshot, weaponDefinition, out _isMainHand, out _bagIndex, out _weaponData))
			return true;

		_weaponData = _uiWeaponData;
		return true;
	}

	private bool TryResolvePresetWeaponSlotForModification(
		MissionPrepPresetSnapshot _snapshot,
		bool _preferredIsMainHand,
		int _preferredBagIndex,
		ItemDefinition _weaponDefinitionHint,
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _requireInstalledModification,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isMainHand = _preferredIsMainHand;
		_bagIndex = _preferredBagIndex;
		_weaponSlot = default;

		if (_snapshot == null)
			return false;

		if (_snapshot.TryGetInventorySlot(_preferredIsMainHand, _preferredBagIndex, out InventorySlotRuntimeData preferredSlot) &&
		    SlotMatchesModificationResolve(preferredSlot, _weaponDefinitionHint, _slotDescriptor, _requireInstalledModification))
		{
			_weaponSlot = preferredSlot;
			return true;
		}

		if (m_ModificationUiState.HasSelection &&
		    (_snapshot.TryGetInventorySlot(m_ModificationUiState.IsMainHand, m_ModificationUiState.BagIndex, out InventorySlotRuntimeData uiSlot) &&
		     SlotMatchesModificationResolve(uiSlot, _weaponDefinitionHint, _slotDescriptor, _requireInstalledModification)))
		{
			_isMainHand = m_ModificationUiState.IsMainHand;
			_bagIndex = m_ModificationUiState.BagIndex;
			_weaponSlot = uiSlot;
			return true;
		}

		CollectModifiableWeaponBindings(m_WeaponSlotBindingBuffer);
		if (m_ExpandedWeaponListIndex >= 0)
		{
			for (int i = 0; i < m_WeaponSlotBindingBuffer.Count; i++)
			{
				WeaponSlotBinding binding = m_WeaponSlotBindingBuffer[i];
				if (binding.ListIndex != m_ExpandedWeaponListIndex)
					continue;

				if (_snapshot.TryGetInventorySlot(binding.IsMainHand, binding.BagIndex, out InventorySlotRuntimeData boundSlot) &&
				    SlotMatchesModificationResolve(boundSlot, _weaponDefinitionHint, _slotDescriptor, _requireInstalledModification))
				{
					_isMainHand = binding.IsMainHand;
					_bagIndex = binding.BagIndex;
					_weaponSlot = boundSlot;
					return true;
				}

				break;
			}
		}

		if (_weaponDefinitionHint != null &&
		    TryFindSnapshotSlotForExpandedWeaponInSnapshot(
			    _snapshot,
			    _weaponDefinitionHint,
			    _preferredIsMainHand,
			    _preferredBagIndex,
			    _requireInstalledModification,
			    out _isMainHand,
			    out _bagIndex,
			    out _weaponSlot))
			return true;

		if (_requireInstalledModification &&
		    TryFindSnapshotWeaponWithInstalledModification(_snapshot, _slotDescriptor, _weaponDefinitionHint, out _isMainHand, out _bagIndex, out _weaponSlot))
			return true;

		return false;
	}

	private static bool TryFindSnapshotSlotByWeaponDefinition(
		MissionPrepPresetSnapshot _snapshot,
		ItemDefinition _weaponDefinition,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isMainHand = false;
		_bagIndex = -1;
		_weaponSlot = default;

		if (_snapshot == null || _weaponDefinition == null)
			return false;

		if (_snapshot.TryGetInventorySlot(true, -1, out InventorySlotRuntimeData mainHand) &&
		    !mainHand.IsEmpty &&
		    mainHand.Definition == _weaponDefinition)
		{
			_isMainHand = true;
			_bagIndex = -1;
			_weaponSlot = mainHand;
			return true;
		}

		for (int bagIndex = 0; bagIndex < _snapshot.BagCount; bagIndex++)
		{
			if (!_snapshot.TryGetInventorySlot(false, bagIndex, out InventorySlotRuntimeData bagSlot) ||
			    bagSlot.IsEmpty ||
			    bagSlot.Definition != _weaponDefinition)
				continue;

			_isMainHand = false;
			_bagIndex = bagIndex;
			_weaponSlot = bagSlot;
			return true;
		}

		return false;
	}

	private static bool TryFindSnapshotWeaponWithInstalledModification(
		MissionPrepPresetSnapshot _snapshot,
		ItemModificationSlotDescriptor _slotDescriptor,
		ItemDefinition _weaponDefinitionHint,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isMainHand = false;
		_bagIndex = -1;
		_weaponSlot = default;

		if (_snapshot == null)
			return false;

		if (_snapshot.TryGetInventorySlot(true, -1, out InventorySlotRuntimeData mainHand) &&
		    SlotMatchesModificationResolve(mainHand, _weaponDefinitionHint, _slotDescriptor, _requireInstalledModification: true))
		{
			_isMainHand = true;
			_bagIndex = -1;
			_weaponSlot = mainHand;
			return true;
		}

		for (int bagIndex = 0; bagIndex < _snapshot.BagCount; bagIndex++)
		{
			if (!_snapshot.TryGetInventorySlot(false, bagIndex, out InventorySlotRuntimeData bagSlot) ||
			    !SlotMatchesModificationResolve(bagSlot, _weaponDefinitionHint, _slotDescriptor, _requireInstalledModification: true))
				continue;

			_isMainHand = false;
			_bagIndex = bagIndex;
			_weaponSlot = bagSlot;
			return true;
		}

		return false;
	}

	private static bool SlotMatchesModificationResolve(
		InventorySlotRuntimeData _candidate,
		ItemDefinition _weaponDefinitionHint,
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _requireInstalledModification)
	{
		if (_candidate.IsEmpty || !ItemModificationUtility.IsModifiableWeapon(_candidate.Definition))
			return false;

		if (_weaponDefinitionHint != null && _candidate.Definition != _weaponDefinitionHint)
			return false;

		if (!_requireInstalledModification)
			return true;

		return ItemModificationUtility.TryGetInstalledItem(_slotDescriptor, _candidate, out _);
	}

	private void BuildVisibleModificationDescriptors(
		InventorySlotRuntimeData _weaponData,
		bool _expandEmpty,
		List<ItemModificationSlotDescriptor> _outVisibleDescriptors)
	{
		ItemModificationUtility.BuildVisibleModificationDescriptors(
			_weaponData,
			_expandEmpty,
			m_ModificationDescriptorBuffer,
			_outVisibleDescriptors);
	}

	private InventorySlotRuntimeData ResolveWeaponDataForModificationUi(in WeaponSlotBinding _binding)
	{
		InventorySlotRuntimeData slotData = _binding.SlotView != null && _binding.SlotView.HasItem
			? MissionPrepInventoryCopyUtility.CloneSlot(_binding.SlotView.Data)
			: default;

		if (m_SharedPresetStore != null)
		{
			MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
			if (snapshot.TryGetInventorySlot(_binding.IsMainHand, _binding.BagIndex, out InventorySlotRuntimeData snapshotData) &&
			    !snapshotData.IsEmpty)
			{
				InventorySlotRuntimeData clonedSnapshotData = MissionPrepInventoryCopyUtility.CloneSlot(snapshotData);
				if (ItemModificationUtility.HasAnyInstalledModification(clonedSnapshotData))
					return clonedSnapshotData;

				if (ItemModificationUtility.HasAnyInstalledModification(slotData))
					return slotData;

				return clonedSnapshotData;
			}
		}

		if (!slotData.IsEmpty)
			return slotData;

		return MissionPrepInventoryCopyUtility.CloneSlot(_binding.WeaponData);
	}

	private void ValidateModificationUiSelection(IReadOnlyList<WeaponSlotBinding> _bindings)
	{
		if (!m_ModificationUiState.HasSelection)
			return;

		ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;
		if (selectedInstance != null)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (BindingMatchesModificationSelection(binding, selectedInstance))
				{
					ApplyBindingToModificationUiState(binding, binding.WeaponData.InstanceState);
					return;
				}
			}

			if (TryRemapSelectionFromPanelSlots())
				return;

			if (TryRestoreExpandedSelectionFromAuthoritativeData())
				return;

			if (m_ModificationUiState.IsExpanded)
				return;

			m_ModificationUiState = default;
			return;
		}

		for (int i = 0; i < _bindings.Count; i++)
		{
			WeaponSlotBinding binding = _bindings[i];
			if (m_ModificationUiState.Matches(binding.IsMainHand, binding.BagIndex))
			{
				ApplyBindingToModificationUiState(binding, binding.WeaponData.InstanceState);
				return;
			}
		}

		if (TryRemapSelectionFromPanelSlots())
			return;

		if (TryRestoreExpandedSelectionFromAuthoritativeData())
			return;

		if (m_ModificationUiState.IsExpanded)
			return;

		m_ModificationUiState = default;
	}

	private void TryRestoreExpandedModificationSelectionAfterRepaint()
	{
		if (TryApplyForcedExpandedModificationSelection())
		{
			return;
		}

		if (!m_ModificationUiState.IsExpanded)
		{
			return;
		}

		if (!TryRestoreExpandedSelectionFromListIndex())
			TryRestoreExpandedSelectionFromAuthoritativeData();
	}

	private void TryPreserveExpandedModificationSelectionForWeaponSlot(
		bool _isMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState)
	{
		ItemDefinition weaponDefinition = null;
		if (m_SharedPresetStore != null &&
		    m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex)
			    .TryGetInventorySlot(_isMainHand, _bagIndex, out InventorySlotRuntimeData snapshotWeapon) &&
		    !snapshotWeapon.IsEmpty)
			weaponDefinition = snapshotWeapon.Definition;

		ForceExpandedModificationSelection(
			_isMainHand,
			_bagIndex,
			weaponDefinition,
			m_KeepExpandedRequiresInstalledModification);

		if (m_PresetInventoryPanel != null &&
		    TryResolvePresetWeaponListIndex(_isMainHand, _bagIndex, out int listIndex))
		{
			IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
			if (listIndex >= 0 && listIndex < slots.Count)
			{
				PreserveExpandedModificationSelection(_isMainHand, _bagIndex, _weaponInstanceState, slots[listIndex]);
				return;
			}
		}

		PreserveExpandedModificationSelection(_isMainHand, _bagIndex, _weaponInstanceState);
	}

	private void ForceExpandedModificationSelection(
		bool _isMainHand,
		int _bagIndex,
		ItemDefinition _weaponDefinition = null,
		bool _requireInstalledModification = false)
	{
		m_KeepExpandedAfterModificationMutation = true;
		m_KeepExpandedIsMainHand = _isMainHand;
		m_KeepExpandedBagIndex = _bagIndex;
		m_KeepExpandedWeaponDefinition = _weaponDefinition;
		m_KeepExpandedRequiresInstalledModification = _requireInstalledModification;
	}

	private void ClearForcedExpandedModificationSelection()
	{
		m_KeepExpandedAfterModificationMutation = false;
		m_KeepExpandedIsMainHand = false;
		m_KeepExpandedBagIndex = -1;
		m_KeepExpandedWeaponDefinition = null;
		m_KeepExpandedRequiresInstalledModification = false;
	}

	private bool TryApplyForcedExpandedModificationSelection()
	{
		if (!m_KeepExpandedAfterModificationMutation || m_SharedPresetStore == null)
		{
			return false;
		}

		if (!TryFindSnapshotSlotForExpandedWeapon(
			    m_KeepExpandedWeaponDefinition,
			    m_KeepExpandedIsMainHand,
			    m_KeepExpandedBagIndex,
			    m_KeepExpandedRequiresInstalledModification,
			    out bool isMainHand,
			    out int bagIndex,
			    out InventorySlotRuntimeData weaponSlot))
		{
			return false;
		}

		TryPreserveExpandedModificationSelectionForWeaponSlot(
			isMainHand,
			bagIndex,
			weaponSlot.InstanceState);
		return true;
	}

	private void ScheduleForcedExpandedModificationRepaint(bool _isMainHand, int _bagIndex)
	{
		RefreshForcedExpandedModificationSelection(_isMainHand, _bagIndex);

		if (!isActiveAndEnabled)
		{
			return;
		}

		if (m_DeferredForcedExpandedRepaintCoroutine != null)
			StopCoroutine(m_DeferredForcedExpandedRepaintCoroutine);

		m_DeferredForcedExpandedRepaintCoroutine = StartCoroutine(
			CoRepaintForcedExpandedModificationNextFrame(_isMainHand, _bagIndex));
	}

	private void RefreshForcedExpandedModificationSelection(bool _isMainHand, int _bagIndex)
	{
		ItemDefinition weaponDefinition = m_KeepExpandedWeaponDefinition;
		if (m_SharedPresetStore != null &&
		    m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex)
			    .TryGetInventorySlot(_isMainHand, _bagIndex, out InventorySlotRuntimeData snapshotWeapon) &&
		    !snapshotWeapon.IsEmpty)
			weaponDefinition = snapshotWeapon.Definition;

		ForceExpandedModificationSelection(
			_isMainHand,
			_bagIndex,
			weaponDefinition,
			m_KeepExpandedRequiresInstalledModification);
	}

	private IEnumerator CoRepaintForcedExpandedModificationNextFrame(bool _isMainHand, int _bagIndex)
	{
		yield return null;
		m_DeferredForcedExpandedRepaintCoroutine = null;

		if (!isActiveAndEnabled || m_SharedPresetStore == null)
		{
			yield break;
		}

		if (!TryFindSnapshotSlotForExpandedWeapon(
			    m_KeepExpandedWeaponDefinition,
			    _isMainHand,
			    _bagIndex,
			    m_KeepExpandedRequiresInstalledModification,
			    out bool resolvedIsMainHand,
			    out int resolvedBagIndex,
			    out InventorySlotRuntimeData weaponSlot))
		{
			ClearForcedExpandedModificationSelection();
			yield break;
		}

		RefreshForcedExpandedModificationSelection(resolvedIsMainHand, resolvedBagIndex);
		TryPreserveExpandedModificationSelectionForWeaponSlot(
			resolvedIsMainHand,
			resolvedBagIndex,
			weaponSlot.InstanceState);
		RepaintInventoryPanel();
	}

	private bool TryRestoreExpandedSelectionFromListIndex()
	{
		if (m_PresetInventoryPanel == null || m_ExpandedWeaponListIndex < 0)
			return false;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		if (m_ExpandedWeaponListIndex >= slots.Count)
			return false;

		InventorySlotView slot = slots[m_ExpandedWeaponListIndex];
		if (slot == null || !slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
			return false;

		if (!TryResolveInventoryDropTarget(slot, out bool isMainHand, out int bagIndex))
			return false;

		ItemInstanceState weaponInstance = slot.Data.InstanceState;
		if (m_SharedPresetStore != null)
		{
			MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
			if (snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData snapshotSlot) &&
			    !snapshotSlot.IsEmpty)
				weaponInstance = snapshotSlot.InstanceState;
		}

		PreserveExpandedModificationSelection(isMainHand, bagIndex, weaponInstance, slot);
		return true;
	}

	private bool TryRestoreExpandedSelectionFromAuthoritativeData()
	{
		if (m_SharedPresetStore == null)
			return false;

		if (!m_ModificationUiState.HasSelection && m_ExpandedWeaponListIndex < 0)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (!snapshot.TryGetInventorySlot(m_ModificationUiState.IsMainHand, m_ModificationUiState.BagIndex, out InventorySlotRuntimeData weaponSlot) ||
		    weaponSlot.IsEmpty ||
		    !ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
		{
			return TryRemapSelectionFromPanelSlots();
		}

		PreserveModificationSelection(
			m_ModificationUiState.IsMainHand,
			m_ModificationUiState.BagIndex,
			weaponSlot.InstanceState,
			m_ModificationUiState.DisplayState);
		return true;
	}

	private bool TryResolvePresetWeaponListIndex(bool _isMainHand, int _bagIndex, out int _listIndex)
	{
		_listIndex = -1;
		if (m_PresetInventoryPanel == null)
			return false;

		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		for (int i = 0; i < slots.Count; i++)
		{
			bool isMainHand = i < lead && i == 0;
			int bagIndex = isMainHand ? -1 : i - lead;
			if (isMainHand == _isMainHand && bagIndex == _bagIndex)
			{
				_listIndex = i;
				return true;
			}
		}

		return false;
	}

	private void PreserveExpandedModificationSelection(
		bool _isMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState,
		InventorySlotView _slotView = null)
	{
		PreserveModificationSelection(
			_isMainHand,
			_bagIndex,
			_weaponInstanceState,
			RuntimeModifiableWeaponDisplayState.Expanded,
			_slotView);
	}

	private bool TryRemapSelectionFromPanelSlots()
	{
		if (m_PresetInventoryPanel == null)
			return false;

		ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;
		IReadOnlyList<InventorySlotView> slots = m_PresetInventoryPanel.Slots;
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null || !slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
				continue;

			bool isMainHand = i < lead && i == 0;
			int bagIndex = isMainHand ? -1 : i - lead;

			InventorySlotRuntimeData weaponData = slot.Data;
			if (m_SharedPresetStore != null)
			{
				MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
				if (snapshot.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData snapshotSlot) &&
				    !snapshotSlot.IsEmpty)
					weaponData = snapshotSlot;
			}

			bool matchesInstance = selectedInstance != null && weaponData.InstanceState == selectedInstance;
			bool matchesIndex = m_ModificationUiState.Matches(isMainHand, bagIndex);
			if (!matchesInstance && !matchesIndex)
				continue;

			PreserveModificationSelection(
				isMainHand,
				bagIndex,
				weaponData.InstanceState,
				m_ModificationUiState.DisplayState,
				slot);
			return true;
		}

		return false;
	}

	private void PreserveModificationSelection(
		bool _isMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState,
		RuntimeModifiableWeaponDisplayState _displayState,
		InventorySlotView _slotView = null)
	{
		m_SuppressOutsideClickUntilFrame = Time.frameCount + 2;

		if (_slotView != null && m_PresetInventoryPanel != null)
			m_ExpandedWeaponListIndex = _displayState == RuntimeModifiableWeaponDisplayState.Expanded
				? m_PresetInventoryPanel.GetInventorySlotListIndex(_slotView)
				: -1;
		else if (_displayState == RuntimeModifiableWeaponDisplayState.Expanded &&
		         TryResolvePresetWeaponListIndex(_isMainHand, _bagIndex, out int listIndex))
			m_ExpandedWeaponListIndex = listIndex;
		else
			m_ExpandedWeaponListIndex = -1;

		m_ModificationUiState = MissionPrepModificationUiState.CreateSelection(
			_isMainHand,
			_bagIndex,
			_weaponInstanceState,
			_displayState);
		ModificationGraphDataChanged?.Invoke();
	}

	private void SetDisplayState(RuntimeModifiableWeaponDisplayState _displayState)
	{
		if (_displayState == RuntimeModifiableWeaponDisplayState.Collapsed)
			ClearForcedExpandedModificationSelection();

		if (!m_ModificationUiState.HasSelection || m_ModificationUiState.DisplayState == _displayState)
		{
			return;
		}

		if (_displayState == RuntimeModifiableWeaponDisplayState.Collapsed)
		{
			m_ExpandedWeaponListIndex = -1;
		}

		m_ModificationUiState.DisplayState = _displayState;
		RebuildInlineModificationRows();
		ModificationGraphDataChanged?.Invoke();
	}

	private void ApplyBindingToModificationUiState(WeaponSlotBinding _binding, ItemInstanceState _weaponInstanceState)
	{
		RuntimeModifiableWeaponDisplayState displayState = m_ModificationUiState.DisplayState;
		ItemInstanceState weaponInstance = _weaponInstanceState;
		if (weaponInstance == null && _binding.SlotView != null && _binding.SlotView.HasItem)
		{
			if (m_SharedPresetStore != null &&
			    m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex)
				    .TryGetInventorySlot(_binding.IsMainHand, _binding.BagIndex, out InventorySlotRuntimeData snapshotSlot) &&
			    !snapshotSlot.IsEmpty)
				weaponInstance = snapshotSlot.InstanceState;
			else
				weaponInstance = _binding.WeaponData.InstanceState;
		}

		m_ExpandedWeaponListIndex = displayState == RuntimeModifiableWeaponDisplayState.Expanded
			? _binding.ListIndex
			: -1;
		m_ModificationUiState = MissionPrepModificationUiState.CreateSelection(
			_binding.IsMainHand,
			_binding.BagIndex,
			weaponInstance,
			displayState);
	}

	private bool IsSameWeaponAsSelection(ItemInstanceState _weaponInstanceState, bool _isMainHand, int _bagIndex)
	{
		if (!m_ModificationUiState.HasSelection)
			return false;

		if (m_ModificationUiState.Matches(_isMainHand, _bagIndex))
			return true;

		if (_weaponInstanceState != null && m_ModificationUiState.SelectedWeaponInstanceState != null)
			return _weaponInstanceState == m_ModificationUiState.SelectedWeaponInstanceState;

		return false;
	}

	private void ResolveExpandedWeaponSlotAfterMutation(
		ItemDefinition _weaponDefinition,
		bool _preferredIsMainHand,
		int _preferredBagIndex,
		InventorySlotRuntimeData _mutatedWeaponSlot,
		bool _requireInstalledModification,
		out bool _resolvedIsMainHand,
		out int _resolvedBagIndex)
	{
		if (TryFindSnapshotSlotForExpandedWeapon(
			    _weaponDefinition,
			    _preferredIsMainHand,
			    _preferredBagIndex,
			    _requireInstalledModification,
			    out _resolvedIsMainHand,
			    out _resolvedBagIndex,
			    out InventorySlotRuntimeData resolvedWeapon) &&
		    (resolvedWeapon.InstanceState == _mutatedWeaponSlot.InstanceState ||
		     resolvedWeapon.Definition == _mutatedWeaponSlot.Definition))
			return;

		_resolvedIsMainHand = _preferredIsMainHand;
		_resolvedBagIndex = _preferredBagIndex;
	}

	private bool TryFindSnapshotSlotForExpandedWeapon(
		ItemDefinition _weaponDefinition,
		bool _preferredIsMainHand,
		int _preferredBagIndex,
		bool _requireInstalledModification,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isMainHand = false;
		_bagIndex = -1;
		_weaponSlot = default;

		if (m_SharedPresetStore == null)
			return false;

		MissionPrepPresetSnapshot snapshot = m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex);
		if (TryFindSnapshotSlotForExpandedWeaponInSnapshot(
			    snapshot,
			    _weaponDefinition,
			    _preferredIsMainHand,
			    _preferredBagIndex,
			    _requireInstalledModification,
			    out _isMainHand,
			    out _bagIndex,
			    out _weaponSlot))
			return true;

		return false;
	}

	private static bool TryFindSnapshotSlotForExpandedWeaponInSnapshot(
		MissionPrepPresetSnapshot _snapshot,
		ItemDefinition _weaponDefinition,
		bool _preferredIsMainHand,
		int _preferredBagIndex,
		bool _requireInstalledModification,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isMainHand = false;
		_bagIndex = -1;
		_weaponSlot = default;

		if (_snapshot == null)
			return false;

		if (SlotMatchesExpandedWeaponSearch(
			    _snapshot,
			    _preferredIsMainHand,
			    _preferredBagIndex,
			    _weaponDefinition,
			    _requireInstalledModification,
			    out _weaponSlot))
		{
			_isMainHand = _preferredIsMainHand;
			_bagIndex = _preferredBagIndex;
			return true;
		}

		if (SlotMatchesExpandedWeaponSearch(
			    _snapshot,
			    _isMainHandEquipmentSlot: true,
			    _bagIndex: -1,
			    _weaponDefinition,
			    _requireInstalledModification,
			    out _weaponSlot))
		{
			_isMainHand = true;
			_bagIndex = -1;
			return true;
		}

		for (int bagIndex = 0; bagIndex < _snapshot.BagCount; bagIndex++)
		{
			if (bagIndex == _preferredBagIndex && !_preferredIsMainHand)
				continue;

			if (SlotMatchesExpandedWeaponSearch(
				    _snapshot,
				    _isMainHandEquipmentSlot: false,
				    bagIndex,
				    _weaponDefinition,
				    _requireInstalledModification,
				    out _weaponSlot))
			{
				_isMainHand = false;
				_bagIndex = bagIndex;
				return true;
			}
		}

		return false;
	}

	private static bool SlotMatchesExpandedWeaponSearch(
		MissionPrepPresetSnapshot _snapshot,
		bool _isMainHandEquipmentSlot,
		int _bagIndex,
		ItemDefinition _weaponDefinition,
		bool _requireInstalledModification,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_weaponSlot = default;
		if (!_snapshot.TryGetInventorySlot(_isMainHandEquipmentSlot, _bagIndex, out InventorySlotRuntimeData candidate) ||
		    candidate.IsEmpty ||
		    !ItemModificationUtility.IsModifiableWeapon(candidate.Definition))
			return false;

		if (_weaponDefinition != null && candidate.Definition != _weaponDefinition)
			return false;

		if (_requireInstalledModification &&
		    !ItemModificationUtility.HasAnyInstalledModification(candidate))
			return false;

		_weaponSlot = candidate;
		return true;
	}

	private void TryRemapExpandedSelectionFromBindings(IReadOnlyList<WeaponSlotBinding> _bindings)
	{
		if (_bindings == null || _bindings.Count == 0)
			return;

		if (!m_ModificationUiState.IsExpanded && !m_KeepExpandedAfterModificationMutation)
			return;

		if (TryRemapExpandedSelectionToBinding(FindExpandedWeaponBinding(_bindings)))
			return;

		if (m_ExpandedWeaponListIndex >= 0)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (binding.ListIndex != m_ExpandedWeaponListIndex)
					continue;

				TryRemapExpandedSelectionToBinding(binding);
				return;
			}
		}

		if (m_ModificationUiState.HasSelection)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (!m_ModificationUiState.Matches(binding.IsMainHand, binding.BagIndex))
					continue;

				TryRemapExpandedSelectionToBinding(binding);
				return;
			}
		}
	}

	private WeaponSlotBinding? FindExpandedWeaponBinding(IReadOnlyList<WeaponSlotBinding> _bindings)
	{
		WeaponSlotBinding? preferredMatch = null;
		WeaponSlotBinding? definitionMatch = null;
		WeaponSlotBinding? installedModMatch = null;

		for (int i = 0; i < _bindings.Count; i++)
		{
			WeaponSlotBinding binding = _bindings[i];
			if (!BindingMatchesKeepExpandedWeapon(binding))
				continue;

			if (binding.IsMainHand == m_KeepExpandedIsMainHand && binding.BagIndex == m_KeepExpandedBagIndex)
				preferredMatch = binding;

			if (m_KeepExpandedWeaponDefinition != null &&
			    binding.WeaponData.Definition == m_KeepExpandedWeaponDefinition)
				definitionMatch = binding;

			InventorySlotRuntimeData resolvedWeapon = ResolveWeaponDataForModificationUi(binding);
			if (ItemModificationUtility.HasAnyInstalledModification(resolvedWeapon))
				installedModMatch = binding;
		}

		if (preferredMatch.HasValue)
			return preferredMatch;

		if (definitionMatch.HasValue)
			return definitionMatch;

		if (installedModMatch.HasValue && _bindings.Count == 1)
			return installedModMatch;

		if (m_KeepExpandedAfterModificationMutation && _bindings.Count == 1)
			return _bindings[0];

		return null;
	}

	private bool TryRemapExpandedSelectionToBinding(WeaponSlotBinding? _binding)
	{
		if (!_binding.HasValue)
			return false;

		WeaponSlotBinding binding = _binding.Value;
		InventorySlotRuntimeData weaponData = ResolveWeaponDataForModificationUi(binding);
		ItemInstanceState weaponInstance = weaponData.IsEmpty
			? binding.WeaponData.InstanceState
			: weaponData.InstanceState;

		m_KeepExpandedIsMainHand = binding.IsMainHand;
		m_KeepExpandedBagIndex = binding.BagIndex;
		if (weaponData.Definition != null)
			m_KeepExpandedWeaponDefinition = weaponData.Definition;

		RuntimeModifiableWeaponDisplayState displayState = m_ModificationUiState.DisplayState;
		if (m_KeepExpandedAfterModificationMutation || displayState == RuntimeModifiableWeaponDisplayState.Expanded)
			displayState = RuntimeModifiableWeaponDisplayState.Expanded;

		m_ExpandedWeaponListIndex = binding.ListIndex;
		m_ModificationUiState = MissionPrepModificationUiState.CreateSelection(
			binding.IsMainHand,
			binding.BagIndex,
			weaponInstance,
			displayState);
		return true;
	}

	private bool BindingMatchesKeepExpandedWeapon(WeaponSlotBinding _binding)
	{
		if (!m_KeepExpandedAfterModificationMutation)
			return false;

		if (_binding.IsMainHand == m_KeepExpandedIsMainHand && _binding.BagIndex == m_KeepExpandedBagIndex)
			return true;

		if (m_ExpandedWeaponListIndex >= 0 && _binding.ListIndex == m_ExpandedWeaponListIndex)
			return true;

		if (m_ModificationUiState.IsExpanded &&
		    m_ModificationUiState.Matches(_binding.IsMainHand, _binding.BagIndex))
			return true;

		if (m_KeepExpandedWeaponDefinition == null ||
		    _binding.WeaponData.Definition != m_KeepExpandedWeaponDefinition)
			return false;

		if (!m_KeepExpandedRequiresInstalledModification)
			return true;

		InventorySlotRuntimeData resolvedWeapon = ResolveWeaponDataForModificationUi(_binding);
		return ItemModificationUtility.HasAnyInstalledModification(resolvedWeapon);
	}

	private bool ShouldExpandEmptySlotsForBinding(WeaponSlotBinding _binding)
	{
		if (BindingMatchesKeepExpandedWeapon(_binding))
			return true;

		if (!m_ModificationUiState.HasSelection || !m_ModificationUiState.IsExpanded)
			return false;

		if (m_ExpandedWeaponListIndex >= 0 && _binding.ListIndex == m_ExpandedWeaponListIndex)
			return true;

		if (m_ModificationUiState.Matches(_binding.IsMainHand, _binding.BagIndex))
			return true;

		ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;
		if (selectedInstance == null)
			return false;

		return _binding.WeaponData.InstanceState == selectedInstance;
	}

	private bool BindingMatchesModificationSelection(WeaponSlotBinding _binding, ItemInstanceState _selectedInstance)
	{
		if (m_ModificationUiState.IsExpanded &&
		    m_ExpandedWeaponListIndex >= 0 &&
		    _binding.ListIndex == m_ExpandedWeaponListIndex)
			return true;

		if (m_ModificationUiState.Matches(_binding.IsMainHand, _binding.BagIndex))
			return true;

		if (_selectedInstance == null)
			return false;

		if (_binding.WeaponData.InstanceState == _selectedInstance)
			return true;

		return _binding.SlotView != null && _binding.SlotView.Data.InstanceState == _selectedInstance;
	}

	public void RemapModificationSelectionForWeapon(ItemInstanceState _weaponInstanceState)
	{
		if (_weaponInstanceState == null || !m_ModificationUiState.HasSelection)
			return;

		if (m_ModificationUiState.SelectedWeaponInstanceState != null &&
		    m_ModificationUiState.SelectedWeaponInstanceState != _weaponInstanceState)
			return;

		m_ModificationUiState.SelectedWeaponInstanceState = _weaponInstanceState;
		m_WeaponSlotBindingBuffer.Clear();
		CollectModifiableWeaponBindings(m_WeaponSlotBindingBuffer);
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);
	}

	private void RemapModificationSelectionAfterWeaponSlotChange(
		ItemDefinition _weaponDefinition,
		ItemInstanceState _previousInstance,
		bool _fromMainHand,
		int _fromBagIndex)
	{
		ClearForcedExpandedModificationSelection();
		m_ExpandedWeaponListIndex = -1;

		if (_weaponDefinition == null || !ItemModificationUtility.IsModifiableWeapon(_weaponDefinition))
		{
			if (m_ModificationUiState.HasSelection &&
			    (_previousInstance == null ||
			     m_ModificationUiState.SelectedWeaponInstanceState == null ||
			     m_ModificationUiState.SelectedWeaponInstanceState == _previousInstance))
				ClearModificationUiSelection();
			return;
		}

		if (!m_ModificationUiState.HasSelection)
			return;

		if (_previousInstance != null &&
		    m_ModificationUiState.SelectedWeaponInstanceState != null &&
		    m_ModificationUiState.SelectedWeaponInstanceState != _previousInstance)
			return;

		if (m_SharedPresetStore == null ||
		    !TryFindWeaponSelectionLocationAfterSlotChange(
			    m_SharedPresetStore.GetSnapshot(m_EditingPresetCatalogIndex),
			    _fromMainHand,
			    _fromBagIndex,
			    out bool isMainHand,
			    out int bagIndex,
			    out ItemInstanceState weaponInstance))
		{
			ClearModificationUiSelection();
			return;
		}

		m_ModificationUiState = MissionPrepModificationUiState.CreateSelection(
			isMainHand,
			bagIndex,
			weaponInstance,
			RuntimeModifiableWeaponDisplayState.Collapsed);
	}

	private static bool TryFindWeaponSelectionLocationAfterSlotChange(
		MissionPrepPresetSnapshot _snapshot,
		bool _fromMainHand,
		int _fromBagIndex,
		out bool _isMainHand,
		out int _bagIndex,
		out ItemInstanceState _weaponInstance)
	{
		_isMainHand = false;
		_bagIndex = -1;
		_weaponInstance = null;

		if (_snapshot == null)
			return false;

		if (!_fromMainHand)
		{
			if (_snapshot.MainHandEquipment.IsEmpty ||
			    !ItemModificationUtility.IsModifiableWeapon(_snapshot.MainHandEquipment.Definition))
				return false;

			_isMainHand = true;
			_bagIndex = -1;
			_weaponInstance = _snapshot.MainHandEquipment.InstanceState;
			return true;
		}

		if (_snapshot.BagCount <= 0)
			return false;

		int targetBagIndex = _snapshot.BagCount - 1;
		if (!_snapshot.TryGetInventorySlot(false, targetBagIndex, out InventorySlotRuntimeData bagWeapon) ||
		    bagWeapon.IsEmpty ||
		    !ItemModificationUtility.IsModifiableWeapon(bagWeapon.Definition))
			return false;

		_isMainHand = false;
		_bagIndex = targetBagIndex;
		_weaponInstance = bagWeapon.InstanceState;
		return true;
	}

	private IEnumerator CoRefreshInlineModificationRowsNextFrame()
	{
		yield return null;
		m_DeferredInlineRefreshCoroutine = null;
		if (!m_PendingInlineRefresh)
			yield break;

		RebuildInlineModificationRows();
	}

	private void HandleModificationDragContextChanged()
	{
		MissionPrepInlineModificationBuilder.RefreshHighlights(m_PresetInventoryPanel);
		MissionPrepInlineModificationBuilder.RefreshMainHandSlotHighlights(m_PresetInventoryPanel);
		RefreshModificationCompatibilityHighlights();
		ModificationGraphDataChanged?.Invoke();
	}

	private InventorySlotRuntimeData ResolveGraphPreviewCandidate()
	{
		MissionPrepModificationDragPayload dragPayload = MissionPrepModificationDragContext.Current;
		if (dragPayload.HasItem &&
		    dragPayload.SourceKind != MissionPrepModificationDragSourceKind.ModificationSlot &&
		    ItemModificationUtility.IsModificationItem(dragPayload.Item))
			return dragPayload.Item;

		return m_HoveredModificationPreviewCandidate;
	}

	private bool TryBuildPreviewAttachments(
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate,
		out WeaponAttachmentDefinition[] _previewAttachments)
	{
		_previewAttachments = null;
		WeaponRuntimeState weaponState = _weaponSlot.InstanceState != null ? _weaponSlot.InstanceState.WeaponState : null;
		WeaponDefinition weaponDefinition = weaponState != null ? weaponState.WeaponDefinition : _weaponSlot.Definition != null ? _weaponSlot.Definition.WeaponDefinition : null;
		WeaponAttachmentDefinition candidateAttachment = _candidate.Definition != null ? _candidate.Definition.WeaponAttachmentDefinition : null;
		if (weaponDefinition == null || candidateAttachment == null)
			return false;

		m_ModificationDescriptorBuffer.Clear();
		ItemModificationUtility.BuildSlotDescriptors(_weaponSlot.Definition, m_ModificationDescriptorBuffer);
		for (int i = 0; i < m_ModificationDescriptorBuffer.Count; i++)
		{
			ItemModificationSlotDescriptor descriptor = m_ModificationDescriptorBuffer[i];
			if (descriptor.Kind != ItemModificationSlotKind.Attachment)
				continue;

			if (!ItemModificationUtility.CanAcceptItem(descriptor, _weaponSlot, _candidate))
				continue;

			_previewAttachments = BuildAttachmentPreviewArray(weaponDefinition, weaponState, descriptor.WeaponSlotIndex, candidateAttachment);
			return true;
		}

		return false;
	}

	private static WeaponAttachmentDefinition[] BuildAttachmentPreviewArray(
		WeaponDefinition _weaponDefinition,
		WeaponRuntimeState _weaponState,
		int _slotIndex,
		WeaponAttachmentDefinition _candidateAttachment)
	{
		int slotCount = _weaponDefinition != null && _weaponDefinition.AttachmentSlots != null ? _weaponDefinition.AttachmentSlots.Length : 0;
		int currentCount = _weaponState != null && _weaponState.EquippedAttachments != null ? _weaponState.EquippedAttachments.Length : 0;
		int length = Mathf.Max(slotCount, currentCount, _slotIndex + 1);
		if (length <= 0)
			return null;

		WeaponAttachmentDefinition[] result = new WeaponAttachmentDefinition[length];
		if (_weaponState != null && _weaponState.EquippedAttachments != null)
		{
			int copyCount = Mathf.Min(_weaponState.EquippedAttachments.Length, result.Length);
			for (int i = 0; i < copyCount; i++)
				result[i] = _weaponState.EquippedAttachments[i];
		}

		if (_slotIndex >= 0 && _slotIndex < result.Length)
			result[_slotIndex] = _candidateAttachment;

		return result;
	}

	private static WeaponAttachmentDefinition[] CopyAttachmentArray(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null || _attachments.Length == 0)
			return null;

		WeaponAttachmentDefinition[] copy = new WeaponAttachmentDefinition[_attachments.Length];
		for (int i = 0; i < _attachments.Length; i++)
			copy[i] = _attachments[i];

		return copy;
	}

	private static bool PreviewLoadoutDiffersFromCurrent(
		WeaponDefinition _currentWeaponDefinition,
		WeaponAttachmentDefinition[] _currentAttachments,
		WeaponDefinition _previewWeaponDefinition,
		WeaponAttachmentDefinition[] _previewAttachments)
	{
		if (_previewWeaponDefinition == null && _previewAttachments == null)
			return false;

		WeaponDefinition previewWeapon = _previewWeaponDefinition != null ? _previewWeaponDefinition : _currentWeaponDefinition;
		if (_currentWeaponDefinition == null)
			return previewWeapon != null || _previewAttachments != null;

		if (_previewWeaponDefinition != null && _previewWeaponDefinition != _currentWeaponDefinition)
			return true;

		return !AttachmentArraysEquivalent(_currentAttachments, _previewAttachments);
	}

	private static bool AttachmentArraysEquivalent(
		WeaponAttachmentDefinition[] _left,
		WeaponAttachmentDefinition[] _right)
	{
		if (_left == null || _left.Length == 0)
			return _right == null || _right.Length == 0;

		if (_right == null || _right.Length != _left.Length)
			return false;

		for (int i = 0; i < _left.Length; i++)
		{
			if (_left[i] != _right[i])
				return false;
		}

		return true;
	}

	private static string FormatInventorySlotWeapon(InventorySlotRuntimeData _weaponSlot)
	{
		if (_weaponSlot.IsEmpty || _weaponSlot.Definition == null)
			return "empty";

		WeaponDefinition weaponDefinition = _weaponSlot.Definition.WeaponDefinition;
		return weaponDefinition != null ? weaponDefinition.name : _weaponSlot.Definition.name;
	}
	#endregion
}

/// <summary>
/// Слот экипированного оружия пресета: подсветка при drag и приём сброса оружия.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepMainHandEquipmentSlotView : MonoBehaviour, IDropHandler, IInventoryEquipmentSlotDropHandler
{
	#region Private Fields
	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventoryPanelView panel = m_Slot.GetComponentInParent<InventoryPanelView>();
		if (panel != null)
			InventorySlotUiUtility.ConfigureMainHandEquipmentSlot(m_Slot, panel.EquipmentSlotAppearance);

		InventorySlotUiUtility.EnsureEquipmentSlotDropReceiver(this);
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventorySlotUiUtility.ApplyMainHandEquipmentSlotHighlight(m_Slot, InventorySlotUiUtility.IsWeaponEquipDragActive());
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		HandleEquipmentSlotDrop(eventData);
	}

	public void HandleEquipmentSlotDrop(PointerEventData eventData)
	{
		if (MissionPrepModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null || eventData?.pointerDrag == null)
			return;

		if (eventData.pointerDrag.TryGetComponent(out MissionPrepAvailableToPresetDrag availableDrag) &&
		    availableDrag.IsDraggingFromAvailable)
		{
			if (!m_Coordinator.TryEquipAvailableSlotToMainHand(availableDrag.SlotView))
				return;

			availableDrag.NotifyDropAccepted();
			return;
		}

		if (!eventData.pointerDrag.TryGetComponent(out MissionPrepPresetToAvailableDrag presetDrag) ||
		    !presetDrag.IsDraggingFromPreset)
			return;

		if (!presetDrag.HasResolvedSlot || presetDrag.IsMainHandSlot)
			return;

		if (!m_Coordinator.TryMovePresetBagItemToMainHand(presetDrag.BagIndex))
			return;

		presetDrag.NotifyDropAccepted();
	}
	#endregion
}

/// <summary>
/// Drop-цель для ячейки инвентаря пресета: снятие/экипировка оружия и приём из каталога.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepPresetInventorySlotDropView : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		if (MissionPrepModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null || m_Slot == null || eventData?.pointerDrag == null)
			return;

		if (!m_Coordinator.TryResolveInventoryDropTarget(m_Slot, out bool targetIsMainHand, out _))
			return;

		if (eventData.pointerDrag.TryGetComponent(out MissionPrepAvailableToPresetDrag availableDrag) &&
		    availableDrag.IsDraggingFromAvailable)
		{
			if (targetIsMainHand)
			{
				if (m_Coordinator.TryEquipAvailableSlotToMainHand(availableDrag.SlotView))
					availableDrag.NotifyDropAccepted();
			}
			else if (m_Coordinator.TryAcceptAvailableDrag(availableDrag))
			{
				availableDrag.NotifyDropAccepted();
			}

			return;
		}

		if (!eventData.pointerDrag.TryGetComponent(out MissionPrepPresetToAvailableDrag presetDrag) ||
		    !presetDrag.IsDraggingFromPreset)
			return;

		if (presetDrag.IsMainHandSlot && !targetIsMainHand)
		{
			if (m_Coordinator.TryUnequipPresetMainHandToBag())
				presetDrag.NotifyDropAccepted();
			return;
		}

		if (!presetDrag.IsMainHandSlot && targetIsMainHand)
		{
			if (m_Coordinator.TryMovePresetBagItemToMainHand(presetDrag.BagIndex))
				presetDrag.NotifyDropAccepted();
		}
	}
	#endregion
}

/// <summary>
/// Подсветка совместимых предметов на панели «доступное снаряжение», пока раскрыт полный список слотов модулей.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepAvailableEquipmentSlotHighlightView : MonoBehaviour
{
	#region Private Fields
	private readonly Color m_NormalColor = MissionPrepInventoryUiColors.CellBackground;
	private readonly Color m_CompatibleColor = MissionPrepInventoryUiColors.CompatibleHighlight;

	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	private Image m_BackgroundImage;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		EnsureBackgroundImage();
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		EnsureBackgroundImage();
		if (m_BackgroundImage == null || m_Slot == null || !m_Slot.HasItem)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		bool compatible = m_Coordinator != null &&
		                  m_Coordinator.ShouldHighlightCompatibleWithModificationWeapon(m_Slot.Data);

		m_BackgroundImage.color = compatible ? m_CompatibleColor : m_NormalColor;
	}
	#endregion

	#region Private Methods
	private void EnsureBackgroundImage()
	{
		if (m_BackgroundImage != null)
			return;

		m_BackgroundImage = GetComponent<Image>();
		if (m_BackgroundImage != null)
			m_BackgroundImage.color = m_NormalColor;
	}
	#endregion
}

/// <summary>
/// Подсветка совместимых предметов в сумке пресета, пока раскрыт полный список слотов модулей.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepPresetInventorySlotHighlightView : MonoBehaviour
{
	#region Private Fields
	private readonly Color m_NormalColor = MissionPrepInventoryUiColors.CellBackground;
	private readonly Color m_CompatibleColor = MissionPrepInventoryUiColors.CompatibleHighlight;

	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	private Image m_BackgroundImage;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		EnsureBackgroundImage();
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		EnsureBackgroundImage();
		if (m_BackgroundImage == null || m_Slot == null || !m_Slot.HasItem)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		bool compatible = m_Coordinator != null &&
		                  m_Coordinator.ShouldHighlightCompatibleWithModificationWeapon(m_Slot.Data);

		m_BackgroundImage.color = compatible ? m_CompatibleColor : m_NormalColor;
	}
	#endregion

	#region Private Methods
	private void EnsureBackgroundImage()
	{
		if (m_BackgroundImage != null)
			return;

		m_BackgroundImage = GetComponent<Image>();
		if (m_BackgroundImage != null)
			m_BackgroundImage.color = m_NormalColor;
	}
	#endregion
}

/// <summary>
/// Передаёт графикам сравнение с другим оружием при наведении на ячейку с оружием.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepWeaponProfileGraphHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	#region Private Fields
	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	private bool m_IsHovering;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData eventData)
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
		if (m_Coordinator == null || m_Slot == null || !m_Slot.HasItem)
			return;

		if (!ItemModificationUtility.IsModifiableWeapon(m_Slot.Data.Definition))
			return;

		m_IsHovering = true;
		m_Coordinator.SetHoveredWeaponGraphCandidate(m_Slot.Data);
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		if (!m_IsHovering)
			return;

		m_IsHovering = false;
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
		m_Coordinator?.ClearHoveredWeaponGraphCandidate(m_Slot != null ? m_Slot.Data : default);
	}
	#endregion
}

/// <summary>
/// Передаёт графикам временный модуль, когда курсор наведён на совместимый предмет.
/// Drag-preview идёт через <see cref="MissionPrepModificationDragContext"/> и имеет приоритет.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepModificationPreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	#region Private Fields
	private MissionPrepLoadoutCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	private bool m_IsHovering;
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData eventData)
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
		if (m_Coordinator == null || m_Slot == null || !m_Slot.HasItem)
			return;

		m_IsHovering = true;
		m_Coordinator.SetHoveredModificationPreviewCandidate(m_Slot.Data);
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		if (!m_IsHovering)
			return;

		m_IsHovering = false;
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
		m_Coordinator?.ClearHoveredModificationPreviewCandidate(m_Slot != null ? m_Slot.Data : default);
	}
	#endregion
}
