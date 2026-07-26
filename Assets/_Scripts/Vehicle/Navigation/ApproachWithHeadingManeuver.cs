using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ApproachWithHeadingManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.ApproachWithHeading;

		public float TargetHeadingYaw { get; }
		public Vector3 Destination { get; }

		public ApproachWithHeadingManeuver(Vector3 _destination, float _targetHeadingYaw)
		{
			Destination = _destination;
			TargetHeadingYaw = _targetHeadingYaw;
			AllowReverse = true;
			SpeedScale = 0.28f;
			LookAheadOverride = 2f;
			IsArrivalManeuver = true;
		}
	}
}
