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
	private static RtsUnitSelectionManager s_Instance;
	private static GUIStyle s_RtsHintsGuiStyle;
	#endregion

	#region Public Properties
	public static RtsUnitSelectionManager Instance => s_Instance;
	public int SelectedUnitCount => m_SelectedUnits != null ? m_SelectedUnits.Count : 0;
	public InventoryPanelView GroundPanel => m_GroundPanel;
	public InventoryPanelView CharacterInventoryPanel => m_CharacterInventoryPanel;
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
		if (s_Instance == this)
			s_Instance = null;
	}

	private void Start()
	{
		if (m_SelectFirstPlayerUnitOnStart)
			TrySelectFirstPlayerUnit();
		else
			SyncActiveInventoryToSelection();
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

	public bool TryResolveCharacterInventorySlot(InventorySlotView _slot, CharacterInventory _inventory, out bool _isMainHand,
		out int _bagIndex)
	{
		_isMainHand = false;
		_bagIndex = -1;

		if (m_CharacterInventoryPanel == null || _slot == null || _inventory == null || !_slot.HasItem)
		{
			Debug.Log(
				$"{nameof(TryResolveCharacterInventorySlot)}: fail panel={m_CharacterInventoryPanel != null}, slot={_slot != null}, inv={_inventory != null}, HasItem={_slot != null && _slot.HasItem}");
			return false;
		}

		if (!IsSlotOnPanel(_slot, m_CharacterInventoryPanel))
		{
			Debug.Log($"{nameof(TryResolveCharacterInventorySlot)}: слот '{_slot.name}' не на CharacterInventoryPanel.");
			return false;
		}

		int slotIndex = m_CharacterInventoryPanel.GetInventorySlotListIndex(_slot);
		if (slotIndex < 0)
		{
			Debug.Log(
				$"{nameof(TryResolveCharacterInventorySlot)}: GetInventorySlotListIndex < 0 (слот не найден среди InventorySlotView).");
			return false;
		}

		int lead = m_CharacterInventoryPanel.LeadingEquipmentSlotCount;
		if (slotIndex < lead)
		{
			if (slotIndex != 0)
			{
				Debug.Log($"{nameof(TryResolveCharacterInventorySlot)}: slotIndex={slotIndex} < lead={lead}, но не 0 — не поддерживается.");
				return false;
			}

			_isMainHand = true;
			if (!_inventory.HasMainHandEquipment)
			{
				Debug.Log($"{nameof(TryResolveCharacterInventorySlot)}: клик по слоту оружия, но MainHand пуст.");
				return false;
			}

			return true;
		}

		_bagIndex = slotIndex - lead;
		if (_bagIndex < 0 || _bagIndex >= _inventory.BagCount)
		{
			Debug.Log(
				$"{nameof(TryResolveCharacterInventorySlot)}: несовпадение UI и данных: slotIndex={slotIndex}, lead={lead}, bagIndex={_bagIndex}, BagCount={_inventory.BagCount}. Проверь Repaint и LeadingEquipmentSlotCount.");
			return false;
		}

		return true;
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

		if (coordinator.IsScreenPointOverCharacterMainHandSlot(_screenPosition, _eventCamera))
			return coordinator.TryEquipWeaponDragToMainHand();

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

	public bool TryAcceptDraggedGroundSlot(InventoryGroundToCharacterDrag _drag, bool _requireActiveDrag = true)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

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
		else
		{
			int bagIndex = _drag.CapturedBagIndex;
			if (bagIndex < 0 || bagIndex >= inventory.BagCount)
				return false;
			if (!inventory.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(inventory, data, slot, _drag.CapturedFromMainHandEquipmentSlot);
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

		if (!TryResolveCharacterInventorySlot(_slot, inventory, out bool isMainHand, out int bagIndex))
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

		InventorySlotRuntimeData data = _slot.Data;
		if (data.Definition == null || !data.Definition.IsEquipment)
		{
			Debug.Log(
				$"{nameof(TryEquipFromCharacterBagDoubleClick)}: предмет не Equipment (Definition={data.Definition?.name ?? "null"}, IsEquipment={data.Definition != null && data.Definition.IsEquipment}).");
			return false;
		}

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

	/// <summary>Двойной клик по оружию на панели земли — экипировка в основную руку.</summary>
	public bool TryEquipFromGroundDoubleClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem || m_GroundPanel == null)
			return false;

		if (_slot.GetComponentInParent<InventoryPanelView>() != m_GroundPanel)
			return false;

		if (!TryEquipGroundWeaponToMainHand(_slot))
			return false;

		RuntimeInventoryModificationCoordinator.Instance?.ClearModificationUiSelection();
		return true;
	}
	#endregion

	#region Private Methods
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
		return m_InventoryBindings != null ? m_InventoryBindings.ActiveCharacterInventory : null;
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
		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, m_SelectionRaycastMask, QueryTriggerInteraction.Collide))
		{
			if (!_ctrlPressed)
				ClearSelection();
			return;
		}

		RtsUnitMember unit = hit.collider.GetComponentInParent<RtsUnitMember>();
		if (unit == null || !unit.IsPlayerSelectable || MissionPrepSquadSpawner.IsMissionPrepPresentationMember(unit))
		{
			if (!_ctrlPressed)
				ClearSelection();
			return;
		}

		if (_ctrlPressed)
			ToggleUnitSelection(unit);
		else
			SetSelection(new List<RtsUnitMember> { unit });
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

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		bool shiftPressed = Keyboard.current != null &&
			(Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
		bool doubleRightClick = m_LastRightClickTime >= 0f &&
		                        Time.time - m_LastRightClickTime <= m_DoubleRightClickSeconds;
		m_LastRightClickTime = Time.time;

		UnitClickToMove.MoveTier moveTier = shiftPressed
			? UnitClickToMove.MoveTier.Sprint
			: doubleRightClick ? UnitClickToMove.MoveTier.Run : UnitClickToMove.MoveTier.Walk;

		IssueScatteredMoveOrder(hit.point, moveTier);
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
		if (!_slot.TryTakeItem(out InventorySlotRuntimeData data))
			return false;

		InventorySlotRuntimeData forInventory = data;
		forInventory.WorldSource = null;

		if (!_inventory.TryAdd(forInventory))
		{
			_slot.SetItem(data);
			return false;
		}

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(_slot);
		_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private bool TryQuickTransferCharacterToGroundInternal(CharacterInventory _inventory, InventorySlotView _slot)
	{
		if (!TryResolveCharacterInventorySlot(_slot, _inventory, out bool isMainHand, out int bagIndex))
			return false;

		InventorySlotRuntimeData data;
		if (isMainHand)
		{
			if (!_inventory.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else
		{
			if (!_inventory.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(_inventory, data, null, isMainHand);
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

	private bool TryCompleteCharacterToGroundTransfer(CharacterInventory _inventory, InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull, bool _removedFromMainHandSlot)
	{
		if (!TryBuildGroundSlotData(_inventory, _data, out InventorySlotRuntimeData groundData, out WorldPickupItem spawned))
		{
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
			_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return false;
		}

		bool placed;
		if (_adoptExistingSlotOrNull != null)
		{
			if (!m_GroundPanel.AdoptDraggedSlot(_adoptExistingSlotOrNull))
			{
				_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
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
			_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
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
