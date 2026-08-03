using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Интеграция машины в RTS selection/commands.</summary>
public sealed partial class RtsUnitSelectionManager
{
	#region Vehicle Fields
	private VehicleController m_SelectedVehicle;
	private float m_LastVehicleLeftClickTime = -1f;
	private VehicleController m_LastVehicleLeftClick;
	private float m_LastDisembarkKeyTime = -1f;
	private bool m_DisembarkDigitChord;
	private bool m_VehicleEKeyConsumed;
	private readonly List<RtsUnitMember> m_PendingBoardUnits = new List<RtsUnitMember>(16);
	private Coroutine m_VehicleClickCommitCoroutine;
	private float m_LastVehicleRmbTime = -1f;
	private bool m_IsVehicleMovePreviewing;
	private Vector3 m_VehiclePreviewPoint;
	private Vector2 m_VehiclePreviewScreenStart;
	private bool m_VehiclePreviewHasFacing;
	private float m_VehiclePreviewFacingYaw;
	private VehicleSpeedMode m_VehiclePreviewSpeedMode = VehicleSpeedMode.Medium;
	private Coroutine m_PendingVehicleMoveCoroutine;
	private const float c_VehicleBoardDoubleClickSeconds = 0.45f;
	private const float c_VehicleFacingDragThresholdPixels = 5f;
	private const bool c_VehicleClickDebugLogs = false;
	#endregion

	#region Vehicle Public API
	public VehicleController SelectedVehicle => m_SelectedVehicle;

	public bool HasSelectedVehicle => m_SelectedVehicle != null;

	public void CommandSelectedVehicleDisembarkExceptDriver()
	{
		m_SelectedVehicle?.DisembarkAllExceptDriver();
	}

	public void CommandSelectedVehicleDisembarkAll()
	{
		m_SelectedVehicle?.DisembarkAll();
	}

	public void CommandSelectedVehicleToggleGunner()
	{
		m_SelectedVehicle?.ToggleGunnerTurret();
	}

	public void CommandSelectedVehicleToggleEngine()
	{
		if (m_SelectedVehicle == null)
			return;
		m_SelectedVehicle.ToggleEngine();
		NotifySelectionUiRefresh();
	}

	public void CommandSelectedVehicleCycleSpeedCeiling()
	{
		if (m_SelectedVehicle == null)
			return;
		m_SelectedVehicle.CycleSpeedCeiling();
		NotifySelectionUiRefresh();
	}

	public void CommandSelectedVehicleCycleGunnerStance()
	{
		if (m_SelectedVehicle == null)
			return;
		m_SelectedVehicle.CycleGunnerStance();
		NotifySelectionUiRefresh();
	}

	public void CommandSelectedVehicleCycleTurretFireMode()
	{
		m_SelectedVehicle?.CycleTurretFireMode();
	}

	public void CommandSelectedVehicleCycleTurretFireDiscipline()
	{
		m_SelectedVehicle?.CycleTurretFireDiscipline();
	}

	public void CommandSelectedVehicleCycleTurretAimMode()
	{
		m_SelectedVehicle?.CycleTurretAimMode();
	}

	public void CommandSelectedVehicleGunnerReload()
	{
		if (m_SelectedVehicle == null || !m_SelectedVehicle.IsGunnerOnTurret)
			return;

		VehicleTurretGunnerBridge bridge = m_SelectedVehicle.TurretGunnerBridge;
		if (bridge == null || !bridge.TryStartGunnerReload())
			Debug.LogWarning("[Vehicle] Gunner reload failed (no spare M2 box or Mag/Handle missing).", m_SelectedVehicle);
	}

	public void CommandLoadWoundedIntoSelectedOrTargetVehicle()
	{
		if (!TryGetCarryingSelectedUnit(out RtsUnitMember carrier))
			return;

		VehicleController vehicle = m_SelectedVehicle;
		if (vehicle == null)
			vehicle = FindNearestVehicle(carrier.transform.position);
		vehicle?.LoadWoundedFromCarrier(carrier);
	}

	public void NotifySelectionUiRefresh()
	{
		SelectionChanged?.Invoke();
	}

	public bool TryGetCarryingSelectedUnit(out RtsUnitMember _carrier)
	{
		_carrier = null;
		for (int i = 0; i < m_SelectedUnits.Count; i++)
		{
			RtsUnitMember unit = m_SelectedUnits[i];
			if (unit == null)
				continue;
			if (!unit.TryGetComponent(out UnitFiremanCarryController carry) || !carry.IsCarryingFallen)
				continue;
			_carrier = unit;
			return true;
		}

		return false;
	}
	#endregion

	#region Vehicle Private
	private void SubscribeVehicleMenus()
	{
		VehicleInteractionMenuController.Instance.ActionClicked += HandleVehicleInteractionMenuAction;
		VehicleDisembarkMenuController.Instance.ActionClicked += HandleVehicleDisembarkMenuAction;
	}

	private void UnsubscribeVehicleMenus()
	{
		if (VehicleInteractionMenuController.Instance != null)
			VehicleInteractionMenuController.Instance.ActionClicked -= HandleVehicleInteractionMenuAction;
		if (VehicleDisembarkMenuController.Instance != null)
			VehicleDisembarkMenuController.Instance.ActionClicked -= HandleVehicleDisembarkMenuAction;
	}

	public void ClearSelectedVehicle()
	{
		CancelVehicleMovePreview();
		if (m_SelectedVehicle == null)
			return;
		m_SelectedVehicle.SetSelected(false);
		m_SelectedVehicle = null;
		if (InventoryScreenBindings.Instance != null &&
		    InventoryScreenBindings.Instance.IsVehicleInventoryActive)
			SyncActiveInventoryToSelection();
		SelectionChanged?.Invoke();
	}

	public void NotifyVehicleTeamChanged(VehicleController _vehicle)
	{
		if (_vehicle == null || m_SelectedVehicle != _vehicle)
			return;
		if (!_vehicle.IsPlayerSelectable)
			ClearSelectedVehicle();
	}

	private void SetSelectedVehicle(VehicleController _vehicle)
	{
		if (_vehicle != null && !_vehicle.IsPlayerSelectable)
			_vehicle = null;

		if (m_SelectedVehicle == _vehicle)
			return;

		if (m_SelectedVehicle != null)
			m_SelectedVehicle.SetSelected(false);

		m_SelectedVehicle = _vehicle;
		if (m_SelectedVehicle != null)
			m_SelectedVehicle.SetSelected(true);

		if (m_SelectedVehicle != null && m_SelectedUnits.Count == 0)
		{
			VehicleInventory vehicleInventory = m_SelectedVehicle.Inventory;
			if (vehicleInventory != null)
				InventoryScreenBindings.Instance?.SetActiveVehicleInventory(vehicleInventory);
		}

		SelectionChanged?.Invoke();
	}

	private void HandleVehicleLeftClick(VehicleController _vehicle, bool _ctrlPressed)
	{
		if (_vehicle == null)
			return;

		float now = Time.unscaledTime;
		bool isDoubleClick = IsVehicleBoardDoubleClick(_vehicle, now);

		LogVehicleClick(
			$"LMB vehicle='{_vehicle.name}' team={_vehicle.Team} double={isDoubleClick} ctrl={_ctrlPressed} " +
			$"selectedUnits={m_SelectedUnits.Count} pendingBoard={m_PendingBoardUnits.Count}");

		if (isDoubleClick)
		{
			StopVehicleClickCommit();
			List<RtsUnitMember> boarders = CollectBoardIntentUnits();
			m_PendingBoardUnits.Clear();
			RecordVehicleBoardClick(_vehicle, now);
			if (boarders.Count > 0 && _vehicle.CanAcceptAnyBoarder(boarders))
			{
				LogVehicleClick($"LMB double → BoardUnits count={boarders.Count}");
				VehicleInteractionMenuController.Instance?.HideImmediate();
				_vehicle.BoardUnits(boarders, VehicleBoardSide.Any);
				return;
			}

			if (!_vehicle.IsPlayerSelectable)
				return;

			LogVehicleClick("LMB double without units → select vehicle only");
			SetSelection(new List<RtsUnitMember>(0));
			SetSelectedVehicle(_vehicle);
			return;
		}

		RecordVehicleBoardClick(_vehicle, now);

		m_PendingBoardUnits.Clear();
		List<RtsUnitMember> selected = GetValidSelectedUnits();
		for (int i = 0; i < selected.Count; i++)
			m_PendingBoardUnits.Add(selected[i]);

		if (m_PendingBoardUnits.Count > 0 && !_ctrlPressed)
		{
			LogVehicleClick(
				$"LMB single with {m_PendingBoardUnits.Count} units → wait double-click window");
			StopVehicleClickCommit();
			m_VehicleClickCommitCoroutine = StartCoroutine(CommitVehicleSelectionAfterDoubleClickWindow(_vehicle));
			return;
		}

		if (!_vehicle.IsPlayerSelectable)
			return;

		LogVehicleClick("LMB single → select vehicle");
		if (!_ctrlPressed)
			SetSelection(new List<RtsUnitMember>(0));

		SetSelectedVehicle(_vehicle);
	}

	private List<RtsUnitMember> CollectBoardIntentUnits()
	{
		if (m_PendingBoardUnits.Count > 0)
			return new List<RtsUnitMember>(m_PendingBoardUnits);

		return GetValidSelectedUnits();
	}

	private bool IsVehicleBoardDoubleClick(VehicleController _vehicle, float _now)
	{
		return _vehicle != null &&
		       m_LastVehicleLeftClick == _vehicle &&
		       _now - m_LastVehicleLeftClickTime <= c_VehicleBoardDoubleClickSeconds;
	}

	private void RecordVehicleBoardClick(VehicleController _vehicle, float _now)
	{
		m_LastVehicleLeftClick = _vehicle;
		m_LastVehicleLeftClickTime = _now;
	}

	private bool TryExecuteVehicleBoardDoubleClick(VehicleController _vehicle, IReadOnlyList<RtsUnitMember> _boarders)
	{
		if (_vehicle == null || _boarders == null || _boarders.Count == 0)
			return false;
		if (!_vehicle.CanAcceptAnyBoarder(_boarders))
			return false;

		float now = Time.unscaledTime;
		if (!IsVehicleBoardDoubleClick(_vehicle, now))
			return false;

		StopVehicleClickCommit();
		m_PendingBoardUnits.Clear();
		RecordVehicleBoardClick(_vehicle, now);
		LogVehicleClick($"double → BoardUnits count={_boarders.Count}");
		VehicleInteractionMenuController.Instance?.HideImmediate();
		_vehicle.BoardUnits(_boarders, VehicleBoardSide.Any);
		return true;
	}

	private IEnumerator CommitVehicleSelectionAfterDoubleClickWindow(VehicleController _vehicle)
	{
		yield return new WaitForSecondsRealtime(c_VehicleBoardDoubleClickSeconds);
		m_VehicleClickCommitCoroutine = null;
		m_PendingBoardUnits.Clear();
		// Пустую/нейтральную без водителя не выделяем — юниты остаются выделенными.
		if (_vehicle == null || !_vehicle.IsPlayerSelectable)
			yield break;
		LogVehicleClick($"double-click timeout → select vehicle '{_vehicle.name}'");
		SetSelection(new List<RtsUnitMember>(0));
		SetSelectedVehicle(_vehicle);
	}

	private void StopVehicleClickCommit()
	{
		if (m_VehicleClickCommitCoroutine == null)
			return;
		StopCoroutine(m_VehicleClickCommitCoroutine);
		m_VehicleClickCommitCoroutine = null;
	}

	private bool TryHandleVehicleContextMenu(Ray _ray, Vector2 _screenPosition)
	{
		if (!TryRaycastVehicle(_ray, out VehicleController vehicle, out _))
		{
			LogVehicleClick("RMB → no vehicle on ray");
			return false;
		}

		List<RtsUnitMember> selectedUnits = CollectBoardIntentUnits();

		bool canBoard = vehicle.CanAcceptAnyBoarder(selectedUnits);
		bool canLoadWounded = TryGetCarryingSelectedUnit(out RtsUnitMember carrier) &&
		                      carrier != null &&
		                      carrier.TryGetComponent(out UnitFiremanCarryController carry) &&
		                      carry.CarriedVictim != null &&
		                      vehicle.CanAcceptBoarder(carry.CarriedVictim);

		if (!vehicle.IsPlayerSelectable && !canBoard && !canLoadWounded)
		{
			LogVehicleClick($"RMB non-boardable team={vehicle.Team} → ignore orders");
			return true;
		}

		if (TryExecuteVehicleBoardDoubleClick(vehicle, selectedUnits))
			return true;

		// Одиночный ПКМ — меню; двойной (ЛКМ/ПКМ) уже обработан выше.
		StopVehicleClickCommit();

		LogVehicleClick(
			$"RMB vehicle='{vehicle.name}' canBoard={canBoard} canLoadWounded={canLoadWounded} " +
			$"selectedUnits={selectedUnits.Count}");

		if (!canBoard && !canLoadWounded)
		{
			RecordVehicleBoardClick(vehicle, Time.unscaledTime);
			SetSelection(new List<RtsUnitMember>(0));
			SetSelectedVehicle(vehicle);
			return true;
		}

		RecordVehicleBoardClick(vehicle, Time.unscaledTime);

		bool hasLivingSpace = vehicle.Seats == null || vehicle.Seats.HasAnyFreeSeatForLiving();
		bool hasWoundedSpace = vehicle.Seats == null || vehicle.Seats.HasAnyFreeSeatForWounded();
		bool hasFreeGunnerSeat = vehicle.Seats != null && vehicle.Seats.HasFreeGunnerSeat;

		VehicleInteractionMenuController.Instance.ShowForVehicle(
			vehicle,
			_screenPosition,
			canBoard,
			canLoadWounded,
			hasLivingSpace,
			hasWoundedSpace,
			hasFreeGunnerSeat);

		if (m_IsPreviewingMove || m_PreviewPending || m_IsAwaitingDoubleClick)
			CancelMovePreview();
		return true;
	}

	/// <summary>
	/// Ищет машину вдоль луча. Маска Unit|Vehicle недостаточна: дети модели часто на Default.
	/// Во время окна двойного клика посадки юниты перед машиной не блокируют луч —
	/// иначе второй клик «съедается» коллайдером пехоты и посадка всех не срабатывает.
	/// </summary>
	private bool TryRaycastVehicle(Ray _ray, out VehicleController _vehicle, out RaycastHit _hit)
	{
		_vehicle = null;
		_hit = default;

		bool awaitingBoardDoubleClick = m_VehicleClickCommitCoroutine != null &&
		                                m_LastVehicleLeftClick != null &&
		                                m_PendingBoardUnits.Count > 0;
		bool hasBoardIntentUnits = m_SelectedUnits.Count > 0 || m_PendingBoardUnits.Count > 0;

		// ~0: коллайдеры дверей/стёкол на Default всё равно дают VehicleController через parent.
		RaycastHit[] hits = Physics.RaycastAll(_ray, 2000f, ~0, QueryTriggerInteraction.Collide);
		if (hits == null || hits.Length == 0)
		{
			LogVehicleClick("RaycastAll: 0 hits");
			return false;
		}

		System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

		for (int i = 0; i < hits.Length; i++)
		{
			Collider col = hits[i].collider;
			if (col == null)
				continue;

			VehicleController candidate = VehicleController.FindUnderCollider(col);
			if (candidate != null)
			{
				_vehicle = candidate;
				_hit = hits[i];
				LogVehicleClick($"Raycast vehicle='{_vehicle.name}' dist={_hit.distance:F2}");
				return true;
			}

			if (awaitingBoardDoubleClick || hasBoardIntentUnits)
			{
				RtsUnitMember unit = col.GetComponentInParent<RtsUnitMember>();
				if (unit != null &&
				    UnitFallenStateUtility.IsRtsControllable(unit) &&
				    m_SelectedUnits.Contains(unit))
					continue;
			}

			if (awaitingBoardDoubleClick)
				continue;

			// Ближе машины — юнит: клик по пехоте, машину за ним не берём.
			RtsUnitMember blocker = col.GetComponentInParent<RtsUnitMember>();
			if (blocker != null && UnitFallenStateUtility.IsRtsControllable(blocker))
				return false;
		}

		return false;
	}

	/// <summary>
	/// Если второй клик не попал в машину (юнит/земля), но мы ждём double-click посадки —
	/// всё равно обрабатываем как клик по той же машине.
	/// </summary>
	private bool TryConsumePendingVehicleBoardDoubleClick(bool _ctrlPressed)
	{
		if (m_VehicleClickCommitCoroutine == null ||
		    m_LastVehicleLeftClick == null ||
		    m_PendingBoardUnits.Count == 0)
			return false;

		float now = Time.unscaledTime;
		if (now - m_LastVehicleLeftClickTime > c_VehicleBoardDoubleClickSeconds)
			return false;

		LogVehicleClick("pending board double-click fallback → HandleVehicleLeftClick");
		HandleVehicleLeftClick(m_LastVehicleLeftClick, _ctrlPressed);
		return true;
	}

	private static void LogVehicleClick(string _message) { }

	private void HandleSelectedVehicleRightMouse()
	{
		if (m_SelectedVehicle == null || Mouse.current == null || m_SelectionCamera == null)
			return;

		if (IsPointerOverUi())
		{
			if (m_IsVehicleMovePreviewing && Mouse.current.rightButton.wasReleasedThisFrame)
				CancelVehicleMovePreview();
			return;
		}

		Mouse mouse = Mouse.current;
		Vector2 mousePosition = mouse.position.ReadValue();

		if (mouse.rightButton.wasPressedThisFrame)
		{
			Ray ray = m_SelectionCamera.ScreenPointToRay(mousePosition);
			if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
				return;

			VehicleController vehicle = m_SelectedVehicle;
			Vector3 point = hit.point;

			if (IsCtrlPressed())
			{
				CancelVehicleMovePreview();
				vehicle.ClearMovePreview();
				vehicle.IssueMoveOrder(point, VehicleSpeedMode.Slow);
				m_LastVehicleRmbTime = -1f;
				return;
			}

			float now = Time.unscaledTime;
			if (m_LastVehicleRmbTime > 0f && now - m_LastVehicleRmbTime <= m_DoubleRightClickSeconds)
			{
				CancelVehicleMovePreview();
				m_LastVehicleRmbTime = -1f;
				vehicle.ClearMovePreview();
				vehicle.IssueMoveOrder(point, VehicleSpeedMode.Fast);
				return;
			}

			m_LastVehicleRmbTime = now;
			m_IsVehicleMovePreviewing = true;
			m_VehiclePreviewPoint = point;
			m_VehiclePreviewScreenStart = mousePosition;
			m_VehiclePreviewHasFacing = false;
			m_VehiclePreviewFacingYaw = 0f;
			m_VehiclePreviewSpeedMode = VehicleSpeedMode.Medium;
			vehicle.SetMovePreview(point, m_VehiclePreviewSpeedMode, null);
			return;
		}

		if (!m_IsVehicleMovePreviewing)
			return;

		if (mouse.rightButton.isPressed)
		{
			UpdateVehicleMovePreviewFacing(mousePosition);
			return;
		}

		if (mouse.rightButton.wasReleasedThisFrame)
			CommitVehicleMovePreview();
	}

	private void UpdateVehicleMovePreviewFacing(Vector2 _mouseScreen)
	{
		if (m_SelectedVehicle == null || m_SelectionCamera == null)
			return;

		float dragPixels = Vector2.Distance(_mouseScreen, m_VehiclePreviewScreenStart);
		if (dragPixels < c_VehicleFacingDragThresholdPixels)
		{
			m_VehiclePreviewHasFacing = false;
			m_SelectedVehicle.SetMovePreview(m_VehiclePreviewPoint, m_VehiclePreviewSpeedMode, null);
			return;
		}

		Ray ray = m_SelectionCamera.ScreenPointToRay(_mouseScreen);
		if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, m_CommandGroundMask, QueryTriggerInteraction.Ignore))
			return;

		Vector3 flat = hit.point - m_VehiclePreviewPoint;
		flat.y = 0f;
		if (flat.sqrMagnitude < 0.01f)
			return;

		m_VehiclePreviewHasFacing = true;
		m_VehiclePreviewFacingYaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
		m_SelectedVehicle.SetMovePreview(
			m_VehiclePreviewPoint,
			m_VehiclePreviewSpeedMode,
			m_VehiclePreviewFacingYaw);
	}

	private void CommitVehicleMovePreview()
	{
		if (!m_IsVehicleMovePreviewing || m_SelectedVehicle == null)
		{
			CancelVehicleMovePreview();
			return;
		}

		VehicleController vehicle = m_SelectedVehicle;
		Vector3 point = m_VehiclePreviewPoint;
		bool hasFacing = m_VehiclePreviewHasFacing;
		float facingYaw = m_VehiclePreviewFacingYaw;
		VehicleSpeedMode mode = m_VehiclePreviewSpeedMode;

		// Keep preview cleared but preserve double-click window for no-facing clicks.
		m_IsVehicleMovePreviewing = false;
		m_VehiclePreviewHasFacing = false;
		vehicle.ClearMovePreview();

		if (hasFacing)
		{
			vehicle.IssueMoveOrder(VehicleMoveGoal.FromPositionAndHeading(point, facingYaw, mode));
			m_LastVehicleRmbTime = -1f;
			return;
		}

		CancelPendingVehicleMove();
		vehicle.SetMovePreview(point, mode, null);
		m_PendingVehicleMoveCoroutine = StartCoroutine(PendingVehicleMediumMove(vehicle, point, mode));
	}

	private IEnumerator PendingVehicleMediumMove(
		VehicleController _vehicle,
		Vector3 _point,
		VehicleSpeedMode _mode)
	{
		yield return new WaitForSecondsRealtime(m_DoubleRightClickSeconds);
		m_PendingVehicleMoveCoroutine = null;
		if (_vehicle == null || m_SelectedVehicle != _vehicle)
			yield break;
		_vehicle.ClearMovePreview();
		_vehicle.IssueMoveOrder(VehicleMoveGoal.FromPosition(_point, _mode));
		m_LastVehicleRmbTime = -1f;
	}

	private void CancelVehicleMovePreview()
	{
		m_IsVehicleMovePreviewing = false;
		m_VehiclePreviewHasFacing = false;
		CancelPendingVehicleMove();
		m_SelectedVehicle?.ClearMovePreview();
	}

	private void CancelPendingVehicleMove()
	{
		if (m_PendingVehicleMoveCoroutine == null)
			return;
		StopCoroutine(m_PendingVehicleMoveCoroutine);
		m_PendingVehicleMoveCoroutine = null;
	}

	private void HandleVehicleInteractionMenuAction(
		VehicleInteractionMenuController.MenuAction _action,
		VehicleController _vehicle)
	{
		if (_vehicle == null)
			return;

		List<RtsUnitMember> units = CollectBoardIntentUnits();
		m_PendingBoardUnits.Clear();
		StopVehicleClickCommit();

		LogVehicleClick($"menu action={_action} vehicle='{_vehicle.name}' units={units.Count}");
		switch (_action)
		{
			case VehicleInteractionMenuController.MenuAction.Board:
				_vehicle.BoardUnits(units, VehicleBoardSide.Any, _forceRun: false);
				break;
			case VehicleInteractionMenuController.MenuAction.BoardOneSide:
				_vehicle.BoardUnits(units, ResolveNearestBoardSide(_vehicle, units), _forceRun: false);
				break;
			case VehicleInteractionMenuController.MenuAction.BoardGunner:
				_vehicle.BoardUnitsAsGunner(units, VehicleBoardSide.Any, _forceRun: false);
				break;
			case VehicleInteractionMenuController.MenuAction.LoadWounded:
				if (TryGetCarryingSelectedUnit(out RtsUnitMember carrier))
					_vehicle.LoadWoundedFromCarrier(carrier, _forceRun: false);
				break;
			case VehicleInteractionMenuController.MenuAction.Exchange:
			{
				RtsUnitMember player = null;
				if (units.Count == 1)
					player = units[0];
				else if (m_SelectedUnits.Count == 1)
					player = m_SelectedUnits[0];
				if (player != null)
					TryBeginVehicleInventoryExchange(_vehicle, player);
				break;
			}
		}
	}

	/// <summary>
	/// Общая сторона для группы: ближе к центру выделения относительно машины.
	/// Передний слот → передняя дверь этой стороны, задний → задняя.
	/// </summary>
	private static VehicleBoardSide ResolveNearestBoardSide(
		VehicleController _vehicle,
		IReadOnlyList<RtsUnitMember> _units)
	{
		if (_vehicle == null || _units == null || _units.Count == 0)
			return VehicleBoardSide.Left;

		Vector3 average = Vector3.zero;
		int count = 0;
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] == null)
				continue;
			average += _units[i].transform.position;
			count++;
		}

		if (count == 0)
			return VehicleBoardSide.Left;

		average /= count;
		Vector3 local = _vehicle.transform.InverseTransformPoint(average);
		return local.x < 0f ? VehicleBoardSide.Left : VehicleBoardSide.Right;
	}

	private void HandleVehicleDisembarkMenuAction(
		VehicleDisembarkMenuController.MenuAction _action,
		VehicleController _vehicle,
		RtsUnitMember _unit)
	{
		if (_vehicle == null)
			return;

		switch (_action)
		{
			case VehicleDisembarkMenuController.MenuAction.ExceptDriver:
				_vehicle.DisembarkAllExceptDriver();
				break;
			case VehicleDisembarkMenuController.MenuAction.Everyone:
				_vehicle.DisembarkAll();
				break;
			case VehicleDisembarkMenuController.MenuAction.Specific:
				_vehicle.DisembarkUnit(_unit);
				break;
		}
	}

	private void HandleVehicleKeyboardCommands()
	{
		if (Keyboard.current == null)
			return;

		m_VehicleEKeyConsumed = false;

		if (m_SelectedVehicle != null && Keyboard.current.fKey.wasPressedThisFrame)
			m_SelectedVehicle.HardStop();

		if (m_SelectedVehicle != null && Keyboard.current.eKey.wasPressedThisFrame)
		{
			m_SelectedVehicle.ToggleAllPassengersVehicleReady();
			m_VehicleEKeyConsumed = true;
		}

		if (m_SelectedVehicle != null &&
		    m_SelectedVehicle.IsGunnerOnTurret &&
		    Keyboard.current.rKey.wasPressedThisFrame)
		{
			CommandSelectedVehicleGunnerReload();
		}

		if (m_SelectedVehicle == null || !m_SelectedVehicle.HasPassengers)
		{
			m_DisembarkDigitChord = false;
			return;
		}

		if (Keyboard.current.uKey.wasPressedThisFrame)
		{
			float now = Time.unscaledTime;
			if (now - m_LastDisembarkKeyTime <= m_DoubleRightClickSeconds)
			{
				m_SelectedVehicle.DisembarkAll();
				m_LastDisembarkKeyTime = -1f;
				m_DisembarkDigitChord = false;
			}
			else
			{
				m_LastDisembarkKeyTime = now;
				m_DisembarkDigitChord = true;
			}
		}

		if (m_DisembarkDigitChord && Keyboard.current.uKey.isPressed)
		{
			int digit = ReadDisembarkDigit();
			if (digit > 0)
			{
				TryDisembarkIndexedPassenger(digit);
				m_DisembarkDigitChord = false;
				m_LastDisembarkKeyTime = -1f;
			}
		}

		if (m_DisembarkDigitChord &&
		    !Keyboard.current.uKey.isPressed &&
		    Time.unscaledTime - m_LastDisembarkKeyTime > m_DoubleRightClickSeconds)
		{
			m_SelectedVehicle.DisembarkAllExceptDriver();
			m_DisembarkDigitChord = false;
			m_LastDisembarkKeyTime = -1f;
		}
	}

	private int ReadDisembarkDigit()
	{
		if (Keyboard.current.digit1Key.wasPressedThisFrame) return 1;
		if (Keyboard.current.digit2Key.wasPressedThisFrame) return 2;
		if (Keyboard.current.digit3Key.wasPressedThisFrame) return 3;
		if (Keyboard.current.digit4Key.wasPressedThisFrame) return 4;
		if (Keyboard.current.digit5Key.wasPressedThisFrame) return 5;
		if (Keyboard.current.digit6Key.wasPressedThisFrame) return 6;
		if (Keyboard.current.digit7Key.wasPressedThisFrame) return 7;
		if (Keyboard.current.digit8Key.wasPressedThisFrame) return 8;
		return 0;
	}

	private void TryDisembarkIndexedPassenger(int _oneBasedIndex)
	{
		if (m_SelectedVehicle?.Seats == null)
			return;

		var ordered = new List<(VehicleSeatId Seat, RtsUnitMember Unit)>(8);
		m_SelectedVehicle.Seats.CollectOccupantsOrdered(ordered);
		int passengerIndex = 0;
		for (int i = 0; i < ordered.Count; i++)
		{
			if (ordered[i].Seat == VehicleSeatId.Driver)
				continue;
			passengerIndex++;
			if (passengerIndex == _oneBasedIndex)
			{
				m_SelectedVehicle.DisembarkUnit(ordered[i].Unit);
				return;
			}
		}
	}

	private static VehicleController FindNearestVehicle(Vector3 _position)
	{
		IReadOnlyList<VehicleController> instances = VehicleController.Instances;
		VehicleController best = null;
		float bestSqr = float.MaxValue;
		for (int i = 0; i < instances.Count; i++)
		{
			VehicleController vehicle = instances[i];
			if (vehicle == null || !vehicle.CanAcceptBoarderTeam(UnitTeamId.Player))
				continue;
			float sqr = (vehicle.transform.position - _position).sqrMagnitude;
			if (sqr >= bestSqr)
				continue;
			bestSqr = sqr;
			best = vehicle;
		}

		return best;
	}

	private bool TryEquipFromVehicleInventoryDoubleClick(InventorySlotView _slot)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		VehicleInventory inventory = bindings != null ? bindings.ActiveVehicleInventory : null;
		if (inventory == null || m_CharacterInventoryPanel == null || _slot == null)
			return false;
		if (!inventory.CanModifyContents)
			return true; // consume click, view-only

		if (!TryResolveVehicleInventorySlot(_slot, inventory, out bool isWeapon, out bool isFrontal, out bool isSurround, out int bagIndex))
			return false;

		if (isWeapon)
		{
			InventorySlotRuntimeData removedData = inventory.TurretWeapon;
			if (inventory.TryUnequipToBag(VehicleEquipmentSlotId.TurretWeapon))
				PlayVehicleSlotAudio(removedData, VehicleEquipmentSlotId.TurretWeapon, _equip: false);
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		if (isFrontal)
		{
			InventorySlotRuntimeData removedData = inventory.FrontalShield;
			if (inventory.TryUnequipToBag(VehicleEquipmentSlotId.FrontalShield))
				PlayVehicleSlotAudio(removedData, VehicleEquipmentSlotId.FrontalShield, _equip: false);
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		if (isSurround)
		{
			InventorySlotRuntimeData removedData = inventory.SurroundShield;
			if (inventory.TryUnequipToBag(VehicleEquipmentSlotId.SurroundShield))
				PlayVehicleSlotAudio(removedData, VehicleEquipmentSlotId.SurroundShield, _equip: false);
			inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		InventorySlotRuntimeData data = _slot.Data;
		if (data.Definition == null)
			return true;

		if (data.Definition.IsTurretWeapon)
		{
			if (inventory.TryEquipFromBag(bagIndex, VehicleEquipmentSlotId.TurretWeapon))
				PlayVehicleSlotAudio(data, VehicleEquipmentSlotId.TurretWeapon, _equip: true);
		}
		else if (data.Definition.IsTurretFrontalShield)
		{
			if (inventory.TryEquipFromBag(bagIndex, VehicleEquipmentSlotId.FrontalShield))
				PlayVehicleSlotAudio(data, VehicleEquipmentSlotId.FrontalShield, _equip: true);
		}
		else if (data.Definition.IsTurretSurroundShield)
		{
			if (inventory.TryEquipFromBag(bagIndex, VehicleEquipmentSlotId.SurroundShield))
				PlayVehicleSlotAudio(data, VehicleEquipmentSlotId.SurroundShield, _equip: true);
		}

		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private bool TryResolveVehicleInventorySlot(
		InventorySlotView _slot,
		VehicleInventory _inventory,
		out bool _isWeapon,
		out bool _isFrontal,
		out bool _isSurround,
		out int _bagIndex)
	{
		_isWeapon = false;
		_isFrontal = false;
		_isSurround = false;
		_bagIndex = -1;
		if (_slot == null || _inventory == null)
			return false;

		bool vehicleOnGroundPanel = InventoryExchangeController.Instance.IsActive &&
		                            InventoryExchangeController.Instance.PartnerVehicleInventory == _inventory;

		InventoryPanelView vehiclePanel = vehicleOnGroundPanel ? m_GroundPanel : m_CharacterInventoryPanel;
		if (vehiclePanel == null)
			return false;

		IReadOnlyList<InventorySlotView> slots = vehiclePanel.Slots;
		int index = -1;
		for (int i = 0; i < slots.Count; i++)
		{
			if (slots[i] == _slot)
			{
				index = i;
				break;
			}
		}

		if (index < 0)
		{
			Debug.Log($"[VehicleInv] TryResolveVehicleInventorySlot: слот не найден на панели {(vehicleOnGroundPanel ? "ground" : "character")}, slots={slots.Count}, vehicleOnGroundPanel={vehicleOnGroundPanel}");
			return false;
		}

		int lead = Mathf.Max(0, vehiclePanel.LeadingEquipmentSlotCount);
		if (index < lead)
		{
			_isWeapon = index == 0;
			_isFrontal = index == 1;
			_isSurround = index == 2;
			Debug.Log($"[VehicleInv] Resolve slot index={index} -> isWeapon={_isWeapon} isFrontal={_isFrontal} isSurround={_isSurround}");
			return true;
		}

		_bagIndex = index - lead;
		Debug.Log($"[VehicleInv] Resolve slot index={index} lead={lead} -> bagIndex={_bagIndex} bagCount={_inventory.BagCount}");
		return _bagIndex >= 0 && _bagIndex < _inventory.BagCount;
	}

	public bool TryResolveVehicleInventorySlotForDrag(
		InventorySlotView _slot,
		out bool _isMainHand,
		out bool _isHead,
		out bool _isBack,
		out int _bagIndex)
	{
		_isMainHand = false;
		_isHead = false;
		_isBack = false;
		_bagIndex = -1;
		if (_slot == null || m_CharacterInventoryPanel == null)
			return false;

		IReadOnlyList<InventorySlotView> slots = m_CharacterInventoryPanel.Slots;
		int index = -1;
		for (int i = 0; i < slots.Count; i++)
		{
			if (slots[i] == _slot)
			{
				index = i;
				break;
			}
		}

		if (index < 0)
			return false;

		int lead = Mathf.Max(0, m_CharacterInventoryPanel.LeadingEquipmentSlotCount);
		if (index < lead)
		{
			_isMainHand = index == 0;
			_isHead = index == 1;
			_isBack = index == 2;
			return true;
		}

		VehicleInventory inventory = InventoryScreenBindings.Instance?.ActiveVehicleInventory;
		if (inventory == null)
			return false;

		_bagIndex = index - lead;
		return _bagIndex >= 0 && _bagIndex < inventory.BagCount;
	}

	public bool TryRouteVehicleDragOnCharacterPanel(
		InventoryCharacterToGroundDrag _drag,
		Vector2 _screenPosition,
		Camera _eventCamera,
		bool _requireActiveDrag = true)
	{
		if (_drag == null || (_requireActiveDrag && !_drag.WasDraggingThisFrame))
			return false;

		VehicleInventory inventory = InventoryScreenBindings.Instance?.ActiveVehicleInventory;
		if (inventory == null || m_CharacterInventoryPanel == null)
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
			return TryAcceptVehicleEquipmentDragToBag(_drag, VehicleEquipmentSlotId.TurretWeapon);
		}

		if (_drag.CapturedFromHeadEquipmentSlot)
		{
			if (!coordinator.IsScreenPointOverCharacterPanel(_screenPosition, _eventCamera))
				return false;
			if (coordinator.IsScreenPointOverCharacterHeadSlot(_screenPosition, _eventCamera))
				return false;
			return TryAcceptVehicleEquipmentDragToBag(_drag, VehicleEquipmentSlotId.FrontalShield);
		}

		if (_drag.CapturedFromBackEquipmentSlot)
		{
			if (!coordinator.IsScreenPointOverCharacterPanel(_screenPosition, _eventCamera))
				return false;
			if (coordinator.IsScreenPointOverCharacterBackSlot(_screenPosition, _eventCamera))
				return false;
			return TryAcceptVehicleEquipmentDragToBag(_drag, VehicleEquipmentSlotId.SurroundShield);
		}

		if (_drag.CapturedBagIndex >= 0)
		{
			if (coordinator.IsScreenPointOverCharacterMainHandSlot(_screenPosition, _eventCamera))
				return TryAcceptVehicleBagDragToEquipmentSlot(_drag, VehicleEquipmentSlotId.TurretWeapon);
			if (coordinator.IsScreenPointOverCharacterHeadSlot(_screenPosition, _eventCamera))
				return TryAcceptVehicleBagDragToEquipmentSlot(_drag, VehicleEquipmentSlotId.FrontalShield);
			if (coordinator.IsScreenPointOverCharacterBackSlot(_screenPosition, _eventCamera))
				return TryAcceptVehicleBagDragToEquipmentSlot(_drag, VehicleEquipmentSlotId.SurroundShield);
			return false;
		}

		return false;
	}

	private bool TryAcceptVehicleEquipmentDragToBag(InventoryCharacterToGroundDrag _drag, VehicleEquipmentSlotId _slotId)
	{
		VehicleInventory inventory = InventoryScreenBindings.Instance?.ActiveVehicleInventory;
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		InventorySlotRuntimeData removedData = inventory.GetEquipmentSlot(_slotId);
		if (!inventory.TryUnequipToBag(_slotId))
			return false;

		PlayVehicleSlotAudio(removedData, _slotId, _equip: false);
		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private bool TryAcceptVehicleBagDragToEquipmentSlot(InventoryCharacterToGroundDrag _drag, VehicleEquipmentSlotId _slotId)
	{
		if (_drag.CapturedBagIndex < 0)
			return false;

		VehicleInventory inventory = InventoryScreenBindings.Instance?.ActiveVehicleInventory;
		if (inventory == null || m_CharacterInventoryPanel == null)
			return false;

		InventorySlotRuntimeData equippedData = default;
		if (_drag.CapturedBagIndex < inventory.BagCount)
			equippedData = inventory.BagItems[_drag.CapturedBagIndex];

		if (!inventory.TryEquipFromBag(_drag.CapturedBagIndex, _slotId))
			return false;

		PlayVehicleSlotAudio(equippedData, _slotId, _equip: true);
		DestroyDetachedDragSlotIfNeeded(_drag.SlotView, m_CharacterInventoryPanel);
		inventory.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private static void PlayVehicleSlotAudio(InventorySlotRuntimeData _data, VehicleEquipmentSlotId _slotId, bool _equip)
	{
		switch (_slotId)
		{
			case VehicleEquipmentSlotId.TurretWeapon:
				if (_data.IsEmpty || _data.Definition == null)
					return;
				if (_equip)
				{
					if (_data.Definition.TryPickEquipmentAddSound(out _))
						InventoryWindowAudioUtility.TryPlayEquipmentAddSoundFromSlot(null, _data, _useMainHandPosition: false);
					else
						InventoryWindowAudioUtility.TryPlayInventoryAddSoundFromSlot(null, _data, _useMainHandPosition: false);
				}
				else
				{
					if (_data.Definition.TryPickEquipmentRemoveSound(out _))
						InventoryWindowAudioUtility.TryPlayEquipmentRemoveSoundFromSlot(
							_data, null, _useMainHandPosition: false);
					else
						InventoryWindowAudioUtility.TryPlayInventoryRemoveSoundFromSlot(_data, null);
				}
				break;

			case VehicleEquipmentSlotId.FrontalShield:
			case VehicleEquipmentSlotId.SurroundShield:
				if (_equip)
					InventoryWindowAudioUtility.TryPlayAttachmentAttachSound(null, false);
				else
					InventoryWindowAudioUtility.TryPlayAttachmentDetachSound(null, false);
				break;
		}
	}

	public bool TryBeginVehicleInventoryExchange(VehicleController _vehicle, RtsUnitMember _playerUnit)
	{
		return InventoryExchangeController.Instance.TryBeginVehicleExchange(_vehicle, _playerUnit, out _);
	}
	#endregion
}
