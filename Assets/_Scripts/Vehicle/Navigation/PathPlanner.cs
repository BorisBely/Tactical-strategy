using UnityEngine;
using UnityEngine.AI;

namespace VehicleNavigation
{
	/// <summary>
	/// Global path planner. Knows only one thing: how to get from A to B over the NavMesh.
	/// </summary>
	public sealed class PathPlanner
	{
		private readonly NavMeshPath m_Path = new NavMeshPath();

		public PathResult BuildPath(Vector3 _from, Vector3 _to)
		{
			bool fromOnNav = NavMesh.SamplePosition(_from, out NavMeshHit fromHit, 3f, NavMesh.AllAreas);
			bool toOnNav = NavMesh.SamplePosition(_to, out NavMeshHit toHit, 4f, NavMesh.AllAreas);

			if (fromOnNav && toOnNav)
			{
				if (NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, m_Path) &&
				    m_Path.status != NavMeshPathStatus.PathInvalid &&
				    m_Path.corners != null &&
				    m_Path.corners.Length > 0)
				{
					float length = EstimateLength(m_Path.corners);
					return new PathResult(
						m_Path.corners,
						length,
						true,
						m_Path.status == NavMeshPathStatus.PathPartial);
				}
			}

			// Fallback: drive straight to the destination when NavMesh is missing or can't find a path.
			Vector3[] direct = new[] { _from, _to };
			return new PathResult(direct, EstimateLength(direct), true, false);
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
	}
}
