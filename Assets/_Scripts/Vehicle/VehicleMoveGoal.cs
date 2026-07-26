using UnityEngine;

/// <summary>
/// RTS move goal for a vehicle: position + optional arrival heading (from RMB drag arrow).
/// </summary>
public struct VehicleMoveGoal
{
	public Vector3 Position;
	public float HeadingYawDegrees;
	public bool HasHeading;
	public VehicleSpeedMode SpeedMode;

	public static VehicleMoveGoal FromPosition(Vector3 _position, VehicleSpeedMode _speedMode)
	{
		return new VehicleMoveGoal
		{
			Position = _position,
			HeadingYawDegrees = 0f,
			HasHeading = false,
			SpeedMode = _speedMode
		};
	}

	public static VehicleMoveGoal FromPositionAndHeading(
		Vector3 _position,
		float _headingYawDegrees,
		VehicleSpeedMode _speedMode)
	{
		return new VehicleMoveGoal
		{
			Position = _position,
			HeadingYawDegrees = _headingYawDegrees,
			HasHeading = true,
			SpeedMode = _speedMode
		};
	}
}
