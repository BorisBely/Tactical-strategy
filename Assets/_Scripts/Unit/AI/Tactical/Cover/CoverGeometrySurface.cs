using UnityEngine;

/// <summary>
/// One walkable-side world surface used to sample positional cover candidates.
/// Not a collider identity. Not a scored cover slot.
/// </summary>
public struct CoverGeometrySurface
{
	public Vector3 Origin;
	public Vector3 Normal;
	public Vector3 Tangent;
	public float Length;
}
