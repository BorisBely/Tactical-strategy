using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Rejects physically impossible trajectories (yaw teleports, gear/tangent mismatch, excessive curvature).
	/// </summary>
	public static class TrajectoryKinematicsValidator
	{
		private const float c_MinMotionForYaw = 0.02f;
		private const float c_MaxYawPerMeter = 120f;

		public static bool Validate(VehicleTrajectory _traj, float _turnRadius, out string _reason)
		{
			_reason = null;
			if (_traj == null || !_traj.IsValid || _traj.PointCount < 2)
			{
				_reason = "invalid trajectory";
				return false;
			}

			float minRadius = Mathf.Max(0.5f, _turnRadius);
			float maxCurv = 1f / minRadius * 1.15f;

			for (int i = 1; i < _traj.PointCount; i++)
			{
				TrajectoryPoint prev = _traj.Points[i - 1];
				TrajectoryPoint curr = _traj.Points[i];
				float ds = BicycleKinematics.FlatDistance(prev.Position, curr.Position);
				float dyaw = Mathf.Abs(Mathf.DeltaAngle(prev.YawDegrees, curr.YawDegrees));

				// Zero-length yaw teleport is always illegal.
				if (ds < c_MinMotionForYaw && dyaw > 1f)
				{
					_reason = $"zero-length yaw change {dyaw:F1}° at arc={curr.ArcLength:F2}";
					return false;
				}

				// Stationary gear change (cusp) is allowed; only reject if yaw also teleports (above).
				if (prev.Gear != curr.Gear && ds < c_MinMotionForYaw)
					continue;

				if (ds > 0.001f && dyaw / ds > c_MaxYawPerMeter)
				{
					_reason = $"curvature spike {dyaw / ds:F1}°/m at i={i}";
					return false;
				}

				if (Mathf.Abs(curr.Curvature) > maxCurv + 0.02f)
				{
					_reason = $"curvature {curr.Curvature:F3} exceeds R={minRadius:F1}m";
					return false;
				}

				if (curr.ArcLength + 0.001f < prev.ArcLength)
				{
					_reason = $"arc regression at i={i}";
					return false;
				}

				if (ds > c_MinMotionForYaw && prev.Gear == curr.Gear)
				{
					Vector3 delta = curr.Position - prev.Position;
					delta.y = 0f;
					Vector3 tangent = delta.normalized;
					float align = MotionAlign(prev, curr, tangent);
					// Arc chords can under-align vs average yaw; require only that motion is not opposite.
					float minAlign = dyaw > 8f ? -0.05f : 0.25f;
					if (align < minAlign)
					{
						_reason = $"gear/tangent mismatch at i={i} align={align:F2}";
						return false;
					}
				}
			}

			return true;
		}

		private static float MotionAlign(TrajectoryPoint _prev, TrajectoryPoint _curr, Vector3 _tangent)
		{
			float prevMotionYaw = _prev.Gear == TrajectoryGear.Reverse
				? _prev.YawDegrees + 180f
				: _prev.YawDegrees;
			float currMotionYaw = _curr.Gear == TrajectoryGear.Reverse
				? _curr.YawDegrees + 180f
				: _curr.YawDegrees;
			Vector3 prevFwd = BicycleKinematics.YawToForward(prevMotionYaw);
			Vector3 currFwd = BicycleKinematics.YawToForward(currMotionYaw);
			Vector3 avgFwd = (prevFwd + currFwd).normalized;
			if (avgFwd.sqrMagnitude < 1e-6f)
				avgFwd = currFwd;
			return Vector3.Dot(avgFwd, _tangent);
		}

		public static bool IsAtGoal(Vector3 _pos, float _yaw, GoalPose _goal)
		{
			float posErr = BicycleKinematics.FlatDistance(_pos, _goal.Position);
			if (posErr > _goal.PositionTolerance)
				return false;

			if (_goal.RequiresPosePlanning &&
			    Mathf.Abs(Mathf.DeltaAngle(_yaw, _goal.YawDegrees)) > _goal.HeadingToleranceDeg)
				return false;

			return true;
		}
	}

}
