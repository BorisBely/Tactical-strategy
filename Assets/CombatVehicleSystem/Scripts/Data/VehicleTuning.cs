using UnityEngine;

namespace CombatVehicleSystem
{
	[CreateAssetMenu(fileName = "VehicleTuning", menuName = "Combat Vehicle System/Vehicle Tuning", order = 0)]
	public class VehicleTuning : ScriptableObject
	{
		#region Drive
		[Header("Drive")]
		[SerializeField] private VehicleDriveClass m_DriveClass = VehicleDriveClass.WheeledApc;
		[SerializeField] private Vector3 m_CenterOfMass = new Vector3(0f, -0.6f, 0.1f);
		[SerializeField] private float m_MotorForce = 1500f;
		[SerializeField] private float m_AccelerationForce;
		[SerializeField] private float m_ReverseForce;
		[SerializeField] private float m_TopSpeedKmh = 90f;
		[SerializeField] private float m_MaxBrakeTorque = 5000f;
		[SerializeField] private float m_SoftBrakeTorque;
		[SerializeField] private float m_HardBrakeTorque;
		[SerializeField] private float m_CoastDecelTorque = 450f;
		[SerializeField] private float m_ApproachSlowdownDistance = 8f;
		[SerializeField] private float m_ArriveDistance = 2f;
		[SerializeField] private float m_CreepSpeedKmh = 3f;
		[SerializeField] private float m_CreepDistance = 3f;
		[SerializeField] private float m_MaxDecelerationMs2 = 5.5f;
		[Header("Path Follow / Pure Pursuit")]
		[SerializeField, Min(1f)] private float m_LookAheadDistance = 6f;
		[SerializeField, Min(0f)] private float m_LookAheadSpeedScale = 0.12f;
		[SerializeField, Min(0.5f)] private float m_MinLookAheadDistance = 3.5f;
		[SerializeField, Min(1f)] private float m_MaxLookAheadDistance = 10f;
		[SerializeField, Range(90f, 170f)] private float m_ReverseAngleDegrees = 120f;
		[SerializeField, Min(1f)] private float m_ReverseMaxSegmentLength = 8f;
		[SerializeField] private bool m_IdleParkBrake = true;
		[SerializeField] private float m_SteerRate = 160f;
		[SerializeField] private float m_ThrottleResponse = 4f;
		[SerializeField] private float m_DefaultSteerAngle = 28f;
		[SerializeField, Min(0.5f)] private float m_WheelBase = 3.5f;
		[SerializeField] private AnimationCurve m_CurvatureSpeedCurve = new AnimationCurve(
			new Keyframe(0f, 1f),
			new Keyframe(0.15f, 0.55f),
			new Keyframe(0.3f, 0.18f));
		[SerializeField] private float m_EngineStartDelay = 0.55f;
		[SerializeField] private float m_TrackScrollScale = 1f;
		[SerializeField] private float m_RigidbodyMass = 2200f;
		#endregion

		#region Turret
		[Header("Turret")]
		[SerializeField] private float m_TurnRate = 120f;
		[SerializeField] private bool m_LimitYaw = false;
		[SerializeField, Range(0f, 180f)] private float m_LeftYawLimit = 60f;
		[SerializeField, Range(0f, 180f)] private float m_RightYawLimit = 60f;
		[SerializeField, Range(0f, 180f)] private float m_UpPitchLimit = 60f;
		[SerializeField, Range(0f, 180f)] private float m_DownPitchLimit = 12f;
		[SerializeField] private float m_DefaultAimDistance = 200f;
		#endregion

		#region Weapon
		[Header("Weapon")]
		[SerializeField] private float m_FireInterval = 0.17f;
		[SerializeField] private float m_ShellSpeed = 200f;
		[SerializeField] private float m_HullRecoilForce = 100f;
		[SerializeField] private int m_MagazineSize = 300;
		[SerializeField] private bool m_InfiniteAmmo = false;
		[SerializeField] private Vector3 m_ShotSpread = new Vector3(0.1f, 0.1f, 0.1f);
		[SerializeField] private Vector3 m_BarrelKick = Vector3.zero;
		[SerializeField] private float m_BarrelKickSpeed = 8f;
		[SerializeField] private float m_BarrelReturnSpeed = 18f;
		[SerializeField] private float m_HitFxLifetime = 10f;
		[SerializeField] private float m_ShellLifetime = 25f;
		[SerializeField] private float m_MinShotPitch = 0.9f;
		[SerializeField] private float m_MaxShotPitch = 1.1f;
		#endregion

		#region Public Properties
		public VehicleDriveClass DriveClass => m_DriveClass;
		public Vector3 CenterOfMass => m_CenterOfMass;
		public float MotorForce => m_MotorForce;
		public float AccelerationForce => m_AccelerationForce > 0.01f ? m_AccelerationForce : m_MotorForce;
		public float ReverseForce => m_ReverseForce > 0.01f ? m_ReverseForce : m_MotorForce * 0.55f;
		public float TopSpeedKmh => m_TopSpeedKmh;
		public float MaxBrakeTorque => ResolveHardBrakeTorque();
		public float SoftBrakeTorque => m_SoftBrakeTorque > 0.01f ? m_SoftBrakeTorque : m_MaxBrakeTorque * 0.32f;
		public float HardBrakeTorque => ResolveHardBrakeTorque();
		public float CoastDecelTorque => m_CoastDecelTorque;
		public float ApproachSlowdownDistance => m_ApproachSlowdownDistance;
		public float ArriveDistance => m_ArriveDistance;
		public float CreepSpeedKmh => m_CreepSpeedKmh > 0.1f ? m_CreepSpeedKmh : 3f;
		public float CreepDistance => m_CreepDistance > 0.1f ? m_CreepDistance : 3f;
		public float MaxDecelerationMs2 => m_MaxDecelerationMs2 > 0.1f ? m_MaxDecelerationMs2 : 5.5f;
		public float LookAheadDistance => m_LookAheadDistance;
		public float LookAheadSpeedScale => m_LookAheadSpeedScale;
		public float MinLookAheadDistance => m_MinLookAheadDistance;
		public float MaxLookAheadDistance => m_MaxLookAheadDistance;
		public float ReverseAngleDegrees => m_ReverseAngleDegrees;
		public float ReverseMaxSegmentLength => m_ReverseMaxSegmentLength;
		public bool IdleParkBrake => m_IdleParkBrake;
		public float SteerRate => m_SteerRate;
		public float ThrottleResponse => m_ThrottleResponse;
		public float DefaultSteerAngle => m_DefaultSteerAngle;
		public float WheelBase => m_WheelBase;
		public AnimationCurve CurvatureSpeedCurve => m_CurvatureSpeedCurve;
		public float EngineStartDelay => m_EngineStartDelay;
		public float TrackScrollScale => m_TrackScrollScale;
		public float RigidbodyMass => m_RigidbodyMass > 1f ? m_RigidbodyMass : 2200f;
		public float TurnRate => m_TurnRate;
		public bool LimitYaw => m_LimitYaw;
		public float LeftYawLimit => m_LeftYawLimit;
		public float RightYawLimit => m_RightYawLimit;
		public float UpPitchLimit => m_UpPitchLimit;
		public float DownPitchLimit => m_DownPitchLimit;
		public float DefaultAimDistance => m_DefaultAimDistance;
		public float FireInterval => m_FireInterval;
		public float ShellSpeed => m_ShellSpeed;
		public float HullRecoilForce => m_HullRecoilForce;
		public int MagazineSize => m_MagazineSize;
		public bool InfiniteAmmo => m_InfiniteAmmo;
		public Vector3 ShotSpread => m_ShotSpread;
		public Vector3 BarrelKick => m_BarrelKick;
		public float BarrelKickSpeed => m_BarrelKickSpeed;
		public float BarrelReturnSpeed => m_BarrelReturnSpeed;
		public float HitFxLifetime => m_HitFxLifetime;
		public float ShellLifetime => m_ShellLifetime;
		public float MinShotPitch => m_MinShotPitch;
		public float MaxShotPitch => m_MaxShotPitch;
		#endregion

		#region Public Methods
		/// <summary>
		/// Humvee / light utility profile — agile SUV, not wheeled APC torque.
		/// </summary>
		public void ConfigureAsLightUtilityHumvee()
		{
			m_DriveClass = VehicleDriveClass.LightUtility;
			m_CenterOfMass = new Vector3(0f, 0.55f, 0.15f);
			m_MotorForce = 1500f;
			m_AccelerationForce = 1650f;
			m_ReverseForce = 900f;
			m_TopSpeedKmh = 100f;
			m_MaxBrakeTorque = 4200f;
			m_SoftBrakeTorque = 1400f;
			m_HardBrakeTorque = 5200f;
			m_CoastDecelTorque = 380f;
			m_ApproachSlowdownDistance = 6f;
			m_ArriveDistance = 0.4f;
			m_LookAheadDistance = 6f;
			m_LookAheadSpeedScale = 0.12f;
			m_MinLookAheadDistance = 3.5f;
			m_MaxLookAheadDistance = 10f;
			m_ReverseAngleDegrees = 120f;
			m_ReverseMaxSegmentLength = 8f;
			m_IdleParkBrake = true;
			m_SteerRate = 180f;
			m_ThrottleResponse = 5f;
			m_DefaultSteerAngle = 32f;
			m_WheelBase = 3.5f;
			m_CurvatureSpeedCurve = new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(0.15f, 0.55f),
				new Keyframe(0.3f, 0.18f));
			m_EngineStartDelay = 0.5f;
			m_TrackScrollScale = 1f;
			m_RigidbodyMass = 2400f;
			m_TurnRate = 90f;
			m_DownPitchLimit = 20f;
		}

		public static VehicleTuning CreateRuntimeLightUtilityHumvee()
		{
			VehicleTuning tuning = CreateInstance<VehicleTuning>();
			tuning.hideFlags = HideFlags.HideAndDontSave;
			tuning.name = "Tuning_LightUtility_Humvee_Runtime";
			tuning.ConfigureAsLightUtilityHumvee();
			return tuning;
		}
		#endregion

		#region Private Methods
		private float ResolveHardBrakeTorque()
		{
			if (m_HardBrakeTorque > 0.01f)
				return m_HardBrakeTorque;
			return m_MaxBrakeTorque;
		}
		#endregion
	}
}
