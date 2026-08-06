using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Vehicle-relative oval arrival band: tight along the chassis (forward/back),
	/// wider sideways (left/right). Avoids finish micro-corrections for lateral miss
	/// without accepting a large longitudinal short-stop.
	/// </summary>
	public static class ArrivalPositionBand
	{
		public const float DefaultLongitudinal = 0.1f;
		public const float DefaultLateral = 0.45f;

		public static float EquivalentRadius(float _longitudinalTol, float _lateralTol) =>
			Mathf.Max(_longitudinalTol, _lateralTol);

		/// <summary>
		/// Decompose flat goal error into vehicle longitudinal (+)forward and lateral (+)right.
		/// </summary>
		public static void Decompose(
			Vector3 _vehiclePos,
			float _vehicleYawDeg,
			Vector3 _goalPos,
			out float _longitudinal,
			out float _lateral)
		{
			Vector3 err = _goalPos - _vehiclePos;
			err.y = 0f;
			float yawRad = _vehicleYawDeg * Mathf.Deg2Rad;
			float cos = Mathf.Cos(yawRad);
			float sin = Mathf.Sin(yawRad);
			// Unity forward on XZ: (sin, 0, cos); right: (cos, 0, -sin)
			_longitudinal = err.x * sin + err.z * cos;
			_lateral = err.x * cos - err.z * sin;
		}

		public static bool IsInside(
			Vector3 _vehiclePos,
			float _vehicleYawDeg,
			Vector3 _goalPos,
			float _longitudinalTol = DefaultLongitudinal,
			float _lateralTol = DefaultLateral)
		{
			float lonTol = Mathf.Max(0.01f, _longitudinalTol);
			float latTol = Mathf.Max(0.01f, _lateralTol);
			Decompose(_vehiclePos, _vehicleYawDeg, _goalPos, out float lon, out float lat);
			float nx = lon / lonTol;
			float ny = lat / latTol;
			return nx * nx + ny * ny <= 1f;
		}

		public static bool IsInside(GoalPose _goal, Vector3 _vehiclePos, float _vehicleYawDeg) =>
			IsInside(
				_vehiclePos,
				_vehicleYawDeg,
				_goal.Position,
				_goal.LongitudinalTolerance,
				_goal.LateralTolerance);
	}
}
