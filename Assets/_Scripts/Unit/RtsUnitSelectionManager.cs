using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// RTS-выбор юнитов: одиночный ЛКМ, ctrl-toggle, box selection и групповые команды.
/// Инвентарь всегда привязан к последнему юниту в текущем выделении.
/// Клавиши (без UI): F стоп, E готов, Z/C стойки, T зарядка магазина, R перезарядка, V режим огня.
/// На RTS-юнитах прямой клавиатурный ввод слоя готовности отключён — см. <see cref="RtsUnitMember.ApplyDirectInputState"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsUnitSelectionManager : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Camera m_SelectionCamera;
	[SerializeField] private LayerMask m_SelectionRaycastMask = ~0;
	[SerializeField] private LayerMask m_CommandGroundMask = ~0;
	[SerializeField] private bool m_BlockPointerOverUi = true;
	[SerializeField, Min(2f)] private float m_BoxSelectionMinDragPixels = 12f;
	[SerializeField, Min(0.05f)] private float m_DoubleRightClickSeconds = 0.28f;
	[SerializeField, Min(1f)] private float m_QuickRotateDragThresholdPixels = 90f;
	[SerializeField, Min(1f)] private float m_InPlaceFacingDragThresholdPixels = 5f;
	[Header("Path Waypoint Hover")]
	[SerializeField, Min(0.05f)] private float m_PathHoverDelay = 0.2f;
	[SerializeField, Min(5f)] private float m_PathHoverThresholdPixels = 30f;
	[SerializeField, Min(5f)] private float m_ArrowHoverThresholdPixels = 20f;
	[SerializeField, Min(0.05f)] private float m_RouteEditHandleSize = 0.2f;
	[SerializeField, Min(5f)] private float m_RouteEditHandleHitPixels = 24f;
	[SerializeField, Min(5f)] private float m_RouteVertexSnapPixels = 40f;
	[SerializeField, Min(20f)] private float m_ArrowDeleteButtonSize = 26f;
	[SerializeField, Min(16f)] private float m_WaitPointIconSize = 22f;
	[SerializeField, Min(0f)] private float m_WaitPointIconScreenOffsetY = 28f;
	[SerializeField] private bool m_SelectFirstPlayerUnitOnStart = true;

	[Header("Formation Spacing")]
	[Tooltip("Базовый интервал между юнитами в формации (метры).")]
	[SerializeField, Min(0.1f)] private float m_FormationLineSpacing = 2f;
	[Tooltip("Минимальный интервал шеренги при регулировке колёсиком.")]
	[SerializeField, Min(0.1f)] private float m_FormationLineSpacingMin = 0.5f;
	[Tooltip("Максимальный интервал шеренги при регулировке колёсиком.")]
	[SerializeField, Min(0.1f)] private float m_FormationLineSpacingMax = 10f;
	[Tooltip("Шаг изменения интервала колёсиком мыши.")]
	[SerializeField, Min(0.05f)] private float m_FormationLineSpacingStep = 0.25f;
	[Tooltip("Минимальный множитель скорости для синхронизации формации.")]
	[SerializeField, Range(0.1f, 1f)] private float m_FormationSyncMinSpeedMultiplier = 0.35f;
	[Tooltip("Интервал пересчёта FormationSpeedMultiplier для активных групп (сек).")]
	[SerializeField, Range(0.05f, 1f)] private float m_FormationSyncUpdateInterval = 0.25f;

	[Header("Destination Markers")]
	[SerializeField] private GameObject m_DestinationMarkerPrefab;
	[SerializeField, Min(0.1f)] private float m_ClickMarkerLifetimeSeconds = 1f;
	[Tooltip("Маркер вдоль пути (каждые N метров).")]
	[SerializeField] private GameObject m_PathMarkerPrefab;

	[Header("Inventory UI")]
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[SerializeField] private InventoryPanelView m_GroundPanel;
	[SerializeField] private InventoryPanelView m_CharacterInventoryPanel;

	[Header("Runtime Debug")]
	[SerializeField] private List<RtsUnitMember> m_SelectedUnits = new List<RtsUnitMember>(16);
	#endregion

	#region Private Fields
	private Vector2 m_LeftMouseDownScreen;
	private bool m_IsDraggingSelection;
	private bool m_LeftMouseStartedOverUi;
	private float m_LastRightClickTime = -1f;
	private Coroutine m_ExchangeApproachCoroutine;
	private bool m_IsPreviewingMove;
	private bool m_IsAwaitingDoubleClick;
	private bool m_PreviewCancelled;
	private bool m_PreviewPending;
	private float m_PreviewPendingTime;
	private Vector3 m_PreviewCenterPoint;
	private List<Vector3> m_PreviewOffsets;
	private Vector3 m_LastWalkCenter;
	private List<Vector3> m_LastWalkOffsets;
	private UnitClickToMove.MoveTier m_PreviewMoveTier = UnitClickToMove.MoveTier.Walk;
	private bool m_IsQuickRotateFacing;
	private bool m_HasMoveFacingSet;
	private bool m_HasFormationFacingSet;
	private bool m_RmbStartedOnSelectedUnit;
	private bool m_IsInPlaceFacingPreview;
	private Vector2 m_RmbDownMousePos;
	private List<float> m_PreviewFacingAngles;
	private List<float> m_PreviewFormationFacingAngles;
	private Vector3? m_PreviewFormationForwardOverride;
	private GroupFormationFacingMode m_PreviewGroupFormationFacingMode = GroupFormationFacingMode.HoldToEnd;
	private float m_PreviewFormationManualFacingAngle;
	private Vector3? m_PreviewFormationManualLookPoint;
	private FormationLayoutUtility.FormationUnitSlotBinding[] m_FormationPreviewBindings;
	private FormationType m_CachedFormationType = FormationType.TacticalColumn;
	private float m_CachedFormationSpacing;
	private Vector3 m_CachedFormationForward = Vector3.forward;
	private bool m_HasCachedFormationForward;
	private float m_CurrentFormationSpacing;
	private Coroutine m_PendingWalkCoroutine;
	private readonly List<GameObject> m_DirectionMarkers = new List<GameObject>();
	private readonly List<GameObject> m_PreviewDestinationMarkers = new List<GameObject>();
	private readonly List<GameObject> m_PreviewUnitFacingArrows = new List<GameObject>();
	private GameObject m_MovePreviewFacingArrow;
	private bool m_IsFormationPickerKeyHeld;
	private float m_FormationPickerKeyDownTime;
	private bool m_FormationDigitSelectedWhileHeld;
	private const float c_FormationTapMaxSeconds = 0.22f;
	private const float c_FormationForwardRebuildAngleDegrees = 0.25f;
	private bool m_IsHoveringPathSegment;
	private float m_PathHoverStartTime;
	private int m_HoveredUnitIndex = -1;
	private int m_HoveredSegmentIndex = -1;
	private Vector3 m_HoveredSegmentWorldPoint;
	private float m_HoveredSegmentFacingAngle;
	private bool m_IsEditingWaypointFacing;
	private int m_HoveredArrowUnitIndex = -1;
	private RtsUnitMember.FacingArrowDescriptor m_HoveredFacingArrow;
	private float m_ArrowHoverStartTime;
	private bool m_IsArrowDeleteButtonVisible;
	private Rect m_ArrowDeleteButtonScreenRect;
	private bool m_IsRouteEditMode;
	private GameObject m_RouteEditHandle;
	private bool m_IsDraggingRoute;
	private int m_RouteEditWaypointIndex = -1;
	private RouteEditTargetKind m_RouteEditTargetKind;
	private int m_RouteEditVertexIndex = -1;
	private readonly List<RtsUnitMember.FacingArrowDescriptor> m_FacingArrowPickBuffer = new List<RtsUnitMember.FacingArrowDescriptor>(16);
	private readonly List<RtsUnitMember.WaitPointDescriptor> m_WaitPointPickBuffer = new List<RtsUnitMember.WaitPointDescriptor>(16);
	private readonly List<Rect> m_WaitPointIconScreenRects = new List<Rect>(16);
	private readonly List<int> m_WaitPointIconUnitIndices = new List<int>(16);
	private readonly List<int> m_WaitPointIconWaypointIndices = new List<int>(16);
	private static GUIStyle s_ArrowDeleteButtonGuiStyle;
	private static GUIStyle s_WaitPointIconGuiStyle;
	private const float c_WaitPointIconWorldYOffset = 0.03f;
	private int m_EditingUnitIndex = -1;
	private int m_EditingSegmentIndex = -1;
	private float m_EditingWaypointAngle;
	private Vector3 m_EditingWaypointAnchor;
	private RtsUnitMember.FacingArrowMode m_EditingWaypointMode;
	private Vector3 m_EditingWaypointLookPoint;
	private RtsUnitMember m_PendingExchangePlayerUnit;
	private RtsUnitMember m_PendingExchangePartnerUnit;
	private static RtsUnitSelectionManager s_Instance;
	private static GUIStyle s_FormationPickerGuiStyle;
	private static GUIStyle s_RtsHintsGuiStyle;
	private static GUIStyle s_TransientMessageGuiStyle;
	private static string s_TransientMessage;
	private static float s_TransientMessageUntilUnscaledTime = -1f;
	private static readonly HashSet<RtsUnitMember.FormationSyncGroup> s_ProcessedFormationSyncGroups =
		new HashSet<RtsUnitMember.FormationSyncGroup>();

	private enum RouteEditTargetKind
	{
		SegmentPoint,
		WaypointVertex,
	}

	private enum RouteVertexRole
	{
		First,
		Corner,
		End,
	}

	private enum GroupFormationFacingMode
	{
		HoldToEnd,
		LookAtPoint,
	}
	#endregion

	#region Public Events
	public event Action SelectionChanged;
	#endregion

	#region Public Properties
	public static RtsUnitSelectionManager Instance => s_Instance;
	public int SelectedUnitCount => m_SelectedUnits != null ? m_SelectedUnits.Count : 0;
	public bool ShouldPinActiveExchangeInventory =>
		InventoryExchangeController.Instance.IsActive && SelectedUnitCount > 0;
	public InventoryPanelView GroundPanel => m_GroundPanel;
	public InventoryPanelView CharacterInventoryPanel => m_CharacterInventoryPanel;
	public bool IsExchangeActive => InventoryExchangeController.Instance.IsActive;
	public bool HasPendingExchangeApproach =>
		m_PendingExchangePlayerUnit != null && m_PendingExchangePartnerUnit != null;
	#endregion

	#region Public Methods
	public bool TryGetFirstSelectedPlayerCombatStats(out UnitCombatStats _combatStats)
	{
		_combatStats = null;
		if (m_SelectedUnits == null || m_SelectedUnits.Count == 0)
			return false;

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null || !unit.IsPlayerSelectable)
				continue;

			_combatStats = unit.GetComponent<UnitCombatStats>();
			if (_combatStats == null)
				_combatStats = unit.GetComponentInChildren<UnitCombatStats>(true);
			if (_combatStats != null)
				return true;
		}

		return false;
	}

	public int CollectSelectedPlayerCombatStats(List<UnitCombatStats> _buffer)
	{
		if (_buffer == null || m_SelectedUnits == null || m_SelectedUnits.Count == 0)
			return 0;

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null || !unit.IsPlayerSelectable)
				continue;

			UnitCombatStats combatStats = unit.GetComponent<UnitCombatStats>();
			if (combatStats == null)
				combatStats = unit.GetComponentInChildren<UnitCombatStats>(true);
			if (combatStats == null || _buffer.Contains(combatStats))
				continue;

			_buffer.Add(combatStats);
		}

		return _buffer.Count;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		m_CurrentFormationSpacing = m_FormationLineSpacing;

		if (m_InventoryBindings != null)
			m_InventoryBindings.SetSelectionManager(this);

		ClearSelectionVisualsOnly();
	}

	private void OnDestroy()
	{
		if (FallenUnitInteractionMenuController.Instance != null)
			FallenUnitInteractionMenuController.Instance.ActionClicked -= HandleFallenUnitMenuAction;

		if (m_RouteEditHandle != null)
			Destroy(m_RouteEditHandle);

		if (s_Instance == this)
			s_Instance = null;
	}

	private void Start()
	{
		FallenUnitInteractionMenuController.Instance.ActionClicked += HandleFallenUnitMenuAction;

		if (m_SelectFirstPlayerUnitOnStart)
		{
			TrySelectFirstPlayerUnit();
			StartCoroutine(CoEnsurePlayerUnitSelectedAfterSpawn());
		}
		else
			SyncActiveInventoryToSelection();
	}

	/// <summary>Повторный выбор после спавна юнитов (если <see cref="Start"/> отработал раньше спавнера).</summary>
	public void EnsurePlayerUnitSelected()
	{
		if (SelectedUnitCount > 0)
			return;

		TrySelectFirstPlayerUnit();
	}

	private void Update()
	{
		if (PauseMenuController.IsPaused)
			return;

		HandleLeftMouseSelection();
		UpdatePathInteractions();
		HandleRightMouseCommand();
		HandleFormationKeyInput();
		HandleKeyboardCommands();
		UpdateFormationSyncSpeeds();
	}

	private void OnGUI()
	{
		if (m_IsDraggingSelection)
		{
			Rect rect = GetSelectionRect(m_LeftMouseDownScreen, Mouse.current != null ? Mouse.current.position.ReadValue() : m_LeftMouseDownScreen);
			DrawScreenRect(rect, new Color(0.2f, 0.7f, 1f, 0.15f));
			DrawScreenRectBorder(rect, 1f, new Color(0.2f, 0.7f, 1f, 0.95f));
		}

		DrawRtsControlHintsIfAnySelection();
		DrawFormationPickerIfAny();
		DrawArrowDeleteButtonIfAny();
		DrawWaitPointIconsIfAny();
		DrawTransientMessageIfAny();
	}
	#endregion

	#region Public Methods
	public void ClearSelection()
	{
		ClearAllPathInteractions();
		SetSelection(new List<RtsUnitMember>(0));
	}

	public void CommandSelectedStanding()
	{
		CommandSelectedStance(LocomotionStance.Standing);
	}

	public void CommandSelectedCrouch()
	{
		CommandSelectedStance(LocomotionStance.Crouch);
	}

	public void CommandSelectedProne()
	{
		if (!LocomotionProneFeature.Enabled)
			return;
		CommandSelectedStance(LocomotionStance.Prone);
	}

	public void CommandSelectedReady()
	{
		SetSelectedReady(true);
	}

	public void CommandSelectedNotReady()
	{
		SetSelectedReady(false);
	}

	public void ToggleSelectedReady()
	{
		bool hasNotReady = false;
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			UnitWeaponReadyHandsLayer readyHands = unit.GetComponent<UnitWeaponReadyHandsLayer>();
			if (readyHands != null && !readyHands.WantsReady)
			{
				hasNotReady = true;
				break;
			}
		}

		SetSelectedReady(hasNotReady);
	}

	public void ConfigureInventoryPanels(InventoryPanelView _groundPanel, InventoryPanelView _characterInventoryPanel)
	{
		m_GroundPanel = _groundPanel;
		m_CharacterInventoryPanel = _characterInventoryPanel;
	}

	public bool TryMoveGroundSlotToCharacter(int _groundSlotIndex)
	{
		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		var groundSlots = m_GroundPanel.Slots;
		if (_groundSlotIndex < 0 || _groundSlotIndex >= groundSlots.Count)
			return false;

		InventorySlotView slot = groundSlots[_groundSlotIndex];
		if (!slot.TryTakeItem(out InventorySlotRuntimeData data))
			return false;

		InventorySlotRuntimeData forInventory = data;
		forInventory.WorldSource = null;

		if (!inventory.TryAdd(forInventory))
		{
			slot.SetItem(data);
			return false;
		}

		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(inventory, forInventory);

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	public bool TryMoveCharacterSlotToGround(int _characterSlotIndex)
	{
		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		InventorySlotRuntimeData data;
		if (_characterSlotIndex == -1)
		{
			if (!inventory.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else
		{
			if (!inventory.TryRemoveBagAt(_characterSlotIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(inventory, data, null, _characterSlotIndex == -1);
	}

	public bool TryQuickTransferCtrlClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		if (IsSlotOnPanel(_slot, m_GroundPanel))
			return TryQuickTransferGroundToCharacterInternal(inventory, _slot);

		if (IsSlotOnPanel(_slot, m_CharacterInventoryPanel))
			return TryQuickTransferCharacterToGroundInternal(inventory, _slot);

		return false;
	}

	public bool TryResolveCharacterInventorySlot(
		InventorySlotView _slot,
		CharacterInventory _inventory,
		out bool _isMainHand,
		out int _bagIndex)
	{
		return TryResolveCharacterInventorySlot(_slot, _inventory, out _isMainHand, out bool _, out bool _, out _bagIndex);
	}

	public bool TryResolveCharacterInventorySlot(
		InventorySlotView _slot,
		CharacterInventory _inventory,
		out bool _isMainHand,
		out bool _isHead,
		out int _bagIndex)
	{
		return TryResolveCharacterInventorySlot(_slot, _inventory, out _isMainHand, out _isHead, out bool _, out _bagIndex);
	}

	public bool TryResolveCharacterInventorySlot(
		InventorySlotView _slot,
		CharacterInventory _inventory,
		out bool _isMainHand,
		out bool _isHead,
		out bool _isBack,
		out int _bagIndex)
	{
		return TryResolveInventorySlotOnPanel(
			_slot,
			m_CharacterInventoryPanel,
			_inventory,
			out _isMainHand,
			out _isHead,
			out _isBack,
			out _bagIndex);
	}

	public bool TryResolvePartnerInventorySlot(
		InventorySlotView _slot,
		CharacterInventory _inventory,
		out bool _isMainHand,
		out bool _isHead,
		out bool _isBack,
		out int _bagIndex)
	{
		return TryResolveInventorySlotOnPanel(
			_slot,
			m_GroundPanel,
			_inventory,
			out _isMainHand,
			out _isHead,
			out _isBack,
			out _bagIndex);
	}

	public bool TryResolveInventorySlotOnPanel(
		InventorySlotView _slot,
		InventoryPanelView _panel,
		CharacterInventory _inventory,
		out bool _isMainHand,
		out bool _isHead,
		out bool _isBack,
		out int _bagIndex)
	{
		_isMainHand = false;
		_isHead = false;
		_isBack = false;
		_bagIndex = -1;

		if (_panel == null || _slot == null || _inventory == null || !_slot.HasItem)
			return false;

		if (!IsSlotOnPanel(_slot, _panel))
			return false;

		int slotIndex = _panel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
			return false;

		int lead = _panel.LeadingEquipmentSlotCount;
		if (slotIndex < lead)
		{
			if (slotIndex == 0)
			{
				_isMainHand = true;
				return _inventory.HasMainHandEquipment;
			}

			if (slotIndex == 1)
			{
				_isHead = true;
				return _inventory.HasHeadEquipment;
			}

			if (slotIndex == 2)
			{
				_isBack = true;
				return _inventory.HasBackEquipment;
			}

			return false;
		}

		_bagIndex = slotIndex - lead;
		return _bagIndex >= 0 && _bagIndex < _inventory.BagCount;
	}

	/// <summary>С земли на панель персонажа: сначала экипировка (слот оружия), иначе — в сумку.</summary>
	public bool TryRouteGroundDragOnCharacterPanel(
		InventoryGroundToCharacterDrag _drag,
		Vector2 _screenPosition,
		Camera _eventCamera,
		bool _requireActiveDrag = true)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		InventorySlotView slot = _drag.SlotView;
		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		if (slot != null && WeaponEquipUtility.CanEquipToMainHand(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverCharacterMainHandSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipWeaponDragToMainHand())
			return true;

		if (slot != null && HelmetEquipUtility.CanEquipToHead(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverCharacterHeadSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipHelmetDragToHead())
			return true;

		if (slot != null && BackpackEquipUtility.CanEquipToBack(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverCharacterBackSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipBackpackDragToBack())
			return true;

		return TryAcceptDraggedGroundSlot(_drag, _requireActiveDrag);
	}

	/// <summary>С панели персонажа: снять оружие в сумку или экипировать из сумки в слот оружия.</summary>
	public bool TryRouteCharacterDragOnCharacterPanel(
		InventoryCharacterToGroundDrag _drag,
		Vector2 _screenPosition,
		Camera _eventCamera,
		bool _requireActiveDrag = true)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		if (coordinator == null)
			return false;

		if (_drag.CapturedFromMainHandEquipmentSlot)
		{
			if (!coordinator.IsScreenPointOverCharacterPanel(_screenPosition, _eventCamera))
				return false;

			if (coordinator.IsScreenPointOverCharacterMainHandSlot(_screenPosition, _eventCamera))
				return false;

			return TryAcceptMainHandDragToBag(_drag);
		}

		if (_drag.CapturedFromHeadEquipmentSlot)
		{
			if (!coordinator.IsScreenPointOverCharacterPanel(_screenPosition, _eventCamera))
				return false;

			if (coordinator.IsScreenPointOverCharacterHeadSlot(_screenPosition, _eventCamera))
				return false;

			return TryAcceptHeadDragToBag(_drag);
		}

		if (_drag.CapturedFromBackEquipmentSlot)
		{
			if (!coordinator.IsScreenPointOverCharacterPanel(_screenPosition, _eventCamera))
				return false;

			if (coordinator.IsScreenPointOverCharacterBackSlot(_screenPosition, _eventCamera))
				return false;

			return TryAcceptBackDragToBag(_drag);
		}

		if (coordinator.IsScreenPointOverCharacterMainHandSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipWeaponDragToMainHand();

		if (coordinator.IsScreenPointOverCharacterHeadSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipHelmetDragToHead();

		if (coordinator.IsScreenPointOverCharacterBackSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipBackpackDragToBack();

		return false;
	}

	/// <summary>Снять экипированное оружие в сумку (drag на панель инвентаря, не на землю).</summary>
	public bool TryAcceptMainHandDragToBag(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.CapturedFromMainHandEquipmentSlot)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (!inventory.TryUnequipMainHandToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	/// <summary>Снять экипированный шлем в сумку (drag на панель инвентаря, не на землю).</summary>
	public bool TryAcceptHeadDragToBag(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.CapturedFromHeadEquipmentSlot)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (!inventory.TryUnequipHeadToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	/// <summary>Снять экипированный рюкзак в сумку (drag на панель инвентаря, не на землю).</summary>
	public bool TryAcceptBackDragToBag(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.CapturedFromBackEquipmentSlot)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (!inventory.TryUnequipBackToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	public bool TryAcceptDraggedGroundSlot(InventoryGroundToCharacterDrag _drag, bool _requireActiveDrag = true)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		if (IsExchangeActive)
			return TryAcceptPartnerDragToPlayerBag(_drag, _requireActiveDrag);

		CharacterInventory inventory = GetActiveInventory();
		InventorySlotView slot = _drag.SlotView;
		if (inventory == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		InventorySlotRuntimeData data = slot.Data;
		InventorySlotRuntimeData forInventory = data;
		forInventory.WorldSource = null;

		if (!inventory.TryAdd(forInventory))
			return false;

		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(inventory, forInventory);

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		slot.SetItem(forInventory);
		if (!m_CharacterInventoryPanel.AdoptDraggedSlot(slot))
		{
			if (inventory.BagCount > 0)
				inventory.TryRemoveBagAt(inventory.BagCount - 1, out _);
			return false;
		}

		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	public bool TryAcceptDraggedCharacterSlot(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.WasDraggingThisFrame)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		InventorySlotView slot = _drag.SlotView;
		if (inventory == null || m_GroundPanel == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		InventorySlotRuntimeData data;
		if (_drag.CapturedFromMainHandEquipmentSlot)
		{
			if (!inventory.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else if (_drag.CapturedFromHeadEquipmentSlot)
		{
			if (!inventory.TryRemoveHeadEquipment(out data))
				return false;
		}
		else if (_drag.CapturedFromBackEquipmentSlot)
		{
			if (!inventory.TryRemoveBackEquipment(out data))
				return false;
		}
		else
		{
			int bagIndex = _drag.CapturedBagIndex;
			if (bagIndex < 0 || bagIndex >= inventory.BagCount)
				return false;
			if (!inventory.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		if (IsExchangeActive)
			return TryCompleteCharacterToPartnerTransfer(
				inventory,
				data,
				slot,
				_drag.CapturedFromMainHandEquipmentSlot,
				_drag.CapturedFromHeadEquipmentSlot,
				_drag.CapturedFromBackEquipmentSlot);

		return TryCompleteCharacterToGroundTransfer(
			inventory,
			data,
			slot,
			_drag.CapturedFromMainHandEquipmentSlot,
			_drag.CapturedFromHeadEquipmentSlot,
			_drag.CapturedFromBackEquipmentSlot);
	}

	/// <summary>Выброс из панели персонажа за пределы окон инвентаря (в мир / на «землю»).</summary>
	public bool TryDropCharacterDragOutsidePanels(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null)
			return false;

		CharacterInventory inventory = GetActiveInventory();
		InventorySlotView slot = _drag.SlotView;
		if (inventory == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		InventorySlotRuntimeData data;
		if (_drag.CapturedFromMainHandEquipmentSlot)
		{
			if (!inventory.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else if (_drag.CapturedFromHeadEquipmentSlot)
		{
			if (!inventory.TryRemoveHeadEquipment(out data))
				return false;
		}
		else if (_drag.CapturedFromBackEquipmentSlot)
		{
			if (!inventory.TryRemoveBackEquipment(out data))
				return false;
		}
		else
		{
			int bagIndex = _drag.CapturedBagIndex;
			if (bagIndex < 0 || bagIndex >= inventory.BagCount)
				return false;

			if (!inventory.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		return TryCompleteCharacterToWorldDrop(
			inventory,
			data,
			slot,
			_drag.CapturedFromMainHandEquipmentSlot,
			_drag.CapturedFromHeadEquipmentSlot,
			_drag.CapturedFromBackEquipmentSlot);
	}

	/// <summary>Выброс предмета партнёра за пределы окон инвентаря во время обмена.</summary>
	public bool TryDropPartnerDragOutsidePanels(InventoryGroundToCharacterDrag _drag)
	{
		if (!IsExchangeActive || _drag == null)
			return false;

		CharacterInventory partner = GetPartnerInventory();
		InventorySlotView slot = _drag.SlotView;
		if (partner == null || m_GroundPanel == null || slot == null || !slot.HasItem)
			return false;

		if (!TryRemovePartnerItemByGroundSlotIndex(
			    _drag.CapturedGroundSlotIndex,
			    slot,
			    partner,
			    out InventorySlotRuntimeData data,
			    out bool isMainHand,
			    out bool isHead,
			    out bool isBack))
			return false;

		return TryCompletePartnerToWorldDrop(partner, data, slot, isMainHand, isHead, isBack);
	}

	public bool TryEquipFromCharacterBagDoubleClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem)
		{
			Debug.Log($"{nameof(RtsUnitSelectionManager)}.{nameof(TryEquipFromCharacterBagDoubleClick)}: слот null или пустой.");
			return false;
		}

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
		{
			Debug.Log(
				$"{nameof(RtsUnitSelectionManager)}.{nameof(TryEquipFromCharacterBagDoubleClick)}: inventory={(inventory != null ? inventory.name : "null")}, CharacterInventoryPanel={(m_CharacterInventoryPanel != null ? m_CharacterInventoryPanel.name : "null")}.");
			return false;
		}

		if (!TryResolveCharacterInventorySlot(_slot, inventory, out bool isMainHand, out bool isHead, out bool isBack, out int bagIndex))
		{
			Debug.Log(
				$"{nameof(RtsUnitSelectionManager)}.{nameof(TryEquipFromCharacterBagDoubleClick)}: не удалось сопоставить слот UI с инвентарём (панель / Content / LeadingEquipmentSlotCount). Слот '{_slot.name}'.");
			return false;
		}

		Debug.Log(
			$"{nameof(TryEquipFromCharacterBagDoubleClick)}: isMainHand={isMainHand}, bagIndex={bagIndex}, item={_slot.Data.Definition?.name ?? _slot.Data.DisplayName}");

		if (isMainHand)
		{
			if (!inventory.TryUnequipMainHandToBag())
			{
				Debug.Log($"{nameof(TryEquipFromCharacterBagDoubleClick)}: TryUnequipMainHandToBag failed.");
				return false;
			}

			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		if (isHead)
		{
			if (!inventory.TryUnequipHeadToBag())
			{
				Debug.Log($"{nameof(TryEquipFromCharacterBagDoubleClick)}: TryUnequipHeadToBag failed.");
				return false;
			}

			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		if (isBack)
		{
			if (!inventory.TryUnequipBackToBag())
			{
				Debug.Log($"{nameof(TryEquipFromCharacterBagDoubleClick)}: TryUnequipBackToBag failed.");
				return false;
			}

			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		InventorySlotRuntimeData data = _slot.Data;
		if (data.Definition == null || !data.Definition.IsEquipment)
		{
			Debug.Log(
				$"{nameof(TryEquipFromCharacterBagDoubleClick)}: предмет не Equipment (Definition={data.Definition?.name ?? "null"}, IsEquipment={data.Definition != null && data.Definition.IsEquipment}).");
			return false;
		}

		if (HelmetEquipUtility.CanEquipToHead(data))
		{
			if (inventory.HasHeadEquipment && inventory.HeadEquipment.Definition == data.Definition)
			{
				if (!inventory.TryUnequipHeadToBag())
					return false;
				inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
				RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
				return true;
			}

			UnitHeadEquipment headEquipment = inventory.GetComponentInChildren<UnitHeadEquipment>(true);
			if (headEquipment == null)
			{
				Debug.LogWarning($"{nameof(RtsUnitSelectionManager)}: на юните с {nameof(CharacterInventory)} нет {nameof(UnitHeadEquipment)}.", this);
				return false;
			}

			UnitIndividualTraits traits = inventory.GetComponentInChildren<UnitIndividualTraits>(true);
			UnitCharacterAppearance appearance = inventory.GetComponentInChildren<UnitCharacterAppearance>(true);
			if (!inventory.TryMoveBagItemToHead(bagIndex, headEquipment, traits, appearance))
				return false;

			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		if (BackpackEquipUtility.CanEquipToBack(data))
		{
			if (inventory.HasBackEquipment && inventory.BackEquipment.Definition == data.Definition)
			{
				if (!inventory.TryUnequipBackToBag())
					return false;
				inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
				RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
				return true;
			}

			UnitBackEquipment backEquipment = inventory.GetComponentInChildren<UnitBackEquipment>(true);
			if (backEquipment == null)
			{
				Debug.LogWarning($"{nameof(RtsUnitSelectionManager)}: на юните с {nameof(CharacterInventory)} нет {nameof(UnitBackEquipment)}.", this);
				return false;
			}

			if (!inventory.TryMoveBagItemToBack(bagIndex, backEquipment))
				return false;

			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		if (!WeaponEquipUtility.CanEquipToMainHand(data))
			return false;

		if (inventory.HasMainHandEquipment && inventory.MainHandEquipment.Definition == data.Definition)
		{
			if (!inventory.TryUnequipMainHandToBag())
				return false;
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
			return true;
		}

		UnitEquipment equipment = inventory.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
		{
			Debug.LogWarning($"{nameof(RtsUnitSelectionManager)}: на юните с {nameof(CharacterInventory)} нет {nameof(UnitEquipment)}.", this);
			return false;
		}

		if (!inventory.TryMoveBagItemToMainHand(bagIndex, equipment))
		{
			Debug.Log(
				$"{nameof(TryEquipFromCharacterBagDoubleClick)}: TryMoveBagItemToMainHand failed (bagIndex={bagIndex}, BagCount={inventory.BagCount}).");
			return false;
		}

		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
		return true;
	}

	/// <summary>Экипировать оружие из сумки в основную руку (drag на слот экипировки).</summary>
	public bool TryEquipCharacterBagWeaponToMainHand(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null || _bagIndex < 0 || _bagIndex >= inventory.BagCount)
			return false;

		UnitEquipment equipment = inventory.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
			return false;

		if (!inventory.TryMoveBagItemToMainHand(_bagIndex, equipment))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Экипировать шлем из сумки в слот головы (drag на слот экипировки).</summary>
	public bool TryEquipCharacterBagHelmetToHead(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null || _bagIndex < 0 || _bagIndex >= inventory.BagCount)
			return false;

		UnitHeadEquipment headEquipment = inventory.GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment == null)
			return false;

		UnitIndividualTraits traits = inventory.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = inventory.GetComponentInChildren<UnitCharacterAppearance>(true);

		if (!inventory.TryMoveBagItemToHead(_bagIndex, headEquipment, traits, appearance))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Экипировать рюкзак из сумки в слот спины (drag на слот экипировки).</summary>
	public bool TryEquipCharacterBagBackpackToBack(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null || _bagIndex < 0 || _bagIndex >= inventory.BagCount)
			return false;

		UnitBackEquipment backEquipment = inventory.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
			return false;

		if (!inventory.TryMoveBagItemToBack(_bagIndex, backEquipment))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Экипировать оружие с панели земли в основную руку (drag или двойной клик).</summary>
	public bool TryEquipGroundWeaponToMainHand(InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		if (m_GroundPanel == null)
			return false;

		InventorySlotView slot = _slotView;
		if ((slot == null || !slot.HasItem) && _groundSlotIndex >= 0 && _groundSlotIndex < m_GroundPanel.Slots.Count)
			slot = m_GroundPanel.Slots[_groundSlotIndex];

		if (slot == null || !slot.HasItem || !WeaponEquipUtility.CanEquipToMainHand(slot.Data))
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (IsExchangeActive)
			return TryEquipPartnerItemToPlayerMainHand(slot, _slotView, _groundSlotIndex);

		UnitEquipment equipment = inventory.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
			return false;

		if (!slot.TryTakeItem(out InventorySlotRuntimeData taken))
			return false;

		InventorySlotRuntimeData forEquip = taken;
		forEquip.WorldSource = null;

		if (!inventory.TryEquipExternalItemToMainHand(forEquip, equipment))
		{
			slot.SetItem(taken);
			if (taken.WorldSource != null)
				taken.WorldSource.ApplyInventorySlotData(taken);
			return false;
		}

		if (taken.WorldSource != null)
			taken.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Экипировать шлем с панели земли в слот головы.</summary>
	public bool TryEquipGroundHelmetToHead(InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		if (m_GroundPanel == null)
			return false;

		InventorySlotView slot = _slotView;
		if ((slot == null || !slot.HasItem) && _groundSlotIndex >= 0 && _groundSlotIndex < m_GroundPanel.Slots.Count)
			slot = m_GroundPanel.Slots[_groundSlotIndex];

		if (slot == null || !slot.HasItem || !HelmetEquipUtility.CanEquipToHead(slot.Data))
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (IsExchangeActive)
			return TryEquipPartnerItemToPlayerHead(slot, _slotView, _groundSlotIndex);

		UnitHeadEquipment headEquipment = inventory.GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment == null)
			return false;

		UnitIndividualTraits traits = inventory.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = inventory.GetComponentInChildren<UnitCharacterAppearance>(true);

		if (!slot.TryTakeItem(out InventorySlotRuntimeData taken))
			return false;

		InventorySlotRuntimeData forEquip = taken;
		forEquip.WorldSource = null;

		if (!inventory.TryEquipExternalItemToHead(forEquip, headEquipment, traits, appearance))
		{
			slot.SetItem(taken);
			if (taken.WorldSource != null)
				taken.WorldSource.ApplyInventorySlotData(taken);
			return false;
		}

		if (taken.WorldSource != null)
			taken.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Экипировать рюкзак с панели земли в слот спины.</summary>
	public bool TryEquipGroundBackpackToBack(InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		if (m_GroundPanel == null)
			return false;

		InventorySlotView slot = _slotView;
		if ((slot == null || !slot.HasItem) && _groundSlotIndex >= 0 && _groundSlotIndex < m_GroundPanel.Slots.Count)
			slot = m_GroundPanel.Slots[_groundSlotIndex];

		if (slot == null || !slot.HasItem || !BackpackEquipUtility.CanEquipToBack(slot.Data))
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (IsExchangeActive)
			return TryEquipPartnerItemToPlayerBack(slot, _slotView, _groundSlotIndex);

		UnitBackEquipment backEquipment = inventory.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
			return false;

		if (!slot.TryTakeItem(out InventorySlotRuntimeData taken))
			return false;

		InventorySlotRuntimeData forEquip = taken;
		forEquip.WorldSource = null;

		if (!inventory.TryEquipExternalItemToBack(forEquip, backEquipment))
		{
			slot.SetItem(taken);
			if (taken.WorldSource != null)
				taken.WorldSource.ApplyInventorySlotData(taken);
			return false;
		}

		if (taken.WorldSource != null)
			taken.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>Двойной клик по оружию на панели земли — экипировка в основную руку.</summary>
	public bool TryEquipFromGroundDoubleClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || m_GroundPanel == null)
			return false;

		if (_slot.GetComponentInParent<InventoryPanelView>() != m_GroundPanel)
			return false;

		if (!TryEquipGroundWeaponToMainHand(_slot) && !TryEquipGroundHelmetToHead(_slot) &&
		    !TryEquipGroundBackpackToBack(_slot))
			return false;

		RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
		return true;
	}

	/// <summary>С панели партнёра на панель персонажа: экипировка или сумка.</summary>
	public bool TryRouteGroundDragOnPartnerPanel(
		InventoryGroundToCharacterDrag _drag,
		Vector2 _screenPosition,
		Camera _eventCamera,
		bool _requireActiveDrag = true)
	{
		if (!IsExchangeActive || _drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		InventorySlotView slot = _drag.SlotView;

		if (slot != null && WeaponEquipUtility.CanEquipToMainHand(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverPartnerMainHandSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipWeaponDragToPartnerMainHand())
			return true;

		if (slot != null && HelmetEquipUtility.CanEquipToHead(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverPartnerHeadSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipHelmetDragToPartnerHead())
			return true;

		if (slot != null && BackpackEquipUtility.CanEquipToBack(slot.Data) && coordinator != null &&
		    coordinator.IsScreenPointOverPartnerBackSlot(_screenPosition, _eventCamera) &&
		    coordinator.TryEquipBackpackDragToPartnerBack())
			return true;

		return TryAcceptPartnerEquipmentDragToPartnerBag(_drag, _requireActiveDrag);
	}

	/// <summary>С панели персонажа на панель партнёра: экипировка или сумка.</summary>
	public bool TryRouteCharacterDragOnPartnerPanel(
		InventoryCharacterToGroundDrag _drag,
		Vector2 _screenPosition,
		Camera _eventCamera,
		bool _requireActiveDrag = true)
	{
		if (!IsExchangeActive || _drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		if (coordinator == null)
			return false;

		if (_drag.CapturedFromMainHandEquipmentSlot)
		{
			if (coordinator.IsScreenPointOverPartnerMainHandSlot(_screenPosition, _eventCamera))
				return coordinator.TryEquipWeaponDragToPartnerMainHand();

			if (!coordinator.IsScreenPointOverGroundPanel(_screenPosition, _eventCamera))
				return false;

			return TryAcceptPlayerMainHandDragToPartnerBag(_drag);
		}

		if (_drag.CapturedFromHeadEquipmentSlot)
		{
			if (coordinator.IsScreenPointOverPartnerHeadSlot(_screenPosition, _eventCamera))
				return coordinator.TryEquipHelmetDragToPartnerHead();

			if (!coordinator.IsScreenPointOverGroundPanel(_screenPosition, _eventCamera))
				return false;

			return TryAcceptPlayerHeadDragToPartnerBag(_drag);
		}

		if (_drag.CapturedFromBackEquipmentSlot)
		{
			if (coordinator.IsScreenPointOverPartnerBackSlot(_screenPosition, _eventCamera))
				return coordinator.TryEquipBackpackDragToPartnerBack();

			if (!coordinator.IsScreenPointOverGroundPanel(_screenPosition, _eventCamera))
				return false;

			return TryAcceptPlayerBackDragToPartnerBag(_drag);
		}

		if (coordinator.IsScreenPointOverPartnerMainHandSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipWeaponDragToPartnerMainHand();

		if (coordinator.IsScreenPointOverPartnerHeadSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipHelmetDragToPartnerHead();

		if (coordinator.IsScreenPointOverPartnerBackSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipBackpackDragToPartnerBack();

		return TryAcceptDraggedCharacterSlot(_drag);
	}

	public bool TryEquipPlayerBagWeaponToPartnerMainHand(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || m_GroundPanel == null || _bagIndex < 0 || _bagIndex >= player.BagCount)
			return false;

		UnitEquipment equipment = partner.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
			return false;

		if (!player.TryRemoveBagAt(_bagIndex, out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToMainHand(picked, equipment))
		{
			player.TryAdd(picked);
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPlayerBagHelmetToPartnerHead(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || m_GroundPanel == null || _bagIndex < 0 || _bagIndex >= player.BagCount)
			return false;

		UnitHeadEquipment headEquipment = partner.GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment == null)
			return false;

		UnitIndividualTraits traits = partner.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = partner.GetComponentInChildren<UnitCharacterAppearance>(true);

		if (!player.TryRemoveBagAt(_bagIndex, out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToHead(picked, headEquipment, traits, appearance))
		{
			player.TryAdd(picked);
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPlayerBagBackpackToPartnerBack(int _bagIndex, InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || m_GroundPanel == null || _bagIndex < 0 || _bagIndex >= player.BagCount)
			return false;

		UnitBackEquipment backEquipment = partner.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
			return false;

		if (!player.TryRemoveBagAt(_bagIndex, out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToBack(picked, backEquipment))
		{
			player.TryAdd(picked);
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPlayerMainHandToPartnerMainHand(InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || !player.HasMainHandEquipment)
			return false;

		UnitEquipment equipment = partner.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
			return false;

		if (!player.TryRemoveMainHandEquipment(out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToMainHand(picked, equipment))
		{
			player.TryEquipExternalItemToMainHand(picked, player.GetComponentInChildren<UnitEquipment>(true));
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPlayerHeadToPartnerHead(InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || !player.HasHeadEquipment)
			return false;

		UnitHeadEquipment headEquipment = partner.GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment == null)
			return false;

		UnitIndividualTraits traits = partner.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = partner.GetComponentInChildren<UnitCharacterAppearance>(true);

		if (!player.TryRemoveHeadEquipment(out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToHead(picked, headEquipment, traits, appearance))
		{
			player.TryEquipExternalItemToHead(
				picked,
				player.GetComponentInChildren<UnitHeadEquipment>(true),
				player.GetComponentInChildren<UnitIndividualTraits>(true),
				player.GetComponentInChildren<UnitCharacterAppearance>(true));
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPlayerBackToPartnerBack(InventorySlotView _slotView)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null || !player.HasBackEquipment)
			return false;

		UnitBackEquipment backEquipment = partner.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
			return false;

		if (!player.TryRemoveBackEquipment(out InventorySlotRuntimeData picked))
			return false;

		picked.WorldSource = null;
		if (!partner.TryEquipExternalItemToBack(picked, backEquipment))
		{
			player.TryEquipExternalItemToBack(picked, player.GetComponentInChildren<UnitBackEquipment>(true));
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPartnerBagWeaponToPartnerMainHand(int _groundSlotIndex, InventorySlotView _slotView)
	{
		CharacterInventory partner = GetPartnerInventory();
		if (partner == null || m_GroundPanel == null)
			return false;

		if (!TryGetPartnerBagIndexFromGroundSlotIndex(_groundSlotIndex, out int bagIndex))
			return false;

		UnitEquipment equipment = partner.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
			return false;

		if (!partner.TryMoveBagItemToMainHand(bagIndex, equipment))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPartnerBagHelmetToPartnerHead(int _groundSlotIndex, InventorySlotView _slotView)
	{
		CharacterInventory partner = GetPartnerInventory();
		if (partner == null || m_GroundPanel == null)
			return false;

		if (!TryGetPartnerBagIndexFromGroundSlotIndex(_groundSlotIndex, out int bagIndex))
			return false;

		UnitHeadEquipment headEquipment = partner.GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment == null)
			return false;

		UnitIndividualTraits traits = partner.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = partner.GetComponentInChildren<UnitCharacterAppearance>(true);

		if (!partner.TryMoveBagItemToHead(bagIndex, headEquipment, traits, appearance))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}

	public bool TryEquipPartnerBagBackpackToPartnerBack(int _groundSlotIndex, InventorySlotView _slotView)
	{
		CharacterInventory partner = GetPartnerInventory();
		if (partner == null || m_GroundPanel == null)
			return false;

		if (!TryGetPartnerBagIndexFromGroundSlotIndex(_groundSlotIndex, out int bagIndex))
			return false;

		UnitBackEquipment backEquipment = partner.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
			return false;

		if (!partner.TryMoveBagItemToBack(bagIndex, backEquipment))
			return false;

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}
	#endregion

	#region Private Methods
	private void HandleFallenUnitMenuAction(FallenUnitInteractionMenuAction _action, RtsUnitMember _targetUnit)
	{
		if (_targetUnit == null)
			return;

		if (_action == FallenUnitInteractionMenuAction.FirstAid)
		{
			UnitSelfStabilizationController selfStabilization = _targetUnit.GetComponent<UnitSelfStabilizationController>();
			selfStabilization?.RequestSelfStabilization();
			return;
		}

		if (_action == FallenUnitInteractionMenuAction.ReleaseCarry)
		{
			if (!TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember playerUnit))
				return;

			playerUnit.GetComponent<UnitFiremanCarryController>()?.RequestRelease();
			return;
		}

		if (_action == FallenUnitInteractionMenuAction.Stabilize)
		{

			if (!TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember playerUnit))
			{
				Debug.LogWarning($"[RtsUnitSelection] Stabilize rejected: need exactly one controllable player unit.");
				return;
			}

			if (_targetUnit == null)
			{
				Debug.LogWarning("[RtsUnitSelection] Stabilize rejected: menu target is null.");
				return;
			}

			if (ReferenceEquals(_targetUnit, playerUnit))
			{
				Debug.LogWarning($"[RtsUnitSelection] Stabilize rejected: menu target is the same unit as helper.");
				return;
			}

			UnitStabilizeOtherController stabilizeController = playerUnit.GetComponent<UnitStabilizeOtherController>();
			if (stabilizeController == null)
			{
				Debug.LogError($"[RtsUnitSelection] Stabilize failed: '{playerUnit.name}' has no {nameof(UnitStabilizeOtherController)}.", playerUnit);
				return;
			}

			stabilizeController.RequestStabilizeOther(_targetUnit);
			return;
		}

		if (_action == FallenUnitInteractionMenuAction.Lift)
		{

			if (!TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember playerUnit))
			{
				Debug.LogWarning($"[RtsUnitSelection] Lift rejected: need exactly one controllable player unit.");
				return;
			}

			if (_targetUnit == null)
			{
				Debug.LogWarning("[RtsUnitSelection] Lift rejected: menu target is null.");
				return;
			}

			if (ReferenceEquals(_targetUnit, playerUnit))
			{
				Debug.LogWarning($"[RtsUnitSelection] Lift rejected: menu target is the same unit.");
				return;
			}

			UnitFiremanCarryController carryController = playerUnit.GetComponent<UnitFiremanCarryController>();
			if (carryController == null)
			{
				Debug.LogError($"[RtsUnitSelection] Lift failed: '{playerUnit.name}' has no {nameof(UnitFiremanCarryController)}.", playerUnit);
				return;
			}

			carryController.RequestLift(_targetUnit);
			return;
		}

		if (_action != FallenUnitInteractionMenuAction.Exchange)
			return;

		if (!TryGetControllablePlayerUnit(out RtsUnitMember exchangePlayerUnit))
			return;

		if (m_ExchangeApproachCoroutine != null)
			StopCoroutine(m_ExchangeApproachCoroutine);

		m_PendingExchangePlayerUnit = exchangePlayerUnit;
		m_PendingExchangePartnerUnit = _targetUnit;
		m_ExchangeApproachCoroutine = StartCoroutine(CoApproachAndBeginExchange(exchangePlayerUnit, _targetUnit));
	}

	private void ClearPendingExchangeApproach()
	{
		m_PendingExchangePlayerUnit = null;
		m_PendingExchangePartnerUnit = null;
	}

	private IEnumerator CoApproachAndBeginExchange(RtsUnitMember _playerUnit, RtsUnitMember _partnerUnit)
	{
		const float c_ArriveDistance = 1f;
		const float c_MaxApproachSeconds = 45f;

		if (_playerUnit == null || _partnerUnit == null)
		{
			m_ExchangeApproachCoroutine = null;
			ClearPendingExchangeApproach();
			yield break;
		}

		float distance = HorizontalDistance(_playerUnit.transform.position, _partnerUnit.transform.position);
		if (distance > c_ArriveDistance)
		{
			Vector3 approachPoint = ComputeExchangeApproachPoint(_playerUnit, _partnerUnit, c_ArriveDistance * 0.85f);
			_playerUnit.IssueMoveOrder(approachPoint, UnitClickToMove.MoveTier.Walk);

			float elapsed = 0f;
			while (elapsed < c_MaxApproachSeconds)
			{
				if (_playerUnit == null || _partnerUnit == null)
				{
					m_ExchangeApproachCoroutine = null;
					ClearPendingExchangeApproach();
					yield break;
				}

				distance = HorizontalDistance(_playerUnit.transform.position, _partnerUnit.transform.position);
				if (distance <= c_ArriveDistance)
					break;

				elapsed += Time.deltaTime;
				yield return null;
			}
		}

		_playerUnit.HardStop();

		distance = HorizontalDistance(_playerUnit.transform.position, _partnerUnit.transform.position);
		if (distance <= c_ArriveDistance)
			InventoryExchangeController.Instance.TryBeginExchange(_partnerUnit, _playerUnit);

		m_ExchangeApproachCoroutine = null;
		ClearPendingExchangeApproach();
	}

	private static Vector3 ComputeExchangeApproachPoint(
		RtsUnitMember _playerUnit,
		RtsUnitMember _partnerUnit,
		float _standoffMeters)
	{
		Vector3 partnerPosition = _partnerUnit.transform.position;
		Vector3 toPartner = partnerPosition - _playerUnit.transform.position;
		toPartner.y = 0f;

		if (toPartner.sqrMagnitude < 0.04f)
			toPartner = _partnerUnit.transform.forward;

		toPartner.Normalize();
		return partnerPosition - toPartner * _standoffMeters;
	}

	private static float HorizontalDistance(Vector3 _a, Vector3 _b)
	{
		float dx = _a.x - _b.x;
		float dz = _a.z - _b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private static void DestroyDetachedDragSlotIfNeeded(InventorySlotView _slotView, InventoryPanelView _panel)
	{
		if (_slotView == null || !Application.isPlaying)
			return;

		if (_slotView.GetComponentInParent<InventoryPanelView>() == _panel)
			return;

		if (_slotView.IsRuntimeSpawned && _panel != null)
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(_slotView.gameObject, _panel.transform);
		else
			Destroy(_slotView.gameObject);
	}

	private CharacterInventory GetActiveInventory()
	{
		if (m_InventoryBindings != null)
		{
			CharacterInventory inventory = m_InventoryBindings.GetActiveCharacterInventoryForUi();
			if (inventory != null)
				return inventory;
		}

		return TryGetActiveCharacterInventoryForUi();
	}

	private CharacterInventory GetPartnerInventory()
	{
		return InventoryExchangeController.Instance.PartnerInventory;
	}

	private void RepaintExchangePanels()
	{
		InventoryExchangeController.Instance.RepaintBothExchangePanels();
	}

	private static bool TryRemoveFromInventorySlot(
		CharacterInventory _inventory,
		bool _isMainHand,
		bool _isHead,
		bool _isBack,
		int _bagIndex,
		out InventorySlotRuntimeData _removed)
	{
		if (_isMainHand)
			return _inventory.TryRemoveMainHandEquipment(out _removed);

		if (_isHead)
			return _inventory.TryRemoveHeadEquipment(out _removed);

		if (_isBack)
			return _inventory.TryRemoveBackEquipment(out _removed);

		return _inventory.TryRemoveBagAt(_bagIndex, out _removed);
	}

	private static void TryRestoreToInventorySlot(
		CharacterInventory _inventory,
		bool _isMainHand,
		bool _isHead,
		bool _isBack,
		InventorySlotRuntimeData _data)
	{
		_inventory.RestoreAfterFailedDrop(_isMainHand, _isHead, _isBack, _data);
	}

	private bool TryRemovePartnerItemByGroundSlotIndex(
		int _groundSlotIndex,
		InventorySlotView _slotOrNull,
		CharacterInventory _partner,
		out InventorySlotRuntimeData _removed,
		out bool _isMainHand,
		out bool _isHead,
		out bool _isBack)
	{
		_isMainHand = false;
		_isHead = false;
		_isBack = false;
		_removed = default;

		if (_partner == null || m_GroundPanel == null)
			return false;

		if (_groundSlotIndex >= 0)
		{
			int lead = m_GroundPanel.LeadingEquipmentSlotCount;
			if (_groundSlotIndex < lead)
			{
				if (_groundSlotIndex == 0)
				{
					_isMainHand = true;
					return TryRemoveFromInventorySlot(_partner, true, false, false, -1, out _removed);
				}

				if (_groundSlotIndex == 1)
				{
					_isHead = true;
					return TryRemoveFromInventorySlot(_partner, false, true, false, -1, out _removed);
				}

				if (_groundSlotIndex == 2)
				{
					_isBack = true;
					return TryRemoveFromInventorySlot(_partner, false, false, true, -1, out _removed);
				}

				return false;
			}

			int bagIndex = _groundSlotIndex - lead;
			return TryRemoveFromInventorySlot(_partner, false, false, false, bagIndex, out _removed);
		}

		if (_slotOrNull == null ||
		    !TryResolvePartnerInventorySlot(_slotOrNull, _partner, out _isMainHand, out _isHead, out _isBack, out int resolvedBagIndex))
			return false;

		return TryRemoveFromInventorySlot(_partner, _isMainHand, _isHead, _isBack, resolvedBagIndex, out _removed);
	}

	private bool TryGetPartnerBagIndexFromGroundSlotIndex(int _groundSlotIndex, out int _bagIndex)
	{
		_bagIndex = -1;
		if (m_GroundPanel == null || _groundSlotIndex < 0)
			return false;

		int lead = m_GroundPanel.LeadingEquipmentSlotCount;
		if (_groundSlotIndex < lead)
			return false;

		_bagIndex = _groundSlotIndex - lead;
		return true;
	}

	private bool TryAcceptPartnerDragToPlayerBag(InventoryGroundToCharacterDrag _drag, bool _requireActiveDrag)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		InventorySlotView slot = _drag.SlotView;
		if (player == null || partner == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		int groundSlotIndex = _drag.CapturedGroundSlotIndex;
		if (!TryRemovePartnerItemByGroundSlotIndex(
			    groundSlotIndex,
			    slot,
			    partner,
			    out InventorySlotRuntimeData data,
			    out bool isMainHand,
			    out bool isHead,
			    out bool isBack))
			return false;

		InventorySlotRuntimeData forInventory = data;
		forInventory.WorldSource = null;

		if (!player.TryAdd(forInventory))
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, data);
			RepaintExchangePanels();
			return false;
		}

		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(player, forInventory);

		DestroyDetachedDragSlotIfNeeded(slot, m_GroundPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryEquipPartnerItemToPlayerMainHand(InventorySlotView _slot, InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null)
			return false;

		if (!TryRemovePartnerItemByGroundSlotIndex(
			    _groundSlotIndex,
			    _slot,
			    partner,
			    out InventorySlotRuntimeData taken,
			    out bool isMainHand,
			    out bool isHead,
			    out bool isBack))
			return false;

		UnitEquipment equipment = player.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		taken.WorldSource = null;
		if (!player.TryEquipExternalItemToMainHand(taken, equipment))
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}

	private bool TryEquipPartnerItemToPlayerHead(InventorySlotView _slot, InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null)
			return false;

		if (!TryRemovePartnerItemByGroundSlotIndex(
			    _groundSlotIndex,
			    _slot,
			    partner,
			    out InventorySlotRuntimeData taken,
			    out bool isMainHand,
			    out bool isHead,
			    out bool isBack))
			return false;

		UnitHeadEquipment headEquipment = player.GetComponentInChildren<UnitHeadEquipment>(true);
		UnitIndividualTraits traits = player.GetComponentInChildren<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = player.GetComponentInChildren<UnitCharacterAppearance>(true);
		if (headEquipment == null)
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		taken.WorldSource = null;
		if (!player.TryEquipExternalItemToHead(taken, headEquipment, traits, appearance))
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}

	private bool TryEquipPartnerItemToPlayerBack(InventorySlotView _slot, InventorySlotView _slotView, int _groundSlotIndex = -1)
	{
		CharacterInventory player = GetActiveInventory();
		CharacterInventory partner = GetPartnerInventory();
		if (player == null || partner == null)
			return false;

		if (!TryRemovePartnerItemByGroundSlotIndex(
			    _groundSlotIndex,
			    _slot,
			    partner,
			    out InventorySlotRuntimeData taken,
			    out bool isMainHand,
			    out bool isHead,
			    out bool isBack))
			return false;

		UnitBackEquipment backEquipment = player.GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment == null)
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		taken.WorldSource = null;
		if (!player.TryEquipExternalItemToBack(taken, backEquipment))
		{
			TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, taken);
			RepaintExchangePanels();
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_slotView, m_GroundPanel);
		RepaintExchangePanels();
		return true;
	}

	private bool TryCompleteCharacterToPartnerTransfer(
		CharacterInventory _playerInventory,
		InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull,
		bool _removedFromMainHandSlot,
		bool _removedFromHeadSlot = false,
		bool _removedFromBackSlot = false)
	{
		CharacterInventory partner = GetPartnerInventory();
		if (partner == null || m_GroundPanel == null)
		{
			_playerInventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_playerInventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		InventorySlotRuntimeData forPartner = _data;
		forPartner.WorldSource = null;

		if (!partner.TryAdd(forPartner))
		{
			_playerInventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_playerInventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_adoptExistingSlotOrNull, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryAcceptPartnerEquipmentDragToPartnerBag(InventoryGroundToCharacterDrag _drag, bool _requireActiveDrag)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		CharacterInventory partner = GetPartnerInventory();
		InventorySlotView slot = _drag.SlotView;
		if (partner == null || m_GroundPanel == null || slot == null || !slot.HasItem)
			return false;

		int groundSlotIndex = _drag.CapturedGroundSlotIndex;
		int lead = m_GroundPanel.LeadingEquipmentSlotCount;
		if (groundSlotIndex < 0 || groundSlotIndex >= lead)
			return false;

		bool success = groundSlotIndex switch
		{
			0 => partner.TryUnequipMainHandToBag(),
			1 => partner.TryUnequipHeadToBag(),
			2 => partner.TryUnequipBackToBag(),
			_ => false
		};

		if (!success)
			return false;

		DestroyDetachedDragSlotIfNeeded(slot, m_GroundPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryAcceptPlayerMainHandDragToPartnerBag(InventoryCharacterToGroundDrag _drag)
	{
		CharacterInventory player = GetActiveInventory();
		if (player == null || !player.TryUnequipMainHandToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryAcceptPlayerHeadDragToPartnerBag(InventoryCharacterToGroundDrag _drag)
	{
		CharacterInventory player = GetActiveInventory();
		if (player == null || !player.TryUnequipHeadToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryAcceptPlayerBackDragToPartnerBag(InventoryCharacterToGroundDrag _drag)
	{
		CharacterInventory player = GetActiveInventory();
		if (player == null || !player.TryUnequipBackToBag())
			return false;

		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryHandleLeftMouseGroundFacingCommand()
	{
		if (m_IsDraggingSelection)
			return false;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return false;

		bool shiftHeld = IsShiftHeld();
		bool ctrlHeld = IsCtrlPressed();
		if (!shiftHeld && !ctrlHeld)
			return false;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);

		if (Physics.Raycast(ray, out RaycastHit unitHit, 1000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide))
		{
			RtsUnitMember clickedUnit = unitHit.collider.GetComponentInParent<RtsUnitMember>();
			if (clickedUnit != null && clickedUnit.IsPlayerSelectable &&
			    !MissionPrepSquadSpawner.IsMissionPrepPresentationMember(clickedUnit))
				return false;
		}

		if (!Physics.Raycast(ray, out RaycastHit groundHit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return false;

		Vector3 lookPoint = groundHit.point;
		SpawnShortLivedClickMarker(lookPoint);

		bool isGroup = validUnits.Count >= 2;
		for (int i = 0; i < validUnits.Count; i++)
		{
			RtsUnitMember unit = validUnits[i];
			if (unit == null)
				continue;

			bool unitMoving = unit.HasActiveMovementIntent;
			bool withReady = shiftHeld || unitMoving;
			bool trackLookPoint = shiftHeld && unitMoving;
			float stagger = isGroup ? ResolveUnitGroupCommandStaggerDelay(unit) : 0f;
			unit.IssueGroundLookCommand(lookPoint, withReady, trackLookPoint, stagger);
		}

		return true;
	}

	private void SpawnShortLivedClickMarker(Vector3 _worldPoint)
	{
		GameObject marker = CreatePreviewDestinationMarker();
		if (marker == null)
			return;

		marker.name = "GroundClickMarker";
		marker.transform.position = _worldPoint + Vector3.up * 0.05f;

		Collider collider = marker.GetComponent<Collider>();
		if (collider != null)
			Destroy(collider);

		StartCoroutine(DestroyClickMarkerAfter(marker, m_ClickMarkerLifetimeSeconds));
	}

	private static IEnumerator DestroyClickMarkerAfter(GameObject _marker, float _seconds)
	{
		yield return new WaitForSeconds(Mathf.Max(0.1f, _seconds));
		if (_marker != null)
			Destroy(_marker);
	}

	private void HandleLeftMouseSelection()
	{
		if (Mouse.current == null || m_SelectionCamera == null)
			return;

		if (Mouse.current.leftButton.wasPressedThisFrame)
		{
			if (IsPointerOverWaitPointIcon(out int waitUnitIndex, out int waitWaypointIndex))
			{
				m_LeftMouseDownScreen = Mouse.current.position.ReadValue();
				m_IsDraggingSelection = false;
				m_LeftMouseStartedOverUi = true;
				CycleWaitPointIcon(waitUnitIndex, waitWaypointIndex);
				return;
			}

			if (IsPointerOverArrowDeleteButton())
			{
				m_LeftMouseDownScreen = Mouse.current.position.ReadValue();
				m_IsDraggingSelection = false;
				m_LeftMouseStartedOverUi = true;
				if (m_IsPreviewingMove)
					CancelMovePreview();
				RemoveHoveredFacingArrow();
				return;
			}

			if (IsAltHeld() && !IsPointerOverUi() && TryPlaceWaitPointFromRouteClick())
			{
				m_LeftMouseDownScreen = Mouse.current.position.ReadValue();
				m_IsDraggingSelection = false;
				m_LeftMouseStartedOverUi = true;
				return;
			}

			if (TryBeginRouteDragOnPress())
				return;
		}

		if (m_IsDraggingRoute)
		{
			if (Mouse.current.leftButton.wasReleasedThisFrame)
				EndRouteDrag();
			return;
		}

		if (m_IsRouteEditMode || m_IsEditingWaypointFacing)
			return;

		if (Mouse.current.leftButton.wasPressedThisFrame)
		{
			m_LeftMouseDownScreen = Mouse.current.position.ReadValue();
			m_IsDraggingSelection = false;
			m_LeftMouseStartedOverUi = IsPointerOverUi();
			return;
		}

		if (m_LeftMouseStartedOverUi)
		{
			if (Mouse.current.leftButton.wasReleasedThisFrame)
				m_LeftMouseStartedOverUi = false;
			m_IsDraggingSelection = false;
			return;
		}

		if (Mouse.current.leftButton.isPressed)
		{
			if (IsPointerOverUi())
			{
				m_IsDraggingSelection = false;
				return;
			}

			Vector2 current = Mouse.current.position.ReadValue();
			if ((current - m_LeftMouseDownScreen).sqrMagnitude >= m_BoxSelectionMinDragPixels * m_BoxSelectionMinDragPixels)
				m_IsDraggingSelection = true;
			return;
		}

		if (!Mouse.current.leftButton.wasReleasedThisFrame)
			return;

		if (IsPointerOverWaitPointIcon(out _, out _))
		{
			m_IsDraggingSelection = false;
			m_LeftMouseStartedOverUi = false;
			return;
		}

		if (IsPointerOverArrowDeleteButton())
		{
			m_IsDraggingSelection = false;
			m_LeftMouseStartedOverUi = false;
			return;
		}

		if (m_LeftMouseStartedOverUi || IsPointerOverUi())
		{
			m_IsDraggingSelection = false;
			m_LeftMouseStartedOverUi = false;
			return;
		}

		if (TryHandleLeftMouseGroundFacingCommand())
		{
			m_IsDraggingSelection = false;
			m_LeftMouseStartedOverUi = false;
			return;
		}

		bool ctrlPressed = IsCtrlPressed();
		if (m_IsDraggingSelection)
			HandleBoxSelection(ctrlPressed);
		else
			HandleSingleClickSelection(ctrlPressed);

		m_IsDraggingSelection = false;
		m_LeftMouseStartedOverUi = false;
	}

	private void HandleSingleClickSelection(bool _ctrlPressed)
	{
		Vector2 mousePosition = Mouse.current.position.ReadValue();
		FallenUnitInteractionMenuController menu = FallenUnitInteractionMenuController.Instance;
		if (menu != null && menu.IsVisible && menu.IsScreenPointOverMenu(mousePosition))
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);
		if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide))
		{
			FallenUnitInteractionMenuController.Instance?.HideImmediate();
			if (!_ctrlPressed)
				ClearSelection();
			return;
		}

		RtsUnitMember unit = hit.collider.GetComponentInParent<RtsUnitMember>();
		RtsUnitMember fallenTarget = ResolveFallenUnitFromRay(ray, hit);
		if (unit == null || !UnitFallenStateUtility.IsRtsControllable(unit))
		{
			if (fallenTarget != null)
				return;

			FallenUnitInteractionMenuController.Instance?.HideImmediate();
			if (!_ctrlPressed)
				ClearSelection();
			return;
		}

		FallenUnitInteractionMenuController.Instance?.HideImmediate();

		if (_ctrlPressed)
			ToggleUnitSelection(unit);
		else
			SetSelection(new List<RtsUnitMember> { unit });
	}

	private bool TryShowSelectedUnitFirstAidMenu(RtsUnitMember _unit, Vector2 _screenPosition)
	{
		if (_unit == null || m_SelectedUnits.Count != 1 || m_SelectedUnits[0] != _unit)
			return false;

		if (!TryGetControllablePlayerUnit(_unit, out RtsUnitMember controllable))
			return false;

		UnitHealth health = controllable.GetComponent<UnitHealth>();
		if (health == null || health.IsDead || !health.HasUnstabilizedInjuries)
			return false;

		UnitSelfStabilizationController selfStabilization = controllable.GetComponent<UnitSelfStabilizationController>();
		if (selfStabilization == null || !selfStabilization.CanRequestSelfStabilization())
			return false;

		FallenUnitInteractionMenuController.Instance.ShowFirstAidForUnit(controllable, _screenPosition);
		return true;
	}

	private bool TryShowFallenUnitInteractionMenu(Ray _ray, RaycastHit _primaryHit, Vector2 _screenPosition)
	{
		if (!TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember controllerUnit))
		{
			Debug.LogWarning($"[RtsUnitSelection] TryShowFallenMenu: no exactly-one controllable player unit (selectedCount={SelectedUnitCount})");
			return false;
		}

		RtsUnitMember targetUnit = ResolveFallenUnitFromRay(_ray, _primaryHit);
		if (targetUnit == null || targetUnit == controllerUnit)
			return false;

		if (MissionPrepSquadSpawner.IsMissionPrepPresentationMember(targetUnit))
			return false;

		FallenUnitInteractionMenuController.Instance.ShowForUnit(targetUnit, _screenPosition);
		return true;
	}

	private bool TryShowCarryReleaseMenu(RaycastHit _unitHit, Vector2 _screenPosition)
	{
		if (!TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember playerUnit))
			return false;

		UnitFiremanCarryController carryController = playerUnit.GetComponent<UnitFiremanCarryController>();
		if (carryController == null || !carryController.IsCarryingFallen)
			return false;

		RtsUnitMember clickedUnit = _unitHit.collider.GetComponentInParent<RtsUnitMember>();
		if (clickedUnit == null)
			return false;

		RtsUnitMember carriedVictim = carryController.CarriedVictim;
		if (clickedUnit != playerUnit && clickedUnit != carriedVictim)
			return false;

		FallenUnitInteractionMenuController.Instance.ShowReleaseForCarryingUnit(
			carriedVictim != null ? carriedVictim : playerUnit,
			_screenPosition);
		return true;
	}

	private bool TryGetExactlyOneControllablePlayerUnit(out RtsUnitMember _unit)
	{
		_unit = null;
		if (SelectedUnitCount != 1)
			return false;

		return TryGetControllablePlayerUnit(m_SelectedUnits[0], out _unit);
	}

	private RtsUnitMember ResolveFallenUnitFromRay(Ray _ray, RaycastHit _primaryHit)
	{
		RtsUnitMember primary = ResolveFallenUnitFromHit(_primaryHit);
		if (primary != null)
			return primary;

		RaycastHit[] hits = Physics.RaycastAll(_ray, 1000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide);
		System.Array.Sort(hits, (_a, _b) => _a.distance.CompareTo(_b.distance));
		for (int i = 0; i < hits.Length; i++)
		{
			RtsUnitMember fallen = ResolveFallenUnitFromHit(hits[i]);
			if (fallen != null)
				return fallen;
		}

		return null;
	}

	private bool TryGetControllablePlayerUnit(out RtsUnitMember _unit)
	{
		_unit = null;

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			if (TryGetControllablePlayerUnit(m_SelectedUnits[i], out _unit))
				return true;
		}

		IReadOnlyList<RtsUnitMember> instances = RtsUnitMember.Instances;
		for (int i = 0; i < instances.Count; i++)
		{
			RtsUnitMember candidate = instances[i];
			if (candidate == null || !candidate.IsSelected)
				continue;

			if (TryGetControllablePlayerUnit(candidate, out _unit))
				return true;
		}

		return TryGetControllablePlayerUnit(FindSoloConsciousPlayerUnit(), out _unit);
	}

	private static RtsUnitMember ResolveFallenUnitFromHit(RaycastHit _hit)
	{
		if (_hit.collider == null)
			return null;

		UnitConsciousness consciousness = _hit.collider.GetComponentInParent<UnitConsciousness>();
		if (consciousness != null && !consciousness.IsConscious)
		{
			RtsUnitMember member = consciousness.GetComponentInChildren<RtsUnitMember>(true);
			if (member != null)
				return member;
		}

		RtsUnitMember fromMember = _hit.collider.GetComponentInParent<RtsUnitMember>();
		if (fromMember != null && IsFallenUnit(fromMember))
			return fromMember;

		UnitRagdollController ragdoll = _hit.collider.GetComponentInParent<UnitRagdollController>();
		if (ragdoll != null && ragdoll.IsRagdollActive)
		{
			RtsUnitMember ragdollMember = ragdoll.GetComponentInChildren<RtsUnitMember>(true);
			if (ragdollMember != null)
				return ragdollMember;
		}

		return null;
	}

	private static bool TryGetControllablePlayerUnit(RtsUnitMember _unit, out RtsUnitMember _controllable)
	{
		_controllable = null;
		if (!UnitFallenStateUtility.IsRtsControllable(_unit))
			return false;

		_controllable = _unit;
		return true;
	}

	private static RtsUnitMember FindSoloConsciousPlayerUnit()
	{
		RtsUnitMember found = null;
		int count = 0;
		IReadOnlyList<RtsUnitMember> instances = RtsUnitMember.Instances;
		for (int i = 0; i < instances.Count; i++)
		{
			if (!TryGetControllablePlayerUnit(instances[i], out RtsUnitMember controllable))
				continue;

			count++;
			found = controllable;
			if (count > 1)
				return null;
		}

		return found;
	}

	private IEnumerator CoEnsurePlayerUnitSelectedAfterSpawn()
	{
		for (int frame = 0; frame < 5 && SelectedUnitCount == 0; frame++)
		{
			yield return null;
			TrySelectFirstPlayerUnit();
		}
	}

	private static bool IsFallenUnit(RtsUnitMember _unit)
	{
		return UnitFallenStateUtility.IsFallenOrDead(_unit);
	}

	private void HandleBoxSelection(bool _ctrlPressed)
	{
		Rect selectionRect = GetSelectionRect(m_LeftMouseDownScreen, Mouse.current.position.ReadValue());
		List<RtsUnitMember> inRect = new List<RtsUnitMember>(32);
		IReadOnlyList<RtsUnitMember> units = RtsUnitMember.Instances;
		for (int i = 0; i < units.Count; i++)
		{
			RtsUnitMember unit = units[i];
			if (unit == null || !UnitFallenStateUtility.IsRtsControllable(unit))
				continue;
			if (!unit.TryGetSelectionBounds(out Bounds bounds))
				continue;

			Vector3 screenPoint = m_SelectionCamera.WorldToScreenPoint(bounds.center);
			if (screenPoint.z < 0f)
				continue;

			Vector2 guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
			if (selectionRect.Contains(guiPoint))
				inRect.Add(unit);
		}

		if (_ctrlPressed)
		{
			for (int i = 0; i < inRect.Count; i++)
				ToggleUnitSelection(inRect[i], refreshAfterToggle: false);
			RefreshSelectionState();
			return;
		}

		SetSelection(inRect);
	}

	private bool IsMovePreviewBlockingPathInteractions()
	{
		return m_IsPreviewingMove &&
		       Mouse.current != null &&
		       Mouse.current.rightButton.isPressed;
	}

	private void UpdatePathInteractions()
	{
		if (m_IsEditingWaypointFacing)
		{
			ClearArrowHover();
			ClearRouteEditMode();
			ClearPathSegmentHover();
			return;
		}

		if (IsMovePreviewBlockingPathInteractions())
		{
			ClearAllPathInteractions();
			return;
		}

		if (m_SelectionCamera == null || m_SelectedUnits.Count == 0 || Mouse.current == null)
		{
			ClearAllPathInteractions();
			return;
		}

		if (IsPointerOverUi() && !m_IsDraggingRoute)
		{
			ClearAllPathInteractions();
			return;
		}

		if (m_IsDraggingRoute)
		{
			UpdateRouteDrag();
			return;
		}

		Vector2 mouseScreen = Mouse.current.position.ReadValue();

		if (TryPickRouteEditTarget(
			    mouseScreen,
			    out int routeUnitIndex,
			    out RouteEditTargetKind routeTargetKind,
			    out int routeSegmentIndex,
			    out int routeVertexIndex,
			    out Vector3 routeWorldPoint))
		{
			ClearArrowHover();

			if (m_IsRouteEditMode && m_HoveredUnitIndex == routeUnitIndex)
			{
				m_RouteEditTargetKind = routeTargetKind;
				m_RouteEditVertexIndex = routeVertexIndex;
				m_HoveredSegmentIndex = routeSegmentIndex;
				m_HoveredSegmentWorldPoint = routeWorldPoint;
				m_IsHoveringPathSegment = true;
				UpdateRouteEditHandle(routeWorldPoint);
				return;
			}

			if (m_IsRouteEditMode)
				ClearRouteEditMode();

			if (m_HoveredUnitIndex == routeUnitIndex &&
			    m_HoveredSegmentIndex == routeSegmentIndex &&
			    m_RouteEditTargetKind == routeTargetKind &&
			    m_RouteEditVertexIndex == routeVertexIndex)
			{
				m_HoveredSegmentWorldPoint = routeWorldPoint;
				if (Time.unscaledTime - m_PathHoverStartTime >= m_PathHoverDelay)
				{
					EnterRouteEditMode(
						routeUnitIndex,
						routeTargetKind,
						routeSegmentIndex,
						routeVertexIndex,
						routeWorldPoint);
				}
			}
			else
			{
				ClearPathSegmentHover();
				m_HoveredUnitIndex = routeUnitIndex;
				m_HoveredSegmentIndex = routeSegmentIndex;
				m_RouteEditTargetKind = routeTargetKind;
				m_RouteEditVertexIndex = routeVertexIndex;
				m_HoveredSegmentWorldPoint = routeWorldPoint;
				m_PathHoverStartTime = Time.unscaledTime;
			}

			return;
		}

		if (TryPickFacingArrow(mouseScreen, out int arrowUnitIndex, out RtsUnitMember.FacingArrowDescriptor facingArrow))
		{
			ClearRouteEditMode();
			ClearPathSegmentHover();
			UpdateArrowHover(arrowUnitIndex, facingArrow);
			return;
		}

		ClearArrowHover();

		if (m_IsRouteEditMode)
		{
			ClearRouteEditMode();
			ClearPathSegmentHover();
			return;
		}

		ClearPathSegmentHover();
	}

	private bool TryPickFacingArrow(
		Vector2 _mouseScreen,
		out int _unitIndex,
		out RtsUnitMember.FacingArrowDescriptor _descriptor)
	{
		_unitIndex = -1;
		_descriptor = default;
		float thresholdSqr = m_ArrowHoverThresholdPixels * m_ArrowHoverThresholdPixels;
		float bestDistSqr = thresholdSqr;

		for (int unitIndex = 0; unitIndex < m_SelectedUnits.Count; unitIndex++)
		{
			RtsUnitMember unit = m_SelectedUnits[unitIndex];
			if (unit == null)
				continue;

			m_FacingArrowPickBuffer.Clear();
			unit.CollectFacingArrowDescriptors(m_FacingArrowPickBuffer);
			for (int i = 0; i < m_FacingArrowPickBuffer.Count; i++)
			{
				RtsUnitMember.FacingArrowDescriptor descriptor = m_FacingArrowPickBuffer[i];
				RtsUnitMember.GetFacingArrowShaftEndpoints(
					descriptor.Anchor,
					descriptor.Angle,
					descriptor.Mode,
					descriptor.LookPoint,
					descriptor.HasLookPoint,
					out Vector3 shaftStart,
					out Vector3 shaftEnd);
				Vector2 startScreen = m_SelectionCamera.WorldToScreenPoint(shaftStart);
				Vector2 endScreen = m_SelectionCamera.WorldToScreenPoint(shaftEnd);
				float distSqr = DistPointToSegmentSqr(_mouseScreen, startScreen, endScreen, out _, out _);
				if (distSqr < bestDistSqr)
				{
					bestDistSqr = distSqr;
					_unitIndex = unitIndex;
					_descriptor = descriptor;
				}
			}
		}

		return _unitIndex >= 0;
	}

	private bool TryPickRouteEditTarget(
		Vector2 _mouseScreen,
		out int _unitIndex,
		out RouteEditTargetKind _targetKind,
		out int _segmentIndex,
		out int _vertexIndex,
		out Vector3 _worldPoint)
	{
		_unitIndex = -1;
		_targetKind = RouteEditTargetKind.SegmentPoint;
		_segmentIndex = -1;
		_vertexIndex = -1;
		_worldPoint = Vector3.zero;

		if (TryPickRouteVertex(_mouseScreen, out _unitIndex, out _vertexIndex, out _worldPoint))
		{
			_targetKind = RouteEditTargetKind.WaypointVertex;
			_segmentIndex = _vertexIndex;
			return true;
		}

		if (!TryPickRouteSegment(_mouseScreen, out _unitIndex, out _segmentIndex, out _worldPoint))
			return false;

		_targetKind = RouteEditTargetKind.SegmentPoint;
		_vertexIndex = -1;
		return true;
	}

	private bool TryPickRouteVertex(
		Vector2 _mouseScreen,
		out int _unitIndex,
		out int _vertexIndex,
		out Vector3 _worldPoint)
	{
		_unitIndex = -1;
		_vertexIndex = -1;
		_worldPoint = Vector3.zero;

		float bestDistSqr = float.MaxValue;

		for (int unitIndex = 0; unitIndex < m_SelectedUnits.Count; unitIndex++)
		{
			RtsUnitMember unit = m_SelectedUnits[unitIndex];
			if (unit == null || unit.WaypointCount == 0)
				continue;

			for (int waypointIndex = 0; waypointIndex < unit.WaypointCount; waypointIndex++)
			{
				Vector3 waypointWorld = unit.GetWaypointWorld(waypointIndex);
				Vector2 waypointScreen = m_SelectionCamera.WorldToScreenPoint(waypointWorld);
				RouteVertexRole role = ResolveRouteVertexRole(unit, waypointIndex);
				float snapRadius = m_RouteVertexSnapPixels * role switch
				{
					RouteVertexRole.End => 1.25f,
					RouteVertexRole.Corner => 1.15f,
					_ => 1f,
				};
				float snapRadiusSqr = snapRadius * snapRadius;
				float distSqr = (_mouseScreen - waypointScreen).sqrMagnitude;
				if (distSqr >= snapRadiusSqr || distSqr >= bestDistSqr)
					continue;

				bestDistSqr = distSqr;
				_unitIndex = unitIndex;
				_vertexIndex = waypointIndex;
				_worldPoint = waypointWorld;
			}
		}

		return _unitIndex >= 0;
	}

	private bool TryPickRouteSegment(
		Vector2 _mouseScreen,
		out int _unitIndex,
		out int _segmentIndex,
		out Vector3 _worldPoint)
	{
		_unitIndex = -1;
		_segmentIndex = -1;
		_worldPoint = Vector3.zero;

		float thresholdSqr = m_PathHoverThresholdPixels * m_PathHoverThresholdPixels;
		float bestDistSqr = thresholdSqr;

		bool hasMouseWorld = false;
		Vector3 mouseWorld = Vector3.zero;
		Ray mouseRay = m_SelectionCamera.ScreenPointToRay(_mouseScreen);
		if (Physics.Raycast(mouseRay, out RaycastHit mouseHit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
		{
			mouseWorld = mouseHit.point;
			hasMouseWorld = true;
		}

		for (int unitIndex = 0; unitIndex < m_SelectedUnits.Count; unitIndex++)
		{
			RtsUnitMember unit = m_SelectedUnits[unitIndex];
			if (unit == null || unit.WaypointCount == 0)
				continue;

			Vector3 unitWorldPos = unit.transform.position;
			Vector3 waypointZero = unit.GetWaypointWorld(0);
			Vector2 unitScreen = m_SelectionCamera.WorldToScreenPoint(unitWorldPos);
			Vector2 waypointZeroScreen = m_SelectionCamera.WorldToScreenPoint(waypointZero);
			float distSqr = DistPointToSegmentSqr(_mouseScreen, unitScreen, waypointZeroScreen, out _, out float segmentT);
			if (distSqr < bestDistSqr)
			{
				bestDistSqr = distSqr;
				_unitIndex = unitIndex;
				_segmentIndex = 0;
				_worldPoint = hasMouseWorld
					? ClosestPointOnLineSegment(mouseWorld, unitWorldPos, waypointZero)
					: Vector3.Lerp(unitWorldPos, waypointZero, segmentT);
			}

			for (int waypointIndex = 1; waypointIndex < unit.WaypointCount; waypointIndex++)
			{
				Vector3 segmentStart = unit.GetWaypointWorld(waypointIndex - 1);
				Vector3 segmentEnd = unit.GetWaypointWorld(waypointIndex);
				Vector2 startScreen = m_SelectionCamera.WorldToScreenPoint(segmentStart);
				Vector2 endScreen = m_SelectionCamera.WorldToScreenPoint(segmentEnd);
				distSqr = DistPointToSegmentSqr(_mouseScreen, startScreen, endScreen, out _, out segmentT);
				if (distSqr < bestDistSqr)
				{
					bestDistSqr = distSqr;
					_unitIndex = unitIndex;
					_segmentIndex = waypointIndex;
					_worldPoint = hasMouseWorld
						? ClosestPointOnLineSegment(mouseWorld, segmentStart, segmentEnd)
						: Vector3.Lerp(segmentStart, segmentEnd, segmentT);
				}
			}
		}

		return _unitIndex >= 0;
	}

	private void UpdateArrowHover(int _unitIndex, RtsUnitMember.FacingArrowDescriptor _descriptor)
	{
		if (m_HoveredArrowUnitIndex == _unitIndex &&
		    m_HoveredFacingArrow.SegmentIndex == _descriptor.SegmentIndex &&
		    m_HoveredFacingArrow.ArrowIndex == _descriptor.ArrowIndex &&
		    m_HoveredFacingArrow.IsActiveSegment == _descriptor.IsActiveSegment)
		{
			if (Time.unscaledTime - m_ArrowHoverStartTime >= m_PathHoverDelay)
				ShowArrowDeleteButton(_unitIndex, _descriptor);
			return;
		}

		ClearArrowHover();
		m_HoveredArrowUnitIndex = _unitIndex;
		m_HoveredFacingArrow = _descriptor;
		m_ArrowHoverStartTime = Time.unscaledTime;
	}

	private void ShowArrowDeleteButton(int _unitIndex, RtsUnitMember.FacingArrowDescriptor _descriptor)
	{
		m_IsArrowDeleteButtonVisible = true;

		RtsUnitMember.GetFacingArrowShaftEndpoints(
			_descriptor.Anchor,
			_descriptor.Angle,
			_descriptor.Mode,
			_descriptor.LookPoint,
			_descriptor.HasLookPoint,
			out _,
			out Vector3 arrowTip);
		Vector3 screenPoint = m_SelectionCamera.WorldToScreenPoint(arrowTip);
		float buttonSize = m_ArrowDeleteButtonSize;
		m_ArrowDeleteButtonScreenRect = new Rect(
			screenPoint.x - buttonSize * 0.5f,
			Screen.height - screenPoint.y + 6f,
			buttonSize,
			buttonSize);
	}

	private void ClearArrowHover()
	{
		m_HoveredArrowUnitIndex = -1;
		m_HoveredFacingArrow = default;
		m_ArrowHoverStartTime = 0f;
		m_IsArrowDeleteButtonVisible = false;
	}

	private void EnterRouteEditMode(
		int _unitIndex,
		RouteEditTargetKind _targetKind,
		int _segmentIndex,
		int _vertexIndex,
		Vector3 _worldPoint)
	{
		m_IsRouteEditMode = true;
		m_IsHoveringPathSegment = true;
		m_HoveredUnitIndex = _unitIndex;
		m_HoveredSegmentIndex = _segmentIndex;
		m_RouteEditTargetKind = _targetKind;
		m_RouteEditVertexIndex = _vertexIndex;
		m_HoveredSegmentWorldPoint = _worldPoint;
		m_RouteEditWaypointIndex = -1;
		EnsureRouteEditHandle();
		UpdateRouteEditHandle(_worldPoint);
	}

	private void EnsureRouteEditHandle()
	{
		if (m_RouteEditHandle != null)
			return;

		m_RouteEditHandle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		m_RouteEditHandle.name = "RouteEditHandle";
		Collider handleCollider = m_RouteEditHandle.GetComponent<Collider>();
		if (handleCollider != null)
			Destroy(handleCollider);

		if (m_RouteEditHandle.TryGetComponent<Renderer>(out Renderer renderer))
		{
			renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
			renderer.sharedMaterial.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
		}

		m_RouteEditHandle.transform.localScale = Vector3.one * m_RouteEditHandleSize;
	}

	private void UpdateRouteEditHandle(Vector3 _worldPoint)
	{
		if (m_RouteEditHandle == null)
			return;

		Vector3 handlePoint = _worldPoint;
		handlePoint.y += 0.08f;
		m_RouteEditHandle.transform.position = handlePoint;
		m_RouteEditHandle.SetActive(true);
		m_HoveredSegmentWorldPoint = _worldPoint;

		RtsUnitMember unit = null;
		if (m_HoveredUnitIndex >= 0 && m_HoveredUnitIndex < m_SelectedUnits.Count)
			unit = m_SelectedUnits[m_HoveredUnitIndex];

		ApplyRouteEditHandleVisual();
	}

	private void ApplyRouteEditHandleVisual()
	{
		if (m_RouteEditHandle == null)
			return;

		m_RouteEditHandle.transform.localScale = Vector3.one * m_RouteEditHandleSize;

		if (m_RouteEditHandle.TryGetComponent<Renderer>(out Renderer renderer) && renderer.sharedMaterial != null)
			renderer.sharedMaterial.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
	}

	private static RouteVertexRole ResolveRouteVertexRole(RtsUnitMember _unit, int _vertexIndex)
	{
		if (_unit == null || _vertexIndex < 0)
			return RouteVertexRole.First;

		int waypointCount = _unit.WaypointCount;
		if (_vertexIndex >= waypointCount - 1)
			return RouteVertexRole.End;
		if (_vertexIndex > 0)
			return RouteVertexRole.Corner;
		return RouteVertexRole.First;
	}

	private void ClearRouteEditMode()
	{
		m_IsRouteEditMode = false;
		m_IsDraggingRoute = false;
		m_RouteEditWaypointIndex = -1;
		m_RouteEditTargetKind = RouteEditTargetKind.SegmentPoint;
		m_RouteEditVertexIndex = -1;

		if (m_RouteEditHandle != null)
			m_RouteEditHandle.SetActive(false);
	}

	private void ClearPathSegmentHover()
	{
		m_IsHoveringPathSegment = false;
		m_HoveredUnitIndex = -1;
		m_HoveredSegmentIndex = -1;
		m_RouteEditTargetKind = RouteEditTargetKind.SegmentPoint;
		m_RouteEditVertexIndex = -1;
		m_PathHoverStartTime = 0f;
	}

	private void ClearAllPathInteractions()
	{
		CancelWaypointFacingEdit();
		ClearArrowHover();
		ClearRouteEditMode();
		ClearPathSegmentHover();
	}

	/// <summary>Сбрасывает зависшее редактирование маршрута (ПКМ-стрелка, move preview) без применения facing.</summary>
	public void CancelRouteEditInputState()
	{
		CancelWaypointFacingEdit();
		if (m_IsPreviewingMove || m_PreviewPending || m_IsAwaitingDoubleClick)
			CancelMovePreview();
		ClearAllPathInteractions();
	}

	private static GameObject CreateFacingDirectionMarker(Color _color)
	{
		GameObject arrowGo = new GameObject("FacingLine");
		LineRenderer lr = arrowGo.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.useWorldSpace = true;
		lr.startWidth = 0.03f;
		lr.endWidth = 0.03f;
		lr.material = new Material(Shader.Find("Sprites/Default"));
		lr.startColor = _color;
		lr.endColor = _color;
		return arrowGo;
	}

	private void CancelWaypointFacingEdit()
	{
		if (!m_IsEditingWaypointFacing)
			return;

		m_IsEditingWaypointFacing = false;
		m_EditingUnitIndex = -1;
		m_EditingSegmentIndex = -1;
		m_PreviewFacingAngles = null;

		for (int i = 0; i < m_DirectionMarkers.Count; i++)
		{
			if (m_DirectionMarkers[i] != null)
				Destroy(m_DirectionMarkers[i]);
		}

		m_DirectionMarkers.Clear();
	}

	private bool IsPointerOverArrowDeleteButton()
	{
		if (!m_IsArrowDeleteButtonVisible || Mouse.current == null)
			return false;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		Vector2 guiMouse = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
		return m_ArrowDeleteButtonScreenRect.Contains(guiMouse);
	}

	private void RemoveHoveredFacingArrow()
	{
		if (m_HoveredArrowUnitIndex < 0 ||
		    m_HoveredArrowUnitIndex >= m_SelectedUnits.Count)
		{
			ClearArrowHover();
			return;
		}

		RtsUnitMember unit = m_SelectedUnits[m_HoveredArrowUnitIndex];
		if (unit != null)
		{
			unit.TryRemoveFacingArrow(
				m_HoveredFacingArrow.SegmentIndex,
				m_HoveredFacingArrow.ArrowIndex);
		}

		ClearArrowHover();
	}

	private bool TryBeginRouteDragOnPress()
	{
		if (!m_IsRouteEditMode || Mouse.current == null || m_SelectionCamera == null)
			return false;
		if (m_HoveredUnitIndex < 0 || m_HoveredUnitIndex >= m_SelectedUnits.Count)
			return false;
		if (!IsPointerOverRouteHandle())
			return false;

		if (m_IsPreviewingMove)
			CancelMovePreview();

		RtsUnitMember unit = m_SelectedUnits[m_HoveredUnitIndex];
		if (unit == null)
			return false;

		if (m_RouteEditWaypointIndex < 0)
		{
			if (m_RouteEditTargetKind == RouteEditTargetKind.WaypointVertex && m_RouteEditVertexIndex >= 0)
			{
				m_RouteEditWaypointIndex = m_RouteEditVertexIndex;
			}
			else if (!unit.TryInsertRouteWaypointAtSegment(m_HoveredSegmentIndex, m_HoveredSegmentWorldPoint))
			{
				return false;
			}
			else
			{
				m_RouteEditWaypointIndex = m_HoveredSegmentIndex;
			}
		}

		m_IsDraggingRoute = true;
		UpdateRouteDrag();
		return true;
	}

	private bool IsPointerOverRouteHandle()
	{
		if (m_RouteEditHandle == null || !m_RouteEditHandle.activeSelf || Mouse.current == null)
			return false;

		Vector2 mouseScreen = Mouse.current.position.ReadValue();
		Vector3 handleScreen = m_SelectionCamera.WorldToScreenPoint(m_RouteEditHandle.transform.position);
		float hitRadius = m_RouteEditHandleHitPixels;
		Vector2 delta = mouseScreen - new Vector2(handleScreen.x, handleScreen.y);
		return delta.sqrMagnitude <= hitRadius * hitRadius;
	}

	private void UpdateRouteDrag()
	{
		if (!m_IsDraggingRoute || Mouse.current == null || m_SelectionCamera == null)
			return;
		if (m_HoveredUnitIndex < 0 || m_HoveredUnitIndex >= m_SelectedUnits.Count)
			return;
		if (m_RouteEditWaypointIndex < 0)
			return;

		RtsUnitMember unit = m_SelectedUnits[m_HoveredUnitIndex];
		if (unit == null)
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		unit.UpdateRouteEditWaypoint(m_RouteEditWaypointIndex, hit.point);
		m_RouteEditTargetKind = RouteEditTargetKind.WaypointVertex;
		m_RouteEditVertexIndex = m_RouteEditWaypointIndex;
		UpdateRouteEditHandle(unit.GetWaypointWorld(m_RouteEditWaypointIndex));
	}

	private void EndRouteDrag()
	{
		int editedWaypointIndex = m_RouteEditWaypointIndex;
		m_IsDraggingRoute = false;
		m_RouteEditWaypointIndex = -1;

		if (editedWaypointIndex < 0 ||
		    m_HoveredUnitIndex < 0 ||
		    m_HoveredUnitIndex >= m_SelectedUnits.Count)
			return;

		RtsUnitMember unit = m_SelectedUnits[m_HoveredUnitIndex];
		if (unit != null && editedWaypointIndex < unit.WaypointCount)
		{
			m_RouteEditTargetKind = RouteEditTargetKind.WaypointVertex;
			m_RouteEditVertexIndex = editedWaypointIndex;
			UpdateRouteEditHandle(unit.GetWaypointWorld(editedWaypointIndex));
		}
	}

	private void DrawArrowDeleteButtonIfAny()
	{
		if (!m_IsArrowDeleteButtonVisible)
			return;

		if (s_ArrowDeleteButtonGuiStyle == null)
		{
			s_ArrowDeleteButtonGuiStyle = new GUIStyle(GUI.skin.button)
			{
				fontSize = 14,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter
			};
			s_ArrowDeleteButtonGuiStyle.normal.textColor = Color.white;
		}

		if (GUI.Button(m_ArrowDeleteButtonScreenRect, "X", s_ArrowDeleteButtonGuiStyle))
		{
			RemoveHoveredFacingArrow();
			m_LeftMouseStartedOverUi = true;
		}
	}

	private Rect BuildWaitPointIconScreenRect(Vector3 _worldPosition, float _iconSize, out bool _isVisible)
	{
		_isVisible = false;
		if (m_SelectionCamera == null)
			return default;

		Vector3 iconWorld = _worldPosition + Vector3.up * c_WaitPointIconWorldYOffset;
		Vector3 screenPoint = m_SelectionCamera.WorldToScreenPoint(iconWorld);
		if (screenPoint.z <= 0f)
			return default;

		_isVisible = true;
		return new Rect(
			screenPoint.x - _iconSize * 0.5f,
			Screen.height - screenPoint.y - _iconSize * 0.5f + m_WaitPointIconScreenOffsetY,
			_iconSize,
			_iconSize);
	}

	private void DrawWaitPointIconsIfAny()
	{
		m_WaitPointIconScreenRects.Clear();
		m_WaitPointIconUnitIndices.Clear();
		m_WaitPointIconWaypointIndices.Clear();

		if (m_SelectionCamera == null || m_SelectedUnits.Count == 0)
			return;

		if (s_WaitPointIconGuiStyle == null)
		{
			s_WaitPointIconGuiStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 13,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter
			};
			s_WaitPointIconGuiStyle.normal.textColor = Color.white;
		}

		float iconSize = m_WaitPointIconSize;
		for (int unitIndex = 0; unitIndex < m_SelectedUnits.Count; unitIndex++)
		{
			RtsUnitMember unit = m_SelectedUnits[unitIndex];
			if (unit == null)
				continue;

			m_WaitPointPickBuffer.Clear();
			unit.CollectWaitPointDescriptors(m_WaitPointPickBuffer);
			for (int i = 0; i < m_WaitPointPickBuffer.Count; i++)
			{
				RtsUnitMember.WaitPointDescriptor descriptor = m_WaitPointPickBuffer[i];
				Rect iconRect = BuildWaitPointIconScreenRect(descriptor.WorldPosition, iconSize, out bool isVisible);
				if (!isVisible)
					continue;

				m_WaitPointIconScreenRects.Add(iconRect);
				m_WaitPointIconUnitIndices.Add(unitIndex);
				m_WaitPointIconWaypointIndices.Add(descriptor.WaypointIndex);

				Color previousColor = GUI.backgroundColor;
				GUI.backgroundColor = new Color(0.85f, 0.55f, 0.15f, 0.95f);
				GUI.Box(iconRect, descriptor.WaitGroup.ToString(), s_WaitPointIconGuiStyle);
				GUI.backgroundColor = previousColor;
			}
		}
	}

	private bool IsPointerOverWaitPointIcon(out int _unitIndex, out int _waypointIndex)
	{
		_unitIndex = -1;
		_waypointIndex = -1;
		if (Mouse.current == null || m_SelectionCamera == null || m_SelectedUnits.Count == 0)
			return false;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		Vector2 guiMouse = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
		float iconSize = m_WaitPointIconSize;
		float bestDistSqr = float.MaxValue;

		for (int unitIndex = 0; unitIndex < m_SelectedUnits.Count; unitIndex++)
		{
			RtsUnitMember unit = m_SelectedUnits[unitIndex];
			if (unit == null)
				continue;

			m_WaitPointPickBuffer.Clear();
			unit.CollectWaitPointDescriptors(m_WaitPointPickBuffer);
			for (int i = 0; i < m_WaitPointPickBuffer.Count; i++)
			{
				RtsUnitMember.WaitPointDescriptor descriptor = m_WaitPointPickBuffer[i];
				Rect iconRect = BuildWaitPointIconScreenRect(descriptor.WorldPosition, iconSize, out bool isVisible);
				if (!isVisible)
					continue;

				if (!iconRect.Contains(guiMouse))
					continue;

				float distSqr = (guiMouse - iconRect.center).sqrMagnitude;
				if (distSqr >= bestDistSqr)
					continue;

				bestDistSqr = distSqr;
				_unitIndex = unitIndex;
				_waypointIndex = descriptor.WaypointIndex;
			}
		}

		return _unitIndex >= 0;
	}

	private void CycleWaitPointIcon(int _unitIndex, int _waypointIndex)
	{
		if (_unitIndex < 0 || _unitIndex >= m_SelectedUnits.Count)
			return;

		RtsUnitMember unit = m_SelectedUnits[_unitIndex];
		unit?.TryCycleWaitGroupForWaypoint(_waypointIndex);
	}

	private void RemoveWaitPointIcon(int _unitIndex, int _waypointIndex)
	{
		if (_unitIndex < 0 || _unitIndex >= m_SelectedUnits.Count)
			return;

		RtsUnitMember unit = m_SelectedUnits[_unitIndex];
		unit?.TryRemoveWaitPointAtWaypoint(_waypointIndex);
	}

	private bool TryPlaceWaitPointFromRouteClick()
	{
		if (Mouse.current == null || m_SelectionCamera == null || m_SelectedUnits.Count == 0)
			return false;

		if (m_IsHoveringPathSegment && m_HoveredUnitIndex >= 0)
			return TryPlaceWaitPointForRouteTarget(
				m_HoveredUnitIndex,
				m_RouteEditTargetKind,
				m_HoveredSegmentIndex,
				m_RouteEditVertexIndex,
				m_HoveredSegmentWorldPoint);

		Vector2 mouseScreen = Mouse.current.position.ReadValue();

		if (TryPickRouteVertex(mouseScreen, out int routeUnitIndex, out int routeVertexIndex, out _))
		{
			if (routeUnitIndex < 0 || routeUnitIndex >= m_SelectedUnits.Count)
				return false;

			RtsUnitMember unit = m_SelectedUnits[routeUnitIndex];
			if (unit == null || routeVertexIndex < 0)
				return false;

			return unit.TrySetWaitGroupForWaypoint(routeVertexIndex, unit.GetNextAutoWaitGroup());
		}

		if (TryPickRouteSegment(mouseScreen, out routeUnitIndex, out int routeSegmentIndex, out Vector3 routeWorldPoint))
		{
			if (routeUnitIndex < 0 || routeUnitIndex >= m_SelectedUnits.Count)
				return false;

			RtsUnitMember unit = m_SelectedUnits[routeUnitIndex];
			if (unit == null)
				return false;

			return unit.TryInsertRouteWaypointAtSegment(
				routeSegmentIndex,
				routeWorldPoint,
				unit.GetNextAutoWaitGroup());
		}

		return false;
	}

	private bool TryPlaceWaitPointForRouteTarget(
		int _unitIndex,
		RouteEditTargetKind _targetKind,
		int _segmentIndex,
		int _vertexIndex,
		Vector3 _worldPoint)
	{
		if (_unitIndex < 0 || _unitIndex >= m_SelectedUnits.Count)
			return false;

		RtsUnitMember unit = m_SelectedUnits[_unitIndex];
		if (unit == null)
			return false;

		if (_targetKind == RouteEditTargetKind.WaypointVertex)
			return _vertexIndex >= 0 &&
			       unit.TrySetWaitGroupForWaypoint(_vertexIndex, unit.GetNextAutoWaitGroup());

		return unit.TryInsertRouteWaypointAtSegment(
			_segmentIndex,
			_worldPoint,
			unit.GetNextAutoWaitGroup());
	}

	private void ContinueSelectedRouteWaitGroup(int _waitGroup)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (RouteMovementDebug.LoggingEnabled)
			Debug.Log($"[RouteDbg:Manager] Continue wait group {_waitGroup} for all player units");
#endif
		IReadOnlyList<RtsUnitMember> instances = RtsUnitMember.Instances;
		for (int i = 0; i < instances.Count; i++)
		{
			RtsUnitMember unit = instances[i];
			if (unit == null || !unit.IsPlayerSelectable)
				continue;

			unit.TryContinueRouteWaitGroup(_waitGroup);
		}
	}

	private void UpdatePathHover()
	{
		UpdatePathInteractions();
	}

	private static float DistPointToSegmentSqr(Vector2 _p, Vector2 _a, Vector2 _b, out Vector2 _closest, out float _t)
	{
		Vector2 ab = _b - _a;
		float lenSqr = ab.sqrMagnitude;
		if (lenSqr < 0.0001f)
		{
			_closest = _a;
			_t = 0f;
			return (_p - _a).sqrMagnitude;
		}

		float t = Mathf.Clamp01(Vector2.Dot(_p - _a, ab) / lenSqr);
		_closest = _a + t * ab;
		_t = t;
		return (_p - _closest).sqrMagnitude;
	}

	private static Vector3 ClosestPointOnLineSegment(Vector3 _p, Vector3 _a, Vector3 _b)
	{
		Vector3 ab = _b - _a;
		float abSqr = ab.sqrMagnitude;
		if (abSqr < 1e-9f)
			return _a;

		float t = Mathf.Clamp01(Vector3.Dot(_p - _a, ab) / abSqr);
		return _a + ab * t;
	}

	private static Color GetFacingArrowColor(RtsUnitMember.FacingArrowMode _mode)
	{
		return _mode switch
		{
			RtsUnitMember.FacingArrowMode.HoldToEnd => new Color(0.2f, 0.7f, 1f, 0.95f),
			RtsUnitMember.FacingArrowMode.LookAtPoint => new Color(0.3f, 0.95f, 0.3f, 0.95f),
			_ => new Color(1f, 0.85f, 0.2f, 0.95f),
		};
	}

	private void HandleRightMouseCommand()
	{
		if (Mouse.current == null || m_SelectionCamera == null)
			return;

		if (m_IsEditingWaypointFacing)
		{
			if (!Mouse.current.rightButton.isPressed)
				EndWaypointFacingEdit();
			else if (m_SelectedUnits.Count > 0)
				UpdateWaypointFacingEdit();
			return;
		}

		if (Mouse.current.rightButton.wasReleasedThisFrame && m_IsPreviewingMove)
		{
			HandleRightMouseUp();
			return;
		}

		if (m_SelectedUnits.Count == 0)
		{
			if (m_IsPreviewingMove || m_PreviewPending || m_IsAwaitingDoubleClick)
				CancelMovePreview();
			return;
		}

		if (IsPointerOverUi())
			return;

		bool wasPressed = Mouse.current.rightButton.wasPressedThisFrame;
		bool wasReleased = Mouse.current.rightButton.wasReleasedThisFrame;

		if (wasPressed)
			HandleRightMouseDown();

		if (wasReleased)
			HandleRightMouseUp();

		if (m_IsPreviewingMove && Mouse.current.rightButton.isPressed)
		{
			bool handledFormationScroll = HandleFormationScroll();

			if (TryYieldMovePreviewToPathInteraction())
				return;

			if (IsPathInteractionBlockingMovePreview() && !handledFormationScroll)
				return;

			if (CanPreviewMoveFacing())
			{
				Vector2 mousePos = Mouse.current.position.ReadValue();
				float facingDragThreshold = GetMoveFacingDragThresholdPixels();
				if (!m_IsQuickRotateFacing &&
				    (mousePos - m_RmbDownMousePos).magnitude >= facingDragThreshold)
				{
					EnterQuickRotateMode();
				}

				if (m_IsQuickRotateFacing)
				{
					UpdateQuickRotateMode();
					return;
				}
			}

			if (m_IsInPlaceFacingPreview && GetValidSelectedUnits().Count == 1)
				return;

			if (m_IsQuickRotateFacing)
				ExitMoveFacingMode();

			UpdateMovePreview();
		}
	}

	private bool IsPathInteractionBlockingMovePreview()
	{
		if (m_IsDraggingRoute || m_IsRouteEditMode)
			return true;
		if (m_HoveredArrowUnitIndex >= 0)
			return true;
		if (m_IsHoveringPathSegment)
			return true;
		return false;
	}

	private bool TryYieldMovePreviewToPathInteraction()
	{
		if (!m_IsPreviewingMove || m_IsQuickRotateFacing || IsAltHeld())
			return false;

		if (m_IsHoveringPathSegment &&
		    m_HoveredUnitIndex >= 0 &&
		    Time.unscaledTime - m_PathHoverStartTime >= m_PathHoverDelay)
		{
			int unitIndex = m_HoveredUnitIndex;
			int segmentIndex = m_HoveredSegmentIndex;
			CancelMovePreview();
			BeginWaypointFacingEdit(unitIndex, segmentIndex);
			return true;
		}

		return false;
	}

	private static bool IsShiftHeld()
	{
		return Keyboard.current != null &&
		       (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
	}

	private static bool IsAltHeld()
	{
		return Keyboard.current != null &&
		       (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
	}


	private void HandleRightMouseDown()
	{
		Vector2 mousePosition = Mouse.current.position.ReadValue();
		FallenUnitInteractionMenuController menu = FallenUnitInteractionMenuController.Instance;
		if (menu != null && menu.IsVisible && menu.IsScreenPointOverMenu(mousePosition))
			return;

		if (IsPointerOverWaitPointIcon(out int waitUnitIndex, out int waitWaypointIndex))
		{
			RemoveWaitPointIcon(waitUnitIndex, waitWaypointIndex);
			return;
		}

		if (m_IsAwaitingDoubleClick)
		{
			BeginRunPreviewAfterDoubleClick(mousePosition);
			return;
		}

		if (m_IsDraggingRoute)
			return;

		if (m_HoveredArrowUnitIndex >= 0)
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);
		bool hasUnitHit = TryRaycastAnyUnit(ray, out RaycastHit unitHit);
		RtsUnitMember clickedUnit = hasUnitHit
			? unitHit.collider.GetComponentInParent<RtsUnitMember>()
			: null;
		bool clickedSelectedUnit = clickedUnit != null && m_SelectedUnits.Contains(clickedUnit);

		if (m_IsHoveringPathSegment && m_SelectedUnits.Count > 0 && !IsAltHeld() && !clickedSelectedUnit)
		{
			BeginWaypointFacingEdit(m_HoveredUnitIndex, m_HoveredSegmentIndex);
			return;
		}

		if (m_IsRouteEditMode)
		{
			return;
		}

		Vector3? unitForcedGroundPoint = null;

		if (hasUnitHit)
		{
			if (TryShowCarryReleaseMenu(unitHit, mousePosition))
				return;

			if (TryShowFallenUnitInteractionMenu(ray, unitHit, mousePosition))
				return;

			if (TryShowSelectedUnitFirstAidMenu(clickedUnit, mousePosition))
				return;

			if (clickedUnit != null && m_SelectedUnits.Contains(clickedUnit))
			{
				List<RtsUnitMember> unitsForPoint = GetValidSelectedUnits();
				if (unitsForPoint.Count >= 2)
				{
					Vector3 avg = Vector3.zero;
					for (int i = 0; i < unitsForPoint.Count; i++)
						avg += unitsForPoint[i].transform.position;
					avg /= unitsForPoint.Count;
					avg.y = 0f;
					unitForcedGroundPoint = avg;
				}
				else
				{
					Vector3 pos = clickedUnit.transform.position;
					pos.y = 0f;
					unitForcedGroundPoint = pos;
				}
			}
			else
			{
				return;
			}
		}

		FallenUnitInteractionMenuController.Instance?.HideImmediate();

		Vector3 hitPoint;
		bool hasGroundHit = Physics.Raycast(
			ray,
			out RaycastHit hit,
			2000f,
			m_CommandGroundMask,
			QueryTriggerInteraction.Ignore);
		if (unitForcedGroundPoint.HasValue)
			hitPoint = unitForcedGroundPoint.Value;
		else if (!hasGroundHit)
		{
			return;
		}
		else
			hitPoint = hit.point;

		StartMovePreview(
			hitPoint,
			mousePosition,
			unitForcedGroundPoint.HasValue,
			UnitClickToMove.MoveTier.Walk);
	}

	private void BeginRunPreviewAfterDoubleClick(Vector2 _mouseDownScreen)
	{
		StopPendingWalkCoroutine();
		m_IsAwaitingDoubleClick = false;
		m_PreviewMoveTier = UnitClickToMove.MoveTier.Run;
		m_IsPreviewingMove = true;
		m_PreviewCancelled = false;
		m_HasMoveFacingSet = false;
		m_IsQuickRotateFacing = false;
		m_RmbDownMousePos = _mouseDownScreen;
		m_LastRightClickTime = -1f;

		BeginMovePreviewForUnits();
		ApplyPreviewPathLines();
	}

	private void BeginAwaitingDoubleClickForRun()
	{
		m_IsAwaitingDoubleClick = true;
		StopPendingWalkCoroutine();
		m_PendingWalkCoroutine = StartCoroutine(PendingWalkAfterDelay());
	}

	private IEnumerator PendingWalkAfterDelay()
	{
		yield return new WaitForSecondsRealtime(m_DoubleRightClickSeconds);
		m_PendingWalkCoroutine = null;

		if (!m_IsAwaitingDoubleClick)
			yield break;

		m_IsAwaitingDoubleClick = false;
		CommitMovePreviewOrder();
	}

	private void StopPendingWalkCoroutine()
	{
		if (m_PendingWalkCoroutine == null)
			return;

		StopCoroutine(m_PendingWalkCoroutine);
		m_PendingWalkCoroutine = null;
	}

	private void CommitMovePreviewOrder()
	{
		List<Vector3> offsets = m_PreviewOffsets;
		Vector3 center = m_PreviewCenterPoint;
		if (offsets == null || offsets.Count == 0)
		{
			ClearPreviewMarkers();
			return;
		}

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		bool isGroup = validUnits.Count >= 2;
		bool hasGroupFormationFacing = isGroup
		                                 && (m_PreviewFormationForwardOverride.HasValue || m_HasFormationFacingSet);
		bool isGroupCtrlFormationSector = isGroup && IsCtrlPressed() && hasGroupFormationFacing;

		FormationLayoutUtility.FormationBuildResult rebuiltFormation = default;
		bool hasRebuiltFormation = false;
		if (isGroup && hasGroupFormationFacing)
		{
			rebuiltFormation =
				BuildFormationLayout(validUnits, center, m_PreviewFormationForwardOverride, _forceRebuildBindings: true);
			offsets = rebuiltFormation.Offsets;
			m_PreviewOffsets = offsets;
			hasRebuiltFormation = true;
		}

		bool allowArrivalFormationFacing = hasRebuiltFormation
		                                   && rebuiltFormation.FacingAngles != null
		                                   && rebuiltFormation.FacingAngles.Count > 0;
		bool applyReadyFormationMarchSector = FormationLayoutUtility.IndividualSlotSectorsEnabled
		                                     && hasGroupFormationFacing
		                                     && !isGroupCtrlFormationSector;

		bool useMoveFacing = !isGroup && (m_HasMoveFacingSet || m_IsQuickRotateFacing);
		RtsUnitMember.FacingArrowMode? facingMode = useMoveFacing
			? RtsUnitMember.FacingArrowMode.TurnOverDistance
			: null;
		List<float> facingAngles = useMoveFacing ? m_PreviewFacingAngles : null;
		List<float> formationFacingAngles = allowArrivalFormationFacing
			? new List<float>(rebuiltFormation.FacingAngles)
			: null;

		Vector3? formationForwardOverride = m_PreviewFormationForwardOverride;
		UnitClickToMove.MoveTier moveTier = m_PreviewMoveTier;
		bool startedOnSelectedUnit = m_RmbStartedOnSelectedUnit;

		if (IsCtrlShiftHeld() && hasGroupFormationFacing)
			isGroupCtrlFormationSector = true;

		float? formationCtrlBaseYaw = isGroupCtrlFormationSector
			? ResolveFormationCtrlBaseYaw(formationForwardOverride, m_PreviewFormationManualFacingAngle)
			: null;

		if (!FormationLayoutUtility.IndividualSlotSectorsEnabled
		    && isGroupCtrlFormationSector
		    && formationCtrlBaseYaw.HasValue)
		{
			facingAngles = BuildUniformFacingAngles(validUnits.Count, formationCtrlBaseYaw.Value);
			facingMode = RtsUnitMember.FacingArrowMode.HoldToEnd;
		}

		if (isGroup && isGroupCtrlFormationSector
		    && ShouldForceWalkForGroupFormationFacing(validUnits)
		    && moveTier != UnitClickToMove.MoveTier.Run && moveTier != UnitClickToMove.MoveTier.Sprint)
			moveTier = UnitClickToMove.MoveTier.Walk;

		m_LastWalkCenter = center;
		m_LastWalkOffsets = new List<Vector3>(offsets);

		// Tear down preview visuals before issuing the order. Clearing after ExecuteWalkOrder
		// would call ClearWaypoints on idle units and wipe in-place facing set by IssueInPlaceFacingOrder.
		ClearNotReadyFormationFacingForUnits(validUnits);

		ClearPreviewMarkers(_clearFormationFacing: false);

		ExecuteWalkOrder(
			offsets,
			center,
			facingAngles,
			IsAltHeld() ? -1 : 0,
			facingMode,
			formationFacingAngles,
			moveTier,
			startedOnSelectedUnit,
			formationForwardOverride,
			isGroupCtrlFormationSector,
			formationCtrlBaseYaw,
			applyReadyFormationMarchSector,
			allowArrivalFormationFacing);
	}

	private void StartMovePreview(
		Vector3 _centerPoint,
		Vector2 _mouseDownScreen,
		bool _rmbStartedOnSelectedUnit,
		UnitClickToMove.MoveTier _moveTier,
		List<Vector3> _prebuiltOffsets = null)
	{
		StopPendingWalkCoroutine();
		m_IsAwaitingDoubleClick = false;
		ClearPreviewMarkers(_clearFormationFacing: false);

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		m_PreviewPending = false;
		m_PreviewCancelled = false;
		m_HasMoveFacingSet = false;
		m_HasFormationFacingSet = false;
		m_PreviewFormationForwardOverride = null;
		m_PreviewGroupFormationFacingMode = GroupFormationFacingMode.HoldToEnd;
		m_PreviewFormationManualFacingAngle = 0f;
		m_PreviewFormationManualLookPoint = null;
		m_PreviewMoveTier = _moveTier;
		m_RmbStartedOnSelectedUnit = _rmbStartedOnSelectedUnit;
		m_IsPreviewingMove = true;
		m_PreviewCenterPoint = _centerPoint;
		m_RmbDownMousePos = _mouseDownScreen;

		if (validUnits.Count >= 2 && m_CurrentFormationSpacing <= 0f)
			m_CurrentFormationSpacing = m_FormationLineSpacing;

		if (_prebuiltOffsets != null && _prebuiltOffsets.Count > 0)
		{
			m_PreviewOffsets = new List<Vector3>(_prebuiltOffsets);
			if (validUnits.Count >= 2)
			{
				EnsureSelectedGroupFormation(validUnits);
				FormationLayoutUtility.FormationBuildResult built =
					BuildFormationLayout(validUnits, _centerPoint, null, _forceRebuildBindings: true);
				m_PreviewFormationFacingAngles = built.FacingAngles;
			}
			else
			{
				m_PreviewFormationFacingAngles = null;
			}
		}
		else if (validUnits.Count == 1)
		{
			m_PreviewOffsets = new List<Vector3> { Vector3.zero };
			m_PreviewFormationFacingAngles = null;
		}
		else
		{
			EnsureSelectedGroupFormation(validUnits);
			FormationLayoutUtility.FormationBuildResult built =
				BuildFormationLayout(validUnits, _centerPoint, null, _forceRebuildBindings: true);
			m_PreviewOffsets = built.Offsets;
			m_PreviewFormationFacingAngles = built.FacingAngles;
		}

		BeginMovePreviewForUnits();
		ApplyPreviewPathLines();

		m_IsInPlaceFacingPreview = _rmbStartedOnSelectedUnit
		                           && validUnits.Count == 1
		                           && IsInPlaceMovePreview(validUnits, _centerPoint, m_PreviewOffsets);
		if (m_IsInPlaceFacingPreview)
			validUnits[0]?.TryFinalizeIdleNearDestination();
	}

	private void BeginMovePreviewForUnits()
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i]?.BeginMovePreviewVisual();
	}

	private void EndMovePreviewForUnits()
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i]?.EndMovePreviewVisual();
	}

	private void ApplyPreviewPathLines()
	{
		if (m_PreviewOffsets == null)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
			validUnits[i].SetPreviewLine(m_PreviewCenterPoint + m_PreviewOffsets[i]);

		UpdateMovePreviewVisuals();
	}

	private void ShowPreviewMarkers()
	{
		m_PreviewPending = false;

		if (m_PreviewCancelled || m_PreviewOffsets == null)
			return;

		m_IsPreviewingMove = true;
		ApplyPreviewPathLines();
	}

	private void UpdateMovePreview()
	{
		if (m_PreviewCancelled)
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		Vector3 delta = hit.point - m_PreviewCenterPoint;
		delta.y = 0f;
		if (delta.sqrMagnitude < 0.01f)
			return;

		m_PreviewCenterPoint = hit.point;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
			validUnits[i].SetPreviewLine(m_PreviewCenterPoint + m_PreviewOffsets[i]);

		UpdateMovePreviewVisuals();
	}

	private void UpdateMovePreviewVisuals()
	{
		if (!m_IsPreviewingMove || m_PreviewOffsets == null)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		EnsurePreviewDestinationMarkers(validUnits.Count);

		for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
		{
			Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[i];
			if (i < m_PreviewDestinationMarkers.Count && m_PreviewDestinationMarkers[i] != null)
				m_PreviewDestinationMarkers[i].transform.position = dest;
		}

		for (int i = validUnits.Count; i < m_PreviewDestinationMarkers.Count; i++)
		{
			if (m_PreviewDestinationMarkers[i] != null)
				m_PreviewDestinationMarkers[i].SetActive(false);
		}

		UpdatePreviewUnitFacingArrows(validUnits);

		if (!m_IsQuickRotateFacing)
		{
			if (validUnits.Count >= 2 && m_PreviewFormationForwardOverride.HasValue)
			{
				SetMovePreviewFacingArrow(
					m_PreviewCenterPoint,
					m_PreviewFormationForwardOverride.Value,
					true,
					GetFacingArrowColor(RtsUnitMember.FacingArrowMode.TurnOnArrival),
					RtsUnitMember.FacingArrowMode.TurnOnArrival,
					null);
				bool showUnitArrows = FormationLayoutUtility.IndividualSlotSectorsEnabled
				                      && m_PreviewFormationFacingAngles != null
				                      && m_PreviewFormationFacingAngles.Count > 0;
				SetPreviewUnitFacingArrowsVisible(showUnitArrows);
				return;
			}

			SetMovePreviewFacingArrowVisible(false);
			SetPreviewUnitFacingArrowsVisible(false);
			return;
		}

		if (validUnits.Count == 1)
		{
			Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[0];
			float angle = m_PreviewFacingAngles != null && m_PreviewFacingAngles.Count > 0
				? m_PreviewFacingAngles[0]
				: 0f;
			Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
			SetMovePreviewFacingArrow(dest, dir, true);
			SetPreviewUnitFacingArrowsVisible(false);
			return;
		}

		if (m_PreviewFormationForwardOverride.HasValue)
		{
			RtsUnitMember.FacingArrowMode previewMode = m_PreviewGroupFormationFacingMode switch
			{
				GroupFormationFacingMode.HoldToEnd => RtsUnitMember.FacingArrowMode.HoldToEnd,
				GroupFormationFacingMode.LookAtPoint => RtsUnitMember.FacingArrowMode.LookAtPoint,
				_ => RtsUnitMember.FacingArrowMode.TurnOnArrival,
			};
			Color previewColor = GetFacingArrowColor(previewMode);
			Vector3? lookPoint = m_PreviewGroupFormationFacingMode == GroupFormationFacingMode.LookAtPoint
				? m_PreviewFormationManualLookPoint
				: null;
			SetMovePreviewFacingArrow(
				m_PreviewCenterPoint,
				m_PreviewFormationForwardOverride.Value,
				true,
				previewColor,
				previewMode,
				lookPoint);

			bool showUnitArrows = FormationLayoutUtility.IndividualSlotSectorsEnabled
			                      && m_PreviewFormationFacingAngles != null
			                      && m_PreviewFormationFacingAngles.Count > 0;
			SetPreviewUnitFacingArrowsVisible(showUnitArrows);
			return;
		}

		SetPreviewUnitFacingArrowsVisible(false);

		Vector3 forward = FormationLayoutUtility.ResolveFormationForward(
			validUnits,
			m_PreviewCenterPoint,
			null);
		SetMovePreviewFacingArrow(m_PreviewCenterPoint, forward, true);
		SetPreviewUnitFacingArrowsVisible(false);
	}

	private void UpdatePreviewUnitFacingArrows(List<RtsUnitMember> _validUnits)
	{
		if (_validUnits == null || m_PreviewOffsets == null)
			return;

		EnsurePreviewUnitFacingArrows(_validUnits.Count);

		Color arrowColor = GetFacingArrowColor(RtsUnitMember.FacingArrowMode.TurnOnArrival);
		for (int i = 0; i < m_PreviewUnitFacingArrows.Count; i++)
		{
			GameObject arrowGo = m_PreviewUnitFacingArrows[i];
			if (arrowGo == null)
				continue;

			bool active = i < _validUnits.Count && i < m_PreviewOffsets.Count;
			arrowGo.SetActive(active);
			if (!active)
				continue;

			Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[i];
			float angle = m_PreviewFormationFacingAngles != null && i < m_PreviewFormationFacingAngles.Count
				? m_PreviewFormationFacingAngles[i]
				: FormationLayoutUtility.ResolveFormationForwardYawDegrees(
					m_PreviewFormationForwardOverride ?? Vector3.forward);
			Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
			LineRenderer lr = arrowGo.GetComponent<LineRenderer>();
			if (lr == null)
				continue;

			lr.startColor = arrowColor;
			lr.endColor = arrowColor;
			Vector3 yOffset = Vector3.up * 0.08f;
			Vector3 shaftStart = dest + dir * 0.12f + yOffset;
			Vector3 tip = dest + dir * 1.35f + yOffset;
			lr.SetPosition(0, shaftStart);
			lr.SetPosition(1, tip);
		}
	}

	private void EnsurePreviewUnitFacingArrows(int _count)
	{
		while (m_PreviewUnitFacingArrows.Count < _count)
			m_PreviewUnitFacingArrows.Add(CreatePreviewUnitFacingArrow());

		for (int i = 0; i < m_PreviewUnitFacingArrows.Count; i++)
		{
			if (m_PreviewUnitFacingArrows[i] != null)
				m_PreviewUnitFacingArrows[i].SetActive(i < _count);
		}
	}

	private static GameObject CreatePreviewUnitFacingArrow()
	{
		GameObject arrow = new GameObject("MovePreviewUnitFacingArrow");
		LineRenderer lr = arrow.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.startWidth = 0.02f;
		lr.endWidth = 0.02f;
		lr.material = new Material(Shader.Find("Sprites/Default"));
		lr.startColor = new Color(1f, 0.85f, 0.2f, 0.95f);
		lr.endColor = new Color(1f, 0.85f, 0.2f, 0.95f);
		lr.enabled = false;
		return arrow;
	}

	private void SetPreviewUnitFacingArrowsVisible(bool _visible)
	{
		for (int i = 0; i < m_PreviewUnitFacingArrows.Count; i++)
		{
			GameObject arrowGo = m_PreviewUnitFacingArrows[i];
			if (arrowGo == null)
				continue;

			LineRenderer lr = arrowGo.GetComponent<LineRenderer>();
			if (lr != null)
				lr.enabled = _visible && arrowGo.activeSelf;
		}
	}

	private void EnsurePreviewDestinationMarkers(int _count)
	{
		while (m_PreviewDestinationMarkers.Count < _count)
		{
			GameObject marker = CreatePreviewDestinationMarker();
			m_PreviewDestinationMarkers.Add(marker);
		}

		for (int i = 0; i < m_PreviewDestinationMarkers.Count; i++)
		{
			if (m_PreviewDestinationMarkers[i] == null)
				continue;

			bool active = i < _count;
			m_PreviewDestinationMarkers[i].SetActive(active);
		}
	}

	private GameObject CreatePreviewDestinationMarker()
	{
		GameObject marker = m_DestinationMarkerPrefab != null
			? Instantiate(m_DestinationMarkerPrefab)
			: null;

		if (marker == null)
		{
			marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			marker.name = "MovePreviewDestinationMarker";
			marker.transform.localScale = new Vector3(0.55f, 0.02f, 0.55f);
			Renderer renderer = marker.GetComponent<Renderer>();
			if (renderer != null)
			{
				renderer.material = new Material(Shader.Find("Sprites/Default"));
				renderer.material.color = new Color(0.2f, 0.85f, 1f, 0.55f);
			}
		}

		Collider[] colliders = marker.GetComponentsInChildren<Collider>();
		for (int i = 0; i < colliders.Length; i++)
			Destroy(colliders[i]);

		return marker;
	}

	private void EnsureMovePreviewFacingArrow()
	{
		if (m_MovePreviewFacingArrow != null)
			return;

		m_MovePreviewFacingArrow = new GameObject("MovePreviewFacingArrow");
		LineRenderer lr = m_MovePreviewFacingArrow.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.startWidth = 0.03f;
		lr.endWidth = 0.03f;
		lr.material = new Material(Shader.Find("Sprites/Default"));
		lr.startColor = new Color(1f, 0.85f, 0.2f, 0.95f);
		lr.endColor = new Color(1f, 0.85f, 0.2f, 0.95f);
		lr.enabled = false;
	}

	private void SetMovePreviewFacingArrow(
		Vector3 _anchor,
		Vector3 _direction,
		bool _visible,
		Color? _color = null,
		RtsUnitMember.FacingArrowMode _mode = RtsUnitMember.FacingArrowMode.TurnOverDistance,
		Vector3? _lookPoint = null)
	{
		EnsureMovePreviewFacingArrow();
		if (m_MovePreviewFacingArrow == null)
			return;

		LineRenderer lr = m_MovePreviewFacingArrow.GetComponent<LineRenderer>();
		if (lr == null)
			return;

		lr.enabled = _visible;
		if (!_visible)
			return;

		Color arrowColor = _color ?? new Color(1f, 0.85f, 0.2f, 0.95f);
		lr.startColor = arrowColor;
		lr.endColor = arrowColor;

		Vector3 dir = _direction.sqrMagnitude > 0.0001f ? _direction.normalized : Vector3.forward;
		Vector3 yOffset = Vector3.up * 0.05f;
		Vector3 shaftStart = _anchor + dir * 0.15f + yOffset;
		Vector3 tip = _mode == RtsUnitMember.FacingArrowMode.LookAtPoint && _lookPoint.HasValue
			? _lookPoint.Value + yOffset
			: _anchor + dir * 2.5f + yOffset;
		lr.SetPosition(0, shaftStart);
		lr.SetPosition(1, tip);
	}

	private void SetMovePreviewFacingArrowVisible(bool _visible)
	{
		if (m_MovePreviewFacingArrow == null)
			return;

		LineRenderer lr = m_MovePreviewFacingArrow.GetComponent<LineRenderer>();
		if (lr != null)
			lr.enabled = _visible;
	}

	private void ClearMovePreviewVisuals()
	{
		for (int i = 0; i < m_PreviewDestinationMarkers.Count; i++)
		{
			if (m_PreviewDestinationMarkers[i] != null)
				Destroy(m_PreviewDestinationMarkers[i]);
		}
		m_PreviewDestinationMarkers.Clear();

		for (int i = 0; i < m_PreviewUnitFacingArrows.Count; i++)
		{
			if (m_PreviewUnitFacingArrows[i] != null)
				Destroy(m_PreviewUnitFacingArrows[i]);
		}
		m_PreviewUnitFacingArrows.Clear();

		if (m_MovePreviewFacingArrow != null)
		{
			Destroy(m_MovePreviewFacingArrow);
			m_MovePreviewFacingArrow = null;
		}
	}

	private bool HandleFormationScroll()
	{
		if (Mouse.current == null)
			return false;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count < 2)
			return false;

		Vector2 scroll = Mouse.current.scroll.ReadValue();
		if (Mathf.Approximately(scroll.y, 0f))
			return false;

		float scrollStep = Mathf.Abs(scroll.y) > 1f ? Mathf.Sign(scroll.y) : scroll.y;
		float step = m_FormationLineSpacingStep;
		m_CurrentFormationSpacing = Mathf.Clamp(
			m_CurrentFormationSpacing + scrollStep * step,
			m_FormationLineSpacingMin,
			m_FormationLineSpacingMax);

		FormationLayoutUtility.FormationBuildResult built =
			BuildFormationLayout(validUnits, m_PreviewCenterPoint, m_PreviewFormationForwardOverride, _forceRebuildBindings: true);
		m_PreviewOffsets = built.Offsets;
		m_PreviewFormationFacingAngles = built.FacingAngles;
		ApplyPreviewPathLines();
		return true;
	}

	private bool CanPreviewMoveFacing()
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		return validUnits.Count >= 1;
	}

	private void EnterMoveFacingMode()
	{
		if (!CanPreviewMoveFacing())
			return;

		m_IsQuickRotateFacing = true;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 1 && m_PreviewFacingAngles == null)
			m_PreviewFacingAngles = new List<float>(1) { 0f };

		UpdateQuickRotateMode();
	}

	private void ExitMoveFacingMode()
	{
		if (!m_IsQuickRotateFacing)
			return;

		m_IsQuickRotateFacing = false;
		SetMovePreviewFacingArrowVisible(false);

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count < 2 &&
		    m_PreviewFacingAngles != null && m_PreviewFacingAngles.Count > 0)
			m_HasMoveFacingSet = true;

		UpdateMovePreviewVisuals();
	}

	private void EnterQuickRotateMode()
	{
		m_PreviewPending = false;
		m_IsPreviewingMove = true;
		ClearAllPathInteractions();
		EnterMoveFacingMode();
	}

	private void UpdateQuickRotateMode()
	{
		if (m_PreviewOffsets == null)
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		if (validUnits.Count == 1)
		{
			if (m_PreviewFacingAngles == null)
				m_PreviewFacingAngles = new List<float>(1) { 0f };

			Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[0];
			Vector3 toCursor = hit.point - dest;
			toCursor.y = 0f;
			float angle;
			if (toCursor.sqrMagnitude > 0.05f)
				angle = Mathf.Atan2(toCursor.x, toCursor.z) * Mathf.Rad2Deg;
			else
				angle = ScreenDragToWorldYaw(Mouse.current.position.ReadValue() - m_RmbDownMousePos, dest);
			m_PreviewFacingAngles[0] = angle;
			ApplyPreviewPathLines();
			return;
		}

		Vector3 toFormationCursor = hit.point - m_PreviewCenterPoint;
		toFormationCursor.y = 0f;
		if (toFormationCursor.sqrMagnitude < 0.01f)
			return;

		Vector3 formationForward = toFormationCursor.normalized;
		m_PreviewFormationForwardOverride = formationForward;
		m_PreviewGroupFormationFacingMode = ResolveFormationGroupFacingModeFromModifiers();
		m_PreviewFormationManualFacingAngle = Mathf.Atan2(formationForward.x, formationForward.z) * Mathf.Rad2Deg;
		m_PreviewFormationManualLookPoint = hit.point;

		FormationLayoutUtility.FormationBuildResult built =
			BuildFormationLayout(validUnits, m_PreviewCenterPoint, formationForward, _forceRebuildBindings: false);
		m_PreviewOffsets = built.Offsets;
		m_PreviewFormationFacingAngles = built.FacingAngles;
		ApplyPreviewPathLines();
		UpdateMovePreviewVisuals();
	}

	private GroupFormationFacingMode ResolveFormationGroupFacingModeFromModifiers()
	{
		if (IsCtrlShiftHeld() || IsCtrlPressed())
			return GroupFormationFacingMode.HoldToEnd;
		return GroupFormationFacingMode.HoldToEnd;
	}

	private static bool ShouldForceWalkForGroupFormationFacing(List<RtsUnitMember> _units)
	{
		if (_units == null || _units.Count < 2)
			return false;

		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] != null && _units[i].WantsReady)
				return true;
		}

		return false;
	}

	private RtsUnitMember.FacingArrowMode ResolveFacingArrowModeFromModifiers()
	{
		if (IsCtrlPressed())
			return RtsUnitMember.FacingArrowMode.HoldToEnd;
		if (IsShiftHeld())
			return RtsUnitMember.FacingArrowMode.LookAtPoint;
		return RtsUnitMember.FacingArrowMode.TurnOverDistance;
	}

	private void BeginWaypointFacingEdit(int _unitIndex, int _segmentIndex)
	{
		m_IsEditingWaypointFacing = true;
		m_EditingUnitIndex = _unitIndex;
		m_EditingSegmentIndex = _segmentIndex;
		m_EditingWaypointAnchor = m_HoveredSegmentWorldPoint;
		m_EditingWaypointMode = ResolveFacingArrowModeFromModifiers();

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
		{
			EndWaypointFacingEdit();
			return;
		}

		if (m_DirectionMarkers.Count == 0)
			m_DirectionMarkers.Add(CreateFacingDirectionMarker(GetFacingArrowColor(m_EditingWaypointMode)));

		m_PreviewFacingAngles = new List<float>(1) { 0f };
		UpdateWaypointFacingEdit();
	}

	private void UpdateWaypointFacingEdit()
	{
		if (!m_IsEditingWaypointFacing)
			return;

		m_EditingWaypointMode = ResolveFacingArrowModeFromModifiers();
		Color arrowColor = GetFacingArrowColor(m_EditingWaypointMode);

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
		{
			if (m_DirectionMarkers.Count > 0 && m_DirectionMarkers[0] != null)
			{
				LineRenderer lr = m_DirectionMarkers[0].GetComponent<LineRenderer>();
				if (lr != null)
				{
					lr.startColor = arrowColor;
					lr.endColor = arrowColor;
				}
			}

			return;
		}

		Vector3 anchor = m_EditingWaypointAnchor;
		Vector3 yOffset = Vector3.up * 0.05f;
		Vector3 toCursor = hit.point - anchor;
		toCursor.y = 0f;

		float angle = toCursor.sqrMagnitude > 0.01f
			? Mathf.Atan2(toCursor.x, toCursor.z) * Mathf.Rad2Deg
			: 0f;
		m_EditingWaypointAngle = angle;
		m_EditingWaypointLookPoint = hit.point;

		if (m_DirectionMarkers.Count > 0 && m_DirectionMarkers[0] != null)
		{
			Vector3 dir = toCursor.sqrMagnitude > 0.01f ? toCursor.normalized : Vector3.forward;
			Vector3 shaftStart = anchor + yOffset + dir * 0.15f;
			Vector3 tip = m_EditingWaypointMode == RtsUnitMember.FacingArrowMode.LookAtPoint && toCursor.sqrMagnitude > 0.01f
				? hit.point + yOffset
				: anchor + yOffset + dir * 2.5f;
			LineRenderer lr = m_DirectionMarkers[0].GetComponent<LineRenderer>();
			if (lr != null)
			{
				lr.startColor = arrowColor;
				lr.endColor = arrowColor;
				lr.enabled = true;
				lr.SetPosition(0, shaftStart);
				lr.SetPosition(1, tip);
			}
		}
	}

	private void EndWaypointFacingEdit()
	{
		m_IsEditingWaypointFacing = false;
		int unitIndex = m_EditingUnitIndex;
		int segmentIndex = m_EditingSegmentIndex;
		float angle = m_EditingWaypointAngle;
		Vector3 anchor = m_EditingWaypointAnchor;
		Vector3 lookPoint = m_EditingWaypointLookPoint;
		RtsUnitMember.FacingArrowMode mode = m_EditingWaypointMode;
		m_EditingUnitIndex = -1;
		m_EditingSegmentIndex = -1;

		for (int i = 0; i < m_DirectionMarkers.Count; i++)
		{
			if (m_DirectionMarkers[i] != null)
				Destroy(m_DirectionMarkers[i]);
		}
		m_DirectionMarkers.Clear();
		m_PreviewFacingAngles = null;

		if (segmentIndex < 0)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count; i++)
		{
			if (validUnits[i] == null || validUnits[i].WaypointCount <= segmentIndex)
				continue;

			Vector3? lookPointArg = mode == RtsUnitMember.FacingArrowMode.LookAtPoint
				? lookPoint
				: null;
			validUnits[i].SetWaypointFacing(segmentIndex, angle, anchor, mode, lookPointArg);
		}

		ClearAllPathInteractions();
	}

	private void EnsureFormationSyncGroup(List<RtsUnitMember> _units, List<Vector3> _offsets, Vector3 _center)
	{
		if (_units.Count < 2)
			return;
		if (!FormationLayoutUtility.IsGroupFormation(GetDominantFormation(_units), _units.Count))
			return;

		RtsUnitMember.FormationSyncGroup group = null;
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] != null)
			{
				group = _units[i].ActiveFormationSync;
				if (group != null)
					break;
			}
		}

		RecalcSyncSpeeds(_units, _offsets, _center, group);
	}

	private void RecalcSyncSpeeds(List<RtsUnitMember> _units, List<Vector3> _offsets, Vector3 _center,
		RtsUnitMember.FormationSyncGroup _existingGroup)
	{
		RtsUnitMember.FormationSyncGroup syncGroup = _existingGroup
			?? new RtsUnitMember.FormationSyncGroup();

		syncGroup.Members.Clear();

		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] == null)
				continue;
			_units[i].AssignFormationSyncGroup(syncGroup);
		}

		if (syncGroup.Members.Count < 2)
			return;

		syncGroup.LastSpeedUpdateTime = Time.time;
		RecalcFormationSyncGroupSpeeds(syncGroup);
	}

	private void ApplyFormationSyncSpeeds(List<RtsUnitMember> _units, List<Vector3> _offsets, Vector3 _center)
	{
		RecalcSyncSpeeds(_units, _offsets, _center, null);
	}

	private void UpdateFormationSyncSpeeds()
	{
		s_ProcessedFormationSyncGroups.Clear();
		float now = Time.time;
		float updateInterval = Mathf.Max(0.05f, m_FormationSyncUpdateInterval);

		IReadOnlyList<RtsUnitMember> instances = RtsUnitMember.Instances;
		for (int i = 0; i < instances.Count; i++)
		{
			RtsUnitMember unit = instances[i];
			if (unit == null)
				continue;

			RtsUnitMember.FormationSyncGroup group = unit.ActiveFormationSync;
			if (group == null || group.Members.Count < 2)
				continue;
			if (!s_ProcessedFormationSyncGroups.Add(group))
				continue;
			if (now - group.LastSpeedUpdateTime < updateInterval)
				continue;

			group.LastSpeedUpdateTime = now;
			RecalcFormationSyncGroupSpeeds(group);
		}
	}

	private void RecalcFormationSyncGroupSpeeds(RtsUnitMember.FormationSyncGroup _group)
	{
		List<RtsUnitMember> members = _group.Members;
		float maxRemaining = 0f;
		int activeMembers = 0;

		for (int i = 0; i < members.Count; i++)
		{
			RtsUnitMember member = members[i];
			if (member == null)
				continue;
			if (!member.HasActiveDestination && member.WaypointCount == 0)
			{
				member.AssignFormationSpeedMultiplier(1f);
				continue;
			}

			float remaining = member.GetTotalRouteRemainingDistance();
			if (remaining > maxRemaining)
				maxRemaining = remaining;
			activeMembers++;
		}

		if (maxRemaining < 0.1f || activeMembers < 2)
		{
			for (int i = 0; i < members.Count; i++)
				members[i]?.AssignFormationSpeedMultiplier(1f);
			return;
		}

		for (int i = 0; i < members.Count; i++)
		{
			RtsUnitMember member = members[i];
			if (member == null)
				continue;
			if (!member.HasActiveDestination && member.WaypointCount == 0)
				continue;

			float remaining = member.GetTotalRouteRemainingDistance();
			float multiplier = Mathf.Clamp(
				remaining / maxRemaining,
				m_FormationSyncMinSpeedMultiplier,
				1f);
			member.AssignFormationSpeedMultiplier(multiplier);
		}
	}

	private void HandleRightMouseUp()
	{
		if (!m_IsPreviewingMove)
		{
			m_LastRightClickTime = Time.unscaledTime;
			return;
		}

		m_PreviewPending = false;
		m_IsPreviewingMove = false;
		m_LastRightClickTime = Time.unscaledTime;

		if (m_PreviewCancelled)
		{
			ClearPreviewMarkers();
			return;
		}

		if (m_PreviewOffsets == null || m_PreviewOffsets.Count == 0)
		{
			ClearPreviewMarkers();
			return;
		}

		if (m_PreviewMoveTier == UnitClickToMove.MoveTier.Run)
		{
			PreserveFormationFacingSetFromQuickRotate();
			CommitMovePreviewOrder();
			return;
		}

		if (m_IsQuickRotateFacing && m_RmbStartedOnSelectedUnit && GetValidSelectedUnits().Count == 1)
		{
			CommitMovePreviewOrder();
			return;
		}

		PreserveFormationFacingSetFromQuickRotate();

		BeginAwaitingDoubleClickForRun();
	}

	private void PreserveFormationFacingSetFromQuickRotate()
	{
		if (!m_IsQuickRotateFacing)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count >= 2 && m_PreviewFormationForwardOverride.HasValue)
			m_HasFormationFacingSet = true;

		ExitMoveFacingMode();
	}

	private void ExecuteWalkOrder(
		List<Vector3> _offsets,
		Vector3 _center,
		List<float> _facingAngles,
		int _waitGroup = 0,
		RtsUnitMember.FacingArrowMode? _facingMode = null,
		List<float> _formationFacingAngles = null,
		UnitClickToMove.MoveTier _moveTier = UnitClickToMove.MoveTier.Walk,
		bool _rmbStartedOnSelectedUnit = false,
		Vector3? _formationForwardOverride = null,
		bool _isGroupCtrlFormationSector = false,
		float? _formationCtrlBaseYaw = null,
		bool _applyReadyFormationMarchSector = false,
		bool _allowNotReadyArrivalFormationFacing = false)
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		bool shift = IsShiftHeld();
		bool altWaitEnqueue = _waitGroup < 0;
		bool enqueue = shift || altWaitEnqueue;
		bool useWait = _waitGroup != 0;
		bool isGroup = validUnits.Count >= 2;
		bool isRunTier = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
		RtsUnitMember.FacingArrowMode facingMode = _facingMode ?? RtsUnitMember.FacingArrowMode.TurnOverDistance;
		List<float> facingAngles = _facingAngles;
		List<float> formationFacingAngles = _formationFacingAngles;
		bool allowArrivalFormationFacing = _allowNotReadyArrivalFormationFacing;

		if (isGroup && (_isGroupCtrlFormationSector || _applyReadyFormationMarchSector)
		    && FormationLayoutUtility.IndividualSlotSectorsEnabled)
			BuildFormationLayout(validUnits, _center, _formationForwardOverride);

		if (isGroup && _isGroupCtrlFormationSector)
			ForceReadyForGroupManualFormationFacing(validUnits);

		bool useLiveSectorCtrl = _isGroupCtrlFormationSector && FormationLayoutUtility.IndividualSlotSectorsEnabled;
		bool useLiveSectorReady = _applyReadyFormationMarchSector && FormationLayoutUtility.IndividualSlotSectorsEnabled;
		bool holdFacingForEntireSegment = isGroup
		                                  && facingMode == RtsUnitMember.FacingArrowMode.HoldToEnd
		                                  && facingAngles != null;

		if (enqueue)
		{
			ShiftEnqueueMoveOrders(
				validUnits,
				_offsets,
				_center,
				facingAngles,
				_moveTier,
				_waitGroup,
				facingMode,
				null,
				holdFacingForEntireSegment,
				formationFacingAngles,
				allowArrivalFormationFacing);
			ApplyFormationFacingStateAfterMoveOrderSetup(
				validUnits,
				useLiveSectorCtrl,
				useLiveSectorReady,
				facingMode,
				isRunTier);
			EnsureFormationSyncGroup(validUnits, _offsets, _center);
			ApplyFormationMarchSlotSectorToUnits(validUnits, _formationCtrlBaseYaw, _applyReadyFormationMarchSector);
			return;
		}

		if (useWait && !enqueue)
		{
			for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
			{
				int waitGroup = _waitGroup > 0 ? _waitGroup : 1;
				Vector3 dest = _center + _offsets[i];
				float? facing = (facingAngles != null && i < facingAngles.Count)
					? facingAngles[i]
					: (float?)null;
				RtsUnitMember.FacingArrowMode waitFacingMode = facingMode;
				bool waitActivateAtSegmentStart = holdFacingForEntireSegment;
				if (facing.HasValue && isGroup && !_isGroupCtrlFormationSector && !allowArrivalFormationFacing)
				{
					waitFacingMode = RtsUnitMember.FacingArrowMode.TurnOnArrival;
					waitActivateAtSegmentStart = false;
				}
				if (!facing.HasValue &&
				    !allowArrivalFormationFacing &&
				    TryResolveArrivalFormationFacing(
					    validUnits[i],
					    i,
					    formationFacingAngles,
					    allowArrivalFormationFacing,
					    out float arrivalFacingAngle))
				{
					facing = arrivalFacingAngle;
					waitFacingMode = RtsUnitMember.FacingArrowMode.TurnOnArrival;
				}

				float stagger = isGroup ? ResolveUnitGroupCommandStaggerDelay(validUnits[i]) : 0f;
				validUnits[i].IssueDirectMoveOrderWithWait(
					dest,
					_moveTier,
					facing,
					waitFacingMode,
					waitGroup,
					null,
					waitActivateAtSegmentStart,
					_groupStaggerDelaySeconds: stagger);
			}

			ApplyFormationFacingStateAfterMoveOrderSetup(
				validUnits,
				useLiveSectorCtrl,
				useLiveSectorReady,
				facingMode,
				isRunTier);
			ApplyFormationSyncSpeeds(validUnits, _offsets, _center);
			ApplyFormationMarchSlotSectorToUnits(validUnits, _formationCtrlBaseYaw, _applyReadyFormationMarchSector);
			ApplyFormationSlotArrivalYawToUnits(validUnits, formationFacingAngles, allowArrivalFormationFacing);
			return;
		}

		if (isRunTier && isGroup)
		{
			bool allUpgraded = validUnits.Count > 0;
			for (int i = 0; i < validUnits.Count; i++)
			{
				Vector3 dest = i < _offsets.Count ? _center + _offsets[i] : _center;
				if (!validUnits[i].TryUpgradeMoveTargetToRun(dest))
				{
					allUpgraded = false;
					break;
				}
			}

			if (allUpgraded)
			{
				ApplyFormationSyncSpeeds(validUnits, _offsets, _center);
				return;
			}
		}

		if (isRunTier && !isGroup && validUnits.Count == 1 && _offsets.Count > 0)
		{
			Vector3 dest = _center + _offsets[0];
			if (validUnits[0].TryUpgradeMoveTargetToRun(dest))
				return;
		}

		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i].ClearCommandQueue();

		bool[] inPlaceFacing = new bool[validUnits.Count];
		bool forceInPlace = validUnits.Count == 1 && _rmbStartedOnSelectedUnit;
		for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
		{
			Vector3 dest = _center + _offsets[i];
			bool hasFacing = facingAngles != null && i < facingAngles.Count;
			inPlaceFacing[i] = hasFacing &&
			                   (forceInPlace || IsNearMoveDestination(validUnits[i].transform.position, dest));

			if (inPlaceFacing[i])
			{
				float stagger = isGroup ? ResolveUnitGroupCommandStaggerDelay(validUnits[i]) : 0f;
				validUnits[i].IssueInPlaceFacingOrder(facingAngles[i], facingMode, stagger);
				continue;
			}

			validUnits[i].SetDestinationDirect(dest);
			if (hasFacing)
			{
				RtsUnitMember.FacingArrowMode waypointFacingMode = facingMode;
				bool waypointActivateAtSegmentStart = holdFacingForEntireSegment;
				if (isGroup && !_isGroupCtrlFormationSector && !allowArrivalFormationFacing)
				{
					waypointFacingMode = RtsUnitMember.FacingArrowMode.TurnOnArrival;
					waypointActivateAtSegmentStart = false;
				}

				Vector3 facingAnchor = dest;
				validUnits[i].SetWaypointFacing(
					0,
					facingAngles[i],
					facingAnchor,
					waypointFacingMode,
					null,
					_forceReadyOnActivation: holdFacingForEntireSegment,
					_activateAtSegmentStart: waypointActivateAtSegmentStart);
			}
		}

		ApplyFormationFacingStateAfterMoveOrderSetup(
			validUnits,
			useLiveSectorCtrl,
			useLiveSectorReady,
			facingMode,
			isRunTier);

		ApplyFormationSyncSpeeds(validUnits, _offsets, _center);
		ApplyFormationMarchSlotSectorToUnits(validUnits, _formationCtrlBaseYaw, _applyReadyFormationMarchSector);
		ApplyFormationSlotArrivalYawToUnits(validUnits, formationFacingAngles, allowArrivalFormationFacing);

		for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
		{
			if (inPlaceFacing[i])
				continue;

			float moveStagger = isGroup ? ResolveUnitGroupCommandStaggerDelay(validUnits[i]) : 0f;
			validUnits[i].IssueMoveOrder(_center + _offsets[i], _moveTier, moveStagger);
		}
	}

	private static bool IsNearMoveDestination(Vector3 _from, Vector3 _to, float _epsilon = 0.75f)
	{
		return (Flatten(_to) - Flatten(_from)).sqrMagnitude <= _epsilon * _epsilon;
	}

	private static Vector3 Flatten(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}

	private void ShiftEnqueueMoveOrders(List<RtsUnitMember> _units, List<Vector3> _offsets,
		Vector3 _center, List<float> _facingAngles, UnitClickToMove.MoveTier _tier, int _waitGroup = 0,
		RtsUnitMember.FacingArrowMode _facingMode = RtsUnitMember.FacingArrowMode.TurnOverDistance,
		Vector3? _lookPoint = null,
		bool _activateAtSegmentStart = false,
		List<float> _formationFacingAngles = null,
		bool _allowArrivalFormationFacing = false)
	{
		bool useGroupStagger = _units.Count >= 2;
		for (int i = 0; i < _units.Count && i < _offsets.Count; i++)
		{
			Vector3 dest = _center + _offsets[i];
			if (_tier == UnitClickToMove.MoveTier.Run && _units[i].TryUpgradeMoveTargetToRun(dest))
				continue;

			int waitGroup = _waitGroup;
			if (_waitGroup < 0)
				waitGroup = _units[i].GetNextAutoWaitGroup();

			float? facing = (_facingAngles != null && i < _facingAngles.Count)
				? _facingAngles[i]
				: (float?)null;
			RtsUnitMember.FacingArrowMode facingMode = _facingMode;
			Vector3? lookPoint = _lookPoint;
			bool activateAtSegmentStart = _activateAtSegmentStart;
			if (facing.HasValue && _units.Count >= 2 && !_activateAtSegmentStart && !_allowArrivalFormationFacing
			    && _facingMode != RtsUnitMember.FacingArrowMode.HoldToEnd
			    && _facingMode != RtsUnitMember.FacingArrowMode.LookAtPoint)
			{
				facingMode = RtsUnitMember.FacingArrowMode.TurnOnArrival;
				lookPoint = null;
				activateAtSegmentStart = false;
			}
			if (!facing.HasValue &&
			    !_allowArrivalFormationFacing &&
			    TryResolveArrivalFormationFacing(
				    _units[i],
				    i,
				    _formationFacingAngles,
				    _allowArrivalFormationFacing,
				    out float arrivalFacingAngle))
			{
				facing = arrivalFacingAngle;
				facingMode = RtsUnitMember.FacingArrowMode.TurnOnArrival;
			}

			float? formationSlotYaw = null;
			if (_allowArrivalFormationFacing
			    && _formationFacingAngles != null
			    && i < _formationFacingAngles.Count)
				formationSlotYaw = _formationFacingAngles[i];

			_units[i].EnqueueWaypoint(
				dest,
				_tier,
				facing,
				facingMode,
				waitGroup,
				lookPoint,
				activateAtSegmentStart,
				useGroupStagger ? ResolveUnitGroupCommandStaggerDelay(_units[i]) : 0f,
				formationSlotYaw);
		}
	}

	private void CancelMovePreview()
	{
		if (!m_IsPreviewingMove && !m_PreviewPending && !m_IsAwaitingDoubleClick)
			return;

		StopPendingWalkCoroutine();
		m_IsAwaitingDoubleClick = false;
		m_PreviewPending = false;
		m_IsPreviewingMove = false;
		m_PreviewCancelled = true;
		ClearPreviewMarkers(_clearFormationFacing: false);
	}

	private void ClearPreviewMarkers(bool _clearFormationFacing = true)
	{
		StopPendingWalkCoroutine();
		m_IsAwaitingDoubleClick = false;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count; i++)
		{
			RtsUnitMember unit = validUnits[i];
			if (unit == null)
				continue;
			if (!unit.HasActiveDestination && !unit.HasQueuedCommands && !unit.HasWantedFacing)
				unit.ClearWaypoints();
			if (_clearFormationFacing)
				unit.ClearFormationFacing();
		}

		for (int i = 0; i < m_DirectionMarkers.Count; i++)
		{
			if (m_DirectionMarkers[i] != null)
				Destroy(m_DirectionMarkers[i]);
		}
		m_DirectionMarkers.Clear();

		ClearMovePreviewVisuals();
		EndMovePreviewForUnits();

		m_IsQuickRotateFacing = false;
		m_HasMoveFacingSet = false;
		m_HasFormationFacingSet = false;
		m_RmbStartedOnSelectedUnit = false;
		m_IsInPlaceFacingPreview = false;
		m_PreviewFacingAngles = null;
		m_PreviewFormationFacingAngles = null;
		m_PreviewFormationForwardOverride = null;
		m_PreviewGroupFormationFacingMode = GroupFormationFacingMode.HoldToEnd;
		m_PreviewFormationManualFacingAngle = 0f;
		m_PreviewFormationManualLookPoint = null;
		m_PreviewOffsets = null;
		m_PreviewMoveTier = UnitClickToMove.MoveTier.Walk;
		InvalidateFormationPreviewBindings();
	}

	private List<RtsUnitMember> GetValidSelectedUnits()
	{
		List<RtsUnitMember> validUnits = new List<RtsUnitMember>(m_SelectedUnits.Count);
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit != null && UnitFallenStateUtility.IsRtsControllable(unit))
				validUnits.Add(unit);
		}
		return validUnits;
	}

	public void NotifyUnitBecameNonControllable(RtsUnitMember _unit)
	{
		if (_unit == null || !m_SelectedUnits.Contains(_unit))
			return;

		_unit.SetSelected(false);
		m_SelectedUnits.Remove(_unit);
		RefreshSelectionState();
	}

	private bool TryRaycastAnyUnit(Ray _ray, out RaycastHit _hit)
	{
		return Physics.Raycast(_ray, out _hit, 2000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide) &&
		       _hit.collider != null &&
		       _hit.collider.GetComponentInParent<RtsUnitMember>() != null;
	}

	private void HandleKeyboardCommands()
	{
		if (Keyboard.current == null)
			return;

		if (GameInputGate.ShouldBlockGameplayInput())
			return;

		if (Keyboard.current.f1Key.wasPressedThisFrame)
		{
			ContinueSelectedRouteWaitGroup(1);
			return;
		}

		if (Keyboard.current.f2Key.wasPressedThisFrame)
		{
			ContinueSelectedRouteWaitGroup(2);
			return;
		}

		if (Keyboard.current.f3Key.wasPressedThisFrame)
		{
			ContinueSelectedRouteWaitGroup(3);
			return;
		}

		if (m_SelectedUnits.Count == 0)
			return;

		if (Keyboard.current.fKey.wasPressedThisFrame)
		{
			if (m_IsPreviewingMove || m_IsAwaitingDoubleClick)
				CancelMovePreview();
			else
				CommandSelectedHardStop();
			return;
		}

		if (Keyboard.current.eKey.wasPressedThisFrame)
		{
			ToggleSelectedReady();
			if (m_IsPreviewingMove)
				UpdateMovePreviewVisuals();
			return;
		}

		if (Keyboard.current.zKey.wasPressedThisFrame)
		{
			if (LocomotionProneFeature.Enabled)
				CommandSelectedStance(GetNextZTargetStance());
			return;
		}

		if (Keyboard.current.cKey.wasPressedThisFrame)
		{
			CommandSelectedStance(GetNextCTargetStance());
			return;
		}

		if (Keyboard.current.tKey.wasPressedThisFrame)
		{
			CommandSelectedManualMagazineLoading();
			return;
		}

		if (Keyboard.current.rKey.wasPressedThisFrame)
		{
			CommandSelectedWeaponReload();
			return;
		}

		if (Keyboard.current.bKey.wasPressedThisFrame)
		{
			CommandSelectedCycleWeaponAimMode();
			return;
		}

		if (Keyboard.current.vKey.wasPressedThisFrame)
			CommandSelectedCycleWeaponFireMode();
	}

	private void HandleFormationKeyInput()
	{
		if (Keyboard.current == null || m_SelectedUnits.Count < 2)
			return;

		if (Keyboard.current.xKey.wasPressedThisFrame)
		{
			m_IsFormationPickerKeyHeld = true;
			m_FormationPickerKeyDownTime = Time.unscaledTime;
			m_FormationDigitSelectedWhileHeld = false;
		}

		if (m_IsFormationPickerKeyHeld && Keyboard.current.xKey.isPressed)
			TrySelectFormationByDigitWhileHeld();

		if (m_IsFormationPickerKeyHeld && Keyboard.current.xKey.wasReleasedThisFrame)
		{
			m_IsFormationPickerKeyHeld = false;
			if (!m_FormationDigitSelectedWhileHeld &&
			    Time.unscaledTime - m_FormationPickerKeyDownTime <= c_FormationTapMaxSeconds)
				CycleSelectedFormation();
		}
	}

	private void CommandSelectedStance(LocomotionStance _stance)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.RequestStance(_stance, _stagger));
	}

	private void SetSelectedReady(bool _ready)
	{
		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.SetReadyWanted(_ready, _stagger));
	}

	private void CommandSelectedHardStop()
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			unit.HardStop();
		}
	}

	private void CommandSelectedManualMagazineLoading()
	{
		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.StartManualMagazineLoading(_stagger));
	}

	private void CommandSelectedWeaponReload()
	{
		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.StartWeaponReload(_stagger));
	}

	private void CommandSelectedCycleWeaponFireMode()
	{
		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.CycleWeaponFireMode(_stagger));
	}

	private void CommandSelectedCycleWeaponAimMode()
	{
		ForEachSelectedUnitWithGroupStagger((_unit, _stagger) => _unit.CycleWeaponAimMode(_stagger));
	}

	private void ForEachSelectedUnitWithGroupStagger(Action<RtsUnitMember, float> _action)
	{
		if (_action == null)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		bool useGroupStagger = validUnits.Count >= 2;
		for (int i = 0; i < validUnits.Count; i++)
		{
			RtsUnitMember unit = validUnits[i];
			if (unit == null)
				continue;

			float stagger = useGroupStagger ? ResolveUnitGroupCommandStaggerDelay(unit) : 0f;
			_action(unit, stagger);
		}
	}

	private static float ResolveUnitGroupCommandStaggerDelay(RtsUnitMember _unit)
	{
		if (_unit == null)
			return 0f;

		UnitCombatStats combatStats = _unit.GetComponent<UnitCombatStats>();
		return combatStats != null ? combatStats.GetCommandVisionStaggerDelaySeconds() : 0f;
	}

	private void CycleSelectedFormation()
	{
		if (m_SelectedUnits.Count < 2)
			return;

		FormationType current = GetDominantFormation(m_SelectedUnits);
		FormationType next = FormationLayoutUtility.CycleFormation(current);
		SetSelectedFormation(next, true);
	}

	private bool TrySelectFormationByDigitWhileHeld()
	{
		if (Keyboard.current == null || m_SelectedUnits.Count < 2)
			return false;

		for (int digit = 1; digit <= 7; digit++)
		{
			if (!IsDigitKeyPressed(digit))
				continue;

			SetSelectedFormation(FormationLayoutUtility.FormationFromHotkeyIndex(digit), true);
			m_FormationDigitSelectedWhileHeld = true;
			return true;
		}

		return false;
	}

	private static bool IsDigitKeyPressed(int _digit)
	{
		if (Keyboard.current == null || _digit < 1 || _digit > 9)
			return false;

		return _digit switch
		{
			1 => Keyboard.current.digit1Key.wasPressedThisFrame,
			2 => Keyboard.current.digit2Key.wasPressedThisFrame,
			3 => Keyboard.current.digit3Key.wasPressedThisFrame,
			4 => Keyboard.current.digit4Key.wasPressedThisFrame,
			5 => Keyboard.current.digit5Key.wasPressedThisFrame,
			6 => Keyboard.current.digit6Key.wasPressedThisFrame,
			7 => Keyboard.current.digit7Key.wasPressedThisFrame,
			8 => Keyboard.current.digit8Key.wasPressedThisFrame,
			9 => Keyboard.current.digit9Key.wasPressedThisFrame,
			_ => false,
		};
	}

	private void SetSelectedFormation(FormationType _formation, bool _showMessage)
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.CurrentFormation = _formation;
		}

		if (m_CurrentFormationSpacing <= 0f)
			m_CurrentFormationSpacing = m_FormationLineSpacing;

		if (_showMessage)
			ShowTransientMessage($"Формация: {FormationLayoutUtility.GetDisplayName(_formation)}");

		if (m_IsPreviewingMove)
		{
			List<RtsUnitMember> validUnits = GetValidSelectedUnits();
			if (validUnits.Count >= 2)
			{
				FormationLayoutUtility.FormationBuildResult built =
					BuildFormationLayout(validUnits, m_PreviewCenterPoint, m_PreviewFormationForwardOverride, _forceRebuildBindings: true);
				m_PreviewOffsets = built.Offsets;
				m_PreviewFormationFacingAngles = built.FacingAngles;
				ApplyPreviewPathLines();
			}
		}
	}

	private void EnsureSelectedGroupFormation(List<RtsUnitMember> _units)
	{
		if (_units == null || _units.Count < 2)
			return;

		FormationType dominant = GetDominantFormation(_units);
		dominant = FormationLayoutUtility.NormalizeGroupFormation(dominant);
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] != null)
				_units[i].CurrentFormation = dominant;
		}
	}

	private FormationLayoutUtility.FormationBuildResult BuildFormationLayout(
		List<RtsUnitMember> _units,
		Vector3 _centerPoint,
		Vector3? _formationForwardOverride,
		bool _forceRebuildBindings = false)
	{
		if (_units == null || _units.Count == 0)
			return new FormationLayoutUtility.FormationBuildResult(new List<Vector3>(), new List<float>());

		if (_units.Count == 1)
			return new FormationLayoutUtility.FormationBuildResult(new List<Vector3> { Vector3.zero }, new List<float>());

		EnsureSelectedGroupFormation(_units);
		FormationType formation = GetDominantFormation(_units);
		float spacing = m_CurrentFormationSpacing > 0f ? m_CurrentFormationSpacing : m_FormationLineSpacing;
		Vector3 forward = FormationLayoutUtility.ResolveFormationForward(_units, _centerPoint, _formationForwardOverride);
		bool forwardChanged = !m_HasCachedFormationForward
		                      || Vector3.Angle(forward, m_CachedFormationForward) > c_FormationForwardRebuildAngleDegrees;

		bool needsRebuild = _forceRebuildBindings
		                    || m_FormationPreviewBindings == null
		                    || m_FormationPreviewBindings.Length != _units.Count
		                    || m_CachedFormationType != formation
		                    || !Mathf.Approximately(m_CachedFormationSpacing, spacing)
		                    || forwardChanged;

		if (needsRebuild)
		{
			m_FormationPreviewBindings = FormationLayoutUtility.CreateStableBindings(
				formation,
				_units,
				_centerPoint,
				spacing,
				_formationForwardOverride);
			m_CachedFormationType = formation;
			m_CachedFormationSpacing = spacing;
			m_CachedFormationForward = forward;
			m_HasCachedFormationForward = true;
		}

		return FormationLayoutUtility.ApplyBindings(m_FormationPreviewBindings, forward);
	}

	private void InvalidateFormationPreviewBindings()
	{
		m_FormationPreviewBindings = null;
		m_HasCachedFormationForward = false;
	}

	private static void ApplyFormationSlotArrivalYawToUnits(
		List<RtsUnitMember> _units,
		List<float> _formationFacingAngles,
		bool _applyFormationSlotArrival)
	{
		if (_units == null)
			return;

		for (int i = 0; i < _units.Count; i++)
			_units[i]?.ClearPendingFormationSlotArrivalYaw();

		if (!_applyFormationSlotArrival || _formationFacingAngles == null)
			return;

		for (int i = 0; i < _units.Count && i < _formationFacingAngles.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;

			unit.SetPendingFormationSlotArrivalYaw(_formationFacingAngles[i]);
		}
	}

	private static void ApplyFormationFacingStateAfterMoveOrderSetup(
		List<RtsUnitMember> _units,
		bool _isGroupCtrlFormationSector,
		bool _applyReadyFormationMarchSector,
		RtsUnitMember.FacingArrowMode _facingMode,
		bool _isRunTier)
	{
		if (_units == null || _units.Count == 0)
			return;

		if (_isRunTier || _facingMode == RtsUnitMember.FacingArrowMode.TurnOnArrival)
		{
			ClearLiveFormationSectorForUnits(_units);
			return;
		}

		if (_isGroupCtrlFormationSector || _applyReadyFormationMarchSector)
		{
			ClearNotReadyFormationFacingForUnits(_units);
			return;
		}

		ClearLiveFormationSectorForUnits(_units);
	}

	private void ApplyFormationMarchSlotSectorToUnits(
		List<RtsUnitMember> _units,
		float? _fixedCtrlBaseYaw,
		bool _applyReadyOnlyWithoutCtrlBase)
	{
		if (!FormationLayoutUtility.IndividualSlotSectorsEnabled)
			return;

		if (_units == null || m_FormationPreviewBindings == null)
			return;

		int count = Mathf.Min(_units.Count, m_FormationPreviewBindings.Length);
		for (int i = 0; i < count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;

			float slotOffset = m_FormationPreviewBindings[i].FacingOffsetFromForward;
			if (_fixedCtrlBaseYaw.HasValue)
			{
				unit.SetFormationSlotSectorConfig(slotOffset, _fixedCtrlBaseYaw.Value);
				continue;
			}

			if (_applyReadyOnlyWithoutCtrlBase && unit.WantsReady)
				unit.SetFormationSlotSectorConfig(slotOffset, null);
			else
				unit.ClearFormationSlotSectorConfig();
		}
	}

	private static float? ResolveFormationCtrlBaseYaw(Vector3? _formationForwardOverride, float _manualFacingAngle)
	{
		if (_formationForwardOverride.HasValue)
			return FormationLayoutUtility.ResolveFormationForwardYawDegrees(_formationForwardOverride.Value);

		return _manualFacingAngle;
	}

	private static List<float> BuildUniformFacingAngles(int _count, float _angle)
	{
		var angles = new List<float>(_count);
		for (int i = 0; i < _count; i++)
			angles.Add(_angle);
		return angles;
	}

	private static void ForceReadyForGroupManualFormationFacing(List<RtsUnitMember> _units)
	{
		if (_units == null)
			return;

		bool useGroupStagger = _units.Count >= 2;
		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null || unit.WantsReady)
				continue;

			float stagger = useGroupStagger ? ResolveUnitGroupCommandStaggerDelay(unit) : 0f;
			unit.SetReadyWanted(true, stagger);
		}
	}

	private static void ClearLiveFormationSectorForUnits(List<RtsUnitMember> _units)
	{
		if (_units == null)
			return;

		for (int i = 0; i < _units.Count; i++)
			_units[i]?.ClearFormationFacing();
	}

	private static void ClearNotReadyFormationFacingForUnits(List<RtsUnitMember> _units)
	{
		if (_units == null)
			return;

		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null || UnitUsesLiveFormationSectorFacing(unit))
				continue;

			unit.ClearFormationFacing();
		}
	}

	private static bool UnitUsesLiveFormationSectorFacing(RtsUnitMember _unit)
	{
		return _unit != null && _unit.WantsReady;
	}

	private static bool TryResolveArrivalFormationFacing(
		RtsUnitMember _unit,
		int _unitIndex,
		List<float> _formationFacingAngles,
		bool _allowArrivalFormationFacing,
		out float _facingAngle)
	{
		_facingAngle = 0f;
		if (!_allowArrivalFormationFacing || _unit == null)
			return false;
		if (_formationFacingAngles == null || _unitIndex < 0 || _unitIndex >= _formationFacingAngles.Count)
			return false;

		_facingAngle = _formationFacingAngles[_unitIndex];
		return true;
	}

	private void SyncFormationToSelection()
	{
		if (m_SelectedUnits.Count < 2)
			return;

		FormationType dominant = FormationLayoutUtility.NormalizeGroupFormation(GetDominantFormation(m_SelectedUnits));
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.CurrentFormation = dominant;
		}
	}

	private FormationType GetDominantFormation(List<RtsUnitMember> _units)
	{
		int bestCount = 0;
		FormationType bestType = FormationType.TacticalColumn;

		for (int typeIndex = (int)FormationType.SingleFile; typeIndex <= (int)FormationType.Diamond; typeIndex++)
		{
			FormationType candidate = (FormationType)typeIndex;
			int count = 0;
			for (int i = 0; i < _units.Count; i++)
			{
				RtsUnitMember unit = _units[i];
				if (unit == null)
					continue;

				FormationType unitFormation = unit.CurrentFormation == FormationType.None
					? FormationType.TacticalColumn
					: unit.CurrentFormation;
				if (unitFormation == candidate)
					count++;
			}

			if (count > bestCount)
			{
				bestCount = count;
				bestType = candidate;
			}
		}

		return bestType;
	}

	private LocomotionStance GetNextZTargetStance()
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null || !unit.TryGetCurrentStance(out LocomotionStance stance))
				continue;
			if (stance != LocomotionStance.Prone)
				return LocomotionStance.Prone;
		}

		return LocomotionStance.Standing;
	}

	private LocomotionStance GetNextCTargetStance()
	{
		bool hasProne = false;
		bool hasNonCrouch = false;

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null || !unit.TryGetCurrentStance(out LocomotionStance stance))
				continue;

			if (stance == LocomotionStance.Prone)
				hasProne = true;
			if (stance != LocomotionStance.Crouch)
				hasNonCrouch = true;
		}

		if (hasProne)
			return LocomotionStance.Crouch;

		return hasNonCrouch ? LocomotionStance.Crouch : LocomotionStance.Standing;
	}

	private void ToggleUnitSelection(RtsUnitMember _unit, bool refreshAfterToggle = true)
	{
		if (_unit == null || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(_unit))
			return;
		if (!m_SelectedUnits.Contains(_unit) && !UnitFallenStateUtility.IsRtsControllable(_unit))
			return;

		if (m_SelectedUnits.Contains(_unit))
			m_SelectedUnits.Remove(_unit);
		else
			m_SelectedUnits.Add(_unit);

		if (refreshAfterToggle)
			RefreshSelectionState();
	}

	private void SetSelection(List<RtsUnitMember> _units)
	{
		ClearAllPathInteractions();
		ClearSelectionVisualsOnly();
		m_SelectedUnits.Clear();

		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null || !UnitFallenStateUtility.IsRtsControllable(unit))
				continue;
			if (m_SelectedUnits.Contains(unit))
				continue;
			m_SelectedUnits.Add(unit);
		}

		RefreshSelectionState();
	}

	private void RefreshSelectionState()
	{
		ClearSelectionVisualsOnly();

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.SetSelected(true);
		}

		m_SelectedUnits.RemoveAll(_unit =>
			_unit == null || !UnitFallenStateUtility.IsRtsControllable(_unit));

		SyncFormationToSelection();
		PrepareSoloSelectedUnitState();
		SyncActiveInventoryToSelection();
		SelectionChanged?.Invoke();
	}

	private void PrepareSoloSelectedUnitState()
	{
		if (m_SelectedUnits.Count != 1)
			return;

		RtsUnitMember unit = m_SelectedUnits[0];
		if (unit == null)
			return;

		unit.TryFinalizeIdleNearDestination();
		unit.ClearFormationSync();
		unit.ClearFormationFacing();

		if (!m_IsPreviewingMove)
			InvalidateFormationPreviewBindings();
	}

	private float GetMoveFacingDragThresholdPixels()
	{
		if (m_IsInPlaceFacingPreview && GetValidSelectedUnits().Count == 1)
			return m_InPlaceFacingDragThresholdPixels;

		return m_QuickRotateDragThresholdPixels;
	}

	private static bool IsInPlaceMovePreview(
		List<RtsUnitMember> _units,
		Vector3 _center,
		List<Vector3> _offsets)
	{
		if (_units == null || _offsets == null || _units.Count != 1 || _offsets.Count < 1)
			return false;

		RtsUnitMember unit = _units[0];
		if (unit == null)
			return false;

		Vector3 dest = _center + _offsets[0];
		return IsNearMoveDestination(unit.transform.position, dest);
	}

	private float ScreenDragToWorldYaw(Vector2 _screenDelta, Vector3 _worldAnchor)
	{
		if (_screenDelta.sqrMagnitude < 0.0001f || m_SelectionCamera == null)
			return 0f;

		Vector3 anchorScreen = m_SelectionCamera.WorldToScreenPoint(_worldAnchor);
		Vector3 fromScreen = new Vector3(anchorScreen.x, anchorScreen.y, anchorScreen.z);
		Vector3 toScreen = fromScreen + new Vector3(_screenDelta.x, _screenDelta.y, 0f);

		Ray fromRay = m_SelectionCamera.ScreenPointToRay(fromScreen);
		Ray toRay = m_SelectionCamera.ScreenPointToRay(toScreen);
		Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, _worldAnchor.y, 0f));

		if (!groundPlane.Raycast(fromRay, out float enterFrom) || !groundPlane.Raycast(toRay, out float enterTo))
			return 0f;

		Vector3 fromWorld = fromRay.GetPoint(enterFrom);
		Vector3 toWorld = toRay.GetPoint(enterTo);
		Vector3 direction = toWorld - fromWorld;
		direction.y = 0f;
		if (direction.sqrMagnitude < 0.0001f)
			return 0f;

		return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
	}

	private void ClearSelectionVisualsOnly()
	{
		IReadOnlyList<RtsUnitMember> units = RtsUnitMember.Instances;
		for (int i = 0; i < units.Count; i++)
		{
			RtsUnitMember unit = units[i];
			if (unit != null)
				unit.SetSelected(false);
		}
	}

	private void SyncActiveInventoryToSelection()
	{
		if (m_InventoryBindings != null)
			m_InventoryBindings.SetSelectionManager(this);

		if (m_InventoryBindings == null)
			return;

		CharacterInventory inventory = TryGetActiveCharacterInventoryForUi();
		m_InventoryBindings.SetActiveCharacterInventory(inventory);
	}

	/// <summary>Синхронизировать <see cref="InventoryScreenBindings.ActiveCharacterInventory"/> с текущим выделением RTS.</summary>
	public void SyncActiveInventoryForUi()
	{
		SyncActiveInventoryToSelection();
	}

	public CharacterInventory TryGetActiveCharacterInventoryForUi()
	{
		InventoryExchangeController exchange = InventoryExchangeController.Instance;
		if (exchange.IsActive && exchange.PlayerInventory != null && ShouldPinActiveExchangeInventory)
			return exchange.PlayerInventory;

		if (m_PendingExchangePlayerUnit != null)
		{
			CharacterInventory pendingInventory = m_PendingExchangePlayerUnit.CharacterInventory;
			if (pendingInventory != null)
				return pendingInventory;

			if (m_PendingExchangePlayerUnit.TryGetComponent(out pendingInventory))
				return pendingInventory;

			return m_PendingExchangePlayerUnit.GetComponentInChildren<CharacterInventory>(true);
		}

		for (int i = m_SelectedUnits.Count - 1; i >= 0; i--)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			CharacterInventory inventory = unit.CharacterInventory;
			if (inventory != null)
				return inventory;
		}

		return null;
	}

	private static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_panel == null)
			return false;

		var slots = _panel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			if (slots[i] == _slot)
				return true;
		}

		return false;
	}

	private bool TryQuickTransferGroundToCharacterInternal(CharacterInventory _inventory, InventorySlotView _slot)
	{
		if (IsExchangeActive)
		{
			CharacterInventory partner = GetPartnerInventory();
			if (partner == null)
				return false;

			if (!TryRemovePartnerItemByGroundSlotIndex(
				    -1,
				    _slot,
				    partner,
				    out InventorySlotRuntimeData data,
				    out bool isMainHand,
				    out bool isHead,
				    out bool isBack))
				return false;

			InventorySlotRuntimeData forPlayer = data;
			forPlayer.WorldSource = null;

			if (!_inventory.TryAdd(forPlayer))
			{
				TryRestoreToInventorySlot(partner, isMainHand, isHead, isBack, data);
				RepaintExchangePanels();
				return false;
			}

			ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(_inventory, forPlayer);

			RepaintExchangePanels();
			return true;
		}

		if (!_slot.TryTakeItem(out InventorySlotRuntimeData dataNormal))
			return false;

		InventorySlotRuntimeData forInventory = dataNormal;
		forInventory.WorldSource = null;

		if (!_inventory.TryAdd(forInventory))
		{
			_slot.SetItem(dataNormal);
			return false;
		}

		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(_inventory, forInventory);

		if (dataNormal.WorldSource != null)
			dataNormal.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(_slot);
		_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private bool TryQuickTransferCharacterToGroundInternal(CharacterInventory _inventory, InventorySlotView _slot)
	{
		if (!TryResolveCharacterInventorySlot(_slot, _inventory, out bool isMainHand, out bool isHead, out int bagIndex))
			return false;

		InventorySlotRuntimeData data;
		if (isMainHand)
		{
			if (!_inventory.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else if (isHead)
		{
			if (!_inventory.TryRemoveHeadEquipment(out data))
				return false;
		}
		else
		{
			if (!_inventory.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		if (IsExchangeActive)
			return TryCompleteCharacterToPartnerTransfer(_inventory, data, null, isMainHand, isHead);

		return TryCompleteCharacterToGroundTransfer(_inventory, data, null, isMainHand, isHead);
	}

	private static WorldPickupItem SpawnDropWorldPickup(CharacterInventory _inventory, InventorySlotRuntimeData _data)
	{
		ItemDefinition definition = _data.Definition;
		if (definition == null)
			return null;

		_inventory.GetDropWorldPose(out Vector3 position, out Quaternion rotation);
		position += Vector3.up * 0.08f;
		// Не использовать Instantiate<GameObject>: у битой ссылки на префаб (неверный fileID в .asset) generic бросает InvalidCastException.
		UnityEngine.Object prefabObj = definition.DropWorldPrefab;
		if (prefabObj == null)
			return null;
		UnityEngine.Object instanceObj = UnityEngine.Object.Instantiate(prefabObj, position, rotation);
		GameObject go = instanceObj as GameObject;
		if (go == null && instanceObj is Component instanceComp)
			go = instanceComp.gameObject;
		if (go == null)
			return null;
		PrepareDroppedWorldPickupPhysics(go);
		WorldPickupItem pickup = go.GetComponent<WorldPickupItem>();
		if (pickup == null)
		{
			Destroy(go);
			return null;
		}

		pickup.ConfigureForDroppedFromInventory(_data);
		return pickup;
	}

	private static void PrepareDroppedWorldPickupPhysics(GameObject _root)
	{
		if (_root == null)
			return;

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody rb = bodies[i];
			if (rb == null)
				continue;

			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
		}
	}

	/// <summary>
	/// Добавить предмет на панель «земля»: UI-ячейка и при наличии <see cref="ItemDefinition.DropWorldPrefab"/> — лут в мире.
	/// Используется при выбросе из сумки и при снятии модулей с оружия.
	/// </summary>
	public bool TryPlaceItemOnGroundPanel(CharacterInventory _inventory, InventorySlotRuntimeData _data)
	{
		if (m_GroundPanel == null || _data.IsEmpty)
			return false;

		if (!TryBuildGroundSlotData(_inventory, _data, out InventorySlotRuntimeData groundData, out WorldPickupItem spawned))
			return false;

		if (!m_GroundPanel.TryAdd(groundData))
		{
			if (spawned != null)
				Destroy(spawned.gameObject);
			return false;
		}

		FinalizeGroundPanelPlacement(spawned);
		ItemInventoryAudioUtility.TryPlayInventoryRemoveSoundFromSlot(_inventory, _data, spawned);
		return true;
	}

	private bool TryCompleteCharacterToGroundTransfer(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull,
		bool _removedFromMainHandSlot,
		bool _removedFromHeadSlot = false,
		bool _removedFromBackSlot = false)
	{
		if (!TryBuildGroundSlotData(_inventory, _data, out InventorySlotRuntimeData groundData, out WorldPickupItem spawned))
		{
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		bool placed;
		if (_adoptExistingSlotOrNull != null)
		{
			if (!m_GroundPanel.AdoptDraggedSlot(_adoptExistingSlotOrNull))
			{
				_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
				_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
				if (spawned != null)
					Destroy(spawned.gameObject);
				return false;
			}

			_adoptExistingSlotOrNull.SetItem(groundData);
			placed = true;
		}
		else
			placed = m_GroundPanel.TryAdd(groundData);

		if (!placed)
		{
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			if (spawned != null)
				Destroy(spawned.gameObject);
			return false;
		}

		FinalizeGroundPanelPlacement(spawned);
		ItemInventoryAudioUtility.TryPlayRemoveSoundFromSlot(_data, _inventory, spawned, _removedFromMainHandSlot);
		_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryCompleteCharacterToWorldDrop(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull,
		bool _removedFromMainHandSlot,
		bool _removedFromHeadSlot = false,
		bool _removedFromBackSlot = false)
	{
		if (!IsExchangeActive)
		{
			return TryCompleteCharacterToGroundTransfer(
				_inventory,
				_data,
				_adoptExistingSlotOrNull,
				_removedFromMainHandSlot,
				_removedFromHeadSlot,
				_removedFromBackSlot);
		}

		if (!TryBuildGroundSlotData(_inventory, _data, out _, out WorldPickupItem spawned))
		{
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		if (spawned == null)
		{
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		DestroyDetachedDragSlotIfNeeded(_adoptExistingSlotOrNull, m_CharacterInventoryPanel);
		_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private bool TryCompletePartnerToWorldDrop(
		CharacterInventory _partnerInventory,
		InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull,
		bool _removedFromMainHandSlot,
		bool _removedFromHeadSlot = false,
		bool _removedFromBackSlot = false)
	{
		if (!TryBuildGroundSlotData(_partnerInventory, _data, out _, out WorldPickupItem spawned))
		{
			TryRestoreToInventorySlot(_partnerInventory, _removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			RepaintExchangePanels();
			return false;
		}

		if (spawned == null)
		{
			TryRestoreToInventorySlot(_partnerInventory, _removedFromMainHandSlot, _removedFromHeadSlot, _removedFromBackSlot, _data);
			RepaintExchangePanels();
			return false;
		}

		ItemInventoryAudioUtility.TryPlayRemoveSoundFromSlot(_data, _partnerInventory, spawned, _removedFromMainHandSlot);
		DestroyDetachedDragSlotIfNeeded(_adoptExistingSlotOrNull, m_GroundPanel);
		RepaintExchangePanels();
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
		return true;
	}

	private static bool TryBuildGroundSlotData(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		out InventorySlotRuntimeData _groundData,
		out WorldPickupItem _spawned)
	{
		_spawned = null;
		_groundData = _data;
		ItemDefinition definition = _data.Definition;
		if (definition == null || definition.DropWorldPrefab == null)
		{
			_groundData.WorldSource = null;
			return true;
		}

		if (_inventory == null)
			return false;

		_spawned = SpawnDropWorldPickup(_inventory, _data);
		if (_spawned == null)
			return false;

		_groundData.WorldSource = _spawned;
		return true;
	}

	private void FinalizeGroundPanelPlacement(WorldPickupItem _spawned)
	{
		if (_spawned != null)
		{
			_spawned.RegisterListedInGroundUi();
			RegisterSpawnedPickupWithZone(_spawned);
		}

		m_GroundPanel.RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.EnsureGroundPanelUiHooks();
		RuntimeInventoryModificationCoordinator.Instance?.OnGroundPanelRepopulated();
	}

	private static void RegisterSpawnedPickupWithZone(WorldPickupItem _spawned)
	{
		if (_spawned == null)
			return;

		CharacterInventory inventory = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.GetActiveCharacterInventoryForUi()
			: null;
		if (inventory == null)
			return;

		InventoryPickupZone zone = inventory.GetComponentInChildren<InventoryPickupZone>(true);
		zone?.RegisterPickupOverlap(_spawned);
	}

	private void TrySelectFirstPlayerUnit()
	{
		IReadOnlyList<RtsUnitMember> units = RtsUnitMember.Instances;
		for (int i = 0; i < units.Count; i++)
		{
			RtsUnitMember unit = units[i];
			if (unit == null || !UnitFallenStateUtility.IsRtsControllable(unit))
				continue;

			SetSelection(new List<RtsUnitMember> { unit });
			return;
		}

		SyncActiveInventoryToSelection();
	}

	private bool IsPointerOverUi()
	{
		return m_BlockPointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
	}

	private static bool IsCtrlPressed()
	{
		return Keyboard.current != null &&
		       (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
	}

	private static bool IsCtrlShiftHeld()
	{
		return IsCtrlPressed() && IsShiftHeld();
	}

	private void DrawRtsControlHintsIfAnySelection()
	{
		if (PauseMenuController.IsPaused)
			return;
		if (m_SelectedUnits == null || m_SelectedUnits.Count == 0)
			return;

		string hintText = m_SelectedUnits.Count >= 2
			? "ПКМ — перемещение · удерж. ПКМ + колёсико — интервал · потянуть ПКМ — фронт формации · Ctrl — фикс. взгляд · Ctrl+Shift — в очередь + фикс. взгляд · Ctrl+ЛКМ — взгляд · Shift+ЛКМ — готов + взгляд · X (коротко) — следующая формация · удерж. X — список · X+1..7 — выбор · двойной ПКМ — бег · маршрут: ПКМ по отрезку — стрелка (Ctrl — удержать взгляд)"
			: "ПКМ — перемещение · потянуть ПКМ — направление · Ctrl+ЛКМ — взгляд · Shift+ЛКМ — готов + взгляд · двойной ПКМ — бег · маршрут: ПКМ по отрезку — стрелка (Ctrl — удержать взгляд)";
		const float pad = 10f;
		const float height = 34f;

		if (s_RtsHintsGuiStyle == null)
		{
			s_RtsHintsGuiStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 13,
				alignment = TextAnchor.MiddleCenter,
				wordWrap = true
			};
			s_RtsHintsGuiStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
		}

		float width = Mathf.Min(920f, Screen.width - pad * 2f);
		GUI.Box(new Rect(pad, Screen.height - height - pad, width, height), hintText, s_RtsHintsGuiStyle);
	}

	private void DrawFormationPickerIfAny()
	{
		if (PauseMenuController.IsPaused)
			return;
		if (m_SelectedUnits == null || m_SelectedUnits.Count < 2)
			return;
		if (Keyboard.current == null || !Keyboard.current.xKey.isPressed)
			return;

		if (s_FormationPickerGuiStyle == null)
		{
			s_FormationPickerGuiStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 13,
				alignment = TextAnchor.UpperLeft,
				wordWrap = false,
				richText = true
			};
			s_FormationPickerGuiStyle.normal.textColor = new Color(0.92f, 0.92f, 0.98f, 1f);
		}

		FormationType current = GetDominantFormation(m_SelectedUnits);
		var lines = new System.Text.StringBuilder(256);
		for (int i = 1; i <= 7; i++)
		{
			FormationType type = FormationLayoutUtility.FormationFromHotkeyIndex(i);
			bool selected = type == current;
			string prefix = selected ? "<b>" : string.Empty;
			string suffix = selected ? "</b>" : string.Empty;
			lines.Append(prefix).Append(i).Append(". ").Append(FormationLayoutUtility.GetDisplayName(type)).Append(suffix);
			if (i < 7)
				lines.Append("   ");
		}

		const float pad = 10f;
		const float height = 52f;
		float width = Mathf.Min(980f, Screen.width - pad * 2f);
		float y = Screen.height - height - pad - 40f;
		GUI.Box(new Rect(pad, y, width, height), lines.ToString(), s_FormationPickerGuiStyle);
	}

	private static void ShowTransientMessage(string _message, float _durationSeconds = 3f)
	{
		if (string.IsNullOrWhiteSpace(_message))
			return;

		s_TransientMessage = _message;
		s_TransientMessageUntilUnscaledTime = Time.unscaledTime + Mathf.Max(0.5f, _durationSeconds);
	}

	private static void DrawTransientMessageIfAny()
	{
		if (string.IsNullOrEmpty(s_TransientMessage) || Time.unscaledTime > s_TransientMessageUntilUnscaledTime)
			return;

		if (s_TransientMessageGuiStyle == null)
		{
			s_TransientMessageGuiStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 15,
				alignment = TextAnchor.MiddleCenter,
				wordWrap = true
			};
			s_TransientMessageGuiStyle.normal.textColor = new Color(1f, 0.92f, 0.55f, 1f);
		}

		const float pad = 12f;
		const float height = 42f;
		float width = Mathf.Min(560f, Screen.width - pad * 2f);
		float y = Screen.height * 0.22f;
		GUI.Box(new Rect((Screen.width - width) * 0.5f, y, width, height), s_TransientMessage, s_TransientMessageGuiStyle);
	}

	private static Rect GetSelectionRect(Vector2 _start, Vector2 _end)
	{
		Vector2 topLeft = Vector2.Min(_start, _end);
		Vector2 bottomRight = Vector2.Max(_start, _end);
		return Rect.MinMaxRect(topLeft.x, Screen.height - bottomRight.y, bottomRight.x, Screen.height - topLeft.y);
	}

	private static void DrawScreenRect(Rect _rect, Color _color)
	{
		GUI.color = _color;
		GUI.DrawTexture(_rect, Texture2D.whiteTexture);
		GUI.color = Color.white;
	}

	private static void DrawScreenRectBorder(Rect _rect, float _thickness, Color _color)
	{
		DrawScreenRect(new Rect(_rect.xMin, _rect.yMin, _rect.width, _thickness), _color);
		DrawScreenRect(new Rect(_rect.xMin, _rect.yMax - _thickness, _rect.width, _thickness), _color);
		DrawScreenRect(new Rect(_rect.xMin, _rect.yMin, _thickness, _rect.height), _color);
		DrawScreenRect(new Rect(_rect.xMax - _thickness, _rect.yMin, _thickness, _rect.height), _color);
	}
	#endregion
}
