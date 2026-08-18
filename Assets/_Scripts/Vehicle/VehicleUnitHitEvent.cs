using UnityEngine;

/// <summary>
/// Contact / overlap between a vehicle proxy and an infantry unit.
/// Consumed later by injury / ragdoll systems; never apply forces to the drive RB.
/// </summary>
public readonly struct VehicleUnitHitEvent
{
	public readonly VehicleController Vehicle;
	public readonly RtsUnitMember Unit;
	public readonly Vector3 ContactPoint;
	public readonly Vector3 ContactNormal;
	public readonly float RelativeSpeedMs;
	public readonly bool FromSolidContact;

	public VehicleUnitHitEvent(
		VehicleController _vehicle,
		RtsUnitMember _unit,
		Vector3 _contactPoint,
		Vector3 _contactNormal,
		float _relativeSpeedMs,
		bool _fromSolidContact)
	{
		Vehicle = _vehicle;
		Unit = _unit;
		ContactPoint = _contactPoint;
		ContactNormal = _contactNormal;
		RelativeSpeedMs = _relativeSpeedMs;
		FromSolidContact = _fromSolidContact;
	}
}

public interface IVehicleUnitHitReceiver
{
	void OnVehicleUnitHit(in VehicleUnitHitEvent _hit);
}
