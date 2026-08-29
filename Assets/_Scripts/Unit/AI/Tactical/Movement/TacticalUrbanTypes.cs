using UnityEngine;

/// <summary>
/// One walkable-side wall sample. Derived from #13 geometry, not a designer Street flag.
/// Position is on the wall face. Normal points toward walkable space.
/// </summary>
public struct TacticalWallAnchor
{
	public Vector3 Position;
	public Vector3 Normal;
	public float Length;

	public static TacticalWallAnchor FromCover(CoverCandidate _cover, float _insetMeters)
	{
		if (_cover == null)
			return default;
		Vector3 normal = Flatten(_cover.Normal);
		if (normal.sqrMagnitude < 0.01f)
			return default;
		normal.Normalize();
		float inset = Mathf.Max(0.05f, _insetMeters);
		return new TacticalWallAnchor
		{
			Position = _cover.Position - normal * inset,
			Normal = normal,
			Length = 0f
		};
	}

	public static Vector3 Flatten(Vector3 _normal)
	{
		_normal.y = 0f;
		return _normal;
	}
}

/// <summary>
/// Cheap urban readout for one evaluation. Not CQB. Not a street authoring flag.
/// </summary>
public struct TacticalUrbanGeometryContext
{
	public bool Present;
	public int AnchorCount;
	public float BuildingDensity01;
}

/// <summary>
/// Per-route wall corridor sample. Prototype, not freeze.
/// WallProximity is a preferred band, not “closer is better”.
/// </summary>
public struct TacticalUrbanRouteSample
{
	public float WallProximity01;
	public float OpenExposure01;
	public float MeanWallMeters;
}
