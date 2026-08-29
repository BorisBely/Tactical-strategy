using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.2B.2 Opening seed from two collinear surfaces and a walkable gap.
/// One seed per passage. Not peek / step / fire. Width knobs are prototype, not freeze.
/// </summary>
public struct CoverOpeningSeed
{
	public Vector3 Center;
	public Vector3 Normal;
	public Vector3 Axis;
	public float Width;
	public float LeftOffset;
	public float RightOffset;
}

/// <summary>
/// Finds open gaps between already extracted <see cref="CoverGeometrySurface"/>s.
/// Does not scan the scene. Does not move units.
/// </summary>
public static class CoverOpeningGeometry
{
	#region Public Methods
	public static void Collect(
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		List<CoverOpeningSeed> _destination)
	{
		if (_destination == null)
			return;
		_destination.Clear();
		if (_surfaces == null || _surfaces.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float minWidth = Mathf.Max(0.05f, settings.MinOpeningWidthMeters);
		float maxWidth = Mathf.Max(minWidth, settings.MaxOpeningWidthMeters);
		float maxPlane = Mathf.Max(0.05f, settings.MaxOpeningPlaneOffsetMeters);
		float align = Mathf.Clamp(settings.OpeningNormalAlignDot, 0.5f, 0.99f);
		float minLength = Mathf.Max(0.05f, settings.MinOpeningSurfaceLengthMeters);

		for (int i = 0; i < _surfaces.Count; i++)
		{
			CoverGeometrySurface a = _surfaces[i];
			if (a.Length < minLength || !a.TryGetPlanarEnds(out Vector3 aStart, out Vector3 aEnd))
				continue;
			Vector3 normalA = PlanarUnit(a.Normal);
			if (normalA.sqrMagnitude < 0.5f)
				continue;

			for (int j = i + 1; j < _surfaces.Count; j++)
			{
				CoverGeometrySurface b = _surfaces[j];
				if (b.Length < minLength || !b.TryGetPlanarEnds(out Vector3 bStart, out Vector3 bEnd))
					continue;
				Vector3 normalB = PlanarUnit(b.Normal);
				if (normalB.sqrMagnitude < 0.5f)
					continue;
				if (Vector3.Dot(normalA, normalB) < align)
					continue;

				Vector3 normal = PlanarUnit(normalA + normalB);
				if (normal.sqrMagnitude < 0.5f)
					continue;
				if (!TryGap(
					    aStart,
					    aEnd,
					    bStart,
					    bEnd,
					    a.Length,
					    b.Length,
					    normal,
					    minWidth,
					    maxWidth,
					    maxPlane,
					    out CoverOpeningSeed seed))
					continue;
				_destination.Add(seed);
			}
		}

		Cluster(_destination, settings.DedupRadiusMeters);
	}

	/// <summary>
	/// Joins the two opposite collider faces of one physical passage.
	/// Surface cutting still uses the original side seeds; bake emits this list once.
	/// </summary>
	public static void CollapsePhysical(
		IReadOnlyList<CoverOpeningSeed> _sideSeeds,
		CoverGenerationSettings _settings,
		List<CoverOpeningSeed> _destination)
	{
		if (_destination == null)
			return;
		_destination.Clear();
		if (_sideSeeds == null || _sideSeeds.Count == 0)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		var consumed = new bool[_sideSeeds.Count];
		for (int i = 0; i < _sideSeeds.Count; i++)
		{
			if (consumed[i])
				continue;

			CoverOpeningSeed a = _sideSeeds[i];
			int best = -1;
			float bestSeparation = float.MaxValue;
			for (int j = i + 1; j < _sideSeeds.Count; j++)
			{
				if (consumed[j])
					continue;
				if (!CanPairOppositeFaces(a, _sideSeeds[j], settings, out float separation))
					continue;
				if (separation >= bestSeparation)
					continue;
				best = j;
				bestSeparation = separation;
			}

			consumed[i] = true;
			if (best < 0)
			{
				_destination.Add(Canonicalize(a));
				continue;
			}

			consumed[best] = true;
			CoverOpeningSeed b = _sideSeeds[best];
			Vector3 axis = CanonicalDirection(PlanarUnit(a.Axis));
			Vector3 normal = CanonicalDirection(PlanarUnit(a.Normal));
			float width = 0.5f * (a.Width + b.Width);
			_destination.Add(new CoverOpeningSeed
			{
				Center = Flatten((a.Center + b.Center) * 0.5f),
				Normal = normal,
				Axis = axis,
				Width = width,
				LeftOffset = width * 0.5f,
				RightOffset = width * 0.5f
			});
		}
	}

	public static Vector3 StandPosition(in CoverOpeningSeed _seed, float _standoffMeters)
	{
		Vector3 n = PlanarUnit(_seed.Normal);
		float standoff = Mathf.Max(0.05f, _standoffMeters);
		Vector3 pos = _seed.Center + n * standoff;
		pos.y = 0f;
		return pos;
	}

	public static void ApplySeed(CoverCandidate _candidate, in CoverOpeningSeed _seed)
	{
		if (_candidate == null)
			return;
		_candidate.OpeningSeed = true;
		_candidate.OpeningValid = false;
		_candidate.OpeningCenter = _seed.Center;
		_candidate.OpeningAxis = _seed.Axis;
		_candidate.OpeningWidth = _seed.Width;
		_candidate.LeftOffset = _seed.LeftOffset;
		_candidate.RightOffset = _seed.RightOffset;
	}

	public static void TagOpenings(List<CoverCandidate> _candidates, CoverGenerationSettings _settings)
	{
		if (_candidates == null)
			return;
		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float minWidth = Mathf.Max(0.05f, settings.MinOpeningWidthMeters);
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null)
				continue;
			if (!candidate.OpeningSeed && candidate.OpeningWidth < minWidth)
				continue;

			candidate.OpeningValid = true;
			candidate.Capabilities |=
				CoverCapabilities.CanStepLeft |
				CoverCapabilities.CanStepRight |
				CoverCapabilities.CanOpen |
				CoverCapabilities.CanClose;
			if (candidate.StandingValid)
				candidate.Capabilities |= CoverCapabilities.CanStand;
			if (candidate.CrouchValid)
				candidate.Capabilities |= CoverCapabilities.CanCrouch;
			candidate.CoverType = CoverClassifier.ResolveType(candidate);
		}
	}

	public static void AbsorbPassageEdges(
		List<CoverCandidate> _candidates,
		CoverGenerationSettings _settings)
	{
		if (_candidates == null || _candidates.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float slack = Mathf.Max(0.35f, settings.EdgeInsetMeters) * 2f + 0.2f;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate opening = _candidates[i];
			if (opening == null || !opening.OpeningValid || opening.OpeningWidth < 0.05f)
				continue;

			for (int j = _candidates.Count - 1; j >= 0; j--)
			{
				if (j == i)
					continue;
				CoverCandidate other = _candidates[j];
				if (other == null || other.OpeningValid)
					continue;
				if (!other.EdgeValid && !other.EdgeSeed)
					continue;
				if (!IsOnPassage(opening, other, slack))
					continue;
				CoverSpatialReduce.MergeGeometryFlags(opening, other);
				opening.CoverType = CoverClassifier.ResolveType(opening);
				_candidates.RemoveAt(j);
				if (j < i)
					i--;
			}
		}
	}
	#endregion

	#region Private Methods
	private static bool CanPairOppositeFaces(
		CoverOpeningSeed _a,
		CoverOpeningSeed _b,
		CoverGenerationSettings _settings,
		out float _separation)
	{
		_separation = float.MaxValue;
		Vector3 normalA = PlanarUnit(_a.Normal);
		Vector3 normalB = PlanarUnit(_b.Normal);
		Vector3 axisA = PlanarUnit(_a.Axis);
		Vector3 axisB = PlanarUnit(_b.Axis);
		if (normalA.sqrMagnitude < 0.5f || normalB.sqrMagnitude < 0.5f ||
		    axisA.sqrMagnitude < 0.5f || axisB.sqrMagnitude < 0.5f)
			return false;

		float align = Mathf.Clamp(_settings.OpeningNormalAlignDot, 0.5f, 0.99f);
		if (Vector3.Dot(normalA, normalB) > -align)
			return false;
		if (Mathf.Abs(Vector3.Dot(axisA, axisB)) < align)
			return false;

		float widthSlack = Mathf.Max(0.25f, Mathf.Min(_a.Width, _b.Width) * 0.2f);
		if (Mathf.Abs(_a.Width - _b.Width) > widthSlack)
			return false;

		Vector3 delta = Flatten(_b.Center - _a.Center);
		float along = Mathf.Abs(Vector3.Dot(delta, axisA));
		if (along > widthSlack)
			return false;

		// Opposing faces of one solid wall point away from each other.
		float signedThickness = Vector3.Dot(delta, normalA);
		float maxThickness = Mathf.Max(0.1f, _settings.MaxOpeningWallThicknessMeters);
		if (signedThickness >= -0.05f || -signedThickness > maxThickness)
			return false;

		Vector3 residual = delta - axisA * Vector3.Dot(delta, axisA) -
		                   normalA * signedThickness;
		if (residual.sqrMagnitude > 0.04f)
			return false;

		_separation = -signedThickness;
		return true;
	}

	private static CoverOpeningSeed Canonicalize(CoverOpeningSeed _seed)
	{
		_seed.Center = Flatten(_seed.Center);
		_seed.Axis = CanonicalDirection(PlanarUnit(_seed.Axis));
		_seed.Normal = CanonicalDirection(PlanarUnit(_seed.Normal));
		return _seed;
	}

	private static Vector3 CanonicalDirection(Vector3 _direction)
	{
		Vector3 direction = PlanarUnit(_direction);
		if (direction.x < -0.001f ||
		    (Mathf.Abs(direction.x) <= 0.001f && direction.z < 0f))
			direction = -direction;
		return direction;
	}

	private static Vector3 Flatten(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}

	private static bool TryGap(
		Vector3 _aStart,
		Vector3 _aEnd,
		Vector3 _bStart,
		Vector3 _bEnd,
		float _aLength,
		float _bLength,
		Vector3 _normal,
		float _minWidth,
		float _maxWidth,
		float _maxPlane,
		out CoverOpeningSeed _seed)
	{
		_seed = default;
		_ = _aLength;
		_ = _bLength;
		Vector3 axis = Vector3.Cross(Vector3.up, _normal);
		if (axis.sqrMagnitude < 0.01f)
			return false;
		axis.Normalize();

		IntervalAlong(axis, _aStart, _aEnd, out float aMin, out float aMax);
		IntervalAlong(axis, _bStart, _bEnd, out float bMin, out float bMax);
		float planeA = 0.5f * (Vector3.Dot(_aStart, _normal) + Vector3.Dot(_aEnd, _normal));
		float planeB = 0.5f * (Vector3.Dot(_bStart, _normal) + Vector3.Dot(_bEnd, _normal));
		if (Mathf.Abs(planeA - planeB) > _maxPlane)
			return false;

		float gap;
		float leftT;
		float rightT;
		if (aMax <= bMin)
		{
			gap = bMin - aMax;
			leftT = aMax;
			rightT = bMin;
		}
		else if (bMax <= aMin)
		{
			gap = aMin - bMax;
			leftT = bMax;
			rightT = aMin;
		}
		else
			return false;

		if (gap < _minWidth || gap > _maxWidth)
			return false;

		float plane = 0.5f * (planeA + planeB);
		float midT = 0.5f * (leftT + rightT);
		Vector3 center = axis * midT + _normal * plane;
		center.y = 0f;
		_seed = new CoverOpeningSeed
		{
			Center = center,
			Normal = _normal,
			Axis = axis,
			Width = gap,
			LeftOffset = midT - leftT,
			RightOffset = rightT - midT
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

	private static void Cluster(List<CoverOpeningSeed> _seeds, float _radiusMeters)
	{
		if (_seeds == null || _seeds.Count <= 1)
			return;

		_seeds.Sort(CompareSeed);
		float radiusSqr = Mathf.Max(0.05f, _radiusMeters) * Mathf.Max(0.05f, _radiusMeters);
		var kept = new List<CoverOpeningSeed>(_seeds.Count);
		for (int i = 0; i < _seeds.Count; i++)
		{
			CoverOpeningSeed seed = _seeds[i];
			bool merged = false;
			for (int k = 0; k < kept.Count; k++)
			{
				CoverOpeningSeed other = kept[k];
				if (CoverSpatialMath.PlanarDistanceSqr(seed.Center, other.Center) > radiusSqr)
					continue;
				if (Vector3.Dot(seed.Normal, other.Normal) <= 0.5f)
					continue;
				if (seed.Width > other.Width)
					kept[k] = seed;
				merged = true;
				break;
			}

			if (!merged)
				kept.Add(seed);
		}

		_seeds.Clear();
		_seeds.AddRange(kept);
	}

	private static int CompareSeed(CoverOpeningSeed _a, CoverOpeningSeed _b)
	{
		int x = _a.Center.x.CompareTo(_b.Center.x);
		if (x != 0)
			return x;
		int z = _a.Center.z.CompareTo(_b.Center.z);
		if (z != 0)
			return z;
		return _a.Width.CompareTo(_b.Width);
	}

	private static bool IsOnPassage(CoverCandidate _opening, CoverCandidate _other, float _slack)
	{
		if (Vector3.Dot(_opening.Normal, _other.Normal) <= 0.5f)
			return false;
		Vector3 axis = _opening.OpeningAxis;
		axis.y = 0f;
		if (axis.sqrMagnitude < 0.01f)
			return false;
		axis.Normalize();
		Vector3 delta = _other.Position - _opening.OpeningCenter;
		delta.y = 0f;
		float along = Mathf.Abs(Vector3.Dot(delta, axis));
		return along <= _opening.OpeningWidth * 0.5f + _slack;
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}
