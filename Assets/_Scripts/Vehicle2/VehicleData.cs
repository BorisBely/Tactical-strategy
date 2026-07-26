using UnityEngine;

/// <summary>
/// Single source of truth for all vehicle tuning parameters.
/// Create via Assets → Create → Vehicle → Vehicle Data.
/// </summary>
[CreateAssetMenu(menuName = "Vehicle/Vehicle Data", fileName = "VehicleData")]
public class VehicleData : ScriptableObject
{
	[Header("Body")]
	public float Mass = 2400f;
	public Vector3 CenterOfMass = new Vector3(0f, 0.40f, 0f);
	public float AngularDamping = 15f;
	[Tooltip("rad/s, ~57 deg/s default")]
	public float MaxAngularVelocity = 1f;

	[Header("Wheel Geometry")]
	public float WheelRadius = 0.45f;
	public float WheelMass = 100f;
	[Range(0.01f, 1f)]
	public float WheelDampingRate = 0.40f;

	[Tooltip("FL, FR, RL, RR — local positions relative to vehicle root")]
	public Vector3[] WheelLocalPositions = new Vector3[]
	{
		new Vector3(-0.94f, 0.526f,  1.691f),
		new Vector3( 0.94f, 0.526f,  1.691f),
		new Vector3(-0.94f, 0.526f, -1.531f),
		new Vector3( 0.94f, 0.526f, -1.531f),
	};

	[Tooltip("Which axles steer? [FL, FR, RL, RR]")]
	public bool[] SteerAxles = new bool[] { true, true, false, false };

	[Header("Suspension")]
	public float SuspensionTravel = 0.30f;

	[Tooltip("N/m — spring stiffness")]
	public float SpringRate = 50000f;

	[Tooltip("N·s/m — damper resistance")]
	public float DamperRate = 11000f;

	[Range(0f, 1f)]
	[Tooltip("0 = fully drooped, 0.5 = mid, 1 = fully compressed at rest")]
	public float RestCompression = 0.55f;

	[Tooltip("0 = force applied at contact patch. Positive = higher, increases pitch/roll coupling")]
	public float ForceAppPointDistance = 0f;

	[Header("Friction")]
	public float ForwardStiffness = 2.5f;
	public float ForwardExtremumSlip = 2.5f;
	public float ForwardAsymptoteSlip = 1.2f;
	public float ForwardAsymptoteValue = 0.6f;
	public float SidewaysStiffness = 1.8f;
	public float SidewaysExtremumSlip = 0.6f;
	public float SidewaysAsymptoteSlip = 0.8f;
	public float SidewaysAsymptoteValue = 0.7f;

	[Header("Engine")]
	public float MaxMotorTorque = 1500f;
	public float MaxSpeedMs = 25f;         // ~90 km/h

	[Tooltip("Reverse torque multiplier")]
	public float ReverseTorque = 800f;

	public float BrakeTorque = 5000f;
	public float CoastDecelTorque = 450f;  // engine braking when throttle=0

	[Tooltip("Motor torque ramp rate (Nm/s), 0 = instant")]
	public float TorqueRampRate = 3000f;

	[Header("Steering")]
	public float SteerAngleMax = 32f;      // degrees
	public float SteerSpeed = 160f;        // degrees per second
	public float ThrottleResponse = 4f;    // higher = snappier

	[Header("Anti-Stuck")]
	public bool AntiStuckEnabled = true;

	[Tooltip("Max radius increase when stuck")]
	public float AntiStuckMaxOffset = 0.08f;

	[Tooltip("Only active below this speed (km/h)")]
	public float AntiStuckMaxSpeedKmh = 5f;

	[Header("Substeps (Unity 6)")]
	public bool UseVehicleSubsteps = true;

	[Tooltip("Speed threshold below which max substeps apply")]
	public float SubstepsSpeedThreshold = 10f;
	public int SubstepsBelow = 30;
	public int SubstepsAbove = 20;
}
