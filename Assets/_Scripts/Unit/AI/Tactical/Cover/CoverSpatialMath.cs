using UnityEngine;

/// <summary>
/// Pure region mapping for #13 shared cover cache. Not a score.
/// </summary>
public static class CoverSpatialMath
{
	#region Constants
	public const float DefaultRegionSizeMeters = 16f;
	public const int DefaultMaxCoverCandidates = 16;
	#endregion

	#region Public Methods
	public static CoverRegionId WorldToRegion(Vector3 _world, float _regionSizeMeters)
	{
		float size = Mathf.Max(1f, _regionSizeMeters);
		int x = Mathf.FloorToInt(_world.x / size);
		int z = Mathf.FloorToInt(_world.z / size);
		return new CoverRegionId(x, z);
	}

	public static Bounds RegionBounds(CoverRegionId _id, float _regionSizeMeters)
	{
		float size = Mathf.Max(1f, _regionSizeMeters);
		Vector3 center = new Vector3((_id.X + 0.5f) * size, 0f, (_id.Z + 0.5f) * size);
		return new Bounds(center, new Vector3(size, 8f, size));
	}

	public static bool SameRegion(Vector3 _a, Vector3 _b, float _regionSizeMeters)
	{
		return WorldToRegion(_a, _regionSizeMeters) == WorldToRegion(_b, _regionSizeMeters);
	}

	public static Bounds ExpandHorizontally(Bounds _bounds, float _marginMeters)
	{
		float margin = Mathf.Max(0f, _marginMeters);
		Vector3 size = _bounds.size;
		size.x += margin * 2f;
		size.z += margin * 2f;
		return new Bounds(_bounds.center, size);
	}

	public static bool ContainsPlanar(Bounds _bounds, Vector3 _world)
	{
		Vector3 planar = _world;
		planar.y = _bounds.center.y;
		return _bounds.Contains(planar);
	}

	public static float PlanarDistanceSqr(Vector3 _a, Vector3 _b)
	{
		float dx = _a.x - _b.x;
		float dz = _a.z - _b.z;
		return dx * dx + dz * dz;
	}
	#endregion
}
