using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Production NavMesh sample for #13.1. Tests inject ICoverNavMeshProbe instead.
/// </summary>
public sealed class NavMeshCoverProbe : ICoverNavMeshProbe
{
	#region Private Fields
	private readonly float m_SampleRadius;
	#endregion

	#region Public Constructors
	public NavMeshCoverProbe(float _sampleRadiusMeters = 1f)
	{
		m_SampleRadius = Mathf.Max(0.05f, _sampleRadiusMeters);
	}
	#endregion

	#region Public Methods
	public bool TrySample(Vector3 _world, out Vector3 _onMesh)
	{
		_onMesh = _world;
		if (!NavMesh.SamplePosition(_world, out NavMeshHit hit, m_SampleRadius, NavMesh.AllAreas))
			return false;
		_onMesh = hit.position;
		return true;
	}
	#endregion
}
