using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Play / production gate: NavMesh sample + complete path from the unit.
/// </summary>
public sealed class UnitAISearchNavMeshReachability : ISearchReachability
{
	#region Constants
	private const float c_SampleRadius = 2f;
	#endregion

	#region Public Properties
	public static readonly UnitAISearchNavMeshReachability Instance = new UnitAISearchNavMeshReachability();
	#endregion

	#region Public Methods
	public bool TryAccept(Vector3 _from, Vector3 _candidate, out Vector3 _sampled)
	{
		_sampled = _candidate;
		if (!NavMesh.SamplePosition(_candidate, out NavMeshHit toHit, c_SampleRadius, NavMesh.AllAreas))
			return false;
		if (!NavMesh.SamplePosition(_from, out NavMeshHit fromHit, c_SampleRadius, NavMesh.AllAreas))
			return false;

		var path = new NavMeshPath();
		if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path))
			return false;
		if (path.status != NavMeshPathStatus.PathComplete)
			return false;

		_sampled = toHit.position;
		return true;
	}
	#endregion
}
