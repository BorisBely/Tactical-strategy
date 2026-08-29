using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two-surface corner geometry plus a legacy candidate stand probe.
/// ProtectionGeometry serializes Vertex and wall directions, not Position.
/// </summary>
public struct CoverCornerSeed
{
	public Vector3 Vertex;
	public Vector3 Position;
	public Vector3 Facing;
	public Vector3 NormalA;
	public Vector3 NormalB;
	public Vector3 DirectionA;
	public Vector3 DirectionB;
	public float Height;
	public CoverCornerOrientation Orientation;
}

/// <summary>
/// #13.2B.4 Corner from already extracted surfaces. Not a world rescan. Not unit behavior.
/// </summary>
public static class CoverCornerGeometry
{
	#region Public Methods
	public static void Collect(
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		List<CoverCornerSeed> _destination)
	{
		if (_destination == null)
			return;
		_destination.Clear();
		if (_surfaces == null || _surfaces.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float minLength = Mathf.Max(0.05f, settings.MinCornerSurfaceLengthMeters);
		float maxVertex = Mathf.Max(0.05f, settings.MaxCornerVertexSeparationMeters);
		float maxAlign = Mathf.Clamp(settings.CornerNormalMaxAlignDot, 0.2f, 0.95f);
		float standoff = Mathf.Max(0.05f, settings.StandOffMeters);
		float vertexSqr = maxVertex * maxVertex;

		for (int i = 0; i < _surfaces.Count; i++)
		{
			CoverGeometrySurface a = _surfaces[i];
			if (a.Length < minLength || !a.TryGetPlanarEnds(out Vector3 aStart, out Vector3 aEnd))
				continue;
			Vector3 nA = PlanarUnit(a.Normal);
			if (nA.sqrMagnitude < 0.5f)
				continue;

			for (int j = i + 1; j < _surfaces.Count; j++)
			{
				CoverGeometrySurface b = _surfaces[j];
				if (b.Length < minLength || !b.TryGetPlanarEnds(out Vector3 bStart, out Vector3 bEnd))
					continue;
				Vector3 nB = PlanarUnit(b.Normal);
				if (nB.sqrMagnitude < 0.5f)
					continue;
				if (Mathf.Abs(Vector3.Dot(nA, nB)) > maxAlign)
					continue;
				if (!TryClosestEnds(aStart, aEnd, bStart, bEnd, vertexSqr, out Vector3 endA, out Vector3 endB))
					continue;

				Vector3 vertex = (endA + endB) * 0.5f;
				vertex.y = 0f;
				Vector3 facing = PlanarUnit(nA + nB);
				if (facing.sqrMagnitude < 0.5f)
					continue;

				Vector3 farA = FarEnd(aStart, aEnd, endA);
				Vector3 farB = FarEnd(bStart, bEnd, endB);
				Vector3 wallDirA = PlanarUnit(farA - vertex);
				Vector3 wallDirB = PlanarUnit(farB - vertex);
				float extend = Vector3.Dot(wallDirA, facing) + Vector3.Dot(wallDirB, facing);
				CoverCornerOrientation orientation = extend >= 0f
					? CoverCornerOrientation.Inner
					: CoverCornerOrientation.Outer;

				Vector3 position = StandPosition(vertex, facing, standoff);
				if (Vector3.Dot(PlanarUnit(position - a.Origin), nA) < 0.05f)
					continue;
				if (Vector3.Dot(PlanarUnit(position - b.Origin), nB) < 0.05f)
					continue;
				if (!HasOpenFront(position, facing, a, b, _surfaces, settings))
					continue;

				_destination.Add(new CoverCornerSeed
				{
					Vertex = vertex,
					Position = position,
					Facing = facing,
					NormalA = nA,
					NormalB = nB,
					DirectionA = wallDirA,
					DirectionB = wallDirB,
					Height = Mathf.Min(a.Height, b.Height),
					Orientation = orientation
				});
			}
		}

		Cluster(_destination, settings.DedupRadiusMeters);
	}

	/// <summary>
	/// Collects only protected inner pockets for #13.2C ProtectionGeometry.
	/// Legacy #13.2B candidate corners keep using <see cref="Collect"/>.
	/// </summary>
	public static void CollectProtected(
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		List<CoverCornerSeed> _destination)
	{
		if (_destination == null)
			return;
		_destination.Clear();
		if (_surfaces == null || _surfaces.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float minSurfaceLength = Mathf.Max(0.05f, settings.MinCornerSurfaceLengthMeters);
		float minArmLength = Mathf.Max(minSurfaceLength, settings.MinProtectedCornerArmLengthMeters);
		float minHeight = Mathf.Max(0.05f, settings.MinProtectedCornerHeightMeters);
		float connectionSlack = Mathf.Max(0.05f, settings.MaxCornerVertexSeparationMeters);
		float maxAlign = Mathf.Clamp(settings.CornerNormalMaxAlignDot, 0.2f, 0.95f);
		float minFacingDot = Mathf.Clamp(settings.MinProtectedCornerFacingDot, 0.05f, 0.7f);
		float standoff = Mathf.Max(0.05f, settings.StandOffMeters);

		for (int i = 0; i < _surfaces.Count; i++)
		{
			CoverGeometrySurface a = _surfaces[i];
			if (a.Length < minSurfaceLength || a.Height < minHeight ||
			    !a.TryGetPlanarEnds(out Vector3 aStart, out Vector3 aEnd))
				continue;
			Vector3 nA = PlanarUnit(a.Normal);
			if (nA.sqrMagnitude < 0.5f)
				continue;

			for (int j = i + 1; j < _surfaces.Count; j++)
			{
				CoverGeometrySurface b = _surfaces[j];
				if (b.Length < minSurfaceLength || b.Height < minHeight ||
				    !b.TryGetPlanarEnds(out Vector3 bStart, out Vector3 bEnd))
					continue;
				Vector3 nB = PlanarUnit(b.Normal);
				if (nB.sqrMagnitude < 0.5f ||
				    Mathf.Abs(Vector3.Dot(nA, nB)) > maxAlign)
					continue;

				Vector3 facing = PlanarUnit(nA + nB);
				if (facing.sqrMagnitude < 0.5f)
					continue;
				if (!TryProtectedJunction(
					    aStart,
					    aEnd,
					    bStart,
					    bEnd,
					    facing,
					    connectionSlack,
					    minArmLength,
					    minFacingDot,
					    out Vector3 vertex,
					    out Vector3 directionA,
					    out Vector3 directionB))
					continue;

				Vector3 position = StandPosition(vertex, facing, standoff);
				if (!HasOpenFront(position, facing, a, b, _surfaces, settings))
					continue;

				_destination.Add(new CoverCornerSeed
				{
					Vertex = vertex,
					Position = position,
					Facing = facing,
					NormalA = nA,
					NormalB = nB,
					DirectionA = directionA,
					DirectionB = directionB,
					Height = Mathf.Min(a.Height, b.Height),
					Orientation = CoverCornerOrientation.Inner
				});
			}
		}

		Cluster(_destination, settings.DedupRadiusMeters);
	}

	public static Vector3 StandPosition(Vector3 _vertex, Vector3 _facing, float _standoffMeters)
	{
		Vector3 facing = PlanarUnit(_facing);
		float standoff = Mathf.Max(0.05f, _standoffMeters);
		Vector3 pos = _vertex + facing * (standoff * 1.41421356f);
		pos.y = 0f;
		return pos;
	}

	public static void ApplySeed(CoverCandidate _candidate, in CoverCornerSeed _seed)
	{
		if (_candidate == null)
			return;
		_candidate.CornerSeed = true;
		_candidate.CornerFacing = _seed.Facing;
		_candidate.CornerNormalA = _seed.NormalA;
		_candidate.CornerNormalB = _seed.NormalB;
		_candidate.CornerVertex = _seed.Vertex;
		_candidate.CornerOrientation = _seed.Orientation;
		_candidate.Normal = _seed.Facing;
	}

	public static void TagCorners(
		List<CoverCandidate> _candidates,
		IReadOnlyList<CoverCornerSeed> _seeds,
		CoverGenerationSettings _settings)
	{
		if (_candidates == null || _seeds == null || _seeds.Count == 0)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float proximity = Mathf.Max(0.35f, settings.StandOffMeters + 0.4f);
		float proximitySqr = proximity * proximity;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null || !candidate.CornerSeed)
				continue;
			if (candidate.OpeningValid || candidate.WindowValid)
				continue;
			if (!TryMatchSeed(candidate, _seeds, proximitySqr, out CoverCornerSeed seed))
			{
				if (candidate.CornerFacing.sqrMagnitude < 0.01f)
					continue;
				seed = new CoverCornerSeed
				{
					Vertex = candidate.CornerVertex,
					Position = candidate.Position,
					Facing = candidate.CornerFacing,
					NormalA = candidate.CornerNormalA,
					NormalB = candidate.CornerNormalB,
					Orientation = candidate.CornerOrientation
				};
			}

			candidate.CornerValid = true;
			candidate.CornerSeed = true;
			candidate.CornerFacing = seed.Facing;
			candidate.CornerNormalA = seed.NormalA;
			candidate.CornerNormalB = seed.NormalB;
			candidate.CornerVertex = seed.Vertex;
			candidate.CornerOrientation = seed.Orientation;
			if (candidate.StandingValid)
				candidate.Capabilities |= CoverCapabilities.CanStand;
			if (candidate.CrouchValid)
				candidate.Capabilities |= CoverCapabilities.CanCrouch;
			candidate.CoverType = CoverClassifier.ResolveType(candidate);
		}
	}

	public static void AbsorbCornerEdges(
		List<CoverCandidate> _candidates,
		CoverGenerationSettings _settings)
	{
		if (_candidates == null || _candidates.Count < 2)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float slack = Mathf.Max(0.8f, settings.EdgeEndProximityMeters);
		float slackSqr = slack * slack;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate corner = _candidates[i];
			if (corner == null || !CoverClassifier.HasGeometricCorner(corner))
				continue;

			Vector3 vertex = corner.CornerVertex.sqrMagnitude > 0.01f
				? corner.CornerVertex
				: corner.Position;
			for (int j = _candidates.Count - 1; j >= 0; j--)
			{
				if (j == i)
					continue;
				CoverCandidate other = _candidates[j];
				if (other == null || CoverClassifier.HasGeometricCorner(other))
					continue;
				if (other.OpeningValid || other.WindowValid)
					continue;
				if (!other.EdgeValid && !other.EdgeSeed)
					continue;
				if (CoverSpatialMath.PlanarDistanceSqr(other.Position, vertex) > slackSqr &&
				    CoverSpatialMath.PlanarDistanceSqr(other.Position, corner.Position) > slackSqr)
					continue;
				CoverSpatialReduce.MergeGeometryFlags(corner, other);
				corner.CoverType = CoverClassifier.ResolveType(corner);
				_candidates.RemoveAt(j);
				if (j < i)
					i--;
			}
		}
	}
	#endregion

	#region Private Methods
	private static bool TryProtectedJunction(
		Vector3 _aStart,
		Vector3 _aEnd,
		Vector3 _bStart,
		Vector3 _bEnd,
		Vector3 _facing,
		float _connectionSlack,
		float _minArmLength,
		float _minFacingDot,
		out Vector3 _vertex,
		out Vector3 _directionA,
		out Vector3 _directionB)
	{
		_vertex = Vector3.zero;
		_directionA = Vector3.zero;
		_directionB = Vector3.zero;

		Vector3 deltaA = _aEnd - _aStart;
		Vector3 deltaB = _bEnd - _bStart;
		deltaA.y = 0f;
		deltaB.y = 0f;
		float lengthA = deltaA.magnitude;
		float lengthB = deltaB.magnitude;
		if (lengthA < 0.05f || lengthB < 0.05f)
			return false;

		Vector3 axisA = deltaA / lengthA;
		Vector3 axisB = deltaB / lengthB;
		float denominator = Cross2D(axisA, axisB);
		if (Mathf.Abs(denominator) < 0.001f)
			return false;

		Vector3 offset = _bStart - _aStart;
		float parameterA = Cross2D(offset, axisB) / denominator;
		float parameterB = Cross2D(offset, axisA) / denominator;
		float slack = Mathf.Max(0.01f, _connectionSlack);
		if (parameterA < -slack || parameterA > lengthA + slack ||
		    parameterB < -slack || parameterB > lengthB + slack)
			return false;

		bool aAtEndpoint = IsNearEndpoint(parameterA, lengthA, slack);
		bool bAtEndpoint = IsNearEndpoint(parameterB, lengthB, slack);
		if (!aAtEndpoint && !bAtEndpoint)
			return false;

		if (!TrySelectProtectedArm(
			    axisA,
			    lengthA,
			    parameterA,
			    aAtEndpoint,
			    _facing,
			    _minArmLength,
			    _minFacingDot,
			    out _directionA))
			return false;
		if (!TrySelectProtectedArm(
			    axisB,
			    lengthB,
			    parameterB,
			    bAtEndpoint,
			    _facing,
			    _minArmLength,
			    _minFacingDot,
			    out _directionB))
			return false;

		_vertex = _aStart + axisA * parameterA;
		_vertex.y = 0f;
		return true;
	}

	private static bool TrySelectProtectedArm(
		Vector3 _axis,
		float _length,
		float _parameter,
		bool _atEndpoint,
		Vector3 _facing,
		float _minLength,
		float _minFacingDot,
		out Vector3 _direction)
	{
		_direction = Vector3.zero;
		float positiveLength = Mathf.Max(0f, _length - Mathf.Clamp(_parameter, 0f, _length));
		float negativeLength = Mathf.Max(0f, Mathf.Clamp(_parameter, 0f, _length));

		if (_atEndpoint)
		{
			bool startIsNearer = Mathf.Abs(_parameter) <= Mathf.Abs(_parameter - _length);
			_direction = startIsNearer ? _axis : -_axis;
			float armLength = startIsNearer ? positiveLength : negativeLength;
			return armLength >= _minLength &&
			       Vector3.Dot(_direction, _facing) >= _minFacingDot;
		}

		float positiveDot = Vector3.Dot(_axis, _facing);
		float negativeDot = -positiveDot;
		if (positiveDot >= negativeDot)
		{
			_direction = _axis;
			return positiveLength >= _minLength && positiveDot >= _minFacingDot;
		}

		_direction = -_axis;
		return negativeLength >= _minLength && negativeDot >= _minFacingDot;
	}

	private static bool IsNearEndpoint(float _parameter, float _length, float _slack)
	{
		return Mathf.Abs(_parameter) <= _slack ||
		       Mathf.Abs(_parameter - _length) <= _slack;
	}

	private static float Cross2D(Vector3 _a, Vector3 _b)
	{
		return _a.x * _b.z - _a.z * _b.x;
	}

	private static bool TryMatchSeed(
		CoverCandidate _candidate,
		IReadOnlyList<CoverCornerSeed> _seeds,
		float _proximitySqr,
		out CoverCornerSeed _seed)
	{
		_seed = default;
		bool found = false;
		float best = float.MaxValue;
		for (int i = 0; i < _seeds.Count; i++)
		{
			CoverCornerSeed seed = _seeds[i];
			float d = CoverSpatialMath.PlanarDistanceSqr(_candidate.Position, seed.Position);
			if (d > _proximitySqr || d >= best)
				continue;
			best = d;
			_seed = seed;
			found = true;
		}

		return found;
	}

	private static bool TryClosestEnds(
		Vector3 _aStart,
		Vector3 _aEnd,
		Vector3 _bStart,
		Vector3 _bEnd,
		float _maxSqr,
		out Vector3 _endA,
		out Vector3 _endB)
	{
		_endA = _aStart;
		_endB = _bStart;
		float best = CoverSpatialMath.PlanarDistanceSqr(_aStart, _bStart);
		TryCloser(_aStart, _bEnd, ref best, ref _endA, ref _endB);
		TryCloser(_aEnd, _bStart, ref best, ref _endA, ref _endB);
		TryCloser(_aEnd, _bEnd, ref best, ref _endA, ref _endB);
		return best <= _maxSqr;
	}

	private static void TryCloser(
		Vector3 _a,
		Vector3 _b,
		ref float _best,
		ref Vector3 _endA,
		ref Vector3 _endB)
	{
		float d = CoverSpatialMath.PlanarDistanceSqr(_a, _b);
		if (d >= _best)
			return;
		_best = d;
		_endA = _a;
		_endB = _b;
	}

	private static Vector3 FarEnd(Vector3 _start, Vector3 _end, Vector3 _near)
	{
		return CoverSpatialMath.PlanarDistanceSqr(_start, _near) <=
		       CoverSpatialMath.PlanarDistanceSqr(_end, _near)
			? _end
			: _start;
	}

	private static bool HasOpenFront(
		Vector3 _position,
		Vector3 _facing,
		CoverGeometrySurface _wallA,
		CoverGeometrySurface _wallB,
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings)
	{
		int rays = Mathf.Max(1, _settings.CornerFanRays);
		int need = Mathf.Clamp(_settings.MinCornerOpenFanRays, 1, rays);
		float half = Mathf.Max(1f, _settings.CornerFanHalfAngleDegrees);
		float maxDist = Mathf.Max(0.5f, _settings.CornerFrontClearMeters);
		int open = 0;
		for (int i = 0; i < rays; i++)
		{
			float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
			float angle = Mathf.Lerp(-half, half, t);
			Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * _facing;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.01f)
				continue;
			dir.Normalize();
			if (FanBlocked(_position, dir, maxDist, _wallA, _wallB, _surfaces))
				continue;
			open++;
		}

		return open >= need;
	}

	private static bool FanBlocked(
		Vector3 _origin,
		Vector3 _dir,
		float _maxDist,
		CoverGeometrySurface _wallA,
		CoverGeometrySurface _wallB,
		IReadOnlyList<CoverGeometrySurface> _surfaces)
	{
		for (int i = 0; i < _surfaces.Count; i++)
		{
			CoverGeometrySurface surface = _surfaces[i];
			if (SameSurface(surface, _wallA) || SameSurface(surface, _wallB))
				continue;
			if (RayHitsSegment(_origin, _dir, _maxDist, surface))
				return true;
		}

		return false;
	}

	private static bool SameSurface(CoverGeometrySurface _a, CoverGeometrySurface _b)
	{
		return CoverSpatialMath.PlanarDistanceSqr(_a.Origin, _b.Origin) <= 0.04f &&
		       Vector3.Dot(PlanarUnit(_a.Normal), PlanarUnit(_b.Normal)) > 0.9f;
	}

	private static bool RayHitsSegment(
		Vector3 _origin,
		Vector3 _dir,
		float _maxDist,
		CoverGeometrySurface _surface)
	{
		if (!_surface.TryGetPlanarEnds(out Vector3 start, out Vector3 end))
			return false;

		float ax = _dir.x * _maxDist;
		float az = _dir.z * _maxDist;
		float bx = end.x - start.x;
		float bz = end.z - start.z;
		float denom = ax * bz - az * bx;
		if (Mathf.Abs(denom) < 0.0001f)
			return false;

		float dx = start.x - _origin.x;
		float dz = start.z - _origin.z;
		float t = (dx * bz - dz * bx) / denom;
		float u = (dx * az - dz * ax) / denom;
		return t > 0.08f && t < 0.99f && u >= 0f && u <= 1f;
	}

	private static void Cluster(List<CoverCornerSeed> _seeds, float _radiusMeters)
	{
		if (_seeds == null || _seeds.Count <= 1)
			return;

		_seeds.Sort(CompareSeed);
		float radiusSqr = Mathf.Max(0.05f, _radiusMeters) * Mathf.Max(0.05f, _radiusMeters);
		var kept = new List<CoverCornerSeed>(_seeds.Count);
		for (int i = 0; i < _seeds.Count; i++)
		{
			CoverCornerSeed seed = _seeds[i];
			bool merged = false;
			for (int k = 0; k < kept.Count; k++)
			{
				if (CoverSpatialMath.PlanarDistanceSqr(seed.Vertex, kept[k].Vertex) > radiusSqr)
					continue;
				if (Vector3.Dot(seed.Facing, kept[k].Facing) <= 0.5f)
					continue;
				merged = true;
				break;
			}

			if (!merged)
				kept.Add(seed);
		}

		_seeds.Clear();
		_seeds.AddRange(kept);
	}

	private static int CompareSeed(CoverCornerSeed _a, CoverCornerSeed _b)
	{
		int x = _a.Vertex.x.CompareTo(_b.Vertex.x);
		if (x != 0)
			return x;
		int z = _a.Vertex.z.CompareTo(_b.Vertex.z);
		if (z != 0)
			return z;
		return _a.Position.x.CompareTo(_b.Position.x);
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}
