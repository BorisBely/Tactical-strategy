using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global zone dedup after a single world generate. Not per-region. #13.2C.6
/// </summary>
public static class ProtectionZoneDedup
{
	#region Public Methods
	public static void Apply(List<ProtectionZone> _zones, CoverGenerationSettings _settings)
	{
		if (_zones == null || _zones.Count <= 1)
			return;
		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		DeduplicateNear(_zones, settings);
	}

	public static int Compare(ProtectionZone _a, ProtectionZone _b)
	{
		if (_a == null && _b == null)
			return 0;
		if (_a == null)
			return 1;
		if (_b == null)
			return -1;
		int type = ((int)_a.GeometryType).CompareTo((int)_b.GeometryType);
		if (type != 0)
			return type;
		int x = _a.Center.x.CompareTo(_b.Center.x);
		if (x != 0)
			return x;
		int z = _a.Center.z.CompareTo(_b.Center.z);
		if (z != 0)
			return z;
		return _a.Width.CompareTo(_b.Width);
	}
	#endregion

	#region Private Methods
	private static void DeduplicateNear(List<ProtectionZone> _zones, CoverGenerationSettings _settings)
	{
		float radiusSqr = Mathf.Max(0.05f, _settings.ZoneDedupRadiusMeters);
		radiusSqr *= radiusSqr;
		_zones.Sort(Compare);
		var kept = new List<ProtectionZone>(_zones.Count);
		for (int i = 0; i < _zones.Count; i++)
		{
			ProtectionZone zone = _zones[i];
			if (zone == null)
				continue;
			bool duplicate = false;
			for (int k = 0; k < kept.Count; k++)
			{
				ProtectionZone other = kept[k];
				if (other.GeometryType != zone.GeometryType)
					continue;
				if (CoverSpatialMath.PlanarDistanceSqr(zone.Center, other.Center) > radiusSqr)
					continue;
				Vector3 nA = PlanarUnit(zone.SurfaceNormal);
				Vector3 nB = PlanarUnit(other.SurfaceNormal);
				if (nA.sqrMagnitude > 0.5f && nB.sqrMagnitude > 0.5f && Vector3.Dot(nA, nB) <= 0.5f)
					continue;
				if (zone.GeometryType == ProtectionZoneType.Edge)
				{
					if (zone.EdgeKind != other.EdgeKind)
						continue;
					Vector3 dA = PlanarUnit(zone.EdgeDirection);
					Vector3 dB = PlanarUnit(other.EdgeDirection);
					if (dA.sqrMagnitude > 0.5f && dB.sqrMagnitude > 0.5f && Vector3.Dot(dA, dB) <= 0.5f)
						continue;
				}
				if (zone.Width > other.Width)
					kept[k] = zone;
				duplicate = true;
				break;
			}

			if (!duplicate)
				kept.Add(zone);
		}

		_zones.Clear();
		_zones.AddRange(kept);
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}
