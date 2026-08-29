using UnityEngine;

/// <summary>
/// Cheap reachability gate for a sampled stance. EditMode injects a fake.
/// </summary>
public interface ICoverNavMeshProbe
{
	bool TrySample(Vector3 _world, out Vector3 _onMesh);
}
