using UnityEngine;

/// <summary>
/// Cheap candidate gate before scoring. EditMode defaults to always reachable.
/// </summary>
public interface ISearchReachability
{
	bool TryAccept(Vector3 _from, Vector3 _candidate, out Vector3 _sampled);
}
