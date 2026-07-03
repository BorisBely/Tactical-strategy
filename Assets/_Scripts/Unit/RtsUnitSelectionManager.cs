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
	[SerializeField, Min(0.05f)] private float m_DoubleRightClickSeconds = 0.12f;
	[SerializeField, Min(1f)] private float m_QuickRotateDragThresholdPixels = 90f;
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

	[Header("Group Move Formation")]
	[Tooltip("Добавочные метры к диаметру агента (NavMeshAgent.radius * 2) для расстояния между юнитами в строю.")]
	[SerializeField, Min(0f)] private float m_GroupMoveUnitPadding = 0.5f;
	[Tooltip("Максимальный случайный сдвиг позиции юнита в формации для естественности.")]
	[SerializeField, Range(0f, 0.5f)] private float m_GroupMoveFormationJitter = 0.10f;
	[Header("Formation Line")]
	[Tooltip("Базовый интервал между юнитами в шеренге (метры).")]
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
	[Header("Group Move Stagger")]
	[Tooltip("Мин. задержка перед стартом следующего юнита (сек).")]
	[SerializeField, Range(0f, 0.2f)] private float m_GroupMoveStaggerMin = 0.03f;
	[Tooltip("Макс. задержка перед стартом следующего юнита (сек).")]
	[SerializeField, Range(0f, 0.2f)] private float m_GroupMoveStaggerMax = 0.10f;

	[Header("Destination Markers")]
	[SerializeField] private GameObject m_DestinationMarkerPrefab;
	[Tooltip("Маркер вдоль пути (каждые N метров).")]
	[SerializeField] private GameObject m_PathMarkerPrefab;
	[Tooltip("Интервал расстановки маркеров пути (метры).")]
	[SerializeField, Min(0.5f)] private float m_PathMarkerInterval = 1f;

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
	private Coroutine m_StaggerCoroutine;
	private bool m_IsPreviewingMove;
	private bool m_PreviewCancelled;
	private bool m_PreviewPending;
	private float m_PreviewPendingTime;
	private Vector3 m_PreviewCenterPoint;
	private List<Vector3> m_PreviewOffsets;
	private Vector3 m_LastWalkCenter;
	private List<Vector3> m_LastWalkOffsets;
	private bool m_IsQuickRotateFacing;
	private bool m_HasMoveFacingSet;
	private bool m_RmbStartedOnSelectedUnit;
	private Vector2 m_RmbDownMousePos;
	private List<float> m_PreviewFacingAngles;
	private float m_CurrentFormationSpacing;
	private Coroutine m_PendingWalkCoroutine;
	private readonly List<GameObject> m_DirectionMarkers = new List<GameObject>();
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
			if (_combatStats != null)
				return true;
		}

		return false;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;

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
		if (unit == null || !unit.IsPlayerSelectable || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(unit))
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
		if (_unit == null || !_unit.isActiveAndEnabled || !_unit.IsPlayerSelectable)
			return false;
		if (MissionPrepSquadSpawner.IsMissionPrepPresentationMember(_unit))
			return false;

		UnitConsciousness consciousness = _unit.GetComponentInChildren<UnitConsciousness>(true);
		if (consciousness != null && !consciousness.IsConscious)
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
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(unit))
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

	private void UpdatePathInteractions()
	{
		if (m_IsEditingWaypointFacing)
		{
			ClearArrowHover();
			ClearRouteEditMode();
			ClearPathSegmentHover();
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
		ClearArrowHover();
		ClearRouteEditMode();
		ClearPathSegmentHover();
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

		Vector2 mouseScreen = Mouse.current.position.ReadValue();

		if (TryPickRouteSegment(mouseScreen, out int routeUnitIndex, out int routeSegmentIndex, out Vector3 routeWorldPoint))
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

		if (TryPickRouteVertex(mouseScreen, out routeUnitIndex, out int routeVertexIndex, out _))
		{
			if (routeUnitIndex < 0 || routeUnitIndex >= m_SelectedUnits.Count)
				return false;

			RtsUnitMember unit = m_SelectedUnits[routeUnitIndex];
			if (unit == null || routeVertexIndex < 0)
				return false;

			return unit.TrySetWaitGroupForWaypoint(routeVertexIndex, unit.GetNextAutoWaitGroup());
		}

		return false;
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
		if (Mouse.current == null || m_SelectionCamera == null || m_SelectedUnits.Count == 0)
		{
			if (m_IsEditingWaypointFacing)
				EndWaypointFacingEdit();
			if (m_IsPreviewingMove || m_PreviewPending)
				CancelMovePreview();
			return;
		}
		if (IsPointerOverUi())
			return;

		if (m_IsEditingWaypointFacing)
		{
			if (!Mouse.current.rightButton.isPressed)
				EndWaypointFacingEdit();
			else
				UpdateWaypointFacingEdit();
			return;
		}

		bool wasPressed = Mouse.current.rightButton.wasPressedThisFrame;
		bool wasReleased = Mouse.current.rightButton.wasReleasedThisFrame;

		if (wasPressed)
			HandleRightMouseDown();

		if (wasReleased)
			HandleRightMouseUp();

		if (m_IsPreviewingMove && Mouse.current.rightButton.isPressed)
		{
			if (CanPreviewMoveFacing())
			{
				Vector2 mousePos = Mouse.current.position.ReadValue();
				if (!m_IsQuickRotateFacing &&
				    (mousePos - m_RmbDownMousePos).magnitude >= m_QuickRotateDragThresholdPixels)
				{
					EnterQuickRotateMode();
				}

				if (m_IsQuickRotateFacing)
				{
					UpdateQuickRotateMode();
					return;
				}
			}

			if (m_IsQuickRotateFacing)
				ExitMoveFacingMode();

			UpdateMovePreview();
			HandleFormationScroll();
		}
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

		if (m_IsHoveringPathSegment && m_SelectedUnits.Count > 0 && !IsAltHeld())
		{
			BeginWaypointFacingEdit(m_HoveredUnitIndex, m_HoveredSegmentIndex);
			return;
		}

		Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);
		Vector3? unitForcedGroundPoint = null;

		if (TryRaycastAnyUnit(ray, out RaycastHit unitHit))
		{
			if (TryShowCarryReleaseMenu(unitHit, mousePosition))
				return;

			if (TryShowFallenUnitInteractionMenu(ray, unitHit, mousePosition))
				return;

			RtsUnitMember clickedUnit = unitHit.collider.GetComponentInParent<RtsUnitMember>();
			if (TryShowSelectedUnitFirstAidMenu(clickedUnit, mousePosition))
				return;

			if (clickedUnit != null && m_SelectedUnits.Contains(clickedUnit))
			{
				List<RtsUnitMember> unitsForPoint = GetValidSelectedUnits();
				if (unitsForPoint.Count >= 2 && GetDominantFormation(unitsForPoint) == FormationType.Line)
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
		if (unitForcedGroundPoint.HasValue)
		{
			hitPoint = unitForcedGroundPoint.Value;
		}
		else if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
		{
			return;
		}
		else
		{
			hitPoint = hit.point;
		}

		if (m_LastRightClickTime >= 0f &&
		    Time.unscaledTime - m_LastRightClickTime <= m_DoubleRightClickSeconds)
		{
			m_LastRightClickTime = -1f;
			ClearPreviewMarkers();
			m_IsPreviewingMove = false;
			m_PreviewPending = false;

			if (m_PendingWalkCoroutine != null)
			{
				StopCoroutine(m_PendingWalkCoroutine);
				m_PendingWalkCoroutine = null;
			}

			List<RtsUnitMember> doubleUnits = GetValidSelectedUnits();
			if (doubleUnits.Count == 0)
				return;

			bool useSavedWalkTarget = m_LastWalkOffsets != null && m_LastWalkOffsets.Count > 0;
			Vector3 runCenter = useSavedWalkTarget ? m_LastWalkCenter : hitPoint;
			List<Vector3> runOffsets = useSavedWalkTarget
				? new List<Vector3>(m_LastWalkOffsets)
				: BuildFormationOffsets(doubleUnits, hitPoint);

			if (IsShiftHeld())
			{
				ShiftEnqueueMoveOrders(doubleUnits, runOffsets, runCenter, null, UnitClickToMove.MoveTier.Run);
				EnsureFormationSyncGroup(doubleUnits, runOffsets, runCenter);
			}
			else
			{
				bool allUpgraded = doubleUnits.Count > 0;
				for (int i = 0; i < doubleUnits.Count; i++)
				{
					Vector3 dest = i < runOffsets.Count ? runCenter + runOffsets[i] : runCenter;
					if (!doubleUnits[i].TryUpgradeMoveTargetToRun(dest))
					{
						allUpgraded = false;
						break;
					}
				}

				if (allUpgraded)
					return;

				foreach (var u in doubleUnits)
					u.ClearCommandQueue();
				IssueScatteredMoveOrder(runCenter, UnitClickToMove.MoveTier.Run, runOffsets);
			}
			return;
		}

		ClearPreviewMarkers();

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		m_PreviewPending = false;
		m_PreviewCancelled = false;
		m_HasMoveFacingSet = false;
		m_RmbStartedOnSelectedUnit = unitForcedGroundPoint.HasValue;
		m_IsPreviewingMove = true;
		m_PreviewCenterPoint = hitPoint;
		m_RmbDownMousePos = mousePosition;

		if (GetDominantFormation(validUnits) == FormationType.Line && m_CurrentFormationSpacing <= 0f)
			m_CurrentFormationSpacing = m_FormationLineSpacing;

		if (validUnits.Count == 1)
			m_PreviewOffsets = new List<Vector3> { Vector3.zero };
		else
			m_PreviewOffsets = BuildFormationOffsets(validUnits, hitPoint);

		ApplyPreviewPathLines();
	}

	private void ApplyPreviewPathLines()
	{
		if (m_PreviewOffsets == null)
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
			validUnits[i].SetPreviewLine(m_PreviewCenterPoint + m_PreviewOffsets[i]);
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
	}

	private void HandleFormationScroll()
	{
		FormationType dominant = m_SelectedUnits.Count > 0
			? GetDominantFormation(m_SelectedUnits)
			: FormationType.None;

		if (dominant != FormationType.Line)
			return;

		if (Mouse.current == null)
			return;

		Vector2 scroll = Mouse.current.scroll.ReadValue();
		if (Mathf.Approximately(scroll.y, 0f))
			return;

		float step = m_FormationLineSpacingStep;
		m_CurrentFormationSpacing = Mathf.Clamp(
			m_CurrentFormationSpacing + scroll.y * step,
			m_FormationLineSpacingMin,
			m_FormationLineSpacingMax);

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count < 2)
			return;

		m_PreviewOffsets = BuildFormationOffsets(validUnits, m_PreviewCenterPoint);

		for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
			validUnits[i].SetPreviewLine(m_PreviewCenterPoint + m_PreviewOffsets[i]);
	}

	private void CreateDirectionMarkers()
	{
		int count = m_PreviewOffsets.Count;
		m_PreviewFacingAngles = new List<float>(count);
		for (int i = 0; i < count; i++)
			m_PreviewFacingAngles.Add(0f);

		for (int i = 0; i < count; i++)
		{
			GameObject arrowGo = new GameObject("FacingLine");
			LineRenderer lr = arrowGo.AddComponent<LineRenderer>();
			lr.positionCount = 2;
			lr.startWidth = 0.02f;
			lr.endWidth = 0.02f;
			lr.material = new Material(Shader.Find("Sprites/Default"));
			lr.startColor = new Color(1f, 0.85f, 0.2f, 0.95f);
			lr.endColor = new Color(1f, 0.85f, 0.2f, 0.95f);
			m_DirectionMarkers.Add(arrowGo);
		}
	}

	private bool CanPreviewMoveFacing()
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		return validUnits.Count == 1 ||
		       (validUnits.Count >= 2 && GetDominantFormation(validUnits) == FormationType.Line);
	}

	private void EnterMoveFacingMode()
	{
		if (!CanPreviewMoveFacing())
			return;

		m_IsQuickRotateFacing = true;
		if (m_DirectionMarkers.Count == 0)
			CreateDirectionMarkers();
		UpdateQuickRotateMode();
	}

	private void ExitMoveFacingMode()
	{
		if (!m_IsQuickRotateFacing)
			return;

		m_IsQuickRotateFacing = false;
		for (int i = 0; i < m_DirectionMarkers.Count; i++)
		{
			if (m_DirectionMarkers[i] != null)
				Destroy(m_DirectionMarkers[i]);
		}
		m_DirectionMarkers.Clear();

		if (m_PreviewFacingAngles != null && m_PreviewFacingAngles.Count > 0)
			m_HasMoveFacingSet = true;

		ApplyPreviewPathLines();
	}

	private void EnterQuickRotateMode()
	{
		m_PreviewPending = false;
		m_IsPreviewingMove = true;
		EnterMoveFacingMode();
	}

	private void UpdateQuickRotateMode()
	{
		if (m_PreviewOffsets == null || m_PreviewFacingAngles == null)
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		if (validUnits.Count == 1)
		{
			Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[0];
			Vector3 toCursor = hit.point - dest;
			toCursor.y = 0f;
			float angle = toCursor.sqrMagnitude > 0.01f
				? Mathf.Atan2(toCursor.x, toCursor.z) * Mathf.Rad2Deg
				: 0f;
			m_PreviewFacingAngles[0] = angle;

			if (m_DirectionMarkers.Count > 0 && m_DirectionMarkers[0] != null)
			{
				Vector3 dir = toCursor.sqrMagnitude > 0.01f ? toCursor.normalized : Vector3.forward;
				LineRenderer lr = m_DirectionMarkers[0].GetComponent<LineRenderer>();
				if (lr != null)
				{
					lr.SetPosition(0, dest + dir * 0.15f);
					lr.SetPosition(1, dest + dir * 2f);
				}
			}
		}
		else
		{
			Vector3 toCursor = hit.point - m_PreviewCenterPoint;
			toCursor.y = 0f;
			if (toCursor.sqrMagnitude < 0.01f)
				return;

			Vector3 formationForward = toCursor.normalized;
			float formationAngle = Mathf.Atan2(formationForward.x, formationForward.z) * Mathf.Rad2Deg;

			m_PreviewOffsets = BuildLineOffsetsNoJitter(validUnits, m_PreviewCenterPoint, formationForward);

			for (int i = 0; i < validUnits.Count && i < m_PreviewFacingAngles.Count; i++)
				m_PreviewFacingAngles[i] = formationAngle;

			for (int i = 0; i < validUnits.Count && i < m_PreviewOffsets.Count; i++)
			{
				validUnits[i].SetPreviewLine(m_PreviewCenterPoint + m_PreviewOffsets[i]);

				if (i < m_DirectionMarkers.Count && m_DirectionMarkers[i] != null)
				{
					Vector3 dest = m_PreviewCenterPoint + m_PreviewOffsets[i];
					LineRenderer lr = m_DirectionMarkers[i].GetComponent<LineRenderer>();
					if (lr != null)
					{
						lr.SetPosition(0, dest + formationForward * 0.15f);
						lr.SetPosition(1, dest + formationForward * 2f);
					}
				}
			}
		}
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

		// Build a single direction marker for the preview
		if (m_DirectionMarkers.Count == 0)
		{
			GameObject arrowGo = new GameObject("FacingLine");
			LineRenderer lr = arrowGo.AddComponent<LineRenderer>();
			lr.positionCount = 2;
			lr.startWidth = 0.02f;
			lr.endWidth = 0.02f;
			lr.material = new Material(Shader.Find("Sprites/Default"));
			Color initialColor = GetFacingArrowColor(m_EditingWaypointMode);
			lr.startColor = initialColor;
			lr.endColor = initialColor;
			m_DirectionMarkers.Add(arrowGo);
		}

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
			Vector3 shaftStart = anchor + dir * 0.15f;
			Vector3 tip = m_EditingWaypointMode == RtsUnitMember.FacingArrowMode.LookAtPoint && toCursor.sqrMagnitude > 0.01f
				? hit.point
				: anchor + dir * 2f;
			LineRenderer lr = m_DirectionMarkers[0].GetComponent<LineRenderer>();
			if (lr != null)
			{
				lr.startColor = arrowColor;
				lr.endColor = arrowColor;
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

	private List<Vector3> BuildLineOffsetsNoJitter(List<RtsUnitMember> _units, Vector3 _centerPoint, Vector3 _formationForward)
	{
		int count = _units.Count;
		float spacing = m_CurrentFormationSpacing > 0f ? m_CurrentFormationSpacing : m_FormationLineSpacing;

		Vector3 formationRight = Vector3.Cross(Vector3.up, _formationForward).normalized;

		List<UnitProjEntry> projEntries = new List<UnitProjEntry>(count);
		for (int i = 0; i < count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;
			float proj = Vector3.Dot(unit.transform.position, formationRight);
			projEntries.Add(new UnitProjEntry { Index = i, Proj = proj });
		}
		projEntries.Sort((a, b) => a.Proj.CompareTo(b.Proj));

		float totalWidth = (projEntries.Count - 1) * spacing;
		float startX = -totalWidth * 0.5f;

		Vector3[] offsets = new Vector3[count];
		for (int i = 0; i < projEntries.Count; i++)
		{
			float localX = startX + i * spacing;
			offsets[projEntries[i].Index] = formationRight * localX;
		}

		return new List<Vector3>(offsets);
	}

	private void EnsureFormationSyncGroup(List<RtsUnitMember> _units, List<Vector3> _offsets, Vector3 _center)
	{
		if (_units.Count < 2)
			return;
		if (GetDominantFormation(_units) != FormationType.Line)
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

		List<Vector3> offsets = m_PreviewOffsets;
		Vector3 center = m_PreviewCenterPoint;
		bool cancelled = m_PreviewCancelled;
		bool useMoveFacing = m_HasMoveFacingSet || m_IsQuickRotateFacing;
		RtsUnitMember.FacingArrowMode? facingMode = useMoveFacing
			? RtsUnitMember.FacingArrowMode.TurnOverDistance
			: null;
		List<float> facingAngles = useMoveFacing ? m_PreviewFacingAngles : null;

		ClearPreviewMarkers();

		if (cancelled)
			return;

		if (offsets == null || offsets.Count == 0)
			return;

		m_LastWalkCenter = center;
		m_LastWalkOffsets = new List<Vector3>(offsets);
		ExecuteWalkOrder(offsets, center, facingAngles, IsAltHeld() ? -1 : 0, facingMode);
	}

	private void ExecuteWalkOrder(
		List<Vector3> _offsets,
		Vector3 _center,
		List<float> _facingAngles,
		int _waitGroup = 0,
		RtsUnitMember.FacingArrowMode? _facingMode = null)
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		if (validUnits.Count == 0)
			return;

		bool shift = IsShiftHeld();
		bool useWait = _waitGroup != 0;

		RtsUnitMember.FacingArrowMode facingMode = _facingMode ?? RtsUnitMember.FacingArrowMode.TurnOverDistance;

		if (shift)
		{
			ShiftEnqueueMoveOrders(validUnits, _offsets, _center, _facingAngles, UnitClickToMove.MoveTier.Walk, _waitGroup, facingMode);
			EnsureFormationSyncGroup(validUnits, _offsets, _center);
			return;
		}

		if (useWait && !shift)
		{
			for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
			{
				int waitGroup = _waitGroup > 0 ? _waitGroup : 1;
				float? facing = (_facingAngles != null && i < _facingAngles.Count)
					? _facingAngles[i]
					: (float?)null;
				validUnits[i].IssueDirectMoveOrderWithWait(
					_center + _offsets[i],
					UnitClickToMove.MoveTier.Walk,
					facing,
					facingMode,
					waitGroup);
			}

			ApplyFormationSyncSpeeds(validUnits, _offsets, _center);
			return;
		}

		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i].ClearCommandQueue();

		bool[] inPlaceFacing = new bool[validUnits.Count];
		for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
		{
			Vector3 dest = _center + _offsets[i];
			bool hasFacing = _facingAngles != null && i < _facingAngles.Count;
			inPlaceFacing[i] = hasFacing && IsNearMoveDestination(validUnits[i].transform.position, dest);

			if (inPlaceFacing[i])
			{
				validUnits[i].IssueInPlaceFacingOrder(_facingAngles[i], facingMode);
				continue;
			}

			validUnits[i].SetDestinationDirect(dest);
			if (hasFacing)
				validUnits[i].SetWaypointFacing(0, _facingAngles[i], dest, facingMode);
		}

		ApplyFormationSyncSpeeds(validUnits, _offsets, _center);

		bool isFormationSync = validUnits.Count >= 2 && GetDominantFormation(validUnits) == FormationType.Line;
		bool needStagger = !isFormationSync && m_GroupMoveStaggerMax > 0f && validUnits.Count > 1;

		if (needStagger)
		{
			m_StaggerCoroutine = StartCoroutine(
				StaggeredMoveOrdersRoutine(validUnits, _offsets, _center, UnitClickToMove.MoveTier.Walk));
		}
		else
		{
			for (int i = 0; i < validUnits.Count && i < _offsets.Count; i++)
			{
				if (inPlaceFacing[i])
					continue;

				validUnits[i].IssueMoveOrder(_center + _offsets[i], UnitClickToMove.MoveTier.Walk);
			}
		}
	}

	private static bool IsNearMoveDestination(Vector3 _from, Vector3 _to, float _epsilon = 0.75f)
	{
		Vector3 flatFrom = _from;
		flatFrom.y = 0f;
		Vector3 flatTo = _to;
		flatTo.y = 0f;
		return (flatTo - flatFrom).sqrMagnitude <= _epsilon * _epsilon;
	}

	private void ShiftEnqueueMoveOrders(List<RtsUnitMember> _units, List<Vector3> _offsets,
		Vector3 _center, List<float> _facingAngles, UnitClickToMove.MoveTier _tier, int _waitGroup = 0,
		RtsUnitMember.FacingArrowMode _facingMode = RtsUnitMember.FacingArrowMode.TurnOverDistance)
	{
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
			_units[i].EnqueueWaypoint(dest, _tier, facing, _facingMode, waitGroup);
		}
	}

	private void CancelMovePreview()
	{
		if (!m_IsPreviewingMove && !m_PreviewPending)
			return;

		m_PreviewPending = false;
		m_IsPreviewingMove = false;
		m_PreviewCancelled = true;
		ClearPreviewMarkers();
	}

	private void ClearPreviewMarkers()
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();
		for (int i = 0; i < validUnits.Count; i++)
		{
			RtsUnitMember unit = validUnits[i];
			if (unit == null)
				continue;
			if (!unit.HasActiveDestination && !unit.HasQueuedCommands)
				unit.ClearWaypoints();
		}

		for (int i = 0; i < m_DirectionMarkers.Count; i++)
		{
			if (m_DirectionMarkers[i] != null)
				Destroy(m_DirectionMarkers[i]);
		}
		m_DirectionMarkers.Clear();

		m_IsQuickRotateFacing = false;
		m_HasMoveFacingSet = false;
		m_RmbStartedOnSelectedUnit = false;
		m_PreviewFacingAngles = null;
		m_PreviewOffsets = null;
	}

	private List<RtsUnitMember> GetValidSelectedUnits()
	{
		List<RtsUnitMember> validUnits = new List<RtsUnitMember>(m_SelectedUnits.Count);
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit != null)
				validUnits.Add(unit);
		}
		return validUnits;
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

		if (!IsPointerOverUi())
		{
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
		}

		if (m_SelectedUnits.Count == 0)
			return;
		if (IsPointerOverUi())
			return;

		if (Keyboard.current.fKey.wasPressedThisFrame)
		{
			if (m_IsPreviewingMove)
				CancelMovePreview();
			else
				CommandSelectedHardStop();
			return;
		}

		if (Keyboard.current.eKey.wasPressedThisFrame)
		{
			ToggleSelectedReady();
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

		if (Keyboard.current.xKey.wasPressedThisFrame)
		{
			CycleSelectedFormation();
			return;
		}
	}

	private void CommandSelectedStance(LocomotionStance _stance)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.RequestStance(_stance);
		}
	}

	private void SetSelectedReady(bool _ready)
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.SetReadyWanted(_ready);
		}
	}

	private void CommandSelectedHardStop()
	{
		if (m_StaggerCoroutine != null)
		{
			StopCoroutine(m_StaggerCoroutine);
			m_StaggerCoroutine = null;
		}

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
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			unit.StartManualMagazineLoading();
		}
	}

	private void CommandSelectedWeaponReload()
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			unit.StartWeaponReload();
		}
	}

	private void CommandSelectedCycleWeaponFireMode()
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			unit.CycleWeaponFireMode();
		}
	}

	private void CommandSelectedCycleWeaponAimMode()
	{
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;

			unit.CycleWeaponAimMode();
		}
	}

	private void CycleSelectedFormation()
	{
		FormationType current = m_SelectedUnits.Count > 0
			? m_SelectedUnits[0].CurrentFormation
			: FormationType.None;

		FormationType next = current switch
		{
			FormationType.None => FormationType.Line,
			FormationType.Line => FormationType.None,
			_ => FormationType.None
		};

		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			unit.CurrentFormation = next;
		}

		m_CurrentFormationSpacing = m_FormationLineSpacing;
	}

	private void IssueScatteredMoveOrder(Vector3 _centerPoint, UnitClickToMove.MoveTier _moveTier,
		List<Vector3> _prebuiltOffsets = null)
	{
		List<RtsUnitMember> validUnits = GetValidSelectedUnits();

		if (validUnits.Count == 0)
			return;

		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i].ClearCommandQueue();

		if (validUnits.Count == 1)
		{
			validUnits[0].SetDestinationDirect(_centerPoint, _moveTier);
			validUnits[0].BeginActiveRouteMovement(_moveTier);
			return;
		}

		List<Vector3> offsets = _prebuiltOffsets ?? BuildFormationOffsets(validUnits, _centerPoint);

		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i].SetDestinationDirect(_centerPoint + offsets[i], _moveTier);

		ApplyFormationSyncSpeeds(validUnits, offsets, _centerPoint);

		bool isFormationSync = validUnits.Count >= 2 && GetDominantFormation(validUnits) == FormationType.Line;
		if (isFormationSync || m_GroupMoveStaggerMax <= 0f)
		{
			for (int i = 0; i < validUnits.Count; i++)
				validUnits[i].BeginActiveRouteMovement(_moveTier);
		}
		else
		{
			m_StaggerCoroutine = StartCoroutine(StaggeredMoveOrdersRoutine(validUnits, offsets, _centerPoint, _moveTier));
		}
	}

	private System.Collections.IEnumerator StaggeredMoveOrdersRoutine(
		List<RtsUnitMember> _units, List<Vector3> _offsets,
		Vector3 _centerPoint, UnitClickToMove.MoveTier _moveTier)
	{
		float minDelay = Mathf.Max(0f, m_GroupMoveStaggerMin);
		float maxDelay = Mathf.Max(minDelay, m_GroupMoveStaggerMax);

		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] != null)
				_units[i].BeginActiveRouteMovement(_moveTier);

			if (i < _units.Count - 1)
				yield return new WaitForSecondsRealtime(Random.Range(minDelay, maxDelay));
		}

		m_StaggerCoroutine = null;
	}

	private List<Vector3> BuildFormationOffsets(List<RtsUnitMember> _units, Vector3 _centerPoint)
	{
		FormationType formation = GetDominantFormation(_units);

		if (formation == FormationType.Line && _units.Count >= 2)
			return BuildLineOffsets(_units, _centerPoint);

		return BuildScatteredOffsets(_units, _centerPoint);
	}

	private List<Vector3> BuildLineOffsets(List<RtsUnitMember> _units, Vector3 _centerPoint)
	{
		int count = _units.Count;
		float spacing = m_CurrentFormationSpacing > 0f ? m_CurrentFormationSpacing : m_FormationLineSpacing;

		Vector3 avgUnitPos = Vector3.zero;
		for (int i = 0; i < count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit != null)
				avgUnitPos += unit.transform.position;
		}
		avgUnitPos /= count;

		Vector3 toTarget = _centerPoint - avgUnitPos;
		toTarget.y = 0f;

		Vector3 formationForward;
		if (toTarget.sqrMagnitude > 0.01f)
			formationForward = toTarget.normalized;
		else
			formationForward = Vector3.forward;

		Vector3 formationRight = Vector3.Cross(Vector3.up, formationForward).normalized;

		List<UnitProjEntry> projEntries = new List<UnitProjEntry>(count);
		for (int i = 0; i < count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;
			float proj = Vector3.Dot(unit.transform.position, formationRight);
			projEntries.Add(new UnitProjEntry { Index = i, Proj = proj });
		}
		projEntries.Sort((a, b) => a.Proj.CompareTo(b.Proj));

		float totalWidth = (projEntries.Count - 1) * spacing;
		float startX = -totalWidth * 0.5f;

		Vector3[] offsets = new Vector3[count];
		for (int i = 0; i < projEntries.Count; i++)
		{
			float localX = startX + i * spacing;
			offsets[projEntries[i].Index] = formationRight * localX;
		}

		float jitter = Mathf.Min(m_GroupMoveFormationJitter, spacing * 0.3f);
		if (jitter > 0.0001f)
		{
			for (int i = 0; i < count; i++)
			{
				offsets[i].x += Random.Range(-jitter, jitter);
				offsets[i].z += Random.Range(-jitter, jitter);
			}
		}

		return new List<Vector3>(offsets);
	}

	private List<Vector3> BuildScatteredOffsets(List<RtsUnitMember> _units, Vector3 _centerPoint)
	{
		int count = _units.Count;

		float agentRadius = 0.5f;
		for (int i = 0; i < count; i++)
		{
			UnityEngine.AI.NavMeshAgent agent = _units[i].GetComponent<UnityEngine.AI.NavMeshAgent>();
			if (agent != null)
			{
				agentRadius = agent.radius;
				break;
			}
		}

		float spacing = agentRadius * 2f + m_GroupMoveUnitPadding;
		float minSeparation = agentRadius * 2f + Mathf.Max(0.1f, m_GroupMoveUnitPadding * 0.5f);
		float jitter = Mathf.Min(m_GroupMoveFormationJitter, spacing * 0.3f);

		Vector3 avgUnitPos = Vector3.zero;
		for (int i = 0; i < count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit != null)
				avgUnitPos += unit.transform.position;
		}
		avgUnitPos /= count;

		Vector3 toTarget = _centerPoint - avgUnitPos;
		toTarget.y = 0f;

		Vector3 formationForward;
		if (toTarget.sqrMagnitude > 0.01f)
			formationForward = toTarget.normalized;
		else
			formationForward = Vector3.forward;

		// --- sort units by distance to target ---
		List<UnitDistEntry> unitEntries = new List<UnitDistEntry>(count);
		for (int i = 0; i < count; i++)
		{
			Vector3 toUnit = _units[i].transform.position - _centerPoint;
			toUnit.y = 0f;
			unitEntries.Add(new UnitDistEntry { Index = i, SqrDist = toUnit.sqrMagnitude });
		}
		unitEntries.Sort((a, b) => a.SqrDist.CompareTo(b.SqrDist));

		// --- generate random positions with min separation ---
		float scatterRadius = Mathf.Max(spacing, spacing * 0.6f * Mathf.Sqrt(count));
		List<Vector3> positions = new List<Vector3>(count);

		for (int i = 0; i < count; i++)
		{
			bool found = false;

			for (int attempt = 0; attempt < 24; attempt++)
			{
				Vector2 c = Random.insideUnitCircle * scatterRadius;
				Vector3 candidate = new Vector3(c.x, 0f, c.y);

				bool overlaps = false;
				for (int p = 0; p < positions.Count; p++)
				{
					if ((positions[p] - candidate).sqrMagnitude < minSeparation * minSeparation)
					{
						overlaps = true;
						break;
					}
				}

				if (overlaps)
					continue;

				positions.Add(candidate);
				found = true;
				break;
			}

			if (!found)
			{
				float angle = (float)i / count * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
				float radius = scatterRadius * (0.5f + 0.5f * (float)i / count);
				positions.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
			}
		}

		// --- sort positions by depth along formationForward ---
		List<PosDepthEntry> posEntries = new List<PosDepthEntry>(count);
		for (int i = 0; i < count; i++)
			posEntries.Add(new PosDepthEntry { Position = positions[i], Depth = Vector3.Dot(positions[i], formationForward) });
		posEntries.Sort((a, b) => a.Depth.CompareTo(b.Depth));

		// --- assign: closest unit → rear pos (index 0), furthest unit → front pos (index count-1) ---
		Vector3[] offsets = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			int unitOriginalIndex = unitEntries[i].Index;
			offsets[unitOriginalIndex] = posEntries[count - 1 - i].Position;
		}

		// --- apply jitter ---
		if (jitter > 0.0001f)
		{
			for (int i = 0; i < count; i++)
			{
				offsets[i].x += Random.Range(-jitter, jitter);
				offsets[i].z += Random.Range(-jitter, jitter);
			}
		}

		return new List<Vector3>(offsets);
	}

	private struct UnitDistEntry
	{
		public int Index;
		public float SqrDist;
	}

	private struct UnitProjEntry
	{
		public int Index;
		public float Proj;
	}

	private struct PosDepthEntry
	{
		public Vector3 Position;
		public float Depth;
	}

	private void SyncFormationToSelection()
	{
		if (m_SelectedUnits.Count < 2)
			return;

		FormationType dominant = GetDominantFormation(m_SelectedUnits);
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
		int countNone = 0;
		int countLine = 0;

		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;

			switch (unit.CurrentFormation)
			{
				case FormationType.Line:
					countLine++;
					break;
				default:
					countNone++;
					break;
			}
		}

		if (countLine > countNone)
			return FormationType.Line;
		return FormationType.None;
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
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(unit))
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
			_unit == null || !_unit.isActiveAndEnabled || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(_unit));

		SyncFormationToSelection();

		SyncActiveInventoryToSelection();
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
		Object prefabObj = definition.DropWorldPrefab;
		if (prefabObj == null)
			return null;
		Object instanceObj = Object.Instantiate(prefabObj, position, rotation);
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
			_spawned.RegisterListedInGroundUi();

		m_GroundPanel.RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.EnsureGroundPanelUiHooks();
		RuntimeInventoryModificationCoordinator.Instance?.OnGroundPanelRepopulated();
	}

	private void TrySelectFirstPlayerUnit()
	{
		IReadOnlyList<RtsUnitMember> units = RtsUnitMember.Instances;
		for (int i = 0; i < units.Count; i++)
		{
			RtsUnitMember unit = units[i];
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(unit))
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

	private void DrawRtsControlHintsIfAnySelection()
	{
		if (PauseMenuController.IsPaused)
			return;
		if (m_SelectedUnits == null || m_SelectedUnits.Count == 0)
			return;

		const string c_HintText =
			"ПКМ — перемещение · потянуть ПКМ — направление · двойной ПКМ — бег · маршрут: ПКМ по отрезку — стрелка (Ctrl — удержать взгляд) · X — удалить стрелку";
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

		float width = Mathf.Min(820f, Screen.width - pad * 2f);
		GUI.Box(new Rect(pad, Screen.height - height - pad, width, height), c_HintText, s_RtsHintsGuiStyle);
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
