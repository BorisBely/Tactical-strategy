using UnityEngine;
using UnityEngine.AI;

namespace VehicleNavigation
{
	/// <summary>
	/// Global path planner. Knows only one thing: how to get from A to B over the NavMesh.
	/// </summary>
	public sealed class PathPlanner
	{
		public static bool DebugLog = true;
		private readonly NavMeshPath m_Path = new NavMeshPath();

		public PathResult BuildPath(Vector3 _from, Vector3 _to)
		{
			return BuildPath(_from, _to, PathBuildOptions.Default);
		}

		public PathResult BuildPath(Vector3 _from, Vector3 _to, PathBuildOptions _options)
		{
			if (_options == null)
				_options = PathBuildOptions.Default;

			bool fromOnNav = NavMesh.SamplePosition(_from,
				out NavMeshHit fromHit, _options.SampleRadiusFrom, NavMesh.AllAreas);
			bool toOnNav = NavMesh.SamplePosition(_to,
				out NavMeshHit toHit, _options.SampleRadiusTo, NavMesh.AllAreas);

			if (fromOnNav && toOnNav)
			{
				if (NavMesh.CalculatePath(fromHit.position, toHit.position,
					NavMesh.AllAreas, m_Path) &&
					m_Path.status != NavMeshPathStatus.PathInvalid &&
					m_Path.corners != null &&
					m_Path.corners.Length > 0)
				{
					bool isPartial = m_Path.status == NavMeshPathStatus.PathPartial;
					if (isPartial && !_options.AllowPartialPath)
						return PathResult.Invalid;

					Vector3[] corners = ProcessCorners(m_Path.corners, _from, _to, _options);
					if (!ValidatePath(corners, _options))
					{
						if (DebugLog)
							Debug.LogWarning("[PathPlanner] NavMesh path failed validation");
						return PathResult.Invalid;
					}

					float length = EstimateLength(corners);
					return new PathResult(
						corners,
						length,
						true,
						isPartial,
						false,
						_to,
						toHit.position);
				}
			}

			if (!_options.AllowDirectFallback)
			{
				if (DebugLog)
					Debug.LogWarning($"[PathPlanner] NavMesh path failed, direct fallback disabled → Invalid");
				return PathResult.Invalid;
			}

			Vector3[] direct = BuildDirectPath(_from, _to);
			if (!ValidatePath(direct, _options))
				return PathResult.Invalid;

			if (DebugLog)
				Debug.Log($"[PathPlanner] NavMesh failed, using direct fallback [{_from:F0} → {_to:F0}] points={direct.Length} fromNav={fromOnNav} toNav={toOnNav}");
			return new PathResult(direct, EstimateLength(direct), true, false, true, _to, _to);
		}

		public PathResult BuildSafePath(Vector3 _from, Vector3 _to, float _vehicleRadius = 1.5f)
		{
			var options = PathBuildOptions.SafeOnly;
			options.SampleRadiusFrom = Mathf.Max(1f, _vehicleRadius);
			options.SampleRadiusTo = Mathf.Max(1f, _vehicleRadius);
			return BuildPath(_from, _to, options);
		}

		private static Vector3[] ProcessCorners(
			Vector3[] _corners,
			Vector3 _from,
			Vector3 _to,
			PathBuildOptions _options)
		{
			if (_corners == null || _corners.Length == 0)
				return System.Array.Empty<Vector3>();

			var list = new System.Collections.Generic.List<Vector3>();
			for (int i = 0; i < _corners.Length; i++)
			{
				Vector3 c = _corners[i];
				if (list.Count == 0 || FlatDistance(list[list.Count - 1], c) > 0.05f)
					list.Add(c);
			}

			if (list.Count > 0)
				list[0] = _from;

			if (list.Count > 0)
				list[list.Count - 1] = _to;

			return list.ToArray();
		}

		private static bool ValidatePath(Vector3[] _corners, PathBuildOptions _options)
		{
			if (_corners == null || _corners.Length < 2)
				return false;

			for (int i = 0; i < _corners.Length - 1; i++)
			{
				Vector3 a = _corners[i];
				Vector3 b = _corners[i + 1];
				a.y += 0.5f;
				b.y += 0.5f;
				if (!NavMesh.Raycast(a, b, out NavMeshHit _, NavMesh.AllAreas))
					continue;

				if (!_options.AllowDirectFallback)
					return false;
			}

			return true;
		}

		private static float EstimateLength(Vector3[] _corners)
		{
			if (_corners == null || _corners.Length < 2)
				return 0f;

			float length = 0f;
			for (int i = 0; i < _corners.Length - 1; i++)
			{
				Vector3 a = _corners[i];
				Vector3 b = _corners[i + 1];
				a.y = 0f;
				b.y = 0f;
				length += Vector3.Distance(a, b);
			}

			return length;
		}

		private static Vector3[] BuildDirectPath(Vector3 _from, Vector3 _to)
		{
			Vector3 dir = _to - _from;
			dir.y = 0f;
			float dist = dir.magnitude;
			if (dist < 10f) return new[] { _from, _to };
			var list = new System.Collections.Generic.List<Vector3> { _from };
			float step = Mathf.Max(5f, dist / 3f);
			float t = step;
			while (t < dist - step) { list.Add(_from + dir.normalized * t); t += step; }
			list.Add(_to);
			return list.ToArray();
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
