using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// RTS-обёртка над существующими компонентами юнита:
/// регистрация в списке selectable-юнитов, групповые команды и состояние выделения.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsUnitMember : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilizationController;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOtherController;
	[SerializeField] private UnitFiremanCarryController m_FiremanCarryController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private GameObject m_SelectionVisualRoot;
	[SerializeField] private bool m_DisableDirectInputForRts = true;
	[Header("Selection Name Label")]
	[SerializeField] private GameObject m_SelectionNameLabelRoot;
	[SerializeField] private TextMeshProUGUI m_SelectionNameText;
	[SerializeField, Min(0.1f)] private float m_SelectionLabelHeight = 2.2f;
	[Header("Animator Variation")]
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMin = 0.97f;
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMax = 1.03f;
	[SerializeField] private float m_RuntimeMoveAnimatorSpeed = 1f;
	[SerializeField] 	private bool m_IsSelected;
	private FormationType m_CurrentFormation;

	private static Material s_PathLineMaterial;
	private static readonly Vector3 s_PathLineYOffset = Vector3.up * 0.03f;
	private static readonly List<RtsUnitMember> s_Instances = new List<RtsUnitMember>(128);
	private Coroutine m_PendingCommandCoroutine;
	private int m_PendingCommandVersion;
	private UnitRosterDisplayState m_RosterDisplay;
	private Transform m_CachedCameraTransform;
	private LineRenderer m_PathLine;
	private bool m_HasActiveDestination;
	private float m_DestinationSetTime = -1f;
	private bool m_HasWantedFacing;
	private float m_WantedFacingAngle;
	private bool m_IsRotatingToFacing;
	private float m_FacingRotateVelocity;
	private bool m_FacingSuppressedReady;
	private bool m_WasReadyBeforeFacing;
	private FormationSyncGroup m_FormationSyncGroup;
	private bool m_IsFormationSyncWaiting;
	private readonly List<Vector3> m_Waypoints = new List<Vector3>();
	private float m_NextWaypointCheckTime;

	private struct FacingArrow
	{
		public Vector3 Position;
		public float Angle;
	}

	private struct QueuedCommand
	{
		public Vector3 Destination;
		public UnitClickToMove.MoveTier MoveTier;
		public List<FacingArrow> FacingArrows;
	}

	private readonly List<QueuedCommand> m_CommandQueue = new List<QueuedCommand>();

	private static readonly Color s_FacingArrowColor = new Color(1f, 0.85f, 0.2f, 0.95f);
	private static readonly Vector3 s_FacingArrowYOffset = Vector3.up * 0.05f;
	private readonly List<FacingArrowState> m_FacingArrows = new List<FacingArrowState>();
	private bool m_FacingArrowsDirty;
	private List<FacingArrow> m_ActiveFacingArrows;
	private const float FacingArrowActivationDistance = 5f;

	private struct FacingArrowState
	{
		public LineRenderer Line;
		public float Angle;
		public Vector3 Anchor;
	}
	#endregion

	#region Public Properties
	public static IReadOnlyList<RtsUnitMember> Instances => s_Instances;
	public CharacterInventory CharacterInventory => m_CharacterInventory;
	public bool IsSelected => m_IsSelected;
	public bool IsPlayerSelectable => m_Team != null && m_Team.Team == UnitTeamId.Player;
	public bool WantsReady => m_ReadyHands != null && m_ReadyHands.WantsReady;
	public bool HasQueuedCommands => m_CommandQueue.Count > 0;
	public bool HasActiveDestination => m_HasActiveDestination;
	public FormationType CurrentFormation { get => m_CurrentFormation; set => m_CurrentFormation = value; }
	public float FormationSpacing { get; set; } = 2f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_SelectionCollider == null)
			m_SelectionCollider = GetComponent<Collider>();

		m_RuntimeMoveAnimatorSpeed = UnityEngine.Random.Range(
			Mathf.Min(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax),
			Mathf.Max(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax));

		if (m_DisableDirectInputForRts)
			ApplyDirectInputState(false);

		CreatePathLine();
	}

	private void CreatePathLine()
	{
		if (s_PathLineMaterial == null)
		{
			s_PathLineMaterial = new Material(Shader.Find("Sprites/Default"));
			s_PathLineMaterial.hideFlags = HideFlags.HideAndDontSave;
		}

		GameObject lineGo = new GameObject("PathLine");
		lineGo.transform.SetParent(transform, false);
		m_PathLine = lineGo.AddComponent<LineRenderer>();
		m_PathLine.positionCount = 0;
		m_PathLine.startWidth = 0.06f;
		m_PathLine.endWidth = 0.06f;
		m_PathLine.sharedMaterial = s_PathLineMaterial;
		m_PathLine.startColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);
		m_PathLine.endColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);
		m_PathLine.enabled = false;
	}

	private void RebuildPathLine()
	{
		if (m_PathLine == null)
			return;

		if (m_Waypoints.Count == 0)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
			return;
		}

		float dx = transform.position.x - m_Waypoints[0].x;
		float dz = transform.position.z - m_Waypoints[0].z;
		bool atFirstWaypoint = dx * dx + dz * dz < 0.25f;

		if (atFirstWaypoint)
		{
			m_PathLine.positionCount = m_Waypoints.Count;
			for (int i = 0; i < m_Waypoints.Count; i++)
				m_PathLine.SetPosition(i, m_Waypoints[i] + s_PathLineYOffset);
		}
		else
		{
			int count = 1 + m_Waypoints.Count;
			m_PathLine.positionCount = count;
			m_PathLine.SetPosition(0, transform.position + s_PathLineYOffset);
			for (int i = 0; i < m_Waypoints.Count; i++)
				m_PathLine.SetPosition(i + 1, m_Waypoints[i] + s_PathLineYOffset);
		}

		m_PathLine.enabled = m_IsSelected;
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		SetSelected(false);
		ApplyAnimatorSpeedVariation();
	}

	private void OnDisable()
	{
		if (m_FormationSyncGroup != null && m_FormationSyncGroup.MemberCount > 1)
		{
			m_FormationSyncGroup.MemberCount--;
			if (m_IsFormationSyncWaiting)
				m_FormationSyncGroup.ReachedCount = Mathf.Max(0, m_FormationSyncGroup.ReachedCount - 1);
		}
		CancelPendingCommand();
		ClearWaypoints();
		ResetAnimatorSpeed();
		s_Instances.Remove(this);
		SetSelected(false);
	}

	private void Update()
	{
		ApplyAnimatorSpeedVariation();
		UpdateSelectionLabelBillboard();
		UpdatePathLinePosition();
		UpdateActiveFacingArrows();
		SyncFacingArrows();
		UpdateFacingArrows();
		TryRemoveArrivedDestination();
		TryAdvanceWaypointEarly();
		if (m_IsFormationSyncWaiting)
			TryAdvanceFormationSync();
		UpdateFacingRotation();
	}

	private void TryAdvanceWaypointEarly()
	{
		if (!m_HasActiveDestination)
			return;
		if (m_CommandQueue.Count == 0)
			return;
		if (m_IsRotatingToFacing)
			return;
		if (m_FormationSyncGroup != null)
			return;

		if (Time.time < m_NextWaypointCheckTime)
			return;
		m_NextWaypointCheckTime = Time.time + 0.2f;

		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.3f)
			return;

		UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh || agent.pathPending)
			return;
		if (!agent.hasPath)
			return;

		if (agent.remainingDistance > 0.5f)
			return;

		if (m_Waypoints.Count > 0)
			m_Waypoints.RemoveAt(0);
		RebuildPathLine();
		DequeueAndExecuteNextCommand();
	}

	private void UpdatePathLinePosition()
	{
		if (m_PathLine == null || !m_PathLine.enabled || m_PathLine.positionCount < 2)
			return;
		if (m_Waypoints.Count == 0)
			return;

		float dx = transform.position.x - m_Waypoints[0].x;
		float dz = transform.position.z - m_Waypoints[0].z;
		if (dx * dx + dz * dz >= 0.25f)
			m_PathLine.SetPosition(0, transform.position + s_PathLineYOffset);
	}

	private void UpdateFacingRotation()
	{
		if (!m_IsRotatingToFacing)
			return;

		UnitClickToMove clickToMove = m_ClickToMove;
		float rotateSpeed = clickToMove != null ? clickToMove.RotateSpeed : 6f;

		Quaternion targetRot = Quaternion.Euler(0f, m_WantedFacingAngle, 0f);
		float angle = Quaternion.Angle(transform.rotation, targetRot);

		HandleFacingTurnReady(angle);

		if (angle < 0.5f)
		{
			transform.rotation = targetRot;
			m_IsRotatingToFacing = false;
			if (m_FacingSuppressedReady)
			{
				m_ReadyHands?.SetReadyWanted(true);
				m_FacingSuppressedReady = false;
			}

			if (m_Waypoints.Count > 0)
				m_Waypoints.RemoveAt(0);
			RebuildPathLine();

			if (TryHandleFormationSyncArrival())
				return;

			DequeueAndExecuteNextCommand();
			return;
		}

		float smoothAngle = Mathf.SmoothDampAngle(
			transform.rotation.eulerAngles.y,
			m_WantedFacingAngle,
			ref m_FacingRotateVelocity,
			1f / rotateSpeed,
			Mathf.Infinity,
			Time.deltaTime);

		transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);
	}

	private void HandleFacingTurnReady(float _angleDegrees)
	{
		if (m_ReadyHands == null)
			return;
		if (!m_WasReadyBeforeFacing)
			return;

		if (_angleDegrees > 90f)
		{
			if (!m_FacingSuppressedReady)
			{
				if (m_ReadyHands.IsWeaponEquipped() && m_ReadyHands.WantsReady)
					m_ReadyHands.SetReadyWanted(false);
				m_FacingSuppressedReady = true;
			}
		}
		else if (_angleDegrees < 20f && m_FacingSuppressedReady)
		{
			m_ReadyHands.SetReadyWanted(true);
			m_FacingSuppressedReady = false;
		}
	}

	private void TryRemoveArrivedDestination()
	{
		if (!m_HasActiveDestination)
			return;
		if (m_IsFormationSyncWaiting)
			return;

		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.5f)
			return;

		UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh)
			return;
		if (agent.pathPending)
			return;
		if (agent.hasPath)
			return;

		Vector3 v = agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > 0.01f)
			return;

		if (m_HasWantedFacing)
		{
			if (m_IsRotatingToFacing)
			{
				m_FacingRotateVelocity = 0f;
				m_FacingSuppressedReady = false;
				m_WasReadyBeforeFacing = m_ReadyHands != null && m_ReadyHands.WantsReady;
			}
			else
			{
				ClearFacingOverride();
			}
			m_HasWantedFacing = false;
			m_ActiveFacingArrows = null;
			MarkFacingArrowsDirty();
		}

		if (!m_IsRotatingToFacing)
		{
			if (m_Waypoints.Count > 0)
				m_Waypoints.RemoveAt(0);
			RebuildPathLine();

			if (TryHandleFormationSyncArrival())
				return;

			m_HasActiveDestination = false;
			DequeueAndExecuteNextCommand();
		}

		if (!m_HasActiveDestination && m_Waypoints.Count == 0)
		{
			if (m_PathLine != null)
				m_PathLine.enabled = false;
		}
	}
	#endregion

	#region Public Methods
	public void SetSelected(bool _selected)
	{
		m_IsSelected = _selected;
		if (m_SelectionVisualRoot != null)
			m_SelectionVisualRoot.SetActive(false);

		if (_selected)
		{
			m_RosterDisplay = UnitRosterDisplayState.GetOrCreate(gameObject);
			EnsureSelectionNameLabel();
			RefreshSelectionNameLabel();
		}

		if (m_PathLine != null)
			m_PathLine.enabled = _selected && m_PathLine.positionCount >= 2;

		if (m_SelectionNameLabelRoot != null)
			m_SelectionNameLabelRoot.SetActive(_selected);
	}

	public void IssueMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier)
	{
		ScheduleRtsCommand(() =>
		{
			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			if (selfStabilization != null &&
			    (selfStabilization.IsSelfHealing || selfStabilization.IsHealPresentationActive))
				return;

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			if (stabilizeOther != null &&
			    (stabilizeOther.IsStabilizingOther || stabilizeOther.IsHealPresentationActive))
				return;

			if (_moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint)
				m_MagazineLoadingController?.StopLoading();

			bool isRunOrSprint = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
			if (isRunOrSprint && TryGetComponent(out UnitStamina stamina) && stamina.IsExhausted)
				_moveTier = UnitClickToMove.MoveTier.Walk;

			if (m_ClickToMove != null)
			{
				m_ClickToMove.IssueNavOrder(_worldPosition, _moveTier);
				return;
			}

			if (m_LocomotionDriver != null)
			{
				UnitNavLocomotionDriver.MoveTier navTier = _moveTier switch
				{
					UnitClickToMove.MoveTier.Run => UnitNavLocomotionDriver.MoveTier.Run,
					UnitClickToMove.MoveTier.Sprint => UnitNavLocomotionDriver.MoveTier.Sprint,
					_ => UnitNavLocomotionDriver.MoveTier.Walk
				};
				m_LocomotionDriver.IssueNavOrder(_worldPosition, navTier);
			}
		});
	}

	public void SetPreviewLine(Vector3 _dest)
	{
		if (m_PathLine == null)
			return;

		List<Vector3> waypoints = m_Waypoints;
		if (waypoints.Count == 0)
		{
			m_PathLine.positionCount = 2;
			m_PathLine.SetPosition(0, transform.position + s_PathLineYOffset);
			m_PathLine.SetPosition(1, _dest + s_PathLineYOffset);
		}
		else
		{
			float dx = transform.position.x - waypoints[0].x;
			float dz = transform.position.z - waypoints[0].z;
			bool atFirstWaypoint = dx * dx + dz * dz < 0.25f;

			int count = (atFirstWaypoint ? 0 : 1) + waypoints.Count + 1;
			m_PathLine.positionCount = count;

			int idx = 0;
			if (!atFirstWaypoint)
				m_PathLine.SetPosition(idx++, transform.position + s_PathLineYOffset);

			for (int i = 0; i < waypoints.Count; i++)
				m_PathLine.SetPosition(idx++, waypoints[i] + s_PathLineYOffset);

			m_PathLine.SetPosition(idx, _dest + s_PathLineYOffset);
		}

		m_PathLine.enabled = m_IsSelected;
	}

	public void SetDestinationDirect(Vector3 _dest)
	{
		m_Waypoints.Clear();
		m_Waypoints.Add(_dest);
		RebuildPathLine();
		m_HasActiveDestination = true;
		m_DestinationSetTime = Time.time;
		m_IsRotatingToFacing = false;
		m_HasWantedFacing = false;
		ClearFacingOverride();
		m_ActiveFacingArrows = null;
		MarkFacingArrowsDirty();
	}

	public void EnqueueWaypoint(Vector3 _dest, UnitClickToMove.MoveTier _tier, float? _facing)
	{
		m_Waypoints.Add(_dest);

		var cmd = new QueuedCommand
		{
			Destination = _dest,
			MoveTier = _tier,
			FacingArrows = new List<FacingArrow>(),
		};
		
		if (_facing.HasValue && !float.IsNaN(_facing.Value))
		{
			cmd.FacingArrows.Add(new FacingArrow { Position = _dest, Angle = _facing.Value });
		}
		
		m_CommandQueue.Add(cmd);

		RebuildPathLine();

		bool isIdle = !m_HasActiveDestination && !m_IsRotatingToFacing;
		if (m_Waypoints.Count == 1 && isIdle)
			DequeueAndExecuteNextCommand();

		MarkFacingArrowsDirty();
	}

	public int WaypointCount => m_Waypoints.Count;

	public Vector3 GetWaypointWorld(int _index)
	{
		return _index >= 0 && _index < m_Waypoints.Count ? m_Waypoints[_index] : Vector3.zero;
	}

	public float GetWaypointFacing(int _index)
	{
		int cmdIndex = _index;
		if (m_HasActiveDestination)
		{
			if (cmdIndex == 0)
			{
				if (m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0)
					return m_ActiveFacingArrows[m_ActiveFacingArrows.Count - 1].Angle;
				return float.NaN;
			}
			cmdIndex--;
		}
		if (cmdIndex < 0 || cmdIndex >= m_CommandQueue.Count)
			return float.NaN;
		
		var arrows = m_CommandQueue[cmdIndex].FacingArrows;
		if (arrows == null || arrows.Count == 0)
			return float.NaN;
		return arrows[arrows.Count - 1].Angle;
	}

	public void SetWaypointFacing(int _index, float _angle, Vector3 _anchor)
	{
		int cmdIndex = _index;
		if (m_HasActiveDestination)
		{
			if (cmdIndex == 0)
			{
				AddFacingArrowToActiveSegment(_angle, _anchor);
				return;
			}
			cmdIndex--;
		}
		if (cmdIndex < 0 || cmdIndex >= m_CommandQueue.Count)
			return;
		
		var cmd = m_CommandQueue[cmdIndex];
		if (cmd.FacingArrows == null)
			cmd.FacingArrows = new List<FacingArrow>();
		
		cmd.FacingArrows.Add(new FacingArrow { Position = _anchor, Angle = _angle });
		m_CommandQueue[cmdIndex] = cmd;
		MarkFacingArrowsDirty();
	}
	
	private void AddFacingArrowToActiveSegment(float _angle, Vector3 _anchor)
	{
		if (m_ActiveFacingArrows == null)
			m_ActiveFacingArrows = new List<FacingArrow>();
		
		m_ActiveFacingArrows.Add(new FacingArrow { Position = _anchor, Angle = _angle });
		
		if (m_ReadyHands != null && m_ReadyHands.IsWeaponEquipped() && !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);
		
		MarkFacingArrowsDirty();
	}

	public LineRenderer PathLine => m_PathLine;

	public void ClearWaypoints()
	{
		m_CommandQueue.Clear();
		m_Waypoints.Clear();
		ClearFacingArrows();
		m_ActiveFacingArrows = null;
		if (m_PathLine != null)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
		}
		m_HasActiveDestination = false;
		m_HasWantedFacing = false;
		ClearFacingOverride();
		ClearFormationSync();
	}

	public void SetWantedFacingAngle(float _angle)
	{
		m_HasWantedFacing = true;
		m_WantedFacingAngle = _angle;
		m_IsRotatingToFacing = false;

		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = _angle;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.OverrideFacingAngle = _angle;
		else
			m_IsRotatingToFacing = true;
	}

	private void ClearFacingOverride()
	{
		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = null;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.OverrideFacingAngle = null;
	}

	public void SetReadyWanted(bool _ready)
	{
		ScheduleRtsCommand(() =>
		{
			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			if (_ready && firemanCarry != null && firemanCarry.IsCarryingFallen)
				return;

			if (m_ReadyHands != null)
				m_ReadyHands.SetReadyWanted(_ready);
		});
	}

	public void RequestStance(LocomotionStance _stance)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		ScheduleRtsCommand(() =>
		{
			if (_stance == LocomotionStance.Prone)
				m_MagazineLoadingController?.StopLoading();

			if (m_Stance != null)
				m_Stance.RequestStance(_stance);
		});
	}

	public void HardStop()
	{
		ScheduleRtsCommand(() =>
		{
			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			selfStabilization?.StopSelfStabilization();

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			stabilizeOther?.StopStabilizeOther();

			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			firemanCarry?.RequestRelease();

			m_MagazineLoadingController?.StopLoading();
			m_WeaponReloadController?.StopReload();
			m_FireController?.StopFiring();

			ClearWaypoints();

			if (m_ClickToMove != null)
			{
				m_ClickToMove.HardStop();
				return;
			}

			if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();
		});
	}

	public void StartFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StartFiring();
		});
	}

	public void StopFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StopFiring();
		});
	}

	public WeaponShotAttemptResult TryFireSingleShot()
	{
		WeaponShotAttemptResult result = WeaponShotAttemptResult.NoWeapon;
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				result = m_FireController.TryFireSingleShot();
		});

		return result;
	}

	public void StartManualMagazineLoading()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_MagazineLoadingController == null)
				return;

			m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes();
		});
	}

	public void StartWeaponReload()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponReloadController == null)
				return;

			m_WeaponReloadController.TryStartReload();
		});
	}

	/// <summary>Следующий доступный режим огня по <see cref="WeaponDefinition.AvailableFireModes"/>.</summary>
	public void CycleWeaponFireMode()
	{
		ScheduleRtsCommand(() =>
		{
			m_FireController?.ResetBurstStateForFireModeChange();

			if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			{
				Debug.LogWarning($"{name}: смена режима огня — нет состояния оружия.", this);
				return;
			}

			WeaponFireMode before = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			if (!m_WeaponRuntime.TryCycleToNextFireMode())
			{
				Debug.Log($"{name}: режим огня не изменён (один доступный режим или нет экипированного оружия). Сейчас: {before}.", this);
				return;
			}

			WeaponFireMode after = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			WeaponFireMode effectiveAfter = m_FireController != null
				? m_FireController.ResolveEffectiveFireMode()
				: after;
			string afterLabel = after == WeaponFireMode.Auto
				? $"{WeaponFireModeUtility.GetDisplayName(after)}→{WeaponFireModeUtility.GetDisplayName(effectiveAfter)}"
				: WeaponFireModeUtility.GetDisplayName(after);
			Debug.Log(
				$"{name}: режим огня {WeaponFireModeUtility.GetDisplayName(before)} → {afterLabel}.",
				this);
			PlayFireModeSwitchSound();
		});
	}

	/// <summary>Следующий режим прицеливания юнита: полное, быстрое, на вскидку, авто.</summary>
	public void CycleWeaponAimMode()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponRuntime == null)
			{
				Debug.LogWarning($"{name}: смена режима прицеливания — нет runtime оружия.", this);
				return;
			}

			WeaponAimMode before = m_WeaponRuntime.SelectedAimMode;
			if (!m_WeaponRuntime.TryCycleToNextAimMode(out WeaponAimMode after))
			{
				Debug.Log($"{name}: режим прицеливания не изменён. Сейчас: {before}.", this);
				return;
			}

			Debug.Log(
				$"{name}: режим прицеливания {WeaponAimModeUtility.GetDisplayName(before)} → {WeaponAimModeUtility.GetDisplayName(after)} " +
				$"(порог выстрела: {WeaponAimModeUtility.GetRequiredAimProgress01(after, 0f):P0}; в авто порог зависит от дистанции).",
				this);
			PlayFireModeSwitchSound();
		});
	}

	private void PlayFireModeSwitchSound()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		AudioClip clip = weaponDefinition != null ? weaponDefinition.FireModeSwitchSound : null;
		if (clip == null)
			return;

		Vector3 position = transform.position + Vector3.up * 1.35f;
		if (m_UnitEquipment != null && m_UnitEquipment.MainWeaponRoot != null)
			position = m_UnitEquipment.MainWeaponRoot.position;

		float volume = weaponDefinition.FireModeSwitchSoundVolume;
		AudioSource.PlayClipAtPoint(clip, position, volume);
	}

	public bool TryGetCurrentStance(out LocomotionStance _stance)
	{
		if (m_Stance == null)
		{
			_stance = LocomotionStance.Standing;
			return false;
		}

		_stance = m_Stance.CurrentStance;
		return true;
	}

	public bool TryGetSelectionBounds(out Bounds _bounds)
	{
		if (m_SelectionCollider != null)
		{
			_bounds = m_SelectionCollider.bounds;
			return true;
		}

		_bounds = new Bounds(transform.position, Vector3.one);
		return true;
	}

	public void ApplyDirectInputState(bool _enabled)
	{
		if (m_ClickToMove != null)
			m_ClickToMove.SetDirectInputEnabled(_enabled);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(_enabled);
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(_enabled);
	}

	public void ClearCommandQueue()
	{
		m_CommandQueue.Clear();
		if (m_HasActiveDestination && m_Waypoints.Count > 0)
			m_Waypoints.RemoveRange(1, m_Waypoints.Count - 1);
		MarkFacingArrowsDirty();
	}

	#endregion

	#region Private Methods
	private UnitSelfStabilizationController ResolveSelfStabilizationController()
	{
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();

		return m_SelfStabilizationController;
	}

	private UnitStabilizeOtherController ResolveStabilizeOtherController()
	{
		if (m_StabilizeOtherController == null)
			m_StabilizeOtherController = GetComponent<UnitStabilizeOtherController>();

		return m_StabilizeOtherController;
	}

	private UnitFiremanCarryController ResolveFiremanCarryController()
	{
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();

		return m_FiremanCarryController;
	}

	private void DequeueAndExecuteNextCommand()
	{
		if (m_CommandQueue.Count == 0)
			return;

		QueuedCommand cmd = m_CommandQueue[0];
		m_CommandQueue.RemoveAt(0);

		m_ActiveFacingArrows = cmd.FacingArrows != null && cmd.FacingArrows.Count > 0 
			? new List<FacingArrow>(cmd.FacingArrows) 
			: null;
		
		if (m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0
		    && m_ReadyHands != null && m_ReadyHands.IsWeaponEquipped() && !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);
		
		{
			int arrCnt = m_ActiveFacingArrows != null ? m_ActiveFacingArrows.Count : 0;
			Vector3 arrPos = arrCnt > 0 ? m_ActiveFacingArrows[0].Position : Vector3.zero;
			Debug.Log($"[{gameObject.name}] DEQUEUE: arrows={arrCnt} arrowPos0={arrPos} dest={cmd.Destination} tier={cmd.MoveTier}");
		}
		
		ClearFacingOverride();
		m_HasWantedFacing = false;
		m_HasActiveDestination = true;
		m_DestinationSetTime = Time.time;
		
		IssueMoveOrder(cmd.Destination, cmd.MoveTier);

		MarkFacingArrowsDirty();
	}

	private void UpdateActiveFacingArrows()
	{
		if (!m_HasActiveDestination || m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
		{
			if (m_HasWantedFacing && m_ActiveFacingArrows == null)
			{
				ClearFacingOverride();
				m_HasWantedFacing = false;
			}
			return;
		}

		Vector3 unitPos = transform.position;
		float closestDist = float.MaxValue;
		int closestIndex = -1;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			Vector3 arrowPos = m_ActiveFacingArrows[i].Position;
			float dx = unitPos.x - arrowPos.x;
			float dz = unitPos.z - arrowPos.z;
			float dist = Mathf.Sqrt(dx * dx + dz * dz);

			if (dist < closestDist)
			{
				closestDist = dist;
				closestIndex = i;
			}
		}

		if (closestIndex < 0)
			return;

		if (m_ActiveFacingArrows.Count > 0)
			Debug.Log($"[{gameObject.name}] FACE: dist={closestDist:F1} hasWanted={m_HasWantedFacing} arrowAngle={m_ActiveFacingArrows[closestIndex].Angle:F0} queue={m_CommandQueue.Count} hasDest={m_HasActiveDestination} override={m_ClickToMove?.OverrideFacingAngle}");

		if (closestDist <= FacingArrowActivationDistance)
		{
			float angle = m_ActiveFacingArrows[closestIndex].Angle;
			if (!m_HasWantedFacing || Mathf.Abs(m_WantedFacingAngle - angle) > 0.5f)
			{
				SetWantedFacingAngle(angle);
				MarkFacingArrowsDirty();
			}
		}
		else if (m_HasWantedFacing)
		{
			ClearFacingOverride();
			m_HasWantedFacing = false;
		}

		if (closestDist <= 1.5f)
		{
			m_ActiveFacingArrows.RemoveAt(closestIndex);
			if (m_ActiveFacingArrows.Count == 0)
			{
				ClearFacingOverride();
				m_HasWantedFacing = false;
			}
			MarkFacingArrowsDirty();
		}
	}

	private void MarkFacingArrowsDirty()
	{
		m_FacingArrowsDirty = true;
	}

	private void SyncFacingArrows()
	{
		if (!m_FacingArrowsDirty)
			return;
		m_FacingArrowsDirty = false;

		for (int i = 0; i < m_FacingArrows.Count; i++)
		{
			if (m_FacingArrows[i].Line != null)
				Destroy(m_FacingArrows[i].Line.gameObject);
		}
		m_FacingArrows.Clear();

		if (m_ActiveFacingArrows != null && m_HasActiveDestination)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
				CreateFacingArrowVisual(m_ActiveFacingArrows[i].Angle, m_ActiveFacingArrows[i].Position);
		}

		for (int i = 0; i < m_CommandQueue.Count; i++)
		{
			if (m_CommandQueue[i].FacingArrows == null)
				continue;
			
			for (int j = 0; j < m_CommandQueue[i].FacingArrows.Count; j++)
				CreateFacingArrowVisual(m_CommandQueue[i].FacingArrows[j].Angle, m_CommandQueue[i].FacingArrows[j].Position);
		}
	}

	private void CreateFacingArrowVisual(float _angle, Vector3 _anchor)
	{
		if (s_PathLineMaterial == null)
			return;

		GameObject go = new GameObject("FacingArrow");
		go.transform.SetParent(transform, false);
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.startWidth = 0.04f;
		lr.endWidth = 0.04f;
		lr.sharedMaterial = s_PathLineMaterial;
		lr.startColor = s_FacingArrowColor;
		lr.endColor = s_FacingArrowColor;
		lr.enabled = m_IsSelected;

		Vector3 dir = Quaternion.Euler(0f, _angle, 0f) * Vector3.forward;
		lr.SetPosition(0, _anchor + dir * 0.3f + s_FacingArrowYOffset);
		lr.SetPosition(1, _anchor + dir * 4f + s_FacingArrowYOffset);

		m_FacingArrows.Add(new FacingArrowState
		{
			Line = lr,
			Angle = _angle,
			Anchor = _anchor,
		});
	}

	private void UpdateFacingArrows()
	{
		for (int i = 0; i < m_FacingArrows.Count; i++)
		{
			LineRenderer line = m_FacingArrows[i].Line;
			if (line != null)
				line.enabled = m_IsSelected;
		}
	}

	private void ClearFacingArrows()
	{
		for (int i = 0; i < m_FacingArrows.Count; i++)
		{
			if (m_FacingArrows[i].Line != null)
				Destroy(m_FacingArrows[i].Line.gameObject);
		}
		m_FacingArrows.Clear();
		m_FacingArrowsDirty = false;
	}

	private void ScheduleRtsCommand(Action _command)
	{
		if (_command == null)
			return;

		m_PendingCommandVersion++;

		if (m_PendingCommandCoroutine != null)
			StopCoroutine(m_PendingCommandCoroutine);

		m_PendingCommandCoroutine = null;
		_command();
	}

	private void CancelPendingCommand()
	{
		m_PendingCommandVersion++;
		if (m_PendingCommandCoroutine == null)
			return;

		StopCoroutine(m_PendingCommandCoroutine);
		m_PendingCommandCoroutine = null;
	}

	private void EnsureSelectionNameLabel()
	{
		if (m_SelectionNameLabelRoot != null && m_SelectionNameText != null)
			return;

		if (m_SelectionNameLabelRoot == null)
		{
			m_SelectionNameLabelRoot = new GameObject("SelectionNameLabel", typeof(RectTransform));
			RectTransform rt = m_SelectionNameLabelRoot.GetComponent<RectTransform>();
			rt.SetParent(transform, false);
			rt.sizeDelta = new Vector2(2f, 0.5f);

			Canvas canvas = m_SelectionNameLabelRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 31500;

			m_SelectionNameLabelRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();
		}

		if (m_SelectionNameText == null)
		{
			GameObject textGo = new GameObject("NameText", typeof(RectTransform));
			RectTransform textRt = textGo.GetComponent<RectTransform>();
			textRt.SetParent(m_SelectionNameLabelRoot.transform, false);
			textRt.anchorMin = Vector2.zero;
			textRt.anchorMax = Vector2.one;
			textRt.offsetMin = Vector2.zero;
			textRt.offsetMax = Vector2.zero;

			m_SelectionNameText = textGo.AddComponent<TextMeshProUGUI>();
			m_SelectionNameText.fontSize = 0.15f;
			m_SelectionNameText.alignment = TextAlignmentOptions.Center;
			m_SelectionNameText.color = Color.white;
			m_SelectionNameText.outlineWidth = 0.35f;
			m_SelectionNameText.outlineColor = Color.black;
			m_SelectionNameText.fontStyle = FontStyles.Bold;
		}
	}

	private void RefreshSelectionNameLabel()
	{
		if (m_SelectionNameText == null)
			return;

		if (m_RosterDisplay == null)
			m_RosterDisplay = UnitRosterDisplayState.GetOrCreate(gameObject);

		m_SelectionNameText.text = m_RosterDisplay != null ? m_RosterDisplay.FullName : gameObject.name;
	}

	private void UpdateSelectionLabelBillboard()
	{
		if (m_SelectionNameLabelRoot == null || !m_SelectionNameLabelRoot.activeSelf)
			return;

		if (m_CachedCameraTransform == null)
		{
			Camera cam = Camera.main;
			if (cam != null)
				m_CachedCameraTransform = cam.transform;
			else
				return;
		}

		Transform labelTransform = m_SelectionNameLabelRoot.transform;
		Vector3 worldPos = transform.position + Vector3.up * m_SelectionLabelHeight;
		labelTransform.position = worldPos;
		labelTransform.rotation = m_CachedCameraTransform.rotation;
	}

	private void ApplyAnimatorSpeedVariation()
	{
		if (m_Animator == null)
			return;

		float playbackSync = 1f;
		if (m_LocomotionDriver != null)
			playbackSync = m_LocomotionDriver.AnimatorPlaybackSpeedMultiplier;
		else if (m_ClickToMove != null)
			playbackSync = m_ClickToMove.AnimatorPlaybackSpeedMultiplier;

		m_Animator.speed = IsExecutingMoveOrder()
			? m_RuntimeMoveAnimatorSpeed * playbackSync
			: 1f;
	}

	private bool IsExecutingMoveOrder()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.HasMoveIntent;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;

		return false;
	}

	private void ResetAnimatorSpeed()
	{
		if (m_Animator != null)
			m_Animator.speed = 1f;
	}
	#endregion

	public sealed class FormationSyncGroup
	{
		public int MemberCount;
		public int ReachedCount;
		public float LastSpeedUpdateTime;
	}

	public FormationSyncGroup ActiveFormationSync => m_FormationSyncGroup;

	public void AssignFormationSyncGroup(FormationSyncGroup _group)
	{
		if (m_IsFormationSyncWaiting && m_FormationSyncGroup != _group)
		{
			UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
			if (agent != null && agent.isOnNavMesh)
				agent.isStopped = false;
		}
		m_FormationSyncGroup = _group;
		m_IsFormationSyncWaiting = false;
	}

	public void AssignFormationSpeedMultiplier(float _multiplier)
	{
		float clamped = Mathf.Clamp(_multiplier, 0f, 1f);
		if (m_ClickToMove != null)
			m_ClickToMove.FormationSpeedMultiplier = clamped;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.FormationSpeedMultiplier = clamped;
	}

	public void ClearFormationSync()
	{
		m_FormationSyncGroup = null;
		m_IsFormationSyncWaiting = false;
		AssignFormationSpeedMultiplier(1f);
	}

	private bool TryHandleFormationSyncArrival()
	{
		if (m_FormationSyncGroup == null || m_IsFormationSyncWaiting)
			return false;

		m_FormationSyncGroup.ReachedCount++;

		if (m_FormationSyncGroup.ReachedCount >= m_FormationSyncGroup.MemberCount)
		{
			m_FormationSyncGroup.ReachedCount = 0;
			return false;
		}

		m_IsFormationSyncWaiting = true;
		return true;
	}

	private bool TryAdvanceFormationSync()
	{
		if (m_FormationSyncGroup == null)
			return false;

		if (m_FormationSyncGroup.ReachedCount == 0 && m_IsFormationSyncWaiting)
		{
			m_IsFormationSyncWaiting = false;

			AssignFormationSpeedMultiplier(1f);

			DequeueAndExecuteNextCommand();
			return true;
		}

		return false;
	}
}
