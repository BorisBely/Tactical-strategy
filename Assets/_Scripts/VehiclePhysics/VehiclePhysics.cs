using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class VehiclePhysics : MonoBehaviour
{
	#region Nested Types
	[Serializable]
	public sealed class AxleBinding
	{
		[Tooltip("Колёса на этой оси")]
		public WheelColliderAdapter[] Wheels = Array.Empty<WheelColliderAdapter>();
		[Tooltip("Применять моторный момент")]
		public bool ApplyMotor = true;
		[Tooltip("Применять поворот руля")]
		public bool ApplySteer = true;
	}

	public struct DebugState
	{
		public float SpeedKmh;
		public float Gear;
		public float EngineRPM;
		public float Throttle;
		public float Brake;
		public float EngineTorque;
		public float DriveshaftTorque;
		public float CurrentDragForce;
		public float AirborneTime;
		public float RollAngle;
		public Vector3 CenterOfMass;
		public float TotalMass;
		public StabilityController.Level StabilityLevel;
		public StabilityController.SafetyAction SafetyAction;
		public StabilityController.RecoveryAction RecoveryAction;
		public int NumericalGuardTrips;
		public string SurfaceName;
		public float SurfaceGripMultiplier;
		public float[] WheelLoads;
		public float[] WheelSlips;
		public float[] SuspensionTravels;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private VehiclePhysicsProfile m_Profile;
	[SerializeField] private AxleBinding[] m_Axles = Array.Empty<AxleBinding>();
	[SerializeField] private Transform m_BodyVisualRoot;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField] private List<SurfacePhysicsDefinition> m_SurfaceProfiles = new();
	[SerializeField] private SurfacePhysicsDefinition m_DefaultSurface;
	#endregion

	#region Private Fields
	// core
	private Rigidbody m_Body;
	private EngineModel m_Engine;
	private TransmissionModel m_Transmission;
	private DrivetrainModel m_Drivetrain;
	private DifferentialModel[] m_Differentials;
	private SuspensionModel m_Suspension;
	private TireModel m_Tire;
	private AerodynamicsModel m_Aerodynamics;
	private StabilityController m_Stability;
	private SurfacePhysics m_SurfacePhysics;

	// wheel data
	private IWheelInterface[] m_AllWheels;
	private WheelContact[] m_WheelContacts;
	private float[] m_WheelStaticLoads;

	// inputs
	private float m_ThrottleInput;
	private float m_SteerInput;
	private float m_BrakeInput;
	private bool m_Handbrake;

	// state
	private float m_CurrentSpeedKmh;
	private float m_CurrentSteerAngle;
	private int m_WheelCount;
	private SurfacePhysicsDefinition m_CurrentSurface;
	private float m_CurrentSurfaceRoughness;

	// mass
	private float m_TotalMass;
	private Vector3 m_CurrentCenterOfMass;
	private readonly List<IMassContributor> m_MassContributors = new();

	// debug
	private DebugState m_DebugState;
	#endregion

	#region Public Properties
	public float CurrentSpeedKmh => m_CurrentSpeedKmh;
	public float EngineRPM => m_Engine != null ? m_Engine.RPM : 0f;
	public int CurrentGear => m_Transmission != null ? m_Transmission.CurrentGear : 0;
	public DebugState Debug => m_DebugState;
	public IWheelInterface[] Wheels => m_AllWheels;
	public SurfacePhysicsDefinition CurrentSurface => m_CurrentSurface;
	public float SurfaceRoughnessMultiplier => m_CurrentSurfaceRoughness;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Body = GetComponent<Rigidbody>();
		if (m_Body == null)
			m_Body = gameObject.AddComponent<Rigidbody>();

		m_Body.isKinematic = false;
		m_Body.interpolation = RigidbodyInterpolation.Interpolate;
		m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
	}

	private void Start()
	{
		if (m_Profile == null)
		{
			UnityEngine.Debug.LogError($"[VehiclePhysics:{name}] Profile не назначен.", this);
			enabled = false;
			return;
		}

		InitializeModules();
	}

	private void FixedUpdate()
	{
		if (m_Profile == null || m_Engine == null)
			return;

		float dt = Time.fixedDeltaTime;
		if (dt < 0.0001f)
			return;

		m_CurrentSpeedKmh = m_Body.linearVelocity.magnitude * 3.6f;

		SampleSurface();
		UpdateCenterOfMass();

		// pipeline
		TickEngine(dt);
		TickTransmission(dt);
		TickDrivetrain(dt);
		TickSuspensionAndFriction();
		TickAerodynamics();
		TickStability(dt);
		TickVisuals();

		UpdateDebugState();
	}

	private void LateUpdate()
	{
		ApplyBodyTilt();
	}
	#endregion

	#region Public Methods
	public void SetInput(float throttle, float steer, float brake, bool handbrake)
	{
		m_ThrottleInput = Mathf.Clamp(throttle, -1f, 1f);
		m_SteerInput = Mathf.Clamp(steer, -1f, 1f);
		m_BrakeInput = Mathf.Clamp01(brake);
		m_Handbrake = handbrake;
	}

	public void AddMassContributor(IMassContributor contributor)
	{
		if (contributor != null && !m_MassContributors.Contains(contributor))
		{
			m_MassContributors.Add(contributor);
			contributor.OnMassChanged += RecalculateMass;
			RecalculateMass();
		}
	}

	public void RemoveMassContributor(IMassContributor contributor)
	{
		if (contributor != null && m_MassContributors.Remove(contributor))
		{
			contributor.OnMassChanged -= RecalculateMass;
			RecalculateMass();
		}
	}

	public SurfacePhysicsDefinition SurfaceAtPoint(Vector3 worldPoint)
	{
		if (Physics.Raycast(worldPoint + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, m_GroundMask,
			    QueryTriggerInteraction.Ignore))
		{
			return m_SurfacePhysics.Resolve(hit.collider);
		}
		return m_DefaultSurface;
	}
	#endregion

	#region Private Initialization
	private void InitializeModules()
	{
		m_TotalMass = m_Profile.Mass;
		m_CurrentCenterOfMass = m_Profile.BaseCenterOfMass;
		m_Body.mass = m_TotalMass;

		m_Engine = new EngineModel(m_Profile.Engine);
		m_Transmission = new TransmissionModel(m_Profile.Transmission);
		m_Drivetrain = new DrivetrainModel(m_Profile.Drivetrain);
		m_Suspension = new SuspensionModel(m_Profile.Suspension);
		m_Tire = new TireModel(m_Profile.Tire);
		m_Aerodynamics = new AerodynamicsModel(m_Profile.Aerodynamics);
		m_Stability = new StabilityController(m_Profile.Stability);
		m_SurfacePhysics = new SurfacePhysics(m_SurfaceProfiles, m_DefaultSurface);

		m_Stability.Bind(m_Body, transform);

		CollectWheels();
		m_Suspension.CalculateForMass(m_TotalMass, m_WheelCount);

		SetupDifferentials();

		RecalculateMass();
	}

	private void CollectWheels()
	{
		var wheelList = new List<IWheelInterface>();
		m_WheelCount = 0;

		foreach (var axle in m_Axles)
		{
			if (axle == null || axle.Wheels == null)
				continue;

			foreach (var adapter in axle.Wheels)
			{
				if (adapter == null)
					continue;

				adapter.ConfigureBase(m_Profile.Wheel.Radius, m_Profile.Wheel.Mass);
				wheelList.Add(adapter);
				m_WheelCount++;
			}
		}

		m_AllWheels = wheelList.ToArray();

		m_WheelStaticLoads = new float[m_AllWheels.Length];
		m_WheelContacts = new WheelContact[m_AllWheels.Length];

		float staticLoadPerWheel = m_WheelCount > 0
			? (m_TotalMass * Physics.gravity.magnitude) / m_WheelCount
			: 0f;

		for (int i = 0; i < m_AllWheels.Length; i++)
		{
			m_WheelStaticLoads[i] = staticLoadPerWheel;
			m_WheelContacts[i] = new WheelContact(m_AllWheels[i], m_Profile.Tire, staticLoadPerWheel);
		}
	}

	private void SetupDifferentials()
	{
		int axleCount = m_Drivetrain.AxleCount;
		m_Differentials = new DifferentialModel[Mathf.Max(1, axleCount)];
		for (int i = 0; i < m_Differentials.Length; i++)
			m_Differentials[i] = new DifferentialModel(m_Profile.Differential);
	}

	private void RecalculateMass()
	{
		m_TotalMass = m_Profile.Mass;
		Vector3 weightedCOM = m_Profile.BaseCenterOfMass * m_Profile.Mass;

		foreach (var c in m_MassContributors)
		{
			if (c == null)
				continue;
			m_TotalMass += c.Mass;
			weightedCOM += c.LocalOffset * c.Mass;
		}

		m_CurrentCenterOfMass = m_TotalMass > 0.001f
			? weightedCOM / m_TotalMass
			: m_Profile.BaseCenterOfMass;

		m_Body.mass = m_TotalMass;
		m_Body.centerOfMass = new Vector3(
			m_CurrentCenterOfMass.x,
			Mathf.Max(0.55f, m_CurrentCenterOfMass.y),
			m_CurrentCenterOfMass.z);

		m_Suspension.CalculateForMass(m_TotalMass, m_WheelCount);

		float staticLoadPerWheel = m_WheelCount > 0
			? (m_TotalMass * Physics.gravity.magnitude) / m_WheelCount
			: 0f;

		for (int i = 0; i < m_WheelContacts.Length; i++)
		{
			m_WheelStaticLoads[i] = staticLoadPerWheel;
			if (m_WheelContacts[i] != null)
				m_WheelContacts[i].SetStaticLoad(staticLoadPerWheel);
		}
	}
	#endregion

	#region Private Tick Methods
	private void TickEngine(float dt)
	{
		float driveshaftRPM = 0f;
		if (m_AllWheels.Length > 0)
		{
			float avg = 0f;
			int cnt = 0;
			foreach (var w in m_AllWheels)
			{
				if (w != null)
				{
					avg += Mathf.Abs(w.AngularVelocity);
					cnt++;
				}
			}
			if (cnt > 0)
				driveshaftRPM = avg / cnt;
		}

		m_Engine.SetLoadRPM(driveshaftRPM,
			m_Transmission.CurrentRatio,
			m_Profile.Transmission.FinalDrive);

		m_Engine.Tick(m_ThrottleInput, m_Engine.RPM, dt);
	}

	private void TickTransmission(float dt)
	{
		m_Transmission.Tick(m_Engine.RPM, m_ThrottleInput, m_CurrentSpeedKmh, dt);

		if (m_ThrottleInput < -0.1f && m_Transmission.CurrentGear > 0)
			m_Transmission.SetReverse();
		else if (m_ThrottleInput >= 0f && m_Transmission.CurrentGear < 0 && m_CurrentSpeedKmh < 1f)
			m_Transmission.SetNeutral();
	}

	private void TickDrivetrain(float dt)
	{
		float engineTorque = m_Engine.Torque;
		float gearRatio = m_Transmission.CurrentRatio;
		float finalDrive = m_Profile.Transmission.FinalDrive;
		float clutch = m_Transmission.ClutchEngagement;

		float driveshaftTorque = engineTorque * gearRatio * finalDrive * clutch;
		driveshaftTorque = Mathf.Clamp(driveshaftTorque,
			-m_Profile.Transmission.ClutchMaxTorque,
			m_Profile.Transmission.ClutchMaxTorque);

		if (m_Transmission.CurrentGear == 0)
			driveshaftTorque = 0f;

		int axleCount = Mathf.Max(1, m_Drivetrain.AxleCount);
		float[] axleTorques = new float[axleCount];
		m_Drivetrain.Distribute(driveshaftTorque, axleTorques);

		// map axles to wheels
		int wheelIdx = 0;
		for (int a = 0; a < m_Axles.Length && a < axleCount; a++)
		{
			if (m_Axles[a] == null || m_Axles[a].Wheels.Length < 2)
				continue;

			int leftIdx = wheelIdx;
			int rightIdx = wheelIdx + 1;

			if (leftIdx < m_AllWheels.Length && rightIdx < m_AllWheels.Length)
			{
				float rpmL = m_AllWheels[leftIdx] != null ? m_AllWheels[leftIdx].AngularVelocity : 0f;
				float rpmR = m_AllWheels[rightIdx] != null ? m_AllWheels[rightIdx].AngularVelocity : 0f;

				m_Differentials[Mathf.Min(a, m_Differentials.Length - 1)].Distribute(
					axleTorques[Mathf.Min(a, axleCount - 1)],
					rpmL, rpmR,
					m_AllWheels[leftIdx], m_AllWheels[rightIdx],
					out float torqueL, out float torqueR);

				if (m_Axles[a].ApplyMotor)
				{
					m_AllWheels[leftIdx]?.SetMotorTorque(torqueL);
					m_AllWheels[rightIdx]?.SetMotorTorque(torqueR);
				}
			}

			wheelIdx += m_Axles[a].Wheels.Length;
		}

		// brakes
		for (int i = 0; i < m_AllWheels.Length; i++)
		{
			if (m_AllWheels[i] == null)
				continue;

			float brakeTorque = ResolveBrake(i);
			m_AllWheels[i].SetBrakeTorque(brakeTorque);
		}

		// steering
		float steerTarget = m_SteerInput * m_Profile.Steering.MaxSteerAngle;
		m_CurrentSteerAngle = Mathf.MoveTowards(m_CurrentSteerAngle, steerTarget,
			m_Profile.Steering.SteerRate * dt);

		wheelIdx = 0;
		for (int a = 0; a < m_Axles.Length; a++)
		{
			if (m_Axles[a] == null || !m_Axles[a].ApplySteer)
			{
				wheelIdx += m_Axles[a]?.Wheels?.Length ?? 0;
				continue;
			}

			for (int w = 0; w < m_Axles[a].Wheels.Length; w++)
			{
				if (wheelIdx + w < m_AllWheels.Length)
				{
					float ackermannAngle = m_CurrentSteerAngle;
					if (m_Profile.Steering.Ackermann > 0.01f && m_Axles[a].Wheels.Length >= 2)
					{
						float innerFactor = w == 0 ? 1f : 1f / (1f + m_Profile.Steering.Ackermann * 0.3f);
						ackermannAngle *= innerFactor;
					}
					m_AllWheels[wheelIdx + w]?.SetSteerAngle(ackermannAngle);
				}
			}

			wheelIdx += m_Axles[a].Wheels.Length;
		}
	}

	private void TickSuspensionAndFriction()
	{
		SuspensionState suspState = m_Suspension.GetState();

		for (int i = 0; i < m_AllWheels.Length; i++)
		{
			if (m_AllWheels[i] == null)
				continue;

			m_AllWheels[i].ApplySuspension(suspState);

			SurfacePhysicsDefinition surface = m_CurrentSurface;
			float wetness = 0f;
			if (m_AllWheels[i].HitCollider != null)
			{
				surface = m_SurfacePhysics.Resolve(m_AllWheels[i].HitCollider);
				wetness = surface.WaterDepth > 0.01f ? 1f : 0f;
			}

			TireFrictionParams friction = m_Tire.ComputeFriction(surface, wetness);
			m_AllWheels[i].ApplyFriction(friction);

			if (m_WheelContacts[i] != null)
			{
				m_WheelContacts[i].Update(m_TotalMass, m_CurrentCenterOfMass, m_Body, surface, Time.fixedDeltaTime);

				float rollingResist = m_Tire.ComputeRollingResistance(surface, m_WheelContacts[i].Load);
				if (m_AllWheels[i].IsGrounded && m_ThrottleInput == 0f && m_BrakeInput < 0.01f)
				{
					m_AllWheels[i].SetBrakeTorque(rollingResist * 0.1f);
				}
			}
		}
	}

	private void TickAerodynamics()
	{
		m_Aerodynamics.Apply(m_Body);
	}

	private void TickStability(float dt)
	{
		m_Stability.Tick(m_AllWheels, dt);
	}

	private void TickVisuals()
	{
		for (int i = 0; i < m_AllWheels.Length; i++)
		{
			if (m_AllWheels[i] is WheelColliderAdapter adapter)
				adapter.SyncVisual();
		}
	}
	#endregion

	#region Private Helpers
	private float ResolveBrake(int wheelIndex)
	{
		bool isFrontAxle = false;
		int wheelIdx = 0;
		for (int a = 0; a < m_Axles.Length; a++)
		{
			int count = m_Axles[a]?.Wheels?.Length ?? 0;
			if (wheelIndex < wheelIdx + count)
			{
				isFrontAxle = a == 0;
				break;
			}
			wheelIdx += count;
		}

		float brakeBalance = m_Profile.BrakeBalance;
		float balanceForWheel = isFrontAxle ? brakeBalance : (1f - brakeBalance);

		if (m_Handbrake || m_BrakeInput > 0.01f)
		{
			float strength = m_Handbrake ? 1f : m_BrakeInput;
			return m_Profile.MaxBrakeTorque * strength * balanceForWheel;
		}

		if (Mathf.Abs(m_ThrottleInput) < 0.02f)
			return m_Profile.CoastDecelTorque * balanceForWheel;

		return 0f;
	}

	private void SampleSurface()
	{
		Vector3 origin = transform.position + Vector3.up * 1.5f;
		if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, m_GroundMask,
			    QueryTriggerInteraction.Ignore))
		{
			m_CurrentSurface = m_SurfacePhysics.Resolve(hit.collider);
		}
	}

	private void UpdateCenterOfMass()
	{
		m_Body.centerOfMass = new Vector3(
			m_CurrentCenterOfMass.x,
			Mathf.Max(0.55f, m_CurrentCenterOfMass.y),
			m_CurrentCenterOfMass.z);
	}

	private void ApplyBodyTilt()
	{
		if (m_BodyVisualRoot == null)
			return;

		Vector3 avgNormal = Vector3.zero;
		int hitCount = 0;

		foreach (var wheel in m_AllWheels)
		{
			if (wheel != null && wheel.IsGrounded)
			{
				avgNormal += wheel.HitNormal;
				hitCount++;
			}
		}

		if (hitCount == 0)
		{
			m_BodyVisualRoot.localRotation = Quaternion.Slerp(
				m_BodyVisualRoot.localRotation,
				Quaternion.identity,
				1f - Mathf.Exp(-8f * Time.deltaTime));
			return;
		}

		avgNormal /= hitCount;

		Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
		Quaternion yawBasis = Quaternion.LookRotation(flatForward, Vector3.up);

		Vector3 groundLocal = Quaternion.Inverse(yawBasis) * avgNormal.normalized;
		float pitch = Mathf.Atan2(groundLocal.z, groundLocal.y) * Mathf.Rad2Deg;
		float roll = -Mathf.Atan2(groundLocal.x, groundLocal.y) * Mathf.Rad2Deg;

		pitch = Mathf.Clamp(pitch, -16f, 16f);
		roll = Mathf.Clamp(roll, -14f, 14f);

		Quaternion target = Quaternion.Euler(pitch, 0f, roll);
		m_BodyVisualRoot.localRotation = Quaternion.Slerp(
			m_BodyVisualRoot.localRotation, target,
			1f - Mathf.Exp(-8f * Time.deltaTime));
	}

	private void UpdateDebugState()
	{
		m_DebugState.SpeedKmh = m_CurrentSpeedKmh;
		m_DebugState.Gear = m_Transmission.CurrentGear;
		m_DebugState.EngineRPM = m_Engine.RPM;
		m_DebugState.Throttle = m_ThrottleInput;
		m_DebugState.Brake = m_BrakeInput;
		m_DebugState.EngineTorque = m_Engine.Torque;
		m_DebugState.CurrentDragForce = m_Aerodynamics.CurrentDragForce;
		m_DebugState.AirborneTime = m_Stability.AirborneTime;
		m_DebugState.RollAngle = Vector3.Angle(transform.up, Vector3.up);
		m_DebugState.CenterOfMass = m_Body.centerOfMass;
		m_DebugState.TotalMass = m_TotalMass;
		m_DebugState.StabilityLevel = m_Stability.ActiveLevel;
		m_DebugState.SafetyAction = m_Stability.ActiveSafety;
		m_DebugState.RecoveryAction = m_Stability.ActiveRecovery;
		m_DebugState.NumericalGuardTrips = m_Stability.NumericalGuardTrips;
		m_DebugState.SurfaceName = m_CurrentSurface != null ? m_CurrentSurface.name : "Unknown";
		m_DebugState.SurfaceGripMultiplier = m_CurrentSurface != null ? m_CurrentSurface.ForwardGripMultiplier : 1f;

		m_DebugState.WheelLoads = new float[m_AllWheels.Length];
		m_DebugState.WheelSlips = new float[m_AllWheels.Length];
		m_DebugState.SuspensionTravels = new float[m_AllWheels.Length];

		for (int i = 0; i < m_AllWheels.Length; i++)
		{
			if (m_AllWheels[i] != null)
			{
				m_DebugState.WheelLoads[i] = m_WheelContacts[i]?.Load ?? m_AllWheels[i].Load;
				m_DebugState.WheelSlips[i] = m_WheelContacts[i]?.SlipRatio ?? 0f;
				m_DebugState.SuspensionTravels[i] = m_AllWheels[i].SuspensionTravel;
			}
		}
	}
	#endregion
}
