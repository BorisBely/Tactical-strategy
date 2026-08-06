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

		public override bool IsComplete(ManeuverContext _ctx)
		{
			float dist = FlatDistance(_ctx.Position, Destination);
			float headingErr = Mathf.Abs(Mathf.DeltaAngle(
				Quaternion.LookRotation(_ctx.Forward, Vector3.up).eulerAngles.y,
				TargetHeadingYaw));
			return dist <= 0.5f && headingErr <= 5f && _ctx.SpeedKmh <= 1.5f;
		}
	}
}
