using UnityEngine;

public interface IWheelInterface
{
	bool IsGrounded { get; }
	float Load { get; }
	float SlipForward { get; }
	float SlipSideways { get; }
	float SuspensionTravel { get; }
	float SuspensionTravelRatio { get; }
	float Radius { get; }
	float AngularVelocity { get; }
	Vector3 HitNormal { get; }
	Vector3 HitPoint { get; }
	Collider HitCollider { get; }

	void SetMotorTorque(float torque);
	void SetBrakeTorque(float torque);
	void SetSteerAngle(float angle);
	void ApplySuspension(SuspensionState state);
	void ApplyFriction(TireFrictionParams friction);
}

public struct SuspensionState
{
	public float springRate;
	public float damperCompression;
	public float damperRebound;
	public float travel;
	public float targetPosition;
}

public struct TireFrictionParams
{
	public float extremumSlip;
	public float extremumValue;
	public float asymptoteSlip;
	public float asymptoteValue;
	public float stiffness;
}
