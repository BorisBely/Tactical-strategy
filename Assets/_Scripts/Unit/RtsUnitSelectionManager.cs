using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// RTS-выбор юнитов: одиночный ЛКМ, ctrl-toggle, box selection и групповые команды.
/// Инвентарь всегда привязан к последнему юниту в текущем выделении.
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
	#endregion

	#region Public Properties
	public static RtsUnitSelectionManager Instance => s_Instance;
	public InventoryPanelView GroundPanel => m_GroundPanel;
	public InventoryPanelView CharacterInventoryPanel => m_CharacterInventoryPanel;
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
		HandleLeftMouseSelection();
		HandleRightMouseCommand();
		HandleKeyboardCommands();
	}

	private void OnGUI()
	{
		if (!m_IsDraggingSelection)
			return;

		Rect rect = GetSelectionRect(m_LeftMouseDownScreen, Mouse.current != null ? Mouse.current.position.ReadValue() : m_LeftMouseDownScreen);
		DrawScreenRect(rect, new Color(0.2f, 0.7f, 1f, 0.15f));
		DrawScreenRectBorder(rect, 1f, new Color(0.2f, 0.7f, 1f, 0.95f));
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
			return false;

		if (!IsSlotOnPanel(_slot, m_CharacterInventoryPanel))
			return false;

		int containerIndex = m_CharacterInventoryPanel.GetInventorySlotContainerIndex(_slot);
		if (containerIndex < 0)
			return false;

		int lead = m_CharacterInventoryPanel.LeadingEquipmentSlotCount;
		if (containerIndex < lead)
		{
			if (containerIndex != 0)
				return false;
			_isMainHand = true;
			return _inventory.HasMainHandEquipment;
		}

		_bagIndex = containerIndex - lead;
		return _bagIndex >= 0 && _bagIndex < _inventory.BagCount;
	}

	public bool TryAcceptDraggedGroundSlot(InventoryGroundToCharacterDrag _drag)
	{
		if (_drag == null || !_drag.WasDraggingThisFrame)
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
			return false;

		CharacterInventory inventory = GetActiveInventory();
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		if (!TryResolveCharacterInventorySlot(_slot, inventory, out bool isMainHand, out int bagIndex))
			return false;

		if (isMainHand)
		{
			if (!inventory.TryUnequipMainHandToBag())
				return false;
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		InventorySlotRuntimeData data = _slot.Data;
		if (data.Definition == null || !data.Definition.IsEquipment)
			return false;

		if (inventory.HasMainHandEquipment && inventory.MainHandEquipment.Definition == data.Definition)
		{
			if (!inventory.TryUnequipMainHandToBag())
				return false;
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		UnitEquipment equipment = inventory.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
		{
			Debug.LogWarning($"{nameof(RtsUnitSelectionManager)}: no {nameof(UnitEquipment)} on active unit.", this);
			return false;
		}

		if (!inventory.TryMoveBagItemToMainHand(bagIndex, equipment))
			return false;

		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}
	#endregion

	#region Private Methods
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
		if (unit == null || !unit.IsPlayerSelectable)
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
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable)
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
			CommandSelectedStance(GetNextZTargetStance());
			return;
		}

		if (Keyboard.current.cKey.wasPressedThisFrame)
			CommandSelectedStance(GetNextCTargetStance());
	}

	private void CommandSelectedStance(LocomotionStance _stance)
	{
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
		if (_unit == null)
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
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable)
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

		m_SelectedUnits.RemoveAll(_unit => _unit == null || !_unit.isActiveAndEnabled);
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

		CharacterInventory inventory = null;
		for (int i = m_SelectedUnits.Count - 1; i >= 0; i--)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			inventory = unit.CharacterInventory;
			if (inventory != null)
				break;
		}

		m_InventoryBindings.SetActiveCharacterInventory(inventory);
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

	private static WorldPickupItem SpawnDropWorldPickup(CharacterInventory _inventory, ItemDefinition _definition, string _displayName)
	{
		_inventory.GetDropWorldPose(out Vector3 position, out Quaternion rotation);
		GameObject go = Instantiate(_definition.DropWorldPrefab, position, rotation);
		WorldPickupItem pickup = go.GetComponent<WorldPickupItem>();
		if (pickup == null)
		{
			Destroy(go);
			return null;
		}

		pickup.ConfigureForDroppedFromInventory(_definition, _displayName);
		return pickup;
	}

	private bool TryCompleteCharacterToGroundTransfer(CharacterInventory _inventory, InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull, bool _removedFromMainHandSlot)
	{
		WorldPickupItem spawned = null;
		ItemDefinition definition = _data.Definition;
		if (definition != null && definition.DropWorldPrefab != null)
		{
			spawned = SpawnDropWorldPickup(_inventory, definition, _data.DisplayName);
			if (spawned == null)
			{
				_inventory.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
				_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
				return false;
			}
		}

		InventorySlotRuntimeData groundData = _data;
		groundData.WorldSource = spawned;

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

		if (spawned != null)
			spawned.RegisterListedInGroundUi();

		_inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private void TrySelectFirstPlayerUnit()
	{
		IReadOnlyList<RtsUnitMember> units = RtsUnitMember.Instances;
		for (int i = 0; i < units.Count; i++)
		{
			RtsUnitMember unit = units[i];
			if (unit == null || !unit.isActiveAndEnabled || !unit.IsPlayerSelectable)
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
