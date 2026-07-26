using System;
using System.Collections.Generic;
using CombatVehicleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Корневой фасад машины: select, move, board/disembark, gunner, engine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(UnitTeam))]
public sealed class VehicleController : MonoBehaviour, CombatVehicleSystem.IVehicleDriveGating
{
	#region Static
	private static readonly List<VehicleController> s_Instances = new List<VehicleController>(16);

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
	[SerializeField] private VehicleWheelVisuals m_Wheels;
	[SerializeField] private VehicleSeatLayout m_Seats;
	[SerializeField] private VehicleDoorController m_Doors;
	[SerializeField] private VehicleBoardController m_Board;
	[SerializeField] private VehicleGunnerHatch m_GunnerHatch;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private bool m_IsSelected;
	[SerializeField] private VehicleSpeedMode m_SpeedCeiling = VehicleSpeedMode.Max;
	[SerializeField] private VehicleSpeedMode m_LastIssuedSpeedMode = VehicleSpeedMode.Medium;
	[Header("Selection Name Label")]
	[SerializeField] private string m_SelectionDisplayName = "Бронеавтомобиль";
	[SerializeField] private GameObject m_SelectionNameLabelRoot;
	[SerializeField] private TextMeshProUGUI m_SelectionNameText;
	[SerializeField, Min(0.1f)] private float m_SelectionLabelHeight = 2.8f;
	[Header("Drive Sink Debug")]
	[SerializeField] private bool m_LogDriveSink = true;
	[SerializeField] private bool m_LogVehicleBounce = true;
	[SerializeField, Min(1f)] private float m_BounceMonitorSeconds = 5f;
	[Header("Temp Debug")]
	[Tooltip("TEMP: select + move without a seated driver (and allow Neutral team).")]
	[SerializeField] private bool m_TempAllowDriverlessControl = true;
	#endregion

	#region Private Fields
	private Transform m_CachedCameraTransform;
	private VehicleTuning m_RuntimeTuning;
	private VehicleUnitBlocker m_UnitBlocker;
	private float m_BounceMonitorLeft;
	private float m_BounceLogCooldown;
	private float m_PrevBounceFixedY;
	private float m_PrevBounceFixedVelY;
	private bool m_HasBounceSample;
	private bool m_BounceMonitorActive;
	private int m_BounceEventIndex;
	private float m_ChassisStatusLogCooldown;
	private string m_LastStatusPhase = string.Empty;
	private VehicleNavigation.DriverFSM.State m_LastStatusState = VehicleNavigation.DriverFSM.State.Idle;
	#endregion

	#region Bounce Diagnostics Constants
	private const float c_BounceYJumpThreshold = 0.18f;
	private const float c_BounceUpVelYThreshold = 1.8f;
	private const float c_BounceFallVelYThreshold = 3.5f;
	private const float c_BounceVelYDeltaThreshold = 3.5f;
	private const float c_BounceAngSpeedThreshold = 45f;
	private const float c_BounceWheelForceThreshold = 45000f;
	private const float c_BounceLogCooldownSeconds = 0.12f;
	private const float c_ChassisStatusLogInterval = 0.5f;
	#endregion

	#region Events
	public event Action SelectionChanged;
	public event Action OccupancyChanged;
	public event Action TeamChanged;
	public event Action EngineStateChanged;
	#endregion

	#region Public Properties
	public bool IsSelected => m_IsSelected;
	public VehicleNavigation.VehicleNavigation Navigation => m_Navigation;
	public VehicleBrain Brain => m_Brain;
	public VehicleSeatLayout Seats => m_Seats;
	public VehicleDoorController Doors => m_Doors;
	public VehicleBoardController Board => m_Board;
	public VehicleGunnerHatch GunnerHatch => m_GunnerHatch;
	public UnitTeam TeamComponent => m_Team;
	public UnitTeamId Team => m_Team != null ? m_Team.Team : UnitTeamId.Neutral;
	/// <summary>Always-dynamic RB — no kinematic wake cycle.</summary>
	public bool IsDriveWakeStabilizing => false;
	/// <summary>Physics is live from Awake; always ready for drive commands.</summary>
	public bool IsDrivePhysicsReady => true;
	/// <summary>Разрешить мотор/руль (Brain + Navigation).</summary>
	public bool IsDriveMotorAllowed => true;
	/// <summary>
	/// Выделение: своя сторона (водитель) или внутри ещё есть юнит игрока
	/// (после высадки водителя сторона Neutral, но пассажиры должны управлять высадкой).
	/// TEMP: m_TempAllowDriverlessControl also allows empty/Neutral vehicles.
	/// </summary>
	public bool IsPlayerSelectable =>
		m_TempAllowDriverlessControl ||
		Team == UnitTeamId.Player ||
		HasOccupantOfTeam(UnitTeamId.Player);
	public bool HasPassengers => m_Seats != null && m_Seats.OccupantCount > 0;
	public bool HasDriver => m_Seats != null && m_Seats.HasDriver;
	public bool IsEngineRunning => m_Brain != null && m_Brain.EngineRunning;
	public bool CanToggleEngine => HasDriver && Team == UnitTeamId.Player;
	public VehicleSpeedMode SpeedCeiling => m_SpeedCeiling;
	public VehicleSpeedMode LastIssuedSpeedMode => m_LastIssuedSpeedMode;
	public VehiclePathLineVisual PathLine => m_PathLine;
	public VehicleUnitBlocker UnitBlocker => m_UnitBlocker != null
		? m_UnitBlocker
		: GetComponentInChildren<VehicleUnitBlocker>(true);
	public bool IsDriveWakeLocked => false;

	public void SetIgnoreUnitColliders(RtsUnitMember _unit, bool _ignore)
	{
		VehicleUnitBlocker blocker = UnitBlocker;
		if (blocker == null || _unit == null)
			return;

		Collider[] cols = _unit.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < cols.Length; i++)
		{
			if (cols[i] == null || cols[i].isTrigger)
				continue;
			blocker.SetIgnoreUnit(cols[i], _ignore);
		}
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureComponents();
		VehicleHierarchyBinder.EnsureBound(this);
		SyncDriveControlFromDriver();
		LogVehicle("always-dynamic drive RB — no kinematic park/wake");
	}

	private void Start()
	{
		// Runtime auto-setup may bind wheels after first EnsurePhysicsDrive — snap once more.
		if (HasBoundWheelAxles())
			SnapChassisAboveGroundIfNeeded(_force: true);
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		if (m_Brain != null)
			m_Brain.EngineStateChanged += OnBrainEngineStateChanged;
	}

	private void OnDisable()
	{
		s_Instances.Remove(this);
		if (m_Brain != null)
			m_Brain.EngineStateChanged -= OnBrainEngineStateChanged;
		if (m_IsSelected)
			SetSelected(false);
	}

	private void LateUpdate()
	{
		UpdateSelectionLabelBillboard();
	}

	private void FixedUpdate()
	{
		SyncChassisDriveHold();
		TickBounceMonitor();
	}

	private void OnDestroy()
	{
		if (m_RuntimeTuning != null)
		{
			Destroy(m_RuntimeTuning);
			m_RuntimeTuning = null;
		}
	}
	#endregion

	#region Public Methods
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
		{
			m_Team = gameObject.AddComponent<UnitTeam>();
			m_Team.SetTeam(UnitTeamId.Neutral);
		}

		EnsurePhysicsDrive();

		m_Board.Configure(this, m_Seats, m_Doors);

		if (m_Wheels == null)
			TryGetComponent(out m_Wheels);
		if (m_Wheels != null)
			m_Wheels.enabled = false;

		// Legacy bicycle motor — disable if present so it cannot fight Rigidbody drive.
		if (TryGetComponent(out VehicleMotor legacyMotor))
			legacyMotor.enabled = false;

		if (m_SelectionCollider == null)
		{
			TryGetComponent(out m_SelectionCollider);
			if (m_SelectionCollider == null)
				m_SelectionCollider = GetComponentInChildren<Collider>();
		}

		if (!TryGetComponent(out NavMeshAgent agent))
			agent = gameObject.AddComponent<NavMeshAgent>();
		agent.radius = 1.4f;
		agent.height = 1.8f;
		agent.baseOffset = 0f;
		agent.updatePosition = false;
		agent.updateRotation = false;

		EnsureSelectionCollider();
	}

	public void EnsurePhysicsDrive()
	{
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
		// Residual visual tilt made the hull lean while wheels stayed with physics → fake "float" gaps on slopes.
		// Real lean comes from WheelCollider compression differences on the Rigidbody.
		m_BodyTilt.enabled = false;
		m_BodyTilt.BindMotor(m_WheeledMotor);

		if (m_PathLine == null && !TryGetComponent(out m_PathLine))
			m_PathLine = gameObject.AddComponent<VehiclePathLineVisual>();
		m_PathLine.Configure(this, m_Navigation);

		if (!TryGetComponent(out VehicleDriverDebugOverlay _))
			gameObject.AddComponent<VehicleDriverDebugOverlay>();

		if (!TryGetComponent(out VehicleNavigationDebugDrawer _))
			gameObject.AddComponent<VehicleNavigationDebugDrawer>();

		if (!TryGetComponent(out VehicleNavigation.ReverseDebugger _))
			gameObject.AddComponent<VehicleNavigation.ReverseDebugger>();

		VehicleTuning tuning = m_Tuning;
		if (tuning == null)
			tuning = Resources.Load<VehicleTuning>("VehicleTunings/Tuning_LightUtility_Humvee");
		if (tuning == null)
		{
			if (m_RuntimeTuning == null)
				m_RuntimeTuning = VehicleTuning.CreateRuntimeLightUtilityHumvee();
			tuning = m_RuntimeTuning;
		}
		else
		{
			m_Tuning = tuning;
		}

		m_Brain.AutoWire();
		m_Brain.SetTuning(tuning);
		m_Navigation.RebuildLimiters();
		body.mass = tuning.RigidbodyMass;
		Vector3 com = tuning.CenterOfMass;
		// Prevent an underground/low COM (old tuning had y=-0.45) which turns the car
		// into a pendulum and causes violent rocking/jumping on wake. For a wheeled
		// vehicle the COM must sit above the wheel hubs, not below them.
		if (com.y < 0.3f)
		{
			LogVehicle($"COM clamped from y={com.y:F2} to 0.55 (was below wheel hub)");
			com.y = 0.55f;
		}
		body.centerOfMass = com;
		body.isKinematic = false;
		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;
		body.interpolation = RigidbodyInterpolation.Interpolate;
		// ContinuousSpeculative can drop WheelCollider contacts on Unity 6; match LPVC continuous.
		body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

		// NavMeshAgent internally computes velocity every frame and leaks micro-impulses
		// onto the dynamic Rigidbody, even with updatePosition=false. Fully disable it
		// when running physics drive so the car receives zero agent drift.
		if (TryGetComponent(out NavMeshAgent driveAgent))
			driveAgent.enabled = false;

		// Units must be blocked by the hull, but must not shove the drive body.
		EnsureUnitBlockingSetup(body);
		if (HasBoundWheelAxles())
			SnapChassisAboveGroundIfNeeded(_force: true);

		LogVehicle(
			$"EnsurePhysicsDrive dynamic mass={body.mass:F0} com={body.centerOfMass} " +
			$"y={transform.position.y:F3}");
	}

	/// <summary>
	/// Опустить/поднять корпус так, чтобы WheelCollider'ы стояли на земле.
	/// Вызывать после BindPhysicsWheels (VehicleHierarchyBinder).
	/// </summary>
	public void SnapChassisToGround(bool _force = true)
	{
		SnapChassisAboveGroundIfNeeded(_force);
	}

	private bool HasBoundWheelAxles()
	{
		return m_WheeledMotor != null &&
		       m_WheeledMotor.Axles != null &&
		       m_WheeledMotor.Axles.Length > 0;
	}

	private int GetBoundWheelCount()
	{
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return 0;

		int count = 0;
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			if (m_WheeledMotor.Axles[i]?.Collider != null)
				count++;
		}

		return count;
	}

	private void EnsureUnitBlockingSetup(Rigidbody _driveBody)
	{
		int unitLayer = LayerMask.NameToLayer("Unit");
		int vehicleLayer = LayerMask.NameToLayer("Vehicle");
		if (unitLayer >= 0 && vehicleLayer >= 0)
		{
			// Hull blocker must collide with Unit — do not ignore the layer pair.
			Physics.IgnoreLayerCollision(unitLayer, vehicleLayer, false);
		}

		EnsureSelectionCollider();

		BoxCollider selectionBox = m_SelectionCollider as BoxCollider;
		if (selectionBox != null)
		{
			// Selection volume is a trigger: clickable, no physics shove into drive RB.
			selectionBox.isTrigger = true;
		}

		m_UnitBlocker = VehicleUnitBlocker.Ensure(this, selectionBox);
		// Cache the result immediately so geometry probes can ignore the blocker.
		if (m_UnitBlocker == null)
			m_UnitBlocker = VehicleUnitBlocker.Ensure(this, selectionBox);
		DestroyGroundContactIfPresent();
		EnsureHullCollidersForGroundSupport();
		EnsureChassisGroundSupportBox();

		// Do NOT set Rigidbody.excludeLayers — in Unity 6 that can kill WheelCollider
		// ground hits for the whole body. Infantry shove is handled by UnitBlocker +
		// collider-level exclude on non-wheel hull + VehicleDriveUnitPushIgnore.
		LayerMask infantryMask = BuildInfantryPushExcludeMask();
		if (_driveBody != null)
			_driveBody.excludeLayers = 0;

		Collider[] cols = GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < cols.Length; i++)
		{
			Collider col = cols[i];
			if (col == null)
				continue;
			if (col.GetComponentInParent<VehicleUnitBlocker>() != null)
				continue;
			if (col is WheelCollider)
				continue;
			col.excludeLayers |= infantryMask;
		}

		if (!TryGetComponent(out VehicleDriveUnitPushIgnore pushIgnore))
			pushIgnore = gameObject.AddComponent<VehicleDriveUnitPushIgnore>();
		pushIgnore.Configure(this);

		BounceWheelCollidersAfterBind("unit-blocking-setup");
		if (m_WheeledMotor != null)
			m_WheeledMotor.ResetSprungMassesSafe();
	}

	/// <summary>
	/// Previous safety box sat 0.5 m above root; when it rested on ground the chassis
	/// was forced ~0.5 m underground and wheels vanished into the terrain.
	/// </summary>
	private void DestroyGroundContactIfPresent()
	{
		Transform existing = transform.Find("GroundContact");
		if (existing != null)
			Destroy(existing.gameObject);
	}

	/// <summary>
	/// Like LPVC 000: keep solid hull colliders for ground support.
	/// Selection stays trigger; UnitBlocker (separate RB) still blocks infantry.
	/// </summary>
	private void EnsureHullCollidersForGroundSupport()
	{
		Collider[] cols = GetComponentsInChildren<Collider>(true);
		int restored = 0;
		for (int i = 0; i < cols.Length; i++)
		{
			Collider col = cols[i];
			if (col == null)
				continue;
			if (col is WheelCollider)
				continue;
			if (col.GetComponentInParent<VehicleUnitBlocker>() != null)
				continue;

			if (col is BoxCollider box && ReferenceEquals(box, m_SelectionCollider))
			{
				if (!box.isTrigger)
					LogVehicle($"selection BoxCollider '{col.name}' → trigger");
				box.isTrigger = true;
				continue;
			}

			if (!col.enabled)
			{
				col.enabled = true;
				restored++;
			}
		}

		if (restored > 0)
			LogVehicle($"restore hull colliders x{restored} for ground support (LPVC-like)");
	}

	/// <summary>
	/// High freefall catcher only — must sit above tire contact so slopes/bumps are
	/// handled by WheelColliders (LPVC-like lean). Bottom ~0.42 local.
	/// </summary>
	private void EnsureChassisGroundSupportBox()
	{
		const string c_Name = "ChassisGroundSupport";
		Transform existing = transform.Find(c_Name);
		GameObject go = existing != null ? existing.gameObject : new GameObject(c_Name);
		if (existing == null)
			go.transform.SetParent(transform, false);

		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.layer = gameObject.layer;

		if (!go.TryGetComponent(out BoxCollider box))
			box = go.AddComponent<BoxCollider>();

		box.isTrigger = false;
		// Unity 6000.4: WC often report grounded=0 for several frames; without a catcher
		// the chassis freefalls and Soft/hull scrapes hit ~2800°/s. Keep raised freefall box.
		// Raised higher and made smaller so it only catches freefall, not normal driving
		// over small bumps — this removes the up/down jitter while keeping the safety net.
		box.enabled = true;
		box.center = new Vector3(0f, 1.15f, 0.1f);
		box.size = new Vector3(1.2f, 0.5f, 2.4f);

		LayerMask infantryMask = BuildInfantryPushExcludeMask();
		box.excludeLayers |= infantryMask;
		LogVehicle(
			$"ChassisGroundSupport ON (Unity6 freefall catch) bottom={box.center.y - box.size.y * 0.5f:F2} " +
			$"size=({box.size.x:F1},{box.size.y:F1},{box.size.z:F1})");
	}

	/// <summary>
	/// Unity 6: after IgnoreCollision / Rigidbody writes, bounce WheelColliders once
	/// so they regain ground contact. Call only after bind — not every frame.
	/// </summary>
	public void BounceWheelCollidersAfterBind(string _reason)
	{
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return;

		int bounced = 0;
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelCollider col = m_WheeledMotor.Axles[i]?.Collider;
			if (col == null)
				continue;
			col.enabled = false;
			col.enabled = true;
			bounced++;
		}

		if (bounced > 0)
			LogVehicle($"bounce WheelColliders x{bounced} after {_reason}");
	}

	/// <summary>
	/// Legacy no-op — wheel-only strip removed; hull colliders stay enabled like LPVC 000.
	/// </summary>
	private void EnsureDriveCollidersWheelOnly()
	{
		EnsureHullCollidersForGroundSupport();
	}

	/// <summary>
	/// Minimum root Y so every wheel hub can rest on ground (per-wheel raycast).
	/// </summary>
	private bool TryComputeWheelRestRootY(out float _rootY, bool _logDetail = false)
	{
		_rootY = transform.position.y;
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
		{
			if (_logDetail)
				LogVehicle("TryComputeWheelRestRootY FAIL — no axles");
			return false;
		}

		int mask = LayerMask.GetMask("Ground", "Default", "Obstacle");
		if (mask == 0)
			mask = ~0;

		const float c_Clearance = 0.0f;
		bool any = false;
		float maxRootY = float.NegativeInfinity;
		System.Text.StringBuilder detail = _logDetail && m_LogDriveSink ? new System.Text.StringBuilder(256) : null;

		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelAxle axle = m_WheeledMotor.Axles[i];
			if (axle == null || axle.Collider == null)
				continue;

			WheelCollider wc = axle.Collider;
			Vector3 hubWorld = wc.transform.TransformPoint(wc.center);
			Vector3 rayOrigin = hubWorld + Vector3.up * 2.5f;
			if (!TryRaycastGround(rayOrigin, 12f, mask, out RaycastHit hit))
			{
				detail?.Append($" [{wc.name}:NO_HIT hubY={hubWorld.y:F2}]");
				continue;
			}

		float hubLocalY = transform.InverseTransformPoint(hubWorld).y;
		// Empirically the WheelCollider behaves as if its rest drop below the hub is
		// suspensionDistance * targetPosition. Place the hull so the wheel at rest
		// just touches the ground; under the vehicle's weight the body will settle
		// a few centimetres lower.
		float targetPos = Mathf.Clamp01(wc.suspensionSpring.targetPosition);
		float dropBelowHub = wc.suspensionDistance * targetPos;
		const float c_Preload = 0.0f;
		float neededRootY = hit.point.y - hubLocalY + dropBelowHub + wc.radius - c_Preload;
			maxRootY = Mathf.Max(maxRootY, neededRootY);
			any = true;
			detail?.Append(
				$" [{wc.name}: groundY={hit.point.y:F2} hit={hit.collider.name} L={hit.collider.gameObject.layer}" +
				$" hubLocalY={hubLocalY:F2} r={wc.radius:F2} susp={wc.suspensionDistance:F2}" +
				$" tgt={targetPos:F2} drop={dropBelowHub:F2} spring={wc.suspensionSpring.spring:F0} needRootY={neededRootY:F2}]");
		}

		if (!any)
		{
			if (_logDetail)
				LogVehicle($"TryComputeWheelRestRootY FAIL — no ground under wheels{detail}");
			return false;
		}

		_rootY = maxRootY + c_Clearance;
		if (_logDetail)
			LogVehicle($"TryComputeWheelRestRootY OK rootY={_rootY:F3} curY={transform.position.y:F3}{detail}");
		return true;
	}

	private int CountGroundedWheels()
	{
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return 0;

		int grounded = 0;
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelAxle axle = m_WheeledMotor.Axles[i];
			if (axle == null || axle.Collider == null)
				continue;
			if (axle.Collider.GetGroundHit(out _))
				grounded++;
		}

		return grounded;
	}

	/// <summary>
	/// Place the chassis so the WheelColliders start at their no-load target position
	/// just touching the ground. Gravity will then compress the springs down to the
	/// real static equilibrium, avoiding the upward launch caused by pre-compression.
	/// </summary>
	private void SnapChassisAboveGroundIfNeeded(bool _force = false)
	{
		if (TryComputeWheelRestRootY(out float rootY))
		{
			if (!_force && transform.position.y >= rootY - 0.02f && transform.position.y <= rootY + 0.45f)
				return;
			if (_force && Mathf.Abs(transform.position.y - rootY) <= 0.01f)
				return;

			Vector3 snapPos = transform.position;
			snapPos.y = rootY;
			transform.position = snapPos;
			Physics.SyncTransforms();
			return;
		}

		Vector3 origin = transform.position + Vector3.up * 3f;
		int mask = LayerMask.GetMask("Ground", "Default", "Obstacle");
		if (mask == 0)
			mask = ~0;

		if (!TryRaycastGround(origin, 8f, mask, out RaycastHit hit))
			return;

		float avgHubLocalY = 0.45f;
		float avgRadius = 0.45f;
		float avgSuspension = 0.18f;
		float avgTargetPos = 1f;
		int wheelCount = 0;

		if (m_WheeledMotor != null && m_WheeledMotor.Axles != null)
		{
			float sumHub = 0f;
			float sumRadius = 0f;
			float sumSusp = 0f;
			float sumTarget = 0f;
			for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
			{
				WheelAxle axle = m_WheeledMotor.Axles[i];
				if (axle == null || axle.Collider == null)
					continue;
				WheelCollider col = axle.Collider;
				sumHub += col.transform.localPosition.y + col.center.y;
				sumRadius += col.radius;
				sumSusp += col.suspensionDistance;
				sumTarget += col.suspensionSpring.targetPosition;
				wheelCount++;
			}

			if (wheelCount > 0)
			{
				avgHubLocalY = sumHub / wheelCount;
				avgRadius = sumRadius / wheelCount;
				avgSuspension = sumSusp / wheelCount;
				avgTargetPos = sumTarget / wheelCount;
			}
		}

		// Place the hull at the empirical rest drop (suspensionDistance * targetPosition).
		float restDropBelowHub = avgSuspension * avgTargetPos;
		const float c_Preload = 0.0f;
		float targetRootY = hit.point.y - avgHubLocalY + restDropBelowHub + avgRadius - c_Preload;

		if (!_force)
		{
			if (transform.position.y >= targetRootY - 0.02f && transform.position.y <= targetRootY + 0.45f)
				return;
		}
		else if (transform.position.y >= targetRootY - 0.01f)
			return;

		Vector3 fallbackPos = transform.position;
		fallbackPos.y = targetRootY;
		transform.position = fallbackPos;
		Physics.SyncTransforms();
	}

	private bool TryRaycastGround(
		Vector3 _origin,
		float _maxDistance,
		int _mask,
		out RaycastHit _hit)
	{
		_hit = default;
		RaycastHit[] hits = Physics.RaycastAll(
			_origin,
			Vector3.down,
			_maxDistance,
			_mask,
			QueryTriggerInteraction.Ignore);
		if (hits == null || hits.Length == 0)
			return false;

		float bestDist = float.MaxValue;
		bool found = false;
		for (int i = 0; i < hits.Length; i++)
		{
			Collider col = hits[i].collider;
			if (col == null || IsIgnoredGroundProbeCollider(col))
				continue;

			if (hits[i].distance < bestDist)
			{
				bestDist = hits[i].distance;
				_hit = hits[i];
				found = true;
			}
		}

		return found;
	}

	private bool IsIgnoredGroundProbeCollider(Collider _collider)
	{
		if (_collider == null)
			return true;
		if (_collider.transform.IsChildOf(transform))
			return true;
		if (m_UnitBlocker != null && _collider == m_UnitBlocker.BlockCollider)
			return true;
		return false;
	}

	private void ForceParkDriveCommand()
	{
		// Soft/Hard brake on air wheels spins the hull (~2800°/s). Zero torques until grounded.
		if (CountGroundedWheels() <= 0)
		{
			m_WheeledMotor?.ZeroWheelTorques();
			StabilizeAirborneChassis();
			if (m_Brain != null && m_Brain.ControlActive)
				m_Brain.SetCommand(VehicleCommand.Idle);
			return;
		}

		m_WheeledMotor?.ParkWheels();
		if (m_Brain == null || !m_Brain.ControlActive)
			return;

		m_Brain.SetCommand(VehicleCommand.SoftPark);
	}

	/// <summary>
	/// Unity 6: while WC are waking (grounded=0), kill tumble so Soft/hull scrapes
	/// cannot accumulate 2000+ °/s before ChassisGroundSupport / WC catch.
	/// </summary>
	private void StabilizeAirborneChassis()
	{
		if (!TryGetComponent(out Rigidbody body) || body.isKinematic)
			return;

		body.angularVelocity = Vector3.zero;
		Vector3 v = body.linearVelocity;
		if (v.y < -1.5f)
			v.y = Mathf.Max(v.y, -1.5f);
		v.x *= 0.85f;
		v.z *= 0.85f;
		body.linearVelocity = v;
	}

	private void BeginBounceMonitor(string _reason)
	{
		if (!m_LogVehicleBounce)
			return;

		m_BounceMonitorActive = true;
		m_BounceMonitorLeft = m_BounceMonitorSeconds;
		m_BounceLogCooldown = 0f;
		m_HasBounceSample = false;
		m_BounceEventIndex = 0;
		LogBounce($"MONITOR start reason={_reason} duration={m_BounceMonitorSeconds:F1}s");
	}

	private void TickBounceMonitor()
	{
		if (!m_LogVehicleBounce || !m_BounceMonitorActive)
			return;
		if (!TryGetComponent(out Rigidbody body) || body.isKinematic)
			return;

		m_BounceMonitorLeft -= Time.fixedDeltaTime;
		m_BounceLogCooldown -= Time.fixedDeltaTime;

		float y = transform.position.y;
		float velY = body.linearVelocity.y;
		float angSpeedDeg = body.angularVelocity.magnitude * Mathf.Rad2Deg;
		float velXZ = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude;

		string reason = null;
		if (m_HasBounceSample)
		{
			float dy = y - m_PrevBounceFixedY;
			float dVelY = velY - m_PrevBounceFixedVelY;

			if (dy >= c_BounceYJumpThreshold && velY > 0.4f)
				reason = $"Y_JUMP +{dy:F2}m";
			else if (velY >= c_BounceUpVelYThreshold)
				reason = $"UP velY={velY:F2}";
			else if (velY <= -c_BounceFallVelYThreshold)
				reason = $"FALL velY={velY:F2}";
			else if (Mathf.Abs(dVelY) >= c_BounceVelYDeltaThreshold)
				reason = $"VELY_SPIKE d={dVelY:F2}";
			else if (angSpeedDeg >= c_BounceAngSpeedThreshold)
				reason = $"ROLL ang={angSpeedDeg:F0}°/s";
		}

		if (reason == null && TryGetMaxWheelContactForce(out float maxForce) &&
		    maxForce >= c_BounceWheelForceThreshold)
		{
			reason = $"SUSP_F max={maxForce:F0}";
		}

		m_PrevBounceFixedY = y;
		m_PrevBounceFixedVelY = velY;
		m_HasBounceSample = true;

		if (reason != null && m_BounceLogCooldown <= 0f)
		{
			LogBounceEvent(reason, body, velXZ, angSpeedDeg);
			m_BounceLogCooldown = c_BounceLogCooldownSeconds;
			if (m_BounceMonitorLeft < 1.5f)
				m_BounceMonitorLeft = 1.5f;
		}

		if (m_BounceMonitorLeft <= 0f)
		{
			LogBounce($"MONITOR end events={m_BounceEventIndex}");
			m_BounceMonitorActive = false;
		}
	}

	private void LogBounceEvent(string _reason, Rigidbody _body, float _velXZ, float _angSpeedDeg)
	{
		m_BounceEventIndex++;
		int grounded = CountGroundedWheels();
		int total = GetBoundWheelCount();
		LogBounce(
			$"#{m_BounceEventIndex} {_reason} y={transform.position.y:F3} " +
			$"velY={_body.linearVelocity.y:F2} velXZ={_velXZ:F2} ang={_angSpeedDeg:F0}°/s " +
			$"grounded={grounded}/{total} wake={IsDriveWakeStabilizing} " +
			$"{BuildDriveContextSummary()} wheels=[{BuildWheelContactSummary()}]");
	}

	private void LogBounce(string _message)
	{
		if (!m_LogVehicleBounce)
			return;
		Debug.Log($"[VehicleBounce:{name}] {_message}", this);
	}

	private string BuildDriveContextSummary()
	{
		if (m_Navigation == null)
			return "drive=?";

		VehicleBrakeMode brake = m_Brain != null ? m_Brain.CurrentCommand.BrakeMode : VehicleBrakeMode.None;
		string maneuver = m_Navigation.CurrentManeuver != null
			? m_Navigation.CurrentManeuver.Type.ToString()
			: "-";
		return
			$"state={m_Navigation.DriverState} man={maneuver} plan={m_Navigation.ActivePlanReason} " +
			$"thr={m_Navigation.ThrottleCommand:F2} steer={m_Navigation.SteerCommand:F2} brake={brake} " +
			$"ready={IsDriveMotorAllowed} speed={m_Brain?.CurrentSpeedKmh:F1}km/h";
	}

	private string BuildWheelContactSummary()
	{
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return "no-axles";

		var parts = new System.Text.StringBuilder(128);
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelAxle axle = m_WheeledMotor.Axles[i];
			if (axle?.Collider == null)
				continue;

			if (parts.Length > 0)
				parts.Append(' ');

			string label = ShortWheelLabel(axle.Collider.name);
			if (axle.Collider.GetGroundHit(out WheelHit hit))
			{
				parts.Append(
					$"{label}:F={hit.force:F0} y={hit.point.y:F2} " +
					$"n=({hit.normal.y:F2})");
			}
			else
			{
				parts.Append($"{label}:air");
			}
		}

		return parts.Length > 0 ? parts.ToString() : "none";
	}

	private static string ShortWheelLabel(string _wheelName)
	{
		if (string.IsNullOrEmpty(_wheelName))
			return "?";

		if (_wheelName.IndexOf("_FL", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    _wheelName.IndexOf("FrontLeft", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "FL";
		if (_wheelName.IndexOf("_FR", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    _wheelName.IndexOf("FrontRight", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "FR";
		if (_wheelName.IndexOf("_RL", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    _wheelName.IndexOf("RearLeft", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "RL";
		if (_wheelName.IndexOf("_RR", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    _wheelName.IndexOf("RearRight", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "RR";

		return _wheelName.Length <= 4 ? _wheelName : _wheelName.Substring(0, 4);
	}

	private bool TryGetMaxWheelContactForce(out float _maxForce)
	{
		_maxForce = 0f;
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return false;

		bool any = false;
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelCollider col = m_WheeledMotor.Axles[i]?.Collider;
			if (col == null || !col.GetGroundHit(out WheelHit hit))
				continue;

			any = true;
			if (hit.force > _maxForce)
				_maxForce = hit.force;
		}

		return any;
	}

	private void LogVehicle(string _message)
	{
		if (!m_LogDriveSink)
			return;
		Debug.Log($"[VehicleNav] {name} {_message}", this);
	}

	private void LogSinkIfYDropped(string _phase, float _previousY)
	{
		if (!m_LogDriveSink || IsDriveWakeStabilizing)
			return;

		float y = transform.position.y;
		if (y >= _previousY - 0.05f)
			return;

		LogVehicle($"DROP {_phase} y {_previousY:F3} → {y:F3} (delta={y - _previousY:F3})");
	}

	/// <summary>
	/// Layers that may carry infantry solid colliders. Side/team is NOT a layer —
	/// Player/Enemy/Neutral all use Unit (+ optional Target proxies).
	/// </summary>
	private static LayerMask BuildInfantryPushExcludeMask()
	{
		LayerMask mask = 0;
		int unit = LayerMask.NameToLayer("Unit");
		if (unit >= 0)
			mask |= 1 << unit;
		int target = LayerMask.NameToLayer("Target");
		if (target >= 0)
			mask |= 1 << target;
		return mask;
	}

	public void EnsureSelectionCollider()
	{
		int vehicleLayer = LayerMask.NameToLayer("Vehicle");
		if (vehicleLayer >= 0)
			SetLayerRecursively(transform, vehicleLayer);

		BoxCollider box = null;
		if (m_SelectionCollider is BoxCollider existingBox)
			box = existingBox;
		else if (!TryGetComponent(out box))
			box = gameObject.AddComponent<BoxCollider>();

		// Prefab may ship with a solid hull box — keep selection as trigger only.
		box.isTrigger = true;
		box.enabled = true;
		// Only seed defaults when collider looks uninitialized; keep hand-tuned sizes.
		if (box.size.sqrMagnitude < 0.01f)
		{
			box.center = new Vector3(0f, 1.35f, 0.1f);
			box.size = new Vector3(2.6f, 1.9f, 4.8f);
		}
		else
		{
			// Lift bottom above ground if it would fight WheelColliders.
			float bottom = box.center.y - box.size.y * 0.5f;
			if (bottom < 0.55f)
			{
				float lift = 0.6f - bottom;
				box.center = new Vector3(box.center.x, box.center.y + lift * 0.5f, box.center.z);
				box.size = new Vector3(box.size.x, Mathf.Max(0.5f, box.size.y - lift), box.size.z);
			}
		}

		m_SelectionCollider = box;
	}

	private static void SetLayerRecursively(Transform _root, int _layer)
	{
		if (_root == null)
			return;
		_root.gameObject.layer = _layer;
		for (int i = 0; i < _root.childCount; i++)
			SetLayerRecursively(_root.GetChild(i), _layer);
	}

	public void SetSelected(bool _selected)
	{
		if (_selected && !IsPlayerSelectable)
			_selected = false;
		if (m_IsSelected == _selected)
			return;

		m_IsSelected = _selected;
		if (m_IsSelected)
		{
			EnsureSelectionNameLabel();
			RefreshSelectionNameLabel();
		}

		if (m_SelectionNameLabelRoot != null)
			m_SelectionNameLabelRoot.SetActive(m_IsSelected);

		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Сторона машины меняется только при посадке/высадке водителя.
	/// </summary>
	public void SyncOwnershipFromDriverSeat()
	{
		if (m_Seats != null &&
		    m_Seats.TryGetOccupant(VehicleSeatId.Driver, out RtsUnitMember driver) &&
		    driver != null)
		{
			ApplyTeam(ResolveUnitTeam(driver));
			SyncDriveControlFromDriver();
			return;
		}

		ApplyTeam(UnitTeamId.Neutral);
		SyncDriveControlFromDriver();
	}

	/// <summary>
	/// Пустая машина — можно всем. Если внутри есть юнит другой стороны — садиться нельзя
	/// (в т.ч. нейтралам и противоположной стороне).
	/// </summary>
	public bool CanAcceptBoarder(RtsUnitMember _unit)
	{
		return CanAcceptBoarderTeam(ResolveUnitTeam(_unit));
	}

	public bool CanAcceptBoarderTeam(UnitTeamId _boarderTeam)
	{
		if (m_Seats == null || m_Seats.OccupantCount == 0)
			return true;

		var ordered = new List<(VehicleSeatId Seat, RtsUnitMember Unit)>(8);
		m_Seats.CollectOccupantsOrdered(ordered);
		for (int i = 0; i < ordered.Count; i++)
		{
			RtsUnitMember occupant = ordered[i].Unit;
			if (occupant == null)
				continue;
			if (ResolveUnitTeam(occupant) != _boarderTeam)
				return false;
		}

		return true;
	}

	public bool CanAcceptAnyBoarder(IReadOnlyList<RtsUnitMember> _units)
	{
		if (_units == null || _units.Count == 0)
			return false;
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] != null && CanAcceptBoarder(_units[i]))
				return true;
		}

		return false;
	}

	public bool HasOccupantOfTeam(UnitTeamId _team)
	{
		if (m_Seats == null || m_Seats.OccupantCount == 0)
			return false;

		var ordered = new List<(VehicleSeatId Seat, RtsUnitMember Unit)>(8);
		m_Seats.CollectOccupantsOrdered(ordered);
		for (int i = 0; i < ordered.Count; i++)
		{
			if (ordered[i].Unit != null && ResolveUnitTeam(ordered[i].Unit) == _team)
				return true;
		}

		return false;
	}

	public static UnitTeamId ResolveUnitTeam(RtsUnitMember _unit)
	{
		if (_unit != null && _unit.TryGetComponent(out UnitTeam unitTeam))
			return unitTeam.Team;
		return UnitTeamId.Neutral;
	}

	private void ApplyTeam(UnitTeamId _team)
	{
		if (m_Team == null && !TryGetComponent(out m_Team))
			m_Team = gameObject.AddComponent<UnitTeam>();

		UnitTeamId previous = m_Team.Team;
		m_Team.SetTeam(_team);
		if (previous == _team)
			return;

		if (!IsPlayerSelectable && m_IsSelected)
			SetSelected(false);

		TeamChanged?.Invoke();
		RtsUnitSelectionManager.Instance?.NotifyVehicleTeamChanged(this);
	}

	public void IssueMoveOrder(Vector3 _worldPosition)
	{
		IssueMoveOrder(VehicleMoveGoal.FromPosition(_worldPosition, VehicleSpeedMode.Medium));
	}

	public void IssueMoveOrder(Vector3 _worldPosition, VehicleSpeedMode _speedMode)
	{
		IssueMoveOrder(VehicleMoveGoal.FromPosition(_worldPosition, _speedMode));
	}

	public void IssueMoveOrder(VehicleMoveGoal _goal)
	{
		if (!m_TempAllowDriverlessControl && Team != UnitTeamId.Player)
			return;
		if (!m_TempAllowDriverlessControl && (m_Seats == null || !m_Seats.HasDriver))
			return;

		// Drive order aborts boarding queues and seals doors; RB stays dynamic.
		m_Board?.CancelAllJobsAndCloseDoors("vehicle-move-order");

		EnsureDriveReadyForMove();
		if (m_Brain == null || !m_Brain.ControlActive || !m_Brain.EngineRunning)
			return;

		VehicleSpeedMode capped = VehicleSpeedModeUtil.Cap(_goal.SpeedMode, m_SpeedCeiling);
		m_LastIssuedSpeedMode = capped;
		_goal.SpeedMode = capped;
		if (m_LogDriveSink)
			Debug.Log($"[VehicleNav:{name}] IssueMoveOrder to {_goal.Position} speed={_goal.SpeedMode} nav={(m_Navigation != null ? "yes" : "NO")}", this);
		m_Navigation?.SetDestination(_goal);
		m_PathLine?.ClearPreview();
		m_PathLine?.RefreshCommitted();
	}

	public VehicleSpeedMode CycleSpeedCeiling()
	{
		m_SpeedCeiling = VehicleSpeedModeUtil.Next(m_SpeedCeiling);
		return m_SpeedCeiling;
	}

	public void SetMovePreview(Vector3 _worldPoint, VehicleSpeedMode _mode)
	{
		SetMovePreview(_worldPoint, _mode, null);
	}

	public void SetMovePreview(Vector3 _worldPoint, VehicleSpeedMode _mode, float? _headingYawDegrees)
	{
		m_PathLine?.SetPreviewDestination(_worldPoint, _mode, _headingYawDegrees);
	}

	public void ClearMovePreview()
	{
		m_PathLine?.ClearPreview();
	}

	public void HardStop()
	{
		m_Navigation?.StopHard();
		m_PathLine?.ClearPreview();
		m_PathLine?.RefreshCommitted();
	}

	public bool StartEngine()
	{
		if (!CanToggleEngine)
			return false;
		SyncDriveControlFromDriver();
		bool started = m_Brain != null && m_Brain.StartEngine();
		EngineStateChanged?.Invoke();
		return started;
	}

	public void StopEngine()
	{
		if (m_Brain == null)
			return;
		m_Navigation?.StopSoft();
		m_Brain.StopEngine();
		EngineStateChanged?.Invoke();
	}

	public bool ToggleEngine()
	{
		if (!CanToggleEngine)
			return false;
		if (IsEngineRunning)
		{
			StopEngine();
			return false;
		}

		return StartEngine();
	}

	public void BoardUnits(IReadOnlyList<RtsUnitMember> _units, VehicleBoardSide _side)
	{
		SyncChassisDriveHold();
		m_Board?.EnqueueBoard(_units, _side);
	}

	public void BoardUnitsAsGunner(IReadOnlyList<RtsUnitMember> _units, VehicleBoardSide _side)
	{
		SyncChassisDriveHold();
		m_Board?.EnqueueBoardGunner(_units, _side);
	}

	public void LoadWoundedFromCarrier(RtsUnitMember _carrier)
	{
		SyncChassisDriveHold();
		m_Board?.EnqueueLoadWoundedFromCarrier(_carrier);
	}

	public void DisembarkAllExceptDriver()
	{
		m_Board?.EnqueueDisembarkAll(_includeDriver: false);
	}

	public void DisembarkAll()
	{
		m_Board?.EnqueueDisembarkAll(_includeDriver: true);
	}

	public void DisembarkUnit(RtsUnitMember _unit)
	{
		m_Board?.EnqueueDisembarkUnit(_unit);
	}

	public void ToggleGunner()
	{
		ToggleGunnerTurret();
	}

	/// <summary>
	/// Занять турель (обратный порядок посадки) или слезть в свободный слот салона.
	/// </summary>
	public bool ToggleGunnerTurret()
	{
		if (m_Seats == null)
			return false;

		if (m_Seats.HasGunner)
			return TryDemoteGunner();

		return TryPromoteToGunner();
	}

	public bool CanToggleGunnerTurret()
	{
		if (m_Seats == null)
			return false;
		return m_Seats.HasGunner ? m_Seats.CanDemoteGunner() : m_Seats.CanPromoteToGunner();
	}

	public bool IsGunnerOnTurret => m_Seats != null && m_Seats.HasGunner;

	public bool TryPromoteToGunner()
	{
		if (m_Seats == null || !m_Seats.TryFindGunnerPromoteCandidate(out RtsUnitMember unit))
			return false;
		if (!m_Seats.TryGetSeat(VehicleSeatId.Gunner, out VehicleSeatLayout.SeatBinding gunnerSeat) ||
		    gunnerSeat.Anchor == null)
			return false;

		UnitVehicleMountState mount = UnitVehicleMountState.GetOrAdd(unit);
		m_Seats.Occupy(VehicleSeatId.Gunner, unit);
		mount.TransferToSeat(VehicleSeatId.Gunner, gunnerSeat.Anchor, _isLitter: false);
		m_GunnerHatch?.SetGunnerRaised(true);
		NotifyOccupancyChanged();
		return true;
	}

	public bool TryDemoteGunner()
	{
		if (m_Seats == null ||
		    !m_Seats.TryGetOccupant(VehicleSeatId.Gunner, out RtsUnitMember gunner) ||
		    gunner == null)
			return false;
		if (!m_Seats.TryFindGunnerDemoteSeat(out VehicleSeatId cabinSeat))
			return false;
		if (!m_Seats.TryGetSeat(cabinSeat, out VehicleSeatLayout.SeatBinding seat) || seat.Anchor == null)
			return false;

		UnitVehicleMountState mount = UnitVehicleMountState.GetOrAdd(gunner);
		m_Seats.Occupy(cabinSeat, gunner);
		mount.TransferToSeat(cabinSeat, seat.Anchor, seat.IsLitter);
		m_GunnerHatch?.SetGunnerRaised(false);
		if (cabinSeat == VehicleSeatId.Driver)
			SyncOwnershipFromDriverSeat();
		NotifyOccupancyChanged();
		return true;
	}

	public void NotifyOccupancyChanged()
	{
		SyncDriveControlFromDriver();
		OccupancyChanged?.Invoke();
		if (m_IsSelected)
			RtsUnitSelectionManager.Instance?.NotifySelectionUiRefresh();
	}

	public static VehicleController FindUnderCollider(Collider _collider)
	{
		return _collider != null ? _collider.GetComponentInParent<VehicleController>() : null;
	}
	#endregion

	#region Drive / Engine
	/// <summary>
	/// Always-dynamic chassis: never toggle isKinematic. Hold = zero velocity + soft park brake.
	/// </summary>
	private void SyncChassisDriveHold()
	{
		if (!TryGetComponent(out Rigidbody body))
			return;

		float yBefore = transform.position.y;

		if (body.isKinematic)
		{
			body.isKinematic = false;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			LogVehicle($"force dynamic (was kinematic y={yBefore:F3})");
		}

		bool boardHold = m_Board != null && m_Board.ShouldKeepChassisParked;
		bool controlOn = m_Brain != null && m_Brain.ControlActive;
		bool needsSim = m_Navigation != null && m_Navigation.NeedsDriveSimulation;
		bool hasPath = m_Navigation != null && m_Navigation.HasDestination;
		// Release while control is on and a path/manoeuvre exists (do not require
		// CanDrive — engine ready delay would otherwise freeze the chassis every FixedUpdate).
		bool releaseHold = !boardHold && controlOn && (needsSim || hasPath);
		string phase = releaseHold
			? "drive"
			: boardHold
				? "board-hold"
				: "idle-hold";

		if (releaseHold)
		{
			// Even while driving, if all wheels are airborne the vehicle is free-falling.
			// Without this it would keep full throttle into the void (no ground → no drag).
			if (CountGroundedWheels() <= 0)
			{
				m_WheeledMotor?.ZeroWheelTorques();
				StabilizeAirborneChassis();
			}

			LogSinkIfYDropped("while-simulating", yBefore);
			LogChassisStatus(body, phase);
			return;
		}

		// Soft park only (like LPVC 000) — do not zero velocity every FixedUpdate;
		// that killed WC settle and masked freefall in STATUS logs.
		ForceParkDriveCommand();
		LogSinkIfYDropped(phase, yBefore);
		LogChassisStatus(body, phase);
	}

	/// <summary>
	/// Periodic status while parked OR driving — idle previously only logged on Y drop.
	/// </summary>
	private void LogChassisStatus(Rigidbody _body, string _phase)
	{
		if (!m_LogDriveSink || _body == null)
			return;

		m_ChassisStatusLogCooldown -= Time.fixedDeltaTime;
		if (m_ChassisStatusLogCooldown > 0f)
			return;
		m_ChassisStatusLogCooldown = c_ChassisStatusLogInterval;

		bool hasPath = m_Navigation != null && m_Navigation.HasDestination;
		VehicleNavigation.DriverFSM.State state = m_Navigation != null
			? m_Navigation.DriverState
			: VehicleNavigation.DriverFSM.State.Idle;

		// Avoid spamming identical idle frames when the vehicle is simply parked.
		if (!hasPath && _phase == m_LastStatusPhase && state == m_LastStatusState)
			return;

		m_LastStatusPhase = _phase;
		m_LastStatusState = state;

		int grounded = CountGroundedWheels();
		int total = GetBoundWheelCount();
		float velY = _body.linearVelocity.y;
		float velXZ = new Vector2(_body.linearVelocity.x, _body.linearVelocity.z).magnitude;
		float angDeg = _body.angularVelocity.magnitude * Mathf.Rad2Deg;
		bool canDrive = m_Brain != null && m_Brain.CanDrive;
		VehicleBrakeMode brake = m_Brain != null ? m_Brain.CurrentCommand.BrakeMode : VehicleBrakeMode.None;

		LogVehicle(
			$"STATUS {_phase} y={transform.position.y:F3} grounded={grounded}/{total} " +
			$"velXZ={velXZ:F2} ang={angDeg:F0}°/s canDrive={canDrive} " +
			$"state={state} brake={brake} " +
			$"wheels=[{BuildWheelContactSummary()}] context=[{BuildDriveContextSummary()}]");
	}

	private string BuildWheelGroundDiag()
	{
		if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
			return "no-axles";

		int mask = LayerMask.GetMask("Ground", "Default", "Obstacle");
		if (mask == 0)
			mask = ~0;

		var sb = new System.Text.StringBuilder(192);
		for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
		{
			WheelCollider wc = m_WheeledMotor.Axles[i]?.Collider;
			if (wc == null)
				continue;
			if (sb.Length > 0)
				sb.Append(' ');

			Vector3 hub = wc.transform.TransformPoint(wc.center);
			float reach = wc.radius + wc.suspensionDistance;
			bool ray = Physics.Raycast(
				hub + Vector3.up * 0.05f,
				Vector3.down,
				out RaycastHit hit,
				reach + 2f,
				mask,
				QueryTriggerInteraction.Ignore);
			float gap = ray ? hub.y - hit.point.y - wc.radius : -1f;
			sb.Append(wc.name.Replace("WheelCollider_", ""));
			sb.Append(":hubY=");
			sb.Append(hub.y.ToString("F2"));
			sb.Append(ray ? $" gap={gap:F2} hit={hit.collider.name}/{LayerMask.LayerToName(hit.collider.gameObject.layer)}" : " gap=NO_RAY");
			sb.Append(wc.enabled ? "" : " OFF");
		}

		return sb.ToString();
	}

	private void EnsureDriveReadyForMove()
	{
		SyncDriveControlFromDriver();
		if (m_Brain == null || !m_Brain.ControlActive)
			return;
		if (!m_Brain.EngineRunning)
			m_Brain.StartEngine();
	}

	private void SyncDriveControlFromDriver()
	{
		if (m_Brain == null)
			EnsurePhysicsDrive();
		if (m_Brain == null)
			return;

		bool allowControl = HasDriver || m_TempAllowDriverlessControl;
		if (allowControl)
		{
			if (!m_Brain.ControlActive)
				m_Brain.SetControlActive(true);
			if (m_TempAllowDriverlessControl && !HasDriver && !m_Brain.EngineRunning)
				m_Brain.StartEngine();
		}
		else
		{
			m_Navigation?.Stop();
			if (m_Brain.EngineRunning)
				m_Brain.StopEngine();
			if (m_Brain.ControlActive)
				m_Brain.SetControlActive(false);
		}
	}

	private void OnBrainEngineStateChanged(bool _)
	{
		EngineStateChanged?.Invoke();
		if (m_IsSelected)
			RtsUnitSelectionManager.Instance?.NotifySelectionUiRefresh();
	}
	#endregion

	#region Selection Label
	private void EnsureSelectionNameLabel()
	{
		if (m_SelectionNameLabelRoot == null)
		{
			m_SelectionNameLabelRoot = new GameObject("SelectionNameLabel", typeof(RectTransform));
			RectTransform rt = m_SelectionNameLabelRoot.GetComponent<RectTransform>();
			rt.SetParent(transform, false);
			rt.sizeDelta = new Vector2(2.4f, 0.5f);

			Canvas canvas = m_SelectionNameLabelRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 31500;
		}

		if (m_SelectionNameLabelRoot.TryGetComponent(out UnityEngine.UI.GraphicRaycaster legacyRaycaster))
			Destroy(legacyRaycaster);

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
			m_SelectionNameText.raycastTarget = false;
			m_SelectionNameText.fontSize = 0.18f;
			m_SelectionNameText.alignment = TextAlignmentOptions.Center;
			m_SelectionNameText.color = Color.white;
			m_SelectionNameText.outlineWidth = 0.35f;
			m_SelectionNameText.outlineColor = Color.black;
			m_SelectionNameText.fontStyle = FontStyles.Bold;
		}
		else
		{
			m_SelectionNameText.raycastTarget = false;
		}
	}

	private void RefreshSelectionNameLabel()
	{
		if (m_SelectionNameText == null)
			return;

		string label = string.IsNullOrWhiteSpace(m_SelectionDisplayName)
			? gameObject.name
			: m_SelectionDisplayName;
		m_SelectionNameText.text = label;
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
		labelTransform.position = transform.position + Vector3.up * m_SelectionLabelHeight;
		labelTransform.rotation = m_CachedCameraTransform.rotation;
	}
	#endregion
}
