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
	public float Height;

	public bool TryGetPlanarEnds(out Vector3 _start, out Vector3 _end)
	{
		_start = Origin;
		_end = Origin;
		Vector3 tangent = Tangent;
		if (tangent.sqrMagnitude < 0.01f)
			tangent = Vector3.Cross(Vector3.up, Normal);
		tangent = Vector3.ProjectOnPlane(tangent, Vector3.up);
		if (tangent.sqrMagnitude < 0.01f)
			return false;
		tangent.Normalize();
		float half = Length * 0.5f;
		_start = Origin - tangent * half;
		_end = Origin + tangent * half;
		_start.y = 0f;
		_end.y = 0f;
		return true;
	}
}
