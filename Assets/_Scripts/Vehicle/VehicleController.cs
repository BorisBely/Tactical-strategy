using System;
using System.Collections.Generic;
using CombatVehicleSystem;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(UnitTeam))]
public sealed class VehicleController : MonoBehaviour
{
	#region Static
	private static readonly List<VehicleController> s_Instances = new(16);
	public static IReadOnlyList<VehicleController> Instances => s_Instances;
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleNavigation.VehicleNavigation m_Navigation;
	[SerializeField] private VehicleBrain m_Brain;
	[SerializeField] private WheeledMotor m_WheeledMotor;
	[SerializeField] private VehicleTuning m_Tuning;
	[SerializeField] private VehicleSurfaceProbe m_SurfaceProbe;
	[SerializeField] private VehicleBodyTilt m_BodyTilt;
	[SerializeField] private VehiclePathLineVisual m_PathLine;
	[SerializeField] private VehicleSeatLayout m_Seats;
	[SerializeField] private VehicleDoorController m_Doors;
	[SerializeField] private VehicleBoardController m_Board;
	[SerializeField] private VehicleGunnerHatch m_GunnerHatch;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private bool m_IsSelected;
	[SerializeField] private VehicleSpeedMode m_SpeedCeiling = VehicleSpeedMode.Max;
	[SerializeField] private string m_SelectionDisplayName = "Armoured Car";
	[SerializeField] private GameObject m_SelectionNameLabelRoot;
	[SerializeField] private TMPro.TextMeshProUGUI m_SelectionNameText;
	[SerializeField, Min(0.1f)] private float m_SelectionLabelHeight = 2.8f;
	[Header("New Architecture")]
	[SerializeField] private VehicleData m_VehicleData;
	[SerializeField] private bool m_LogDriveSink = true;
	[Header("Temp")]
	[SerializeField] private bool m_TempAllowDriverlessControl = true;
	[Header("Drive Debug")]
	[SerializeField] private bool m_LogDriveDebug = true;
	[SerializeField] private float m_LogDriveInterval = 0.15f;
	private float m_LogDriveTimer;
	private float m_LastVy, m_LastSpd;
	private Vector3 m_LastVel;
	private float m_DiagLastThr;
	private DriveMode m_PrevMode = (DriveMode)(-1);
	#endregion

	#region Private Fields
	private VehicleEngine m_VehicleEngine;
	private VehicleSuspension m_VehicleSuspension;
	private VehicleMovement m_VehicleMovement;
	private Transform m_CachedCameraTransform;
	private int m_DiagFrame;
	private float m_PostCodeVelY;
	private BoxCollider m_BodyCollider;
	private bool m_PhysicsReady;
	#endregion

	#region Properties
	public bool IsSelected => m_IsSelected;
	public VehicleNavigation.VehicleNavigation Navigation => m_Navigation;
	public VehicleBrain Brain => m_Brain;
	public VehicleSeatLayout Seats => m_Seats;
	public VehicleDoorController Doors => m_Doors;
	public VehicleBoardController Board => m_Board;
	public VehicleGunnerHatch GunnerHatch => m_GunnerHatch;
	public UnitTeam TeamComponent => m_Team;
	public UnitTeamId Team => m_Team != null ? m_Team.Team : UnitTeamId.Neutral;
	public bool IsDriveWakeStabilizing => false;
	public bool IsDrivePhysicsReady => true;
	public bool IsDriveMotorAllowed => true;
	public bool IsPlayerSelectable => m_TempAllowDriverlessControl || Team == UnitTeamId.Player || HasOccupantOfTeam(UnitTeamId.Player);
	public bool HasPassengers => m_Seats != null && m_Seats.OccupantCount > 0;
	public bool HasDriver => m_Seats != null && m_Seats.HasDriver;
	public bool IsEngineRunning => m_Brain != null && m_Brain.EngineRunning;
	public bool CanToggleEngine => HasDriver && Team == UnitTeamId.Player;
	public VehicleSpeedMode SpeedCeiling => m_SpeedCeiling;
	public VehiclePathLineVisual PathLine => m_PathLine;
	public bool IsDriveWakeLocked => false;
	public bool UseNewArchitecture => m_VehicleData != null;
	#endregion

	#region Events
	public event Action SelectionChanged;
	public event Action OccupancyChanged;
	public event Action TeamChanged;
	public event Action EngineStateChanged;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureComponents();
		VehicleHierarchyBinder.EnsureBound(this);
		SyncDriveControlFromDriver();
		LogVehicle("always-dynamic RB");
	}

	private void Start()
	{
		if (m_VehicleData != null && m_VehicleSuspension != null && TryGetComponent(out Rigidbody body))
		{
			float hubY = m_VehicleData.WheelLocalPositions[0].y;
			float groundY = 0f;
			float rootY = groundY - hubY + m_VehicleData.SuspensionTravel + m_VehicleData.WheelRadius + 0.05f;
			Vector3 p = transform.position; p.y = rootY;
			transform.position = p;
			Physics.SyncTransforms();
			LogVehicle($"SNAP y={rootY:F3} interp={body.interpolation}");

			// Dump ALL components and their enabled state
			var allComps = GetComponents<Component>();
			var csb = new System.Text.StringBuilder(512);
			csb.AppendLine($"=== ALL COMPONENTS on {name} ===");
			foreach (var c in allComps)
			{
				var mb = c as MonoBehaviour;
				string extra = mb != null ? $" en={mb.enabled}" : "";
				csb.AppendLine($"  {c.GetType().Name}{extra}");
			}
			// Also dump children with interesting components
			foreach (var t in GetComponentsInChildren<Transform>(true))
			{
				if (t == transform) continue;
				var comps = t.GetComponents<Component>();
				if (comps.Length > 1) // more than just Transform
				{
					csb.AppendLine($"  [{t.name}]");
					foreach (var c in comps)
					{
						if (c is Transform) continue;
						var mb = c as MonoBehaviour;
						string extra = mb != null ? $" en={mb.enabled}" : "";
						csb.AppendLine($"    {c.GetType().Name}{extra}");
					}
				}
			}
			LogVehicle(csb.ToString());

			StartCoroutine(RefreshPhysicsAfterSpawn());
		}
	}

	private System.Collections.IEnumerator RefreshPhysicsAfterSpawn()
	{
		yield return new WaitForFixedUpdate();
		yield return new WaitForFixedUpdate();

		Physics.SyncTransforms();
		if (TryGetComponent(out Rigidbody rb)) rb.WakeUp();

		if (m_BodyCollider != null)
		{
			m_BodyCollider.enabled = false;
			Physics.SyncTransforms();
			yield return new WaitForFixedUpdate();
			m_BodyCollider.enabled = true;
			Physics.SyncTransforms();
			LogVehicle("PHYSICS_REFRESH body collider toggled");
		}

		m_PhysicsReady = true;
		LogVehicle("PHYSICS_READY — drive enabled");
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this)) s_Instances.Add(this);
		if (m_Brain != null) m_Brain.EngineStateChanged += OnBrainEngineStateChanged;
	}

	private void OnDisable()
	{
		s_Instances.Remove(this);
		if (m_Brain != null) m_Brain.EngineStateChanged -= OnBrainEngineStateChanged;
		if (m_IsSelected) SetSelected(false);
	}

	private void LateUpdate()
	{
		UpdateSelectionLabelBillboard();

		// Draw WC rays
		if (m_VehicleSuspension?.Wheels != null)
			foreach (var wc in m_VehicleSuspension.Wheels)
				if (wc != null)
					Debug.DrawRay(wc.transform.TransformPoint(wc.center), -transform.up * (wc.radius + wc.suspensionDistance + 0.5f),
						wc.GetGroundHit(out _) ? Color.green : Color.red);
	}

	private bool m_ColFirst;
	private void OnCollisionEnter(Collision c)
	{
		if (m_ColFirst) return; m_ColFirst = true;
		var sb = new System.Text.StringBuilder(256);
		sb.Append($"COL_ENTER n={c.contactCount} rv={c.relativeVelocity.y:F2}");
		for (int i = 0; i < c.contactCount; i++)
		{
			var cp = c.GetContact(i);
			sb.Append($" [{cp.thisCollider.name}↔{cp.otherCollider.name} ptY={cp.point.y:F2}]");
		}
		LogVehicle(sb.ToString());
	}

	private void FixedUpdate()
	{
		if (m_VehicleData == null || m_VehicleEngine == null || m_VehicleSuspension == null)
			return;

		float dt = Time.fixedDeltaTime;
		DriveCommand cmd = default;
		if (m_Navigation != null)
		{
			cmd.Throttle = m_Navigation.ThrottleCommand;
			cmd.Steer = m_Navigation.SteerCommand;
			// Trace WHO wrote the throttle
			var brainCmd = m_Brain?.CurrentCommand;
			if (Mathf.Abs(cmd.Throttle - m_DiagLastThr) > 0.02f)
				LogVehicle($"THR_SRC raw={cmd.Throttle:F2} brain_thr={brainCmd?.Throttle:F2} brain_brk={brainCmd?.BrakeMode}");
			m_DiagLastThr = cmd.Throttle;
		}

		// --- DIAGNOSTIC: per-frame log ---
		if (m_DiagFrame < 30)
		{
			m_DiagFrame++;
			if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
			{
				var sb = new System.Text.StringBuilder(256);
				int g = 0;
				for (int i = 0; i < m_VehicleSuspension.States.Length; i++)
					if (m_VehicleSuspension.States[i].HasContact) g++;
				sb.Append($"F{m_DiagFrame:D2} y={transform.position.y:F3} vy={rb.linearVelocity.y:F2} ang={rb.angularVelocity.magnitude*Mathf.Rad2Deg:F0} g={g}");
				for (int i = 0; i < m_VehicleSuspension.States.Length; i++)
				{
					var s = m_VehicleSuspension.States[i];
					sb.Append($" [W{i} g={s.HasContact} F={s.SuspensionForce:F0} wcY={s.WorldCenter.y:F2}]");
				}
				LogVehicle(sb.ToString());

				// PHYSX_IMPULSE check
				if (m_DiagFrame > 1)
				{
					float pre = m_PostCodeVelY;
					float cur = rb.linearVelocity.y;
					if (Mathf.Abs(cur - pre) > 0.3f)
						LogVehicle($"PHYSX_IMP f={m_DiagFrame} {pre:F2}→{cur:F2} d={cur-pre:F2}");
				}
			}
		}

		if (m_PhysicsReady)
		{
			float preThr = cmd.Throttle;
			m_VehicleEngine.Update(cmd, dt, m_VehicleMovement.SpeedMs);
			if (Mathf.Abs(m_VehicleEngine.AppliedThrottle - preThr) > 0.1f || m_VehicleEngine.Mode != m_PrevMode)
				LogVehicle($"ENG_OUT raw={preThr:F2}→aThr={m_VehicleEngine.AppliedThrottle:F2} mt={m_VehicleEngine.MotorTorque:F0} mode={m_PrevMode}→{m_VehicleEngine.Mode}");
			m_PrevMode = m_VehicleEngine.Mode;
			m_VehicleSuspension.Update(m_VehicleEngine, m_VehicleMovement.SpeedMs, GetComponent<Rigidbody>());
		}
		m_VehicleMovement.Update(m_VehicleSuspension.States);
		m_VehicleMovement.ApplyCoastDrag(m_VehicleEngine.CoastDrag);

		// Save post-code velY for PHYSX_IMPULSE on next frame
		if (m_DiagFrame < 30 && TryGetComponent(out Rigidbody rb2))
			m_PostCodeVelY = rb2.linearVelocity.y;

		// --- DRIVE DEBUG ---
		if (m_LogDriveDebug)
		{
			m_LogDriveTimer += dt;
			Rigidbody rb = GetComponent<Rigidbody>();

			// Spike detector
			float vy = rb.linearVelocity.y;
			float spd = rb.linearVelocity.magnitude;
			if (m_DiagFrame > 5 && (Mathf.Abs(vy - m_LastVy) > 0.4f || Mathf.Abs(spd - m_LastSpd) > 0.5f))
			{
				var sb = new System.Text.StringBuilder(512);
				sb.Append($"SPIKE vy={m_LastVy:F2}→{vy:F2} spd={m_LastSpd:F2}→{spd:F2}");
				sb.Append($" cmd(thr={cmd.Throttle:F2} st={cmd.Steer:F2} br={cmd.Brake})");
				sb.Append($" mt={m_VehicleEngine.MotorTorque:F0} bt={m_VehicleEngine.BrakeTorque:F0}");
				for (int i = 0; i < m_VehicleSuspension.States.Length; i++)
				{
					ref readonly var w = ref m_VehicleSuspension.States[i];
					sb.Append($" [W{i} g={w.HasContact} f={(w.HasContact?w.SuspensionForce:0):F0} rpm={w.Rpm:F0} sF={w.SlipRatio:F2}]");
				}
				LogVehicle(sb.ToString());
			}
			m_LastVy = vy; m_LastSpd = spd;

			// Brake/throttle conflict checks
			if (m_VehicleEngine.BrakeTorque > 10f && !cmd.Brake)
				LogVehicle($"COAST_BRAKE bt={m_VehicleEngine.BrakeTorque:F0} thr={cmd.Throttle:F2} spd={spd*3.6f:F1}");
			if (m_VehicleEngine.BrakeTorque > 10f && cmd.Throttle > 0.1f)
				LogVehicle($"BRAKE+THR conflict bt={m_VehicleEngine.BrakeTorque:F0} thr={cmd.Throttle:F2}");

			// Periodic detailed log
			if (m_LogDriveTimer >= m_LogDriveInterval)
			{
				m_LogDriveTimer = 0f;
				var sb = new System.Text.StringBuilder(512);
				float pitch = transform.rotation.eulerAngles.x; if (pitch > 180f) pitch -= 360f;
				Vector3 localAcc = m_LastVel.sqrMagnitude > 0.001f ? transform.InverseTransformDirection((rb.linearVelocity - m_LastVel) / Mathf.Max(dt, 0.001f)) : Vector3.zero;
				m_LastVel = rb.linearVelocity;
				sb.Append($"DRV spd={spd*3.6f:F1}km/h pitch={pitch:F1}° aFwd={localAcc.z:F1} aUp={localAcc.y:F1}");
				sb.Append($" vel=({rb.linearVelocity.x:F1},{rb.linearVelocity.y:F1},{rb.linearVelocity.z:F1})");
				float delta = transform.position.y - rb.position.y;
				if (Mathf.Abs(delta) > 0.02f)
					sb.Append($" TvsRB_dY={delta:F3}");
				sb.Append($" ang=({rb.angularVelocity.x:F1},{rb.angularVelocity.y:F1},{rb.angularVelocity.z:F1})");
				sb.Append($" cmd(raw={cmd.Throttle:F2} st={cmd.Steer:F2} br={cmd.Brake}) mode={m_VehicleEngine.Mode}");
				if (m_VehicleEngine.DesiredMode != m_VehicleEngine.Mode)
					sb.Append($"→{m_VehicleEngine.DesiredMode} t={m_VehicleEngine.ModeTimer:F2}s");
				sb.Append($" aThr={m_VehicleEngine.AppliedThrottle:F2}");
				sb.Append($" mt={m_VehicleEngine.MotorTorque:F0} bt={m_VehicleEngine.BrakeTorque:F0}({m_VehicleEngine.BrakeSource}) sa={m_VehicleEngine.SteerAngle:F1}");
				if (m_Navigation != null)
					sb.Append($" nav(state={m_Navigation.DriverState} spdMode={m_Navigation.ActiveSpeedMode})");

				WheelState[] st = m_VehicleSuspension.States;
				for (int i = 0; i < st.Length; i++)
				{
					ref readonly var w = ref st[i];
					sb.Append($" [W{i} g={w.HasContact} f={(w.HasContact?w.SuspensionForce:0):F0} rpm={w.Rpm:F0} sF={w.SlipRatio:F2} sS={w.SidewaysSlip:F2} cmp={w.SuspensionCompression:F2}]");
				}
				LogVehicle(sb.ToString());

				// Separate diagnostic: avg slip, pitch, torque summary
				float avgSlip = 0f; int cnt = 0;
				for (int i = 0; i < st.Length; i++)
					if (st[i].HasContact) { avgSlip += Mathf.Abs(st[i].SlipRatio); cnt++; }
				if (cnt > 0) avgSlip /= cnt;
				LogVehicle($"DIAG aThr={m_VehicleEngine.AppliedThrottle:F3} mt={m_VehicleEngine.MotorTorque:F0} avgSlip={avgSlip:F3} pitch={pitch:F2}° mode={m_VehicleEngine.Mode}");
			}
		}
	}
	#endregion

	#region Init
	public void EnsureComponents()
	{
		if (m_Seats == null && !TryGetComponent(out m_Seats))
			m_Seats = gameObject.AddComponent<VehicleSeatLayout>();
		if (m_Doors == null && !TryGetComponent(out m_Doors))
			m_Doors = gameObject.AddComponent<VehicleDoorController>();
		if (m_Board == null && !TryGetComponent(out m_Board))
			m_Board = gameObject.AddComponent<VehicleBoardController>();
		if (m_GunnerHatch == null && !TryGetComponent(out m_GunnerHatch))
			m_GunnerHatch = gameObject.AddComponent<VehicleGunnerHatch>();
		if (m_Team == null && !TryGetComponent(out m_Team))
			m_Team = gameObject.AddComponent<UnitTeam>();

		EnsurePhysicsDrive();
		m_Board?.Configure(this, m_Seats, m_Doors);

		if (!TryGetComponent(out NavMeshAgent agent))
			agent = gameObject.AddComponent<NavMeshAgent>();
		agent.radius = 1.4f; agent.height = 1.8f; agent.baseOffset = 0f;
		agent.updatePosition = false; agent.updateRotation = false;

		EnsureSelectionCollider();
	}

	private bool m_PhysicsInitDone;
	public void EnsurePhysicsDrive()
	{
		if (m_PhysicsInitDone) return;
		m_PhysicsInitDone = true;

		if (!TryGetComponent(out Rigidbody body))
		{
			body = gameObject.AddComponent<Rigidbody>();
			body.interpolation = RigidbodyInterpolation.Interpolate;
			body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		}

		if (m_WheeledMotor == null && !TryGetComponent(out m_WheeledMotor))
			m_WheeledMotor = gameObject.AddComponent<WheeledMotor>();
		if (m_Brain == null && !TryGetComponent(out m_Brain))
			m_Brain = gameObject.AddComponent<VehicleBrain>();
		if (m_Navigation == null && !TryGetComponent(out m_Navigation))
			m_Navigation = gameObject.AddComponent<VehicleNavigation.VehicleNavigation>();
		if (m_SurfaceProbe == null && !TryGetComponent(out m_SurfaceProbe))
			m_SurfaceProbe = gameObject.AddComponent<VehicleSurfaceProbe>();
		if (m_BodyTilt == null && !TryGetComponent(out m_BodyTilt))
			m_BodyTilt = gameObject.AddComponent<VehicleBodyTilt>();
		m_BodyTilt.enabled = m_VehicleData == null; // off with new arch (WC via Suspension, not Motor)
		if (m_VehicleData == null)
			m_BodyTilt.BindMotor(m_WheeledMotor);
		if (m_PathLine == null && !TryGetComponent(out m_PathLine))
			m_PathLine = gameObject.AddComponent<VehiclePathLineVisual>();
		m_PathLine.Configure(this, m_Navigation);
		if (!TryGetComponent(out VehicleSafety _)) gameObject.AddComponent<VehicleSafety>();

		// Disable legacy visual components — they fight with WheelCollider at speed
		if (TryGetComponent(out VehicleWheelVisuals wv)) wv.enabled = false;
		if (TryGetComponent(out VehicleMotor vm)) vm.enabled = false;

		VehicleTuning tuning = m_Tuning;
		if (tuning == null) tuning = VehicleTuning.CreateRuntimeLightUtilityHumvee();
		m_Tuning = tuning;
		m_Brain.AutoWire();
		m_Brain.SetTuning(tuning);
		m_Navigation.RebuildLimiters();

		// New architecture — creates wheels via VehicleSuspension
		if (m_VehicleData == null)
			m_VehicleData = Resources.Load<VehicleData>("VehicleData_Humvee");
		if (m_VehicleData != null)
		{
			// Kill ALL legacy drive — WheeledMotor writes motorTorque in FixedUpdate
			m_WheeledMotor.enabled = false;
			m_BodyTilt.enabled = false;

			// Destroy OLD wheel colliders — they still have ForwardStiffness=2.5!
			foreach (var wc in GetComponentsInChildren<WheelCollider>())
			{
				if (wc.name.StartsWith("WheelCollider_"))
					UnityEngine.Object.Destroy(wc);
			}
			WheelAntiStuck wasComp = GetComponent<WheelAntiStuck>();
			if (wasComp != null) wasComp.enabled = false;

			if (TryGetComponent(out UnityEngine.AI.NavMeshAgent agent))
				agent.enabled = false;
			body.mass = m_VehicleData.Mass;
			body.centerOfMass = m_VehicleData.CenterOfMass;
			body.angularDamping = m_VehicleData.AngularDamping;
			body.maxAngularVelocity = m_VehicleData.MaxAngularVelocity;
			body.isKinematic = false;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.interpolation = RigidbodyInterpolation.Interpolate;
			body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			m_VehicleEngine = new VehicleEngine(m_VehicleData);
			m_VehicleSuspension = new VehicleSuspension(m_VehicleData);
			m_VehicleSuspension.CreateWheels(transform);
			m_VehicleMovement = new VehicleMovement(body);

			LogVehicle($"NEW_ARCH: {m_VehicleSuspension.Wheels.Length} wheels created, spring={m_VehicleData.SpringRate} damper={m_VehicleData.DamperRate}");
			foreach (var wc in m_VehicleSuspension.Wheels)
				LogVehicle($"  WC: {wc.name} lY={wc.transform.localPosition.y:F2} r={wc.radius} sd={wc.suspensionDistance} sp={wc.suspensionSpring.spring} tp={wc.suspensionSpring.targetPosition}");

			LogVehicle($"LEGACY_KILL: WM={m_WheeledMotor.enabled} Br={m_Brain.enabled} BT={m_BodyTilt.enabled} VM={GetComponent<VehicleMotor>()?.enabled} WV={GetComponent<VehicleWheelVisuals>()?.enabled} AS={GetComponent<WheelAntiStuck>()?.enabled}");
		}

		body.isKinematic = false;
		LogVehicle($"EnsurePhysicsDrive mass={body.mass:F0} com={body.centerOfMass} y={transform.position.y:F3}");
	}
	#endregion

	#region Commands
	public void IssueMoveOrder(Vector3 pos) => IssueMoveOrder(VehicleMoveGoal.FromPosition(pos, VehicleSpeedMode.Medium));
	public void IssueMoveOrder(Vector3 pos, VehicleSpeedMode mode) => IssueMoveOrder(VehicleMoveGoal.FromPosition(pos, mode));
	public void IssueMoveOrder(VehicleMoveGoal goal)
	{
		if (!m_TempAllowDriverlessControl && Team != UnitTeamId.Player) return;
		if (m_VehicleData != null && !m_PhysicsReady) return;
		m_Board?.CancelAllJobsAndCloseDoors("move");
		EnsureDriveReadyForMove();
		if (m_Brain == null || !m_Brain.ControlActive || !m_Brain.EngineRunning) return;
		m_Navigation?.SetDestination(goal);
		m_PathLine?.ClearPreview();
		m_PathLine?.RefreshCommitted();
	}

	public void HardStop() { m_Navigation?.StopHard(); m_PathLine?.ClearPreview(); m_PathLine?.RefreshCommitted(); }
	public bool StartEngine() { SyncDriveControlFromDriver(); bool ok = m_Brain != null && m_Brain.StartEngine(); EngineStateChanged?.Invoke(); return ok; }
	public void StopEngine() { m_Navigation?.StopSoft(); m_Brain?.StopEngine(); EngineStateChanged?.Invoke(); }
	public bool ToggleEngine() { if (!CanToggleEngine) return false; if (IsEngineRunning) { StopEngine(); return false; } return StartEngine(); }

	public void SetSelected(bool sel)
	{
		if (sel && !IsPlayerSelectable) sel = false;
		if (m_IsSelected == sel) return;
		m_IsSelected = sel;
		if (m_IsSelected) { EnsureSelectionNameLabel(); RefreshSelectionNameLabel(); }
		if (m_SelectionNameLabelRoot != null) m_SelectionNameLabelRoot.SetActive(m_IsSelected);
		SelectionChanged?.Invoke();
	}

	private void EnsureDriveReadyForMove() { SyncDriveControlFromDriver(); if (m_Brain != null && m_Brain.ControlActive && !m_Brain.EngineRunning) m_Brain.StartEngine(); }
	private void SyncDriveControlFromDriver()
	{
		if (m_Brain == null) EnsurePhysicsDrive();
		if (m_Brain == null) return;
		bool allow = HasDriver || m_TempAllowDriverlessControl;
		if (allow) { if (!m_Brain.ControlActive) m_Brain.SetControlActive(true); if (m_TempAllowDriverlessControl && !HasDriver && !m_Brain.EngineRunning) m_Brain.StartEngine(); }
		else { m_Navigation?.Stop(); if (m_Brain.EngineRunning) m_Brain.StopEngine(); if (m_Brain.ControlActive) m_Brain.SetControlActive(false); }
	}
	private void OnBrainEngineStateChanged(bool _) { EngineStateChanged?.Invoke(); if (m_IsSelected) RtsUnitSelectionManager.Instance?.NotifySelectionUiRefresh(); }
	#endregion

	#region Logging
	private void LogVehicle(string msg) { if (m_LogDriveSink) Debug.Log($"[VehicleNav] {name} {msg}", this); }
	#endregion

	#region Stubs (keep public API)
	public VehicleSpeedMode LastIssuedSpeedMode => VehicleSpeedMode.Medium;
	public void BoardUnits(IReadOnlyList<RtsUnitMember> u, VehicleBoardSide s) => m_Board?.EnqueueBoard(u, s);
	public void BoardUnitsAsGunner(IReadOnlyList<RtsUnitMember> u, VehicleBoardSide s) => m_Board?.EnqueueBoardGunner(u, s);
	public void DisembarkAll() => m_Board?.EnqueueDisembarkAll(true);
	public void DisembarkAllExceptDriver() => m_Board?.EnqueueDisembarkAll(false);
	public void DisembarkUnit(RtsUnitMember u) => m_Board?.EnqueueDisembarkUnit(u);
	public void LoadWoundedFromCarrier(RtsUnitMember c) => m_Board?.EnqueueLoadWoundedFromCarrier(c);
	public bool HasOccupantOfTeam(UnitTeamId t) { if (m_Seats == null) return false; var l = new List<(VehicleSeatId, RtsUnitMember)>(); m_Seats.CollectOccupantsOrdered(l); foreach (var o in l) if (o.Item2 != null && ResolveUnitTeam(o.Item2) == t) return true; return false; }
	public void NotifyOccupancyChanged() { SyncDriveControlFromDriver(); OccupancyChanged?.Invoke(); }
	public void SetMovePreview(Vector3 p, VehicleSpeedMode m) => m_PathLine?.SetPreviewDestination(p, m, null);
	public void SetMovePreview(Vector3 p, VehicleSpeedMode m, float? h) => m_PathLine?.SetPreviewDestination(p, m, h);
	public void ClearMovePreview() => m_PathLine?.ClearPreview();
	public void SnapChassisToGround(bool _force = true) { }

	public class UnitBlockerStub { public Collider BlockCollider => null; public void RefreshIgnoredDriveColliders() { } }
	private UnitBlockerStub m_UnitBlockerStub = new();
	public UnitBlockerStub UnitBlocker => m_UnitBlockerStub;
	public void SetIgnoreUnitColliders(RtsUnitMember u, bool ign) { }
	public void SyncOwnershipFromDriverSeat() { }
	public bool CanToggleGunnerTurret() => false;
	public bool IsGunnerOnTurret => false;
	public bool ToggleGunnerTurret() => false;
	public VehicleSpeedMode CycleSpeedCeiling() { m_SpeedCeiling = VehicleSpeedModeUtil.Next(m_SpeedCeiling); return m_SpeedCeiling; }
	public bool CanAcceptBoarder(RtsUnitMember u) => CanAcceptBoarderTeam(ResolveUnitTeam(u));
	public bool CanAcceptBoarderTeam(UnitTeamId t) { if (m_Seats == null || m_Seats.OccupantCount == 0) return true; var l = new List<(VehicleSeatId, RtsUnitMember)>(); m_Seats.CollectOccupantsOrdered(l); foreach (var o in l) if (o.Item2 != null && ResolveUnitTeam(o.Item2) != t) return false; return true; }
	public bool CanAcceptAnyBoarder(IReadOnlyList<RtsUnitMember> u) { if (u == null || u.Count == 0) return false; for (int i = 0; i < u.Count; i++) if (u[i] != null && CanAcceptBoarder(u[i])) return true; return false; }
	public static UnitTeamId ResolveUnitTeam(RtsUnitMember u) { if (u != null && u.TryGetComponent(out UnitTeam ut)) return ut.Team; return UnitTeamId.Neutral; }
	public static VehicleController FindUnderCollider(Collider c) => c != null ? c.GetComponentInParent<VehicleController>() : null;
	#endregion

	#region Selection Label
	private void EnsureSelectionNameLabel()
	{
		if (m_SelectionNameLabelRoot != null) return;
		m_SelectionNameLabelRoot = new GameObject("SelectionNameLabel", typeof(RectTransform));
		var rt = m_SelectionNameLabelRoot.GetComponent<RectTransform>();
		rt.SetParent(transform, false); rt.sizeDelta = new Vector2(2.4f, 0.5f);
		var c = m_SelectionNameLabelRoot.AddComponent<Canvas>();
		c.renderMode = RenderMode.WorldSpace; c.sortingOrder = 31500;
		if (m_SelectionNameText == null)
		{
			var tg = new GameObject("NameText", typeof(RectTransform));
			var tr = tg.GetComponent<RectTransform>();
			tr.SetParent(m_SelectionNameLabelRoot.transform, false);
			tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
			m_SelectionNameText = tg.AddComponent<TMPro.TextMeshProUGUI>();
			m_SelectionNameText.fontSize = 0.18f;
			m_SelectionNameText.alignment = TMPro.TextAlignmentOptions.Center;
			m_SelectionNameText.color = Color.white;
		}
	}
	private void RefreshSelectionNameLabel() { if (m_SelectionNameText != null) m_SelectionNameText.text = string.IsNullOrWhiteSpace(m_SelectionDisplayName) ? gameObject.name : m_SelectionDisplayName; }
	private void UpdateSelectionLabelBillboard()
	{
		if (m_SelectionNameLabelRoot == null || !m_SelectionNameLabelRoot.activeSelf) return;
		if (m_CachedCameraTransform == null) { var cam = Camera.main; if (cam == null) return; m_CachedCameraTransform = cam.transform; }
		m_SelectionNameLabelRoot.transform.position = transform.position + Vector3.up * m_SelectionLabelHeight;
		m_SelectionNameLabelRoot.transform.rotation = m_CachedCameraTransform.rotation;
	}
	public void EnsureSelectionCollider()
	{
		int vl = LayerMask.NameToLayer("Vehicle");
		if (vl >= 0) SetLayerRecursive(transform, vl);
		if (!TryGetComponent(out BoxCollider box)) box = gameObject.AddComponent<BoxCollider>();
		box.isTrigger = true; box.enabled = true;
		if (box.size.sqrMagnitude < 0.01f) { box.center = new Vector3(0f, 1.35f, 0.1f); box.size = new Vector3(2.6f, 1.9f, 4.8f); }
		m_SelectionCollider = box;
	}
	private static void SetLayerRecursive(Transform t, int l) { t.gameObject.layer = l; for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), l); }
	#endregion
}
