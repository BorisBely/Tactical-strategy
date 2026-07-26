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

		public static PathResult Invalid => new PathResult(null, 0f, false, false, false);

		public PathResult(Vector3[] _corners, float _length, bool _isValid, bool _isPartial, bool _usedDirectFallback = false)
		{
			Corners = _corners ?? System.Array.Empty<Vector3>();
			Length = _length;
			IsValid = _isValid;
			IsPartial = _isPartial;
			UsedDirectFallback = _usedDirectFallback;
		}
	}
}
