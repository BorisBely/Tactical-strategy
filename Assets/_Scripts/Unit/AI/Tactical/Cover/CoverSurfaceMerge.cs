using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.2B.5A: collider faces → logical wall surfaces, before Edge / Opening / Corner.
/// Overlap and blocked seams become one wall. A real door gap is not filled.
/// </summary>
public static class CoverSurfaceMerge
{
	#region Public Methods
	public static void Rebuild(
		List<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		ICoverSeamProbe _seamProbe)
	{
		if (_surfaces == null || _surfaces.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		bool merged = true;
		while (merged)
		{
			_surfaces.Sort(Compare);
			merged = false;
			for (int i = 0; i < _surfaces.Count; i++)
			{
				for (int j = i + 1; j < _surfaces.Count; j++)
				{
					if (!TryCombine(_surfaces[i], _surfaces[j], settings, _seamProbe, out CoverGeometrySurface combined))
						continue;
					_surfaces[i] = combined;
					_surfaces.RemoveAt(j);
					merged = true;
					break;
				}

				if (merged)
					break;
			}
		}
	}

	public static int Compare(CoverGeometrySurface _a, CoverGeometrySurface _b)
	{
		int x = _a.Origin.x.CompareTo(_b.Origin.x);
		if (x != 0)
			return x;
		int z = _a.Origin.z.CompareTo(_b.Origin.z);
		if (z != 0)
			return z;
		int nx = _a.Normal.x.CompareTo(_b.Normal.x);
		if (nx != 0)
			return nx;
		int nz = _a.Normal.z.CompareTo(_b.Normal.z);
		if (nz != 0)
			return nz;
		return _a.Length.CompareTo(_b.Length);
	}
	#endregion

	#region Private Methods
	private static bool TryCombine(
		CoverGeometrySurface _a,
		CoverGeometrySurface _b,
		CoverGenerationSettings _settings,
		ICoverSeamProbe _seamProbe,
		out CoverGeometrySurface _combined)
	{
		_combined = default;
		if (!_a.TryGetPlanarEnds(out Vector3 aStart, out Vector3 aEnd))
			return false;
		if (!_b.TryGetPlanarEnds(out Vector3 bStart, out Vector3 bEnd))
			return false;

		Vector3 normalA = PlanarUnit(_a.Normal);
		Vector3 normalB = PlanarUnit(_b.Normal);
		if (normalA.sqrMagnitude < 0.5f || normalB.sqrMagnitude < 0.5f)
			return false;

		float align = Mathf.Clamp(_settings.MergeNormalAlignDot, 0.5f, 0.99f);
		if (Vector3.Dot(normalA, normalB) < align)
			return false;

		Vector3 normal = PlanarUnit(normalA + normalB);
		if (normal.sqrMagnitude < 0.5f)
			return false;

		Vector3 axis = Vector3.Cross(Vector3.up, normal);
		if (axis.sqrMagnitude < 0.01f)
			return false;
		axis.Normalize();

		float planeA = 0.5f * (Vector3.Dot(aStart, normal) + Vector3.Dot(aEnd, normal));
		float planeB = 0.5f * (Vector3.Dot(bStart, normal) + Vector3.Dot(bEnd, normal));
		float maxPlane = Mathf.Max(0.05f, _settings.MergePlaneOffsetMeters);
		if (Mathf.Abs(planeA - planeB) > maxPlane)
			return false;

		float heightSplit = Mathf.Max(0.05f, _settings.ZoneHeightSplitMeters);
		if (_a.Height > 0.05f && _b.Height > 0.05f && Mathf.Abs(_a.Height - _b.Height) > heightSplit)
			return false;

		IntervalAlong(axis, aStart, aEnd, out float aMin, out float aMax);
		IntervalAlong(axis, bStart, bEnd, out float bMin, out float bMax);
		float min = Mathf.Min(aMin, bMin);
		float max = Mathf.Max(aMax, bMax);
		float overlap = Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin);
		float gap = overlap >= 0f ? 0f : -overlap;

		float seamGap = Mathf.Max(0f, _settings.MergeSeamGapMeters);
		float blockedSeam = Mathf.Max(seamGap, _settings.MergeBlockedSeamMeters);
		float plane = 0.5f * (planeA + planeB);
		bool stitch = overlap >= -0.001f || gap <= seamGap;
		if (!stitch && gap <= blockedSeam && _seamProbe != null)
		{
			float leftT = aMax <= bMin ? aMax : bMax;
			float rightT = aMax <= bMin ? bMin : aMin;
			Vector3 alongStart = axis * leftT + normal * plane;
			Vector3 alongEnd = axis * rightT + normal * plane;
			alongStart.y = 0f;
			alongEnd.y = 0f;
			stitch = _seamProbe.HasSolidInGap(alongStart, alongEnd, normal);
		}

		if (!stitch)
			return false;

		float combinedLength = max - min;
		if (combinedLength < 0.05f)
			return false;

		Vector3 origin = axis * ((min + max) * 0.5f) + normal * plane;
		origin.y = 0f;
		_combined = new CoverGeometrySurface
		{
			Origin = origin,
			Normal = normal,
			Tangent = axis,
			Length = combinedLength,
			Height = Mathf.Max(_a.Height, _b.Height)
		};
		return true;
	}

	private static void IntervalAlong(
		Vector3 _axis,
		Vector3 _start,
		Vector3 _end,
		out float _min,
		out float _max)
	{
		float a = Vector3.Dot(_start, _axis);
		float b = Vector3.Dot(_end, _axis);
		if (a <= b)
		{
			_min = a;
			_max = b;
			return;
		}

		_min = b;
		_max = a;
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}
