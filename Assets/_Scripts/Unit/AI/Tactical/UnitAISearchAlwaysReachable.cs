using UnityEngine;

/// <summary>
/// EditMode / no-NavMesh reachability. Every sampled point is accepted as-is.
/// </summary>
public sealed class UnitAISearchAlwaysReachable : ISearchReachability
{
	#region Public Properties
	public static readonly UnitAISearchAlwaysReachable Instance = new UnitAISearchAlwaysReachable();
	#endregion

	#region Public Methods
	public bool TryAccept(Vector3 _from, Vector3 _candidate, out Vector3 _sampled)
	{
		_sampled = _candidate;
		return true;
	}
	#endregion
}
