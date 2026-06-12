using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI модификации оружия в runtime-инвентаре юнита и на панели «земля».
/// Данные меняются через <see cref="ItemModificationUtility"/>; экипированный main hand — через reload-анимацию.
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
	private Coroutine m_DeferredMagazineRepaintCoroutine;
	private bool m_PendingInlineRefresh;
	private int m_SuppressOutsideClickUntilFrame = -1;
	private System.Action<InventorySlotRuntimeData> m_PendingUiMagazineEjectHandler;
	private UnitWeaponReloadController m_SubscribedReloadController;
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
		InventoryEquipmentEquipHoverContext.Changed += HandleEquipmentEquipHoverChanged;
		TrySubscribeReloadCompletionHandler();
	}

	private void OnDisable()
	{
		RuntimeInventoryModificationDragContext.Changed -= HandleModificationDragContextChanged;
		InventoryEquipmentEquipHoverContext.Changed -= HandleEquipmentEquipHoverChanged;
		TryUnsubscribeReloadCompletionHandler();
		RuntimeModificationSlotDrag.CleanupActiveDragVisual();
		RuntimeInventoryModificationDragContext.ResetAfterDrag();
		InventoryEquipmentEquipHoverContext.ClearAll();
		if (m_DeferredMagazineRepaintCoroutine != null)
		{
			StopCoroutine(m_DeferredMagazineRepaintCoroutine);
			m_DeferredMagazineRepaintCoroutine = null;
		}

		m_ModificationUiState = default;
		m_PendingUiMagazineEjectHandler = null;
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

			if (IsSameWeaponAsSelection(_slot.Data.InstanceState, true, groundSlotIndex, false, -1) &&
			    m_ModificationUiState.IsExpanded)
				return true;

			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
				groundSlotIndex,
				_slot.Data.InstanceState,
				RuntimeModifiableWeaponDisplayState.Expanded);
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

		if (IsSameWeaponAsSelection(weaponSlot.InstanceState, false, -1, isMainHand, bagIndex) &&
		    m_ModificationUiState.IsExpanded)
			return true;

		m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
			isMainHand,
			bagIndex,
			weaponSlot.InstanceState,
			RuntimeModifiableWeaponDisplayState.Expanded);
		RebuildInlineModificationRows();
		return true;
	}

	public bool TryToggleModificationPanel(InventorySlotView _slot)
	{
		if (!TryResolveModificationToggleTarget(
			    _slot,
			    out bool isGroundSlot,
			    out int groundSlotIndex,
			    out bool isMainHand,
			    out int bagIndex,
			    out InventorySlotRuntimeData weaponSlot))
			return false;

		if (isGroundSlot)
		{
			if (IsSameWeaponAsSelection(weaponSlot.InstanceState, true, groundSlotIndex, false, -1))
				SetDisplayState(m_ModificationUiState.IsExpanded
					? RuntimeModifiableWeaponDisplayState.Collapsed
					: RuntimeModifiableWeaponDisplayState.Expanded);
			else
				m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
					groundSlotIndex,
					weaponSlot.InstanceState,
					RuntimeModifiableWeaponDisplayState.Expanded);

			RebuildInlineModificationRows();
			return true;
		}

		if (IsSameWeaponAsSelection(weaponSlot.InstanceState, false, -1, isMainHand, bagIndex))
			SetDisplayState(m_ModificationUiState.IsExpanded
				? RuntimeModifiableWeaponDisplayState.Collapsed
				: RuntimeModifiableWeaponDisplayState.Expanded);
		else
			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
				isMainHand,
				bagIndex,
				weaponSlot.InstanceState,
				RuntimeModifiableWeaponDisplayState.Expanded);

		RebuildInlineModificationRows();
		return true;
	}

	public void TryCollapseModificationPanelForDoubleClick(InventorySlotView _slot)
	{
		if (!TryResolveModificationToggleTarget(
			    _slot,
			    out bool isGroundSlot,
			    out int groundSlotIndex,
			    out bool isMainHand,
			    out int bagIndex,
			    out InventorySlotRuntimeData weaponSlot))
			return;

		if (!m_ModificationUiState.HasSelection || !m_ModificationUiState.IsExpanded)
			return;

		if (!IsSameWeaponAsSelection(weaponSlot.InstanceState, isGroundSlot, groundSlotIndex, isMainHand, bagIndex))
			return;

		SetDisplayState(RuntimeModifiableWeaponDisplayState.Collapsed);
	}

	public RuntimeModifiableWeaponDisplayState GetDisplayStateForSlot(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
			return RuntimeModifiableWeaponDisplayState.Collapsed;

		if (TryResolveGroundSlot(_slot, out int groundSlotIndex))
		{
			return IsSameWeaponAsSelection(_slot.Data.InstanceState, true, groundSlotIndex, false, -1)
				? m_ModificationUiState.DisplayState
				: RuntimeModifiableWeaponDisplayState.Collapsed;
		}

		if (!TryResolveCharacterSlot(_slot, out bool isMainHand, out int bagIndex))
			return RuntimeModifiableWeaponDisplayState.Collapsed;

		CharacterInventory inventory = ActiveInventory;
		if (inventory == null || !inventory.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData weaponSlot))
			return RuntimeModifiableWeaponDisplayState.Collapsed;

		return IsSameWeaponAsSelection(weaponSlot.InstanceState, false, -1, isMainHand, bagIndex)
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
		SetDisplayState(RuntimeModifiableWeaponDisplayState.Collapsed);
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

		m_PendingInlineRefresh = true;
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
		int _weaponGroundSlotIndex = -1,
		InventorySlotView _weaponInventorySlotView = null)
	{
		const string context = "Runtime.TryInstallModificationFromDrag";
		RuntimeInventoryModificationDragPayload payload = RuntimeInventoryModificationDragContext.Current;
		if (!payload.HasItem)
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "validate_payload", "drag payload has no item");
			return false;
		}

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.ModificationSlot)
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "validate_source", "cannot install from another modification slot via drop");
			return false;
		}

		if (!TryResolveWeaponForModification(
			    _weaponIsOnGroundPanel,
			    _weaponGroundSlotIndex,
			    _weaponIsMainHand,
			    _weaponBagIndex,
			    _weaponInventorySlotView,
			    out InventorySlotRuntimeData weaponSlot,
			    out bool resolvedIsMainHand,
			    out int resolvedBagIndex,
			    out bool resolvedIsOnGroundPanel,
			    out int resolvedGroundSlotIndex))
		{
			ItemModificationDiagnostics.LogFlowRejected(
				context,
				"resolve_weapon",
				$"character weapon not found (mainHand={_weaponIsMainHand}, bagIndex={_weaponBagIndex})");
			return false;
		}

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.CharacterBag)
		{
			if (ActiveInventory == null || payload.SlotIndex < 0)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", "invalid character bag drag source");
				return false;
			}
			if (!resolvedIsOnGroundPanel && !resolvedIsMainHand && payload.SlotIndex == resolvedBagIndex)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", "cannot drag from same bag slot as weapon");
				return false;
			}
			if (!ActiveInventory.TryGetInventorySlot(_isMainHandEquipmentSlot: false, payload.SlotIndex, out _))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_bag_source", $"bag slot {payload.SlotIndex} not found");
				return false;
			}
		}

		if (payload.SourceKind == RuntimeInventoryModificationDragSourceKind.GroundPanel)
		{
			if (payload.SlotIndex < 0 || GroundPanel == null)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_ground_source", "invalid ground panel drag source");
				return false;
			}

			if (!TryValidateGroundModificationDragSource(payload))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_ground_source", $"ground slot {payload.SlotIndex} out of range");
				return false;
			}

			if (resolvedIsOnGroundPanel && payload.SlotIndex == resolvedGroundSlotIndex)
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "validate_ground_source", "cannot drag from same ground slot as weapon");
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

		CharacterInventory inventory = ActiveInventory;
		bool useEquippedMagazineReload = !resolvedIsOnGroundPanel &&
		                                 WeaponMagazineModificationApplier.IsMagazineSlot(_slotDescriptor) &&
		                                 inventory != null &&
		                                 WeaponMagazineModificationApplier.IsEquippedMainHandWeapon(
			                                 inventory, resolvedIsMainHand, weaponSlot);

		if (useEquippedMagazineReload)
			return TryInstallEquippedMagazineFromDrag(payload, resolvedIsMainHand, resolvedBagIndex);

		InventorySlotRuntimeData candidate = MissionPrepInventoryCopyUtility.CloneSlot(payload.Item);
		if (!ItemModificationUtility.TryInstallAtSlot(_slotDescriptor, weaponSlot, candidate, out InventorySlotRuntimeData replacedItem))
			return false;

		int targetBagIndex = resolvedBagIndex;
		if (!TryConsumeModificationDragSource(payload, ref targetBagIndex))
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "consume_source", $"failed to consume drag source {payload.SourceKind}");
			return false;
		}

		if (resolvedIsOnGroundPanel)
		{
			if (!TryCommitGroundWeaponSlot(resolvedGroundSlotIndex, weaponSlot))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "commit_weapon", "failed to commit ground weapon slot");
				return false;
			}
		}
		else if (!TryCommitCharacterWeaponAfterModification(resolvedIsMainHand, targetBagIndex, weaponSlot, _weaponInventorySlotView))
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "commit_weapon", "TrySetInventorySlot failed");
			return false;
		}

		if (!replacedItem.IsEmpty)
		{
			if (ActiveInventory != null)
				ActiveInventory.TryAdd(replacedItem);
			else
				GroundPanel?.TryAdd(replacedItem);
		}

		KeepExpandedSelectionAfterModificationInstall(
			resolvedIsOnGroundPanel,
			resolvedGroundSlotIndex,
			resolvedIsMainHand,
			targetBagIndex,
			weaponSlot.InstanceState);

		RefreshEquippedMainHandVisualsIfNeeded(resolvedIsMainHand);
		RuntimeInventoryModificationDragContext.NotifyDropConsumed();
		NotifyInventoryMutated();
		ItemModificationDiagnostics.LogInstallAccepted(context, _slotDescriptor, weaponSlot, payload.Item);
		return true;
	}

	public bool TryClearModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		bool _addToCharacterBag = true,
		bool _weaponIsOnGroundPanel = false,
		int _weaponGroundSlotIndex = -1,
		InventorySlotView _weaponInventorySlotView = null)
	{
		const string context = "Runtime.TryClearModificationSlot";
		if (!TryResolveWeaponForModification(
			    _weaponIsOnGroundPanel,
			    _weaponGroundSlotIndex,
			    _weaponIsMainHand,
			    _weaponBagIndex,
			    _weaponInventorySlotView,
			    out InventorySlotRuntimeData weaponSlot,
			    out bool resolvedIsMainHand,
			    out int resolvedBagIndex,
			    out bool resolvedIsOnGroundPanel,
			    out int resolvedGroundSlotIndex))
		{
			ItemModificationDiagnostics.LogFlowRejected(
				context,
				"resolve_weapon",
				$"character weapon not found (mainHand={_weaponIsMainHand}, bagIndex={_weaponBagIndex})");
			return false;
		}

		if (!ItemModificationUtility.TryGetInstalledItem(_slotDescriptor, weaponSlot, out _))
		{
			ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "slot is empty");
			return false;
		}

		CharacterInventory inventoryForReload = ActiveInventory;
		bool useEquippedMagazineReload = !resolvedIsOnGroundPanel &&
		                                 WeaponMagazineModificationApplier.IsMagazineSlot(_slotDescriptor) &&
		                                 inventoryForReload != null &&
		                                 WeaponMagazineModificationApplier.IsEquippedMainHandWeapon(
			                                 inventoryForReload, resolvedIsMainHand, weaponSlot);

		if (useEquippedMagazineReload)
		{
			if (!WeaponMagazineModificationApplier.CanStartUiMagazineModification(inventoryForReload))
			{
				ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "magazine reload animation already running");
				return false;
			}

			WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = _addToCharacterBag;
			if (!WeaponMagazineModificationApplier.TryStartEquippedMagazineEject(inventoryForReload))
			{
				WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = true;
				ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "TryStartEquippedMagazineEject failed");
				return false;
			}

			if (!_addToCharacterBag)
			{
				m_PendingUiMagazineEjectHandler = _ejectedMagazine =>
				{
					if (_ejectedMagazine.IsEmpty)
					{
						NotifyInventoryMutated();
						return;
					}

					if (TryPlaceEjectedModificationOnGround(inventoryForReload, _ejectedMagazine))
						NotifyInventoryMutated();
					else if (inventoryForReload != null)
					{
						inventoryForReload.TryAdd(_ejectedMagazine);
						NotifyInventoryMutated();
					}
				};
			}

			return true;
		}

		if (!ItemModificationUtility.TryClearSlot(_slotDescriptor, weaponSlot, out InventorySlotRuntimeData removedItem))
			return false;

		if (resolvedIsOnGroundPanel)
		{
			if (!TryCommitGroundWeaponSlot(resolvedGroundSlotIndex, weaponSlot))
			{
				ItemModificationDiagnostics.LogFlowRejected(context, "commit_weapon", "failed to commit ground weapon slot after clear");
				return false;
			}
		}
		else if (!TryCommitCharacterWeaponAfterModification(resolvedIsMainHand, resolvedBagIndex, weaponSlot, _weaponInventorySlotView))
		{
			ItemModificationDiagnostics.LogFlowRejected(context, "commit_weapon", "TrySetInventorySlot failed after clear");
			return false;
		}

		if (!removedItem.IsEmpty && _addToCharacterBag)
		{
			if (ActiveInventory != null)
				ActiveInventory.TryAdd(removedItem);
			else
				GroundPanel?.TryAdd(removedItem);
		}
		else if (!removedItem.IsEmpty)
		{
			if (!TryPlaceEjectedModificationOnGround(ActiveInventory, removedItem))
			{
				if (resolvedIsOnGroundPanel)
				{
					ItemModificationUtility.TryInstallAtSlot(_slotDescriptor, weaponSlot, removedItem, out _);
					TryCommitGroundWeaponSlot(resolvedGroundSlotIndex, weaponSlot);
				}
				else
					ActiveInventory?.TryAdd(removedItem);

				ItemModificationDiagnostics.LogClearRejected(context, _slotDescriptor, weaponSlot, "ground panel rejected ejected item; rolled back");
				NotifyInventoryMutated();
				return false;
			}
		}

		RefreshEquippedMainHandVisualsIfNeeded(resolvedIsMainHand);
		NotifyInventoryMutated();
		ItemModificationDiagnostics.LogClearAccepted(context, _slotDescriptor, weaponSlot, removedItem);
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
			_weaponGroundSlotIndex: _drag.WeaponGroundSlotIndex,
			_weaponInventorySlotView: _drag.WeaponInventorySlotView);
	}

	public bool TryEjectModificationSlotToGround(RuntimeModificationSlotDrag _drag)
	{
		if (_drag == null || GroundPanel == null)
			return false;

		RuntimeModificationSlotDrag.CleanupActiveDragVisual();
		return TryClearModificationSlot(
			_drag.SlotDescriptor,
			_drag.WeaponIsMainHand,
			_drag.WeaponBagIndex,
			_addToCharacterBag: false,
			_weaponIsOnGroundPanel: _drag.WeaponIsOnGroundPanel,
			_weaponGroundSlotIndex: _drag.WeaponGroundSlotIndex,
			_weaponInventorySlotView: _drag.WeaponInventorySlotView);
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
		RuntimeInventoryModificationUiState preservedModificationUi = m_ModificationUiState;
		TrySubscribeReloadCompletionHandler();
		RuntimeInlineModificationBuilder.ClearAllRows(_characterPanel);
		_characterPanel.RepaintFromCharacterInventory(_inventory);
		EnsureCharacterPanelComponents();
		m_ModificationUiState = preservedModificationUi;
		RebuildInlineModificationRows();
		EnsureGroundPanelComponents();
		GroundPanel?.RebuildContentLayout();
		RefreshModificationCompatibilityHighlights();
	}

	public bool TryResolveCharacterSlot(InventorySlotView _slot, out bool _isMainHandEquipmentSlot, out int _bagIndex)
	{
		return TryResolveCharacterSlot(_slot, out _isMainHandEquipmentSlot, out bool _, out _bagIndex);
	}

	public bool TryResolveCharacterSlot(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out bool _isHeadEquipmentSlot,
		out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_isHeadEquipmentSlot = false;
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
			_isHeadEquipmentSlot = slotIndex == 1;
			if (_isMainHandEquipmentSlot)
				return ActiveInventory.HasMainHandEquipment;
			if (_isHeadEquipmentSlot)
				return ActiveInventory.HasHeadEquipment;
			return false;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0 && _bagIndex < ActiveInventory.BagCount;
	}

	/// <summary>Ячейка UI как цель drop (допускает пустой слот экипировки).</summary>
	public bool TryResolveCharacterDropTarget(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out int _bagIndex)
	{
		return TryResolveCharacterDropTarget(_slot, out _isMainHandEquipmentSlot, out bool _, out _bagIndex);
	}

	public bool TryResolveCharacterDropTarget(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out bool _isHeadEquipmentSlot,
		out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_isHeadEquipmentSlot = false;
		_bagIndex = -1;

		if (_slot == null || CharacterPanel == null)
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
			_isHeadEquipmentSlot = slotIndex == 1;
			return _isMainHandEquipmentSlot || _isHeadEquipmentSlot;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0;
	}

	public bool IsScreenPointOverCharacterMainHandSlot(Vector2 _screenPosition, Camera _eventCamera)
	{
		InventorySlotView mainHandSlot = InventorySlotUiUtility.GetMainHandEquipmentSlot(CharacterPanel);
		return InventorySlotUiUtility.IsScreenPointOverMainHandEquipmentSlot(
			mainHandSlot, _screenPosition, _eventCamera);
	}

	public bool IsScreenPointOverCharacterHeadSlot(Vector2 _screenPosition, Camera _eventCamera)
	{
		InventorySlotView headSlot = InventorySlotUiUtility.GetHeadEquipmentSlot(CharacterPanel);
		return InventorySlotUiUtility.IsScreenPointOverHeadEquipmentSlot(
			headSlot, _screenPosition, _eventCamera);
	}

	public bool TryEquipWeaponDragToMainHand()
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return false;

		RuntimeInventoryModificationDragPayload payload = RuntimeInventoryModificationDragContext.Current;
		if (!RuntimeInventoryModificationDragContext.IsWeaponEquipDragSource(payload.SourceKind))
			return false;

		RtsUnitSelectionManager selectionManager = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.SelectionManager
			: null;

		if (selectionManager == null)
			return false;

		bool success = payload.SourceKind switch
		{
			RuntimeInventoryModificationDragSourceKind.CharacterBagWeapon =>
				selectionManager.TryEquipCharacterBagWeaponToMainHand(
					payload.SlotIndex,
					RuntimeInventoryModificationDragContext.SourceSlotView),
			RuntimeInventoryModificationDragSourceKind.GroundWeapon =>
				selectionManager.TryEquipGroundWeaponToMainHand(
					RuntimeInventoryModificationDragContext.SourceSlotView,
					payload.SlotIndex),
			_ => false
		};

		if (!success)
			return false;

		RuntimeInventoryModificationDragContext.NotifyDropConsumed();
		ClearModificationUiSelection();
		return true;
	}

	public bool TryEquipHelmetDragToHead()
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return false;

		RuntimeInventoryModificationDragPayload payload = RuntimeInventoryModificationDragContext.Current;
		if (!RuntimeInventoryModificationDragContext.IsHelmetEquipDragSource(payload.SourceKind))
			return false;

		RtsUnitSelectionManager selectionManager = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.SelectionManager
			: null;

		if (selectionManager == null)
			return false;

		bool success = payload.SourceKind switch
		{
			RuntimeInventoryModificationDragSourceKind.CharacterBagHelmet =>
				selectionManager.TryEquipCharacterBagHelmetToHead(
					payload.SlotIndex,
					RuntimeInventoryModificationDragContext.SourceSlotView),
			RuntimeInventoryModificationDragSourceKind.GroundHelmet =>
				selectionManager.TryEquipGroundHelmetToHead(
					RuntimeInventoryModificationDragContext.SourceSlotView,
					payload.SlotIndex),
			_ => false
		};

		if (!success)
			return false;

		RuntimeInventoryModificationDragContext.NotifyDropConsumed();
		ClearModificationUiSelection();
		return true;
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
		if (m_ModificationUiState.IsExpanded)
			TryRestoreExpandedSelectionFromAuthoritativeData();

		CharacterInventory inventory = ActiveInventory;
		if (inventory != null)
		{
			RefreshInventoryBodyDecorations(inventory);
			TryRepaintCharacterAndGroundPanels(inventory);
		}
		else
			TryRepaintCharacterAndGroundPanels();
	}
	#endregion

	#region Private Methods
	private static void RefreshInventoryBodyDecorations(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		UnitInventoryBodyDecorations decorations = _inventory.GetComponentInParent<UnitInventoryBodyDecorations>(true);
		if (decorations == null)
			decorations = _inventory.GetComponentInChildren<UnitInventoryBodyDecorations>(true);

		decorations?.RefreshFromInventory(_inventory);
	}

	private bool TryPlaceEjectedModificationOnGround(CharacterInventory _inventory, InventorySlotRuntimeData _item)
	{
		EnsureRuntimeReferences();
		RtsUnitSelectionManager selectionManager = m_SelectionManager;
		if (selectionManager == null)
			return false;

		return selectionManager.TryPlaceItemOnGroundPanel(_inventory, _item);
	}

	private void RefreshEquippedMainHandVisualsIfNeeded(bool _weaponIsMainHand)
	{
		if (!_weaponIsMainHand)
			return;

		CharacterInventory inventory = ActiveInventory;
		if (inventory == null)
			return;

		WeaponMagazineModificationApplier.RefreshEquippedWeaponVisuals(inventory);
	}

	private void EnsureRuntimeReferences()
	{
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;

		if (m_InventoryBindings != null)
			m_SelectionManager = m_InventoryBindings.SelectionManager;

		if (m_SelectionManager == null)
			m_SelectionManager = RtsUnitSelectionManager.Instance;
	}

	private CharacterInventory ResolveActiveInventory()
	{
		if (m_InventoryBindings != null && m_InventoryBindings.ActiveCharacterInventory != null)
			return m_InventoryBindings.ActiveCharacterInventory;

		return m_SelectionManager != null
			? m_SelectionManager.TryGetActiveCharacterInventoryForUi()
			: null;
	}

	private void TrySubscribeReloadCompletionHandler()
	{
		CharacterInventory inventory = ResolveActiveInventory();
		if (inventory == null)
		{
			TryUnsubscribeReloadCompletionHandler();
			return;
		}

		if (!WeaponMagazineModificationApplier.TryGetReloadController(inventory, out UnitWeaponReloadController reloadController))
		{
			TryUnsubscribeReloadCompletionHandler();
			return;
		}

		if (m_SubscribedReloadController == reloadController)
			return;

		TryUnsubscribeReloadCompletionHandler();
		m_SubscribedReloadController = reloadController;
		reloadController.UiMagazineModificationCompleted += HandleUiMagazineModificationCompleted;
	}

	private void TryUnsubscribeReloadCompletionHandler()
	{
		if (m_SubscribedReloadController == null)
			return;

		m_SubscribedReloadController.UiMagazineModificationCompleted -= HandleUiMagazineModificationCompleted;
		m_SubscribedReloadController = null;
	}

	private void HandleUiMagazineModificationCompleted(InventorySlotRuntimeData _ejectedMagazine)
	{
		WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag = true;

		System.Action<InventorySlotRuntimeData> pendingHandler = m_PendingUiMagazineEjectHandler;
		m_PendingUiMagazineEjectHandler = null;
		if (pendingHandler != null)
		{
			pendingHandler.Invoke(_ejectedMagazine);
			return;
		}

		RuntimeInventoryModificationUiState preservedModificationUi = m_ModificationUiState;
		if (isActiveAndEnabled)
			ScheduleRepaintAfterMagazineModificationCompleted(preservedModificationUi);
		else
		{
			m_ModificationUiState = preservedModificationUi;
			NotifyInventoryMutated();
		}
	}

	private bool TryInstallEquippedMagazineFromDrag(
		RuntimeInventoryModificationDragPayload _payload,
		bool _weaponIsMainHand,
		int _weaponBagIndex)
	{
		CharacterInventory inventory = ActiveInventory;
		if (inventory == null || !WeaponMagazineModificationApplier.CanStartUiMagazineModification(inventory))
			return false;

		int targetBagIndex = _weaponBagIndex;
		if (!TryConsumeModificationDragSource(_payload, ref targetBagIndex))
			return false;

		if (!WeaponMagazineModificationApplier.TryStartEquippedMagazineInstall(inventory, _payload.Item))
		{
			TryRestoreModificationDragSource(_payload, _payload.Item);
			return false;
		}

		CharacterInventory inventoryAfterInstall = ActiveInventory;
		if (inventoryAfterInstall != null &&
		    inventoryAfterInstall.TryGetInventorySlot(_weaponIsMainHand, targetBagIndex, out InventorySlotRuntimeData installedWeaponSlot))
		{
			KeepExpandedSelectionAfterModificationInstall(
				false,
				-1,
				_weaponIsMainHand,
				targetBagIndex,
				installedWeaponSlot.InstanceState);
		}

		RuntimeInventoryModificationDragContext.NotifyDropConsumed();
		return true;
	}

	private bool TryRestoreModificationDragSource(
		RuntimeInventoryModificationDragPayload _payload,
		InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty)
			return false;

		switch (_payload.SourceKind)
		{
			case RuntimeInventoryModificationDragSourceKind.CharacterBag:
				return ActiveInventory != null && ActiveInventory.TryAdd(_item);

			case RuntimeInventoryModificationDragSourceKind.GroundPanel:
				return GroundPanel != null && GroundPanel.TryAdd(_item);

			default:
				return true;
		}
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
		int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);
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

			InventorySlotUiUtility.EnsureDescriptionHover(slot);

			if (lead > 0 && i < lead)
			{
				RuntimeModificationSlotHighlightView existingHighlight =
					slot.GetComponent<RuntimeModificationSlotHighlightView>();
				if (existingHighlight != null && Application.isPlaying)
					Destroy(existingHighlight);
				continue;
			}

			if (slot.GetComponent<InventoryEquipmentEquipPreviewHover>() == null)
				slot.gameObject.AddComponent<InventoryEquipmentEquipPreviewHover>();

			RuntimeModificationSlotHighlightView highlight = slot.GetComponent<RuntimeModificationSlotHighlightView>();
			if (highlight == null)
				highlight = slot.gameObject.AddComponent<RuntimeModificationSlotHighlightView>();
			highlight.Bind(this);
		}

		EnsureMainHandEquipmentSlot();
		EnsureHeadEquipmentSlot();
		EnsureGroundPanelComponents();
	}

	private void EnsureMainHandEquipmentSlot()
	{
		if (CharacterPanel == null || CharacterPanel.LeadingEquipmentSlotCount <= 0)
			return;

		IReadOnlyList<InventorySlotView> slots = CharacterPanel.Slots;
		if (slots.Count == 0 || slots[0] == null)
			return;

		RuntimeCharacterMainHandEquipmentSlotView mainHandSlot =
			slots[0].GetComponent<RuntimeCharacterMainHandEquipmentSlotView>();
		if (mainHandSlot == null)
			mainHandSlot = slots[0].gameObject.AddComponent<RuntimeCharacterMainHandEquipmentSlotView>();

		mainHandSlot.Bind(this);
	}

	private void EnsureHeadEquipmentSlot()
	{
		if (CharacterPanel == null || CharacterPanel.LeadingEquipmentSlotCount <= 1)
			return;

		InventorySlotView headSlotView = InventorySlotUiUtility.GetHeadEquipmentSlot(CharacterPanel);
		if (headSlotView == null)
			return;

		RuntimeCharacterHeadEquipmentSlotView headSlot =
			headSlotView.GetComponent<RuntimeCharacterHeadEquipmentSlotView>();
		if (headSlot == null)
			headSlot = headSlotView.gameObject.AddComponent<RuntimeCharacterHeadEquipmentSlotView>();

		headSlot.Bind(this);
	}

	private void EnsureGroundPanelComponents()
	{
		if (GroundPanel == null)
			return;

		InventoryGroundDropZone.EnsureOnGroundPanel(GroundPanel);

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

			if (slot.GetComponent<InventoryGroundEquipDoubleClick>() == null)
				slot.gameObject.AddComponent<InventoryGroundEquipDoubleClick>();

			InventorySlotUiUtility.EnsureDescriptionHover(slot);

			if (slot.GetComponent<InventoryEquipmentEquipPreviewHover>() == null)
				slot.gameObject.AddComponent<InventoryEquipmentEquipPreviewHover>();
		}
	}

	private void RebuildInlineModificationRows()
	{
		m_PendingInlineRefresh = false;
		CharacterInventory inventory = ActiveInventory;
		bool canBuildCharacterRows = CharacterPanel != null;

		if (CharacterPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(CharacterPanel);
		if (GroundPanel != null)
			RuntimeInlineModificationBuilder.ClearAllRows(GroundPanel);

		CharacterPanel?.RefreshSlotsFromHierarchy();
		GroundPanel?.RefreshSlotsFromHierarchy();

		m_WeaponSlotBindingBuffer.Clear();
		if (canBuildCharacterRows)
			CollectModifiableCharacterWeaponBindings(m_WeaponSlotBindingBuffer);

		CollectModifiableGroundWeaponBindings(m_WeaponSlotBindingBuffer);
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);
		ReconcileExpandedSelectionAfterRebuild();

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
			if (binding.Panel == null || binding.SlotView == null || !binding.SlotView.HasItem)
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

			RuntimeInlineModificationBuilder.RebuildWeaponRows(
				binding.Panel,
				this,
				binding.SlotView,
				weaponData,
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
		{
			RuntimeInlineModificationBuilder.RefreshHighlights(CharacterPanel);
			RuntimeInlineModificationBuilder.RefreshEquipmentSlotHighlights(CharacterPanel);
		}

		if (GroundPanel != null)
			RuntimeInlineModificationBuilder.RefreshHighlights(GroundPanel);

		RefreshModificationCompatibilityHighlights();
	}

	private void CollectModifiableCharacterWeaponBindings(List<WeaponSlotBinding> _outBindings)
	{
		if (CharacterPanel == null)
			return;

		CharacterInventory inventory = ActiveInventory;

		IReadOnlyList<InventorySlotView> slots = CharacterPanel.Slots;
		int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);

		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView slot = slots[i];
			if (slot == null || !slot.HasItem)
				continue;

			bool isMainHand = i < lead && i == 0;
			int bagIndex = isMainHand ? -1 : i - lead;

			InventorySlotRuntimeData weaponData = default;
			if (isMainHand)
			{
				if (inventory == null ||
				    !inventory.TryGetInventorySlot(true, bagIndex, out weaponData) ||
				    weaponData.IsEmpty)
				{
					if (!ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
						continue;

					weaponData = slot.Data;
				}
			}
			else if (inventory != null &&
			         bagIndex >= 0 &&
			         bagIndex < inventory.BagCount &&
			         inventory.TryGetInventorySlot(false, bagIndex, out InventorySlotRuntimeData inventoryWeapon) &&
			         !inventoryWeapon.IsEmpty)
			{
				weaponData = inventoryWeapon;
			}
			else if (ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
			{
				weaponData = slot.Data;
			}
			else
			{
				continue;
			}

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
		if (_binding.IsGroundSlot)
		{
			if (_binding.SlotView != null && _binding.SlotView.HasItem)
				return MissionPrepInventoryCopyUtility.CloneSlot(_binding.SlotView.Data);

			return MissionPrepInventoryCopyUtility.CloneSlot(_binding.WeaponData);
		}

		CharacterInventory inventory = ActiveInventory;
		InventorySlotRuntimeData slotData = _binding.SlotView != null && _binding.SlotView.HasItem
			? MissionPrepInventoryCopyUtility.CloneSlot(_binding.SlotView.Data)
			: default;

		if (inventory != null &&
		    inventory.TryGetInventorySlot(_binding.IsMainHand, _binding.BagIndex, out InventorySlotRuntimeData inventoryData) &&
		    !inventoryData.IsEmpty)
		{
			InventorySlotRuntimeData clonedInventoryData = MissionPrepInventoryCopyUtility.CloneSlot(inventoryData);
			if (ItemModificationUtility.HasAnyInstalledModification(clonedInventoryData))
				return clonedInventoryData;

			if (ItemModificationUtility.HasAnyInstalledModification(slotData))
				return slotData;

			return clonedInventoryData;
		}

		if (!slotData.IsEmpty)
			return slotData;

		return MissionPrepInventoryCopyUtility.CloneSlot(_binding.WeaponData);
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

	private bool TryValidateGroundModificationDragSource(RuntimeInventoryModificationDragPayload _payload)
	{
		if (_payload.SourceKind != RuntimeInventoryModificationDragSourceKind.GroundPanel || GroundPanel == null)
			return false;

		InventorySlotView sourceSlot = RuntimeInventoryModificationDragContext.SourceSlotView;
		if (sourceSlot != null &&
		    sourceSlot.HasItem &&
		    ItemModificationUtility.IsModificationItem(sourceSlot.Data))
			return true;

		return _payload.SlotIndex >= 0 && _payload.SlotIndex < GroundPanel.Slots.Count;
	}

	private bool TryResolveWeaponForModification(
		bool _weaponIsOnGroundPanel,
		int _weaponGroundSlotIndex,
		bool _weaponIsMainHand,
		int _weaponBagIndex,
		InventorySlotView _weaponInventorySlotView,
		out InventorySlotRuntimeData _weaponSlot,
		out bool _resolvedIsMainHand,
		out int _resolvedBagIndex,
		out bool _resolvedIsOnGroundPanel,
		out int _resolvedGroundSlotIndex)
	{
		_weaponSlot = default;
		_resolvedIsMainHand = _weaponIsMainHand;
		_resolvedBagIndex = _weaponBagIndex;
		_resolvedIsOnGroundPanel = _weaponIsOnGroundPanel;
		_resolvedGroundSlotIndex = _weaponGroundSlotIndex;

		if (_weaponIsOnGroundPanel)
		{
			if (TryGetGroundWeaponSlot(_weaponGroundSlotIndex, out _, out _weaponSlot))
				return true;

			if (_weaponInventorySlotView != null &&
			    _weaponInventorySlotView.HasItem &&
			    ItemModificationUtility.IsModifiableWeapon(_weaponInventorySlotView.Data.Definition))
			{
				_weaponSlot = MissionPrepInventoryCopyUtility.CloneSlot(_weaponInventorySlotView.Data);
				return true;
			}

			return false;
		}

		CharacterInventory inventory = ActiveInventory;
		if (inventory != null &&
		    inventory.TryGetInventorySlot(_weaponIsMainHand, _weaponBagIndex, out InventorySlotRuntimeData inventoryWeapon) &&
		    !inventoryWeapon.IsEmpty)
		{
			_weaponSlot = inventoryWeapon;
			return true;
		}

		if (_weaponInventorySlotView != null &&
		    TryResolveModificationToggleTarget(
			    _weaponInventorySlotView,
			    out bool isGroundSlot,
			    out int groundSlotIndex,
			    out bool isMainHand,
			    out int bagIndex,
			    out InventorySlotRuntimeData resolvedWeaponSlot))
		{
			if (isGroundSlot)
			{
				_resolvedIsOnGroundPanel = true;
				_resolvedGroundSlotIndex = groundSlotIndex;
				_resolvedIsMainHand = false;
				_resolvedBagIndex = -1;
			}
			else
			{
				_resolvedIsMainHand = isMainHand;
				_resolvedBagIndex = bagIndex;
			}

			_weaponSlot = resolvedWeaponSlot;
			return true;
		}

		return false;
	}

	private bool TryCommitCharacterWeaponAfterModification(
		bool _isMainHand,
		int _bagIndex,
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotView _weaponInventorySlotView)
	{
		CharacterInventory inventory = ActiveInventory;
		if (inventory != null && inventory.TrySetInventorySlot(_isMainHand, _bagIndex, _weaponSlot))
			return true;

		if (_weaponInventorySlotView == null)
			return false;

		_weaponInventorySlotView.SetItem(_weaponSlot);
		if (inventory != null &&
		    TryResolveCharacterSlot(_weaponInventorySlotView, out bool resolvedMainHand, out int resolvedBagIndex))
			return inventory.TrySetInventorySlot(resolvedMainHand, resolvedBagIndex, _weaponSlot);

		return _weaponInventorySlotView.HasItem;
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

		ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;
		if (selectedInstance != null)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (BindingMatchesModificationSelection(binding, selectedInstance))
				{
					ApplyBindingToModificationUiState(binding);
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

		if (m_ModificationUiState.IsGroundSlot)
		{
			for (int i = 0; i < _bindings.Count; i++)
			{
				WeaponSlotBinding binding = _bindings[i];
				if (binding.IsGroundSlot && m_ModificationUiState.MatchesGround(binding.GroundSlotIndex))
				{
					ApplyBindingToModificationUiState(binding);
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
			if (binding.IsGroundSlot)
				continue;

			if (m_ModificationUiState.MatchesCharacter(binding.IsMainHand, binding.BagIndex))
			{
				ApplyBindingToModificationUiState(binding);
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

	private void ReconcileExpandedSelectionAfterRebuild()
	{
		if (!m_ModificationUiState.IsExpanded)
			return;

		TryRestoreExpandedSelectionFromAuthoritativeData();
	}

	private bool TryRestoreExpandedSelectionFromAuthoritativeData()
	{
		if (!m_ModificationUiState.HasSelection)
			return false;

		RuntimeModifiableWeaponDisplayState displayState = m_ModificationUiState.DisplayState;

		if (m_ModificationUiState.IsGroundSlot)
		{
			int groundIndex = m_ModificationUiState.GroundSlotIndex;
			if (GroundPanel != null &&
			    groundIndex >= 0 &&
			    groundIndex < GroundPanel.Slots.Count)
			{
				InventorySlotView slotView = GroundPanel.Slots[groundIndex];
				if (slotView != null &&
				    slotView.HasItem &&
				    ItemModificationUtility.IsModifiableWeapon(slotView.Data.Definition))
				{
					m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
						groundIndex,
						slotView.Data.InstanceState,
						displayState);
					return true;
				}
			}

			return TryRemapSelectionFromPanelSlots();
		}

		CharacterInventory inventory = ActiveInventory;
		if (inventory != null &&
		    inventory.TryGetInventorySlot(m_ModificationUiState.IsMainHand, m_ModificationUiState.BagIndex, out InventorySlotRuntimeData weaponSlot) &&
		    !weaponSlot.IsEmpty &&
		    ItemModificationUtility.IsModifiableWeapon(weaponSlot.Definition))
		{
			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
				m_ModificationUiState.IsMainHand,
				m_ModificationUiState.BagIndex,
				weaponSlot.InstanceState,
				displayState);
			return true;
		}

		return TryRemapSelectionFromPanelSlots();
	}

	private bool TryResolveModificationToggleTarget(
		InventorySlotView _slot,
		out bool _isGroundSlot,
		out int _groundSlotIndex,
		out bool _isMainHand,
		out int _bagIndex,
		out InventorySlotRuntimeData _weaponSlot)
	{
		_isGroundSlot = false;
		_groundSlotIndex = -1;
		_isMainHand = false;
		_bagIndex = -1;
		_weaponSlot = default;

		if (_slot == null || !_slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(_slot.Data.Definition))
			return false;

		if (TryResolveGroundSlot(_slot, out _groundSlotIndex))
		{
			_isGroundSlot = true;
			_weaponSlot = _slot.Data;
			return true;
		}

		if (CharacterPanel == null || !RuntimeModificationPanelUtility.IsSlotOnPanel(_slot, CharacterPanel))
			return false;

		int slotIndex = CharacterPanel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
			return false;

		int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);
		_isMainHand = slotIndex < lead && slotIndex == 0;
		_bagIndex = _isMainHand ? -1 : slotIndex - lead;
		_weaponSlot = _slot.Data;

		CharacterInventory inventory = ActiveInventory;
		if (inventory != null &&
		    inventory.TryGetInventorySlot(_isMainHand, _bagIndex, out InventorySlotRuntimeData inventorySlot) &&
		    !inventorySlot.IsEmpty)
			_weaponSlot = inventorySlot;

		return ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Definition);
	}

	private bool TryRemapSelectionFromPanelSlots()
	{
		ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;

		if (CharacterPanel != null)
		{
			IReadOnlyList<InventorySlotView> slots = CharacterPanel.Slots;
			int lead = Mathf.Max(0, CharacterPanel.LeadingEquipmentSlotCount);
			for (int i = 0; i < slots.Count; i++)
			{
				InventorySlotView slot = slots[i];
				if (slot == null || !slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
					continue;

				bool isMainHand = i < lead && i == 0;
				int bagIndex = isMainHand ? -1 : i - lead;
				bool matchesInstance = selectedInstance != null && slot.Data.InstanceState == selectedInstance;
				bool matchesIndex = m_ModificationUiState.MatchesCharacter(isMainHand, bagIndex);
				if (!matchesInstance && !matchesIndex)
					continue;

				InventorySlotRuntimeData weaponData = slot.Data;
				CharacterInventory inventory = ActiveInventory;
				if (inventory != null &&
				    inventory.TryGetInventorySlot(isMainHand, bagIndex, out InventorySlotRuntimeData inventorySlot) &&
				    !inventorySlot.IsEmpty)
					weaponData = inventorySlot;

				m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
					isMainHand,
					bagIndex,
					weaponData.InstanceState,
					m_ModificationUiState.DisplayState);
				return true;
			}
		}

		if (GroundPanel != null)
		{
			IReadOnlyList<InventorySlotView> groundSlots = GroundPanel.Slots;
			for (int i = 0; i < groundSlots.Count; i++)
			{
				InventorySlotView slot = groundSlots[i];
				if (slot == null || !slot.HasItem || !ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
					continue;

				bool matchesInstance = selectedInstance != null && slot.Data.InstanceState == selectedInstance;
				bool matchesIndex = m_ModificationUiState.MatchesGround(i);
				if (!matchesInstance && !matchesIndex)
					continue;

				m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
					i,
					slot.Data.InstanceState,
					m_ModificationUiState.DisplayState);
				return true;
			}
		}

		return false;
	}

	private void SetDisplayState(RuntimeModifiableWeaponDisplayState _displayState)
	{
		if (!m_ModificationUiState.HasSelection || m_ModificationUiState.DisplayState == _displayState)
			return;

		m_ModificationUiState.DisplayState = _displayState;
		RebuildInlineModificationRows();
	}

	private void ApplyBindingToModificationUiState(WeaponSlotBinding _binding)
	{
		RuntimeModifiableWeaponDisplayState displayState = m_ModificationUiState.DisplayState;
		ItemInstanceState weaponInstance = _binding.IsGroundSlot && _binding.SlotView != null
			? _binding.SlotView.Data.InstanceState
			: _binding.WeaponData.InstanceState;

		if (_binding.IsGroundSlot)
		{
			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
				_binding.GroundSlotIndex,
				weaponInstance,
				displayState);
			return;
		}

		m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
			_binding.IsMainHand,
			_binding.BagIndex,
			weaponInstance,
			displayState);
	}

	private bool IsSameWeaponAsSelection(
		ItemInstanceState _weaponInstanceState,
		bool _isGroundSlot,
		int _groundSlotIndex,
		bool _isMainHand,
		int _bagIndex)
	{
		if (!m_ModificationUiState.HasSelection)
			return false;

		if (_isGroundSlot)
		{
			if (m_ModificationUiState.IsGroundSlot && m_ModificationUiState.MatchesGround(_groundSlotIndex))
				return true;
		}
		else if (!m_ModificationUiState.IsGroundSlot &&
		         m_ModificationUiState.MatchesCharacter(_isMainHand, _bagIndex))
		{
			return true;
		}

		if (_weaponInstanceState != null && m_ModificationUiState.SelectedWeaponInstanceState != null)
			return _weaponInstanceState == m_ModificationUiState.SelectedWeaponInstanceState;

		return false;
	}

	private void KeepExpandedSelectionAfterModificationInstall(
		bool _weaponIsOnGroundPanel,
		int _groundSlotIndex,
		bool _weaponIsMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState)
	{
		m_SuppressOutsideClickUntilFrame = Time.frameCount + 1;

		if (_weaponIsOnGroundPanel)
		{
			m_ModificationUiState = RuntimeInventoryModificationUiState.CreateGroundSelection(
				_groundSlotIndex,
				_weaponInstanceState,
				RuntimeModifiableWeaponDisplayState.Expanded);
			return;
		}

		m_ModificationUiState = RuntimeInventoryModificationUiState.CreateCharacterSelection(
			_weaponIsMainHand,
			_bagIndex,
			_weaponInstanceState,
			RuntimeModifiableWeaponDisplayState.Expanded);
	}

	private bool ShouldExpandEmptySlotsForBinding(WeaponSlotBinding _binding)
	{
		if (!m_ModificationUiState.HasSelection || !m_ModificationUiState.IsExpanded)
			return false;

		if (_binding.IsGroundSlot)
		{
			if (m_ModificationUiState.IsGroundSlot && m_ModificationUiState.MatchesGround(_binding.GroundSlotIndex))
				return true;

			ItemInstanceState selectedInstance = m_ModificationUiState.SelectedWeaponInstanceState;
			return selectedInstance != null &&
			       _binding.SlotView != null &&
			       _binding.SlotView.Data.InstanceState == selectedInstance;
		}

		if (!m_ModificationUiState.IsGroundSlot &&
		    m_ModificationUiState.MatchesCharacter(_binding.IsMainHand, _binding.BagIndex))
			return true;

		ItemInstanceState selectedWeaponInstance = m_ModificationUiState.SelectedWeaponInstanceState;
		if (selectedWeaponInstance == null)
			return false;

		if (_binding.WeaponData.InstanceState == selectedWeaponInstance)
			return true;

		return _binding.SlotView != null && _binding.SlotView.Data.InstanceState == selectedWeaponInstance;
	}

	private bool BindingMatchesModificationSelection(WeaponSlotBinding _binding, ItemInstanceState _selectedInstance)
	{
		if (_binding.IsGroundSlot)
		{
			if (m_ModificationUiState.IsGroundSlot && m_ModificationUiState.MatchesGround(_binding.GroundSlotIndex))
				return true;

			return _binding.SlotView != null && _binding.SlotView.Data.InstanceState == _selectedInstance;
		}

		if (!m_ModificationUiState.IsGroundSlot &&
		    m_ModificationUiState.MatchesCharacter(_binding.IsMainHand, _binding.BagIndex))
			return true;

		if (_binding.WeaponData.InstanceState == _selectedInstance)
			return true;

		return _binding.SlotView != null && _binding.SlotView.Data.InstanceState == _selectedInstance;
	}

	public void RemapModificationSelectionForWeapon(ItemInstanceState _weaponInstanceState)
	{
		if (!m_ModificationUiState.HasSelection || _weaponInstanceState == null)
			return;

		if (m_ModificationUiState.SelectedWeaponInstanceState != null &&
		    m_ModificationUiState.SelectedWeaponInstanceState != _weaponInstanceState)
			return;

		m_ModificationUiState.SelectedWeaponInstanceState = _weaponInstanceState;
		m_WeaponSlotBindingBuffer.Clear();

		CharacterInventory inventory = ActiveInventory;
		if (CharacterPanel != null && inventory != null)
			CollectModifiableCharacterWeaponBindings(m_WeaponSlotBindingBuffer);

		CollectModifiableGroundWeaponBindings(m_WeaponSlotBindingBuffer);
		ValidateModificationUiSelection(m_WeaponSlotBindingBuffer);
	}

	private void ScheduleRepaintAfterMagazineModificationCompleted(RuntimeInventoryModificationUiState _preservedModificationUi)
	{
		if (m_DeferredMagazineRepaintCoroutine != null)
			StopCoroutine(m_DeferredMagazineRepaintCoroutine);

		m_DeferredMagazineRepaintCoroutine = StartCoroutine(CoRepaintAfterMagazineModificationCompleted(_preservedModificationUi));
	}

	private IEnumerator CoRepaintAfterMagazineModificationCompleted(RuntimeInventoryModificationUiState _preservedModificationUi)
	{
		yield return null;
		m_DeferredMagazineRepaintCoroutine = null;
		m_ModificationUiState = _preservedModificationUi;
		NotifyInventoryMutated();
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
		RuntimeInlineModificationBuilder.RefreshEquipmentSlotHighlights(CharacterPanel);
		RefreshModificationCompatibilityHighlights();
	}

	private void HandleEquipmentEquipHoverChanged()
	{
		if (CharacterPanel != null)
			RuntimeInlineModificationBuilder.RefreshEquipmentSlotHighlights(CharacterPanel);
	}

	private IEnumerator CoRefreshInlineModificationRowsNextFrame()
	{
		yield return null;
		m_DeferredInlineRefreshCoroutine = null;
		if (!m_PendingInlineRefresh)
			yield break;

		RebuildInlineModificationRows();
	}
	#endregion
}
