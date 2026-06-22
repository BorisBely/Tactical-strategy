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
	[SerializeField, Min(0.05f)] private float m_DoubleRightClickSeconds = 0.25f;
	[SerializeField] private bool m_SelectFirstPlayerUnitOnStart = true;

	[Header("Group Move Scatter")]
	[SerializeField, Min(0f)] private float m_GroupMoveScatterRadius = 0.4f;
	[SerializeField, Min(0f)] private float m_GroupMoveMinSeparation = 0.24f;
	[SerializeField, Range(0f, 0.75f)] private float m_GroupMoveScatterJitter = 0.08f;

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
	private RtsUnitMember m_PendingExchangePlayerUnit;
	private RtsUnitMember m_PendingExchangePartnerUnit;
	private static RtsUnitSelectionManager s_Instance;
	private static GUIStyle s_RtsHintsGuiStyle;
	private static GUIStyle s_TransientMessageGuiStyle;
	private static string s_TransientMessage;
	private static float s_TransientMessageUntilUnscaledTime = -1f;
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
		HandleRightMouseCommand();
		HandleKeyboardCommands();
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
		DrawTransientMessageIfAny();
	}
	#endregion

	#region Public Methods
	public void ClearSelection()
	{
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

	private void HandleRightMouseCommand()
	{
		if (Mouse.current == null || m_SelectionCamera == null || m_SelectedUnits.Count == 0)
			return;
		if (!Mouse.current.rightButton.wasPressedThisFrame)
			return;
		if (IsPointerOverUi())
			return;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		FallenUnitInteractionMenuController menu = FallenUnitInteractionMenuController.Instance;
		if (menu != null && menu.IsVisible && menu.IsScreenPointOverMenu(mousePosition))
			return;

		Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);
		if (TryRaycastAnyUnit(ray, out RaycastHit unitHit))
		{
			if (TryShowCarryReleaseMenu(unitHit, mousePosition))
				return;

			if (TryShowFallenUnitInteractionMenu(ray, unitHit, mousePosition))
				return;

			RtsUnitMember clickedUnit = unitHit.collider.GetComponentInParent<RtsUnitMember>();
			if (TryShowSelectedUnitFirstAidMenu(clickedUnit, mousePosition))
				return;

			return;
		}

		FallenUnitInteractionMenuController.Instance?.HideImmediate();

		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		bool doubleRightClick = m_LastRightClickTime >= 0f &&
		                        Time.time - m_LastRightClickTime <= m_DoubleRightClickSeconds;
		m_LastRightClickTime = Time.time;

		UnitClickToMove.MoveTier moveTier = doubleRightClick
			? UnitClickToMove.MoveTier.Sprint
			: UnitClickToMove.MoveTier.Walk;

		IssueScatteredMoveOrder(hit.point, moveTier);
	}

	private bool TryRaycastAnyUnit(Ray _ray, out RaycastHit _hit)
	{
		return Physics.Raycast(_ray, out _hit, 2000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide) &&
		       _hit.collider != null &&
		       _hit.collider.GetComponentInParent<RtsUnitMember>() != null;
	}

	private void HandleKeyboardCommands()
	{
		if (Keyboard.current == null || m_SelectedUnits.Count == 0)
			return;
		if (IsPointerOverUi())
			return;

		if (Keyboard.current.fKey.wasPressedThisFrame)
		{
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

	private void IssueScatteredMoveOrder(Vector3 _centerPoint, UnitClickToMove.MoveTier _moveTier)
	{
		List<RtsUnitMember> validUnits = new List<RtsUnitMember>(m_SelectedUnits.Count);
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit != null)
				validUnits.Add(unit);
		}

		if (validUnits.Count == 0)
			return;

		if (validUnits.Count == 1)
		{
			validUnits[0].IssueMoveOrder(_centerPoint, _moveTier);
			return;
		}

		List<Vector3> offsets = new List<Vector3>(validUnits.Count)
		{
			Vector3.zero
		};

		float effectiveRadius = Mathf.Max(
			m_GroupMoveScatterRadius,
			m_GroupMoveMinSeparation * 0.45f * Mathf.Sqrt(validUnits.Count));
		float candidateRadius = effectiveRadius * (1f + m_GroupMoveScatterJitter);
		float minSeparation = Mathf.Max(0.12f, m_GroupMoveMinSeparation);

		for (int i = 1; i < validUnits.Count; i++)
		{
			Vector3 chosenOffset = Vector3.zero;
			bool found = false;

			for (int attempt = 0; attempt < 18; attempt++)
			{
				Vector2 candidate2D = Random.insideUnitCircle * candidateRadius;
				Vector3 candidate = new Vector3(candidate2D.x, 0f, candidate2D.y);
				bool overlaps = false;

				for (int placedIndex = 0; placedIndex < offsets.Count; placedIndex++)
				{
					if ((offsets[placedIndex] - candidate).sqrMagnitude < minSeparation * minSeparation)
					{
						overlaps = true;
						break;
					}
				}

				if (overlaps)
					continue;

				chosenOffset = candidate;
				found = true;
				break;
			}

			if (!found)
			{
				float angleRadians = Random.Range(0f, Mathf.PI * 2f);
				float radius = Mathf.Min(candidateRadius, minSeparation * 0.9f + i * 0.03f);
				chosenOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * radius;
			}

			offsets.Add(chosenOffset);
		}

		for (int i = 0; i < validUnits.Count; i++)
			validUnits[i].IssueMoveOrder(_centerPoint + offsets[i], _moveTier);
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
			"Выделение: F стоп · E готов · Z/C стойка · T зарядка магазина · R перезарядка · V режим огня";
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
