using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Maneuver wrapper so FSM / debug overlays can display the active local trajectory.
	/// </summary>
	public sealed class TrajectoryFollowingManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.ApproachWithHeading;

		public VehicleTrajectory Trajectory { get; }

		public TrajectoryFollowingManeuver(VehicleTrajectory _trajectory)
		{
			Trajectory = _trajectory;
			AllowReverse = true;
			SpeedScale = 0.45f;
			LookAheadOverride = 2.5f;
			IsArrivalManeuver = true;
			if (_trajectory != null && _trajectory.IsValid)
				SetWaypoints(_trajectory.ToPositions());
		}

		public override bool IsComplete(ManeuverContext _ctx)
		{
			if (Trajectory == null || !Trajectory.IsValid || Trajectory.PointCount == 0)
				return true;
			Vector3 end = Trajectory.Points[Trajectory.PointCount - 1].Position;
			return FlatDistance(_ctx.Position, end) <= 0.15f && _ctx.SpeedKmh <= 1.5f;
		}
	}
}
