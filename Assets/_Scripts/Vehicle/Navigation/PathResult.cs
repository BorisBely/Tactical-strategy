using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Result of the global path planner (NavMesh). Contains no driving decisions.
	/// </summary>
	public readonly struct PathResult
	{
		public readonly Vector3[] Corners;
		public readonly float Length;
		public readonly bool IsValid;
		public readonly bool IsPartial;
		public readonly bool UsedDirectFallback;
		public readonly Vector3 RequestedGoal;
		public readonly Vector3 SampledGoal;

		public static PathResult Invalid => new PathResult(null, 0f, false, false, false);

		public PathResult(
			Vector3[] _corners,
			float _length,
			bool _isValid,
			bool _isPartial,
			bool _usedDirectFallback = false,
			Vector3 _requestedGoal = default,
			Vector3 _sampledGoal = default)
		{
			Corners = _corners ?? System.Array.Empty<Vector3>();
			Length = _length;
			IsValid = _isValid;
			IsPartial = _isPartial;
			UsedDirectFallback = _usedDirectFallback;
			RequestedGoal = _requestedGoal;
			SampledGoal = _sampledGoal;
		}

		public bool TryGetLastSegmentTangent(out float _yawDegrees)
		{
			_yawDegrees = 0f;
			if (Corners == null || Corners.Length < 2)
				return false;

			for (int i = Corners.Length - 1; i >= 1; i--)
			{
				Vector3 seg = Corners[i] - Corners[i - 1];
				seg.y = 0f;
				if (seg.sqrMagnitude < 0.01f)
					continue;

				_yawDegrees = Quaternion.LookRotation(seg.normalized, Vector3.up).eulerAngles.y;
				return true;
			}

			return false;
		}
	}
}
