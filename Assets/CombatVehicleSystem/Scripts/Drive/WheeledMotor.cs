using UnityEngine;

namespace CombatVehicleSystem
{
	[System.Serializable]
	public class WheelAxle
	{
		#region Public Fields
		public WheelCollider Collider;
		public Transform Visual;
		public bool ApplyMotor = true;
		public bool ApplySteer = true;
		public float SteerAngle = 20f;
		public WheelAntiStuck AntiStuck;
		#endregion
	}

	public class WheeledMotor : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private WheelAxle[] m_Axles;
		#endregion

		#region Private Fields
		private Rigidbody m_Body;
		private float m_MotorForce = 1500f;
		private float m_AccelerationForce = 1500f;
		private float m_ReverseForce = 800f;
		private float m_TopSpeedKmh = 90f;
		private float m_SoftBrakeTorque = 1600f;
		private float m_HardBrakeTorque = 5000f;
		private float m_CoastDecelTorque = 450f;
		private float m_SteerRate = 120f;
		private float m_ThrottleResponse = 4f;
		private float m_CurrentSpeedKmh;
		private float m_SmoothedThrottle;
		private float m_CurrentSteer;
		private Vector3[] m_VisualRestLocalPos = System.Array.Empty<Vector3>();
		private Quaternion[] m_VisualRestLocalRot = System.Array.Empty<Quaternion>();
		#endregion

		#region Public Properties
		public float CurrentSpeedKmh => m_CurrentSpeedKmh;
		public float CurrentSteerNormalized => m_CurrentSteer;
		public WheelAxle[] Axles => m_Axles;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Body = GetComponent<Rigidbody>();
			BindAntiStuck();
			CacheVisualRestPoses();
		}
		#endregion

		#region Public Methods
		public void ApplyTuning(VehicleTuning _tuning)
		{
			if (_tuning == null)
				return;
			m_MotorForce = _tuning.MotorForce;
			m_AccelerationForce = _tuning.AccelerationForce;
			m_ReverseForce = _tuning.ReverseForce;
			m_TopSpeedKmh = _tuning.TopSpeedKmh;
			m_SoftBrakeTorque = _tuning.SoftBrakeTorque;
			m_HardBrakeTorque = _tuning.HardBrakeTorque;
			m_CoastDecelTorque = _tuning.CoastDecelTorque;
			m_SteerRate = Mathf.Max(1f, _tuning.SteerRate);
			m_ThrottleResponse = Mathf.Max(0.1f, _tuning.ThrottleResponse);

			if (m_Axles == null)
				return;
			for (int i = 0; i < m_Axles.Length; i++)
			{
				if (m_Axles[i] == null || !m_Axles[i].ApplySteer)
					continue;
				m_Axles[i].SteerAngle = _tuning.DefaultSteerAngle;
			}
		}

		public void SetAxles(WheelAxle[] _axles)
		{
			m_Axles = _axles;
			BindAntiStuck();
			CacheVisualRestPoses();
		}

		public void TickDrive(VehicleCommand _command)
		{
			if (m_Body == null || m_Axles == null)
				return;

			// Component disabled = isolation / external gate. Do not apply Idle Hard brake.
			if (!isActiveAndEnabled)
			{
				ZeroWheelTorques();
				return;
			}

			if (m_Body.isKinematic)
			{
				ParkWheels();
				return;
			}

			m_CurrentSpeedKmh = m_Body.linearVelocity.magnitude * 3.6f;
			// Called from VehicleBrain.FixedUpdate — use physics dt for throttle smoothing.
			float dt = Time.fixedDeltaTime;
			float throttleTarget = _command.Throttle;
			m_SmoothedThrottle = Mathf.MoveTowards(
				m_SmoothedThrottle,
				throttleTarget,
				m_ThrottleResponse * dt);

			float brakeTorque = ResolveBrakeTorque(_command.BrakeMode, m_SmoothedThrottle);

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null)
					continue;

				// Brake/motor on air wheels spins the hull (~2800°/s).
				if (!axle.Collider.GetGroundHit(out _))
				{
					axle.Collider.motorTorque = 0f;
					axle.Collider.brakeTorque = 0f;
					continue;
				}

				if (brakeTorque > 0.01f &&
				    (_command.BrakeMode != VehicleBrakeMode.None || Mathf.Abs(m_SmoothedThrottle) < 0.02f))
				{
					axle.Collider.motorTorque = 0f;
					axle.Collider.brakeTorque = brakeTorque;
					continue;
				}

				axle.Collider.brakeTorque = 0f;

			if (!axle.ApplyMotor)
				continue;

			float force = m_SmoothedThrottle >= 0f ? m_AccelerationForce : m_ReverseForce;
			if (force < 0.01f)
				force = m_MotorForce;

			// Reduce drive torque on steered wheels while turning so they keep lateral grip
			// and the car actually rotates instead of plowing sideways.
			if (axle.ApplySteer)
			{
				float steerAbs = Mathf.Abs(_command.Steer);
				if (steerAbs > 0.25f)
					force *= Mathf.Lerp(1f, 0.25f, (steerAbs - 0.25f) / 0.75f);
			}

			if (m_CurrentSpeedKmh < m_TopSpeedKmh || m_SmoothedThrottle < 0f)
				axle.Collider.motorTorque = m_SmoothedThrottle * force;
			else
				axle.Collider.motorTorque = 0f;
			}
		}

		public void TickPhysics(bool _controlActive, VehicleCommand _command)
		{
			if (!isActiveAndEnabled)
			{
				ZeroWheelTorques();
				return;
			}

			if (m_Body != null && m_Body.isKinematic)
			{
				ParkWheels();
				return;
			}

			// Sync wheel meshes whenever the body is simulated; also restore rest pose when parked
			// so wheels are not left underground after a bad physics frame.
			if (m_Body != null && !m_Body.isKinematic)
				SyncVisuals();
			else
				RestoreVisualRestPose();

			if (!_controlActive || m_Axles == null)
				return;

			float steerTarget = Mathf.Clamp(_command.Steer, -1f, 1f);
			m_CurrentSteer = Mathf.MoveTowards(
				m_CurrentSteer,
				steerTarget,
				(m_SteerRate / 90f) * Time.fixedDeltaTime);

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null || !axle.ApplySteer)
					continue;
				axle.Collider.steerAngle = axle.SteerAngle * m_CurrentSteer;
			}
		}

		public void SetSpeedCapKmh(float _capKmh)
		{
			m_TopSpeedKmh = Mathf.Max(1f, _capKmh);
		}

		/// <summary>Обнулить мотор/руль без стояночного тормоза (isolation / disabled motor).</summary>
		public void ZeroWheelTorques()
		{
			m_SmoothedThrottle = 0f;
			m_CurrentSteer = 0f;
			m_CurrentSpeedKmh = 0f;
			if (m_Axles == null)
				return;

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null)
					continue;

				axle.Collider.motorTorque = 0f;
				axle.Collider.brakeTorque = 0f;
				axle.Collider.steerAngle = 0f;
			}
		}

		/// <summary>Обнулить мотор/руль; soft park brake только на grounded колёсах.</summary>
		public void ParkWheels()
		{
			m_SmoothedThrottle = 0f;
			m_CurrentSteer = 0f;
			m_CurrentSpeedKmh = 0f;

			if (m_Axles == null)
				return;

			float parkBrake = m_Body != null && !m_Body.isKinematic
				? m_SoftBrakeTorque
				: m_HardBrakeTorque;

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null)
					continue;

				axle.Collider.motorTorque = 0f;
				axle.Collider.steerAngle = 0f;
				axle.Collider.brakeTorque = axle.Collider.GetGroundHit(out _)
					? parkBrake
					: 0f;
			}

			if (m_Body == null || m_Body.isKinematic)
				RestoreVisualRestPose();
		}

		/// <summary>Legacy no-op — drive RB is always dynamic (no kinematic wake).</summary>
		public void PrepareForDriveWake()
		{
		}

		public void ResetSprungMassesSafe()
		{
			if (m_Axles == null || m_Axles.Length == 0)
				return;
			if (m_Axles[0]?.Collider == null)
				return;
			m_Axles[0].Collider.ResetSprungMasses();
		}

		/// <summary>Legacy no-op — wake suspension softening removed with always-dynamic RB.</summary>
		public void ApplyWakeSuspensionProfile(bool _soft)
		{
		}
		#endregion

		#region Private Methods
		private float ResolveBrakeTorque(VehicleBrakeMode _mode, float _throttle)
		{
			switch (_mode)
			{
				case VehicleBrakeMode.Hard:
					return m_HardBrakeTorque;
				case VehicleBrakeMode.Soft:
					return m_SoftBrakeTorque;
				case VehicleBrakeMode.Coast:
					return 0f;
				default:
					if (Mathf.Abs(_throttle) < 0.02f)
						return m_CoastDecelTorque;
					return 0f;
			}
		}

		private void BindAntiStuck()
		{
			if (m_Axles == null)
				return;

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null)
					continue;

				if (axle.AntiStuck == null &&
				    !axle.Collider.TryGetComponent(out axle.AntiStuck))
					axle.AntiStuck = axle.Collider.gameObject.AddComponent<WheelAntiStuck>();

				if (axle.Visual != null)
					axle.AntiStuck.BindVisual(axle.Visual);

				// Soft radius inflate only when actually stuck (motor on, speed dead, obstacle ahead).
				axle.AntiStuck.ConfigureSoft(
					_maxOffset: 0.08f,
					_maxSpeedKmh: 5f,
					_correctionSpeed: 6f);
			}
		}

		private void CacheVisualRestPoses()
		{
			if (m_Axles == null)
			{
				m_VisualRestLocalPos = System.Array.Empty<Vector3>();
				m_VisualRestLocalRot = System.Array.Empty<Quaternion>();
				return;
			}

			m_VisualRestLocalPos = new Vector3[m_Axles.Length];
			m_VisualRestLocalRot = new Quaternion[m_Axles.Length];
			for (int i = 0; i < m_Axles.Length; i++)
			{
				Transform visual = m_Axles[i] != null ? m_Axles[i].Visual : null;
				if (visual == null)
				{
					m_VisualRestLocalPos[i] = Vector3.zero;
					m_VisualRestLocalRot[i] = Quaternion.identity;
					continue;
				}

				m_VisualRestLocalPos[i] = visual.localPosition;
				m_VisualRestLocalRot[i] = visual.localRotation;
			}
		}

		private void RestoreVisualRestPose()
		{
			if (m_Axles == null)
				return;
			if (m_VisualRestLocalPos == null || m_VisualRestLocalPos.Length != m_Axles.Length)
				CacheVisualRestPoses();

			for (int i = 0; i < m_Axles.Length; i++)
			{
				Transform visual = m_Axles[i] != null ? m_Axles[i].Visual : null;
				if (visual == null)
					continue;
				visual.localPosition = m_VisualRestLocalPos[i];
				visual.localRotation = m_VisualRestLocalRot[i];
			}
		}

		private void SyncVisuals()
		{
			if (m_Axles == null)
				return;

			for (int i = 0; i < m_Axles.Length; i++)
			{
				WheelAxle axle = m_Axles[i];
				if (axle == null || axle.Collider == null || axle.Visual == null)
					continue;

				axle.Collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
				axle.Visual.SetPositionAndRotation(pos, rot);
			}
		}
		#endregion
	}
}
