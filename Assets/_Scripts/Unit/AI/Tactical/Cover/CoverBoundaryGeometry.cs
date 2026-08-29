using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One exposed endpoint seen from one side of a surface.
/// </summary>
public struct CoverBoundarySide
{
	public Vector3 Center;
	public Vector3 SurfaceAxis;
	public Vector3 SurfaceNormal;
	public Vector3 Outward;
	public float Range;
	public float Height;
}

/// <summary>
/// One physical protection boundary. A paired WallEnd spans the complete end-cap.
/// </summary>
public struct CoverBoundarySeed
{
	public Vector3 Center;
	public Vector3 Axis;
	public Vector3 Outward;
	public float Width;
	public float Depth;
	public float Height;
	public ProtectionEdgeKind Kind;
	public bool IsPhysicalBand;
}

/// <summary>
/// Collapses opposite surface endpoints into one #13.2C physical BoundaryBand.
/// </summary>
public static class CoverBoundaryGeometry
{
	#region Public Methods
	public static void CollapseWallEnds(
		IReadOnlyList<CoverBoundarySide> _sides,
		CoverGenerationSettings _settings,
		List<CoverBoundarySeed> _destination)
	{
		if (_destination == null)
			return;
		_destination.Clear();
		if (_sides == null || _sides.Count == 0)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		var sides = new List<CoverBoundarySide>(_sides.Count);
		for (int i = 0; i < _sides.Count; i++)
			sides.Add(_sides[i]);
		sides.Sort(CompareSide);

		var consumed = new bool[sides.Count];
		for (int i = 0; i < sides.Count; i++)
		{
			if (consumed[i])
				continue;

			int pair = FindBestPair(i, sides, consumed, settings);
			if (pair < 0)
				continue;
			consumed[i] = true;
			consumed[pair] = true;
			_destination.Add(BuildPhysicalBand(sides[i], sides[pair]));
		}

		for (int i = 0; i < sides.Count; i++)
		{
			if (consumed[i] || IsPartOfPhysicalEndCap(sides[i], _destination, settings))
				continue;
			_destination.Add(BuildOneSidedBand(sides[i], settings));
		}

		_destination.Sort(CompareSeed);
	}
	#endregion

	#region Private Methods
	private static int FindBestPair(
		int _sourceIndex,
		IReadOnlyList<CoverBoundarySide> _sides,
		IReadOnlyList<bool> _consumed,
		CoverGenerationSettings _settings)
	{
		int bestIndex = -1;
		float bestDistance = float.MaxValue;
		CoverBoundarySide source = _sides[_sourceIndex];
		for (int i = _sourceIndex + 1; i < _sides.Count; i++)
		{
			if (_consumed[i] || !CanPair(source, _sides[i], _settings, out float distance))
				continue;
			if (distance >= bestDistance)
				continue;
			bestDistance = distance;
			bestIndex = i;
		}

		return bestIndex;
	}

	private static bool CanPair(
		CoverBoundarySide _a,
		CoverBoundarySide _b,
		CoverGenerationSettings _settings,
		out float _distance)
	{
		_distance = float.MaxValue;
		Vector3 outwardA = PlanarUnit(_a.Outward);
		Vector3 outwardB = PlanarUnit(_b.Outward);
		Vector3 normalA = PlanarUnit(_a.SurfaceNormal);
		Vector3 normalB = PlanarUnit(_b.SurfaceNormal);
		Vector3 axisA = PlanarUnit(_a.SurfaceAxis);
		Vector3 axisB = PlanarUnit(_b.SurfaceAxis);
		if (outwardA.sqrMagnitude < 0.5f || outwardB.sqrMagnitude < 0.5f ||
		    normalA.sqrMagnitude < 0.5f || normalB.sqrMagnitude < 0.5f)
			return false;
		if (Vector3.Dot(outwardA, outwardB) < 0.9f ||
		    Vector3.Dot(normalA, normalB) > -0.85f)
			return false;
		if (axisA.sqrMagnitude > 0.5f && axisB.sqrMagnitude > 0.5f &&
		    Mathf.Abs(Vector3.Dot(axisA, axisB)) < 0.85f)
			return false;

		Vector3 delta = _b.Center - _a.Center;
		delta.y = 0f;
		_distance = delta.magnitude;
		float maxThickness = Mathf.Max(0.1f, _settings.MaxWallEndThicknessMeters);
		if (_distance < 0.05f || _distance > maxThickness)
			return false;

		float outwardOffset = Mathf.Abs(Vector3.Dot(delta, outwardA));
		float maxOutwardOffset = Mathf.Max(0.1f, _settings.MergeSeamGapMeters);
		if (outwardOffset > maxOutwardOffset)
			return false;

		float signedFromA = Vector3.Dot(delta, normalA);
		float signedFromB = Vector3.Dot(-delta, normalB);
		if (signedFromA > -0.03f || signedFromB > -0.03f)
			return false;

		float thickness = Mathf.Abs(signedFromA);
		return thickness <= maxThickness &&
		       Mathf.Abs(thickness - _distance) <= maxOutwardOffset;
	}

	private static CoverBoundarySeed BuildPhysicalBand(CoverBoundarySide _a, CoverBoundarySide _b)
	{
		Vector3 delta = _b.Center - _a.Center;
		delta.y = 0f;
		Vector3 axis = PlanarUnit(delta);
		Vector3 outward = PlanarUnit(_a.Outward + _b.Outward);
		Vector3 center = (_a.Center + _b.Center) * 0.5f;
		center.y = 0f;
		return new CoverBoundarySeed
		{
			Center = center,
			Axis = axis,
			Outward = outward,
			Width = Mathf.Max(0.1f, Mathf.Abs(Vector3.Dot(delta, PlanarUnit(_a.SurfaceNormal)))),
			Depth = Mathf.Max(_a.Range, _b.Range),
			Height = Mathf.Max(_a.Height, _b.Height),
			Kind = ProtectionEdgeKind.WallEnd,
			IsPhysicalBand = true
		};
	}

	private static CoverBoundarySeed BuildOneSidedBand(
		CoverBoundarySide _side,
		CoverGenerationSettings _settings)
	{
		Vector3 center = _side.Center;
		center.y = 0f;
		return new CoverBoundarySeed
		{
			Center = center,
			Axis = PlanarUnit(_side.SurfaceNormal),
			Outward = PlanarUnit(_side.Outward),
			Width = Mathf.Max(0.35f, _settings.ZoneDepthMeters),
			Depth = Mathf.Max(0.35f, _side.Range),
			Height = _side.Height,
			Kind = ProtectionEdgeKind.WallEnd,
			IsPhysicalBand = false
		};
	}

	private static bool IsPartOfPhysicalEndCap(
		CoverBoundarySide _side,
		IReadOnlyList<CoverBoundarySeed> _bands,
		CoverGenerationSettings _settings)
	{
		Vector3 sideOutward = PlanarUnit(_side.Outward);
		float slack = Mathf.Max(0.1f, _settings.MergeSeamGapMeters);
		for (int i = 0; i < _bands.Count; i++)
		{
			CoverBoundarySeed band = _bands[i];
			if (!band.IsPhysicalBand)
				continue;
			Vector3 bandOutward = PlanarUnit(band.Outward);
			if (Mathf.Abs(Vector3.Dot(sideOutward, bandOutward)) > 0.5f)
				continue;

			Vector3 delta = _side.Center - band.Center;
			delta.y = 0f;
			float along = Mathf.Abs(Vector3.Dot(delta, PlanarUnit(band.Axis)));
			float ahead = Mathf.Abs(Vector3.Dot(delta, bandOutward));
			if (along <= band.Width * 0.5f + slack && ahead <= slack)
				return true;
		}

		return false;
	}

	private static int CompareSide(CoverBoundarySide _a, CoverBoundarySide _b)
	{
		int x = _a.Center.x.CompareTo(_b.Center.x);
		if (x != 0)
			return x;
		int z = _a.Center.z.CompareTo(_b.Center.z);
		if (z != 0)
			return z;
		int ox = _a.Outward.x.CompareTo(_b.Outward.x);
		if (ox != 0)
			return ox;
		return _a.Outward.z.CompareTo(_b.Outward.z);
	}

	private static int CompareSeed(CoverBoundarySeed _a, CoverBoundarySeed _b)
	{
		int x = _a.Center.x.CompareTo(_b.Center.x);
		if (x != 0)
			return x;
		int z = _a.Center.z.CompareTo(_b.Center.z);
		if (z != 0)
			return z;
		return _b.IsPhysicalBand.CompareTo(_a.IsPhysicalBand);
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 value = _value;
		value.y = 0f;
		return value.sqrMagnitude < 0.01f ? Vector3.zero : value.normalized;
	}
	#endregion
}
