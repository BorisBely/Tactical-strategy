using UnityEngine;

/// <summary>
/// Line occlusion for cover classification. EditMode injects a fake. Not per-enemy.
/// </summary>
public interface ICoverOcclusionProbe
{
	bool IsBlocked(Vector3 _from, Vector3 _to);
}
