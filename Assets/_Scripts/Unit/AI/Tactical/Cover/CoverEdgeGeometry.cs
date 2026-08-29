using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.2B.1 Edge: hidden base at a surface end. One candidate, no peek points.
/// Does not move units. Does not change CoverScore.
/// </summary>
public static class CoverEdgeGeometry
{
	#region Public Methods
	public static bool SurfaceSupportsEdge(CoverGeometrySurface _surface, float _minLengthMeters)
	{
		return _surface.Length >= Mathf.Max(0.05f, _minLengthMeters) &&
		       _surface.Normal.sqrMagnitude >= 0.01f;
	}

	public static bool IsNearSurfaceEnd(
		Vector3 _position,
		CoverGeometrySurface _surface,
		float _proximityMeters)
	{
		if (!TryEndPoints(_surface, out Vector3 start, out Vector3 end))
			return false;
		float proximity = Mathf.Max(0.05f, _proximityMeters);
		float proximitySqr = proximity * proximity;
		return CoverSpatialMath.PlanarDistanceSqr(_position, start) <= proximitySqr ||
		       CoverSpatialMath.PlanarDistanceSqr(_position, end) <= proximitySqr;
	}

	public static Vector3 EdgeDirection(Vector3 _position, CoverGeometrySurface _surface)
	{
		if (!TryEndPoints(_surface, out Vector3 start, out Vector3 end))
			return Vector3.zero;
		float toStart = CoverSpatialMath.PlanarDistanceSqr(_position, start);
		float toEnd = CoverSpatialMath.PlanarDistanceSqr(_position, end);
		Vector3 target = toStart <= toEnd ? start : end;
		Vector3 dir = target - _position;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.0001f)
		{
			dir = end - start;
			dir.y = 0f;
		}

		return dir.sqrMagnitude < 0.0001f ? Vector3.zero : dir.normalized;
	}

	public static bool IsFullyHiddenBase(CoverCandidate _candidate)
	{
		return _candidate != null && (_candidate.StandingValid || _candidate.CrouchValid);
	}

	public static void TagEdges(
		List<CoverCandidate> _candidates,
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings)
	{
		if (_candidates == null || _surfaces == null)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null || !IsFullyHiddenBase(candidate))
				continue;

			CoverGeometrySurface matched = default;
			bool found = candidate.EdgeSeed;
			if (found)
			{
				found = TryMatchSurface(candidate.Position, _surfaces, settings, out matched);
			}

			if (!found)
			{
				for (int s = 0; s < _surfaces.Count; s++)
				{
					CoverGeometrySurface surface = _surfaces[s];
					if (!SurfaceSupportsEdge(surface, settings.MinEdgeSurfaceLengthMeters))
						continue;
					if (!IsNearSurfaceEnd(candidate.Position, surface, settings.EdgeEndProximityMeters))
						continue;
					matched = surface;
					found = true;
					break;
				}
			}

			if (!found)
				continue;

			candidate.EdgeValid = true;
			candidate.EdgeDirection = EdgeDirection(candidate.Position, matched);
			candidate.Capabilities |= CoverCapabilities.CanPeek;
			if (candidate.StandingValid)
				candidate.Capabilities |= CoverCapabilities.CanStand;
			if (candidate.CrouchValid)
				candidate.Capabilities |= CoverCapabilities.CanCrouch;
			candidate.CoverType = CoverClassifier.ResolveType(candidate);
		}
	}

	public static Vector3 EndSamplePosition(
		CoverGeometrySurface _surface,
		float _standoffMeters,
		float _insetMeters,
		bool _start)
	{
		if (!TryEndPoints(_surface, out Vector3 start, out Vector3 end))
			return _surface.Origin;
		Vector3 along = end - start;
		along.y = 0f;
		float length = along.magnitude;
		if (length < 0.05f)
			return _surface.Origin + _surface.Normal.normalized * _standoffMeters;
		along /= length;
		float inset = Mathf.Clamp(_insetMeters, 0.05f, length * 0.45f);
		Vector3 onSurface = _start ? start + along * inset : end - along * inset;
		Vector3 normal = _surface.Normal;
		normal.y = 0f;
		if (normal.sqrMagnitude > 0.01f)
			normal.Normalize();
		return onSurface + normal * _standoffMeters;
	}
	#endregion

	#region Private Methods
	private static bool TryMatchSurface(
		Vector3 _position,
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		out CoverGeometrySurface _matched)
	{
		_matched = default;
		float best = float.MaxValue;
		bool found = false;
		for (int s = 0; s < _surfaces.Count; s++)
		{
			CoverGeometrySurface surface = _surfaces[s];
			if (!SurfaceSupportsEdge(surface, _settings.MinEdgeSurfaceLengthMeters))
				continue;
			if (!TryEndPoints(surface, out Vector3 start, out Vector3 end))
				continue;
			float d = Mathf.Min(
				CoverSpatialMath.PlanarDistanceSqr(_position, start),
				CoverSpatialMath.PlanarDistanceSqr(_position, end));
			if (d >= best)
				continue;
			best = d;
			_matched = surface;
			found = true;
		}

		return found;
	}

	private static bool TryEndPoints(
		CoverGeometrySurface _surface,
		out Vector3 _start,
		out Vector3 _end)
	{
		return _surface.TryGetPlanarEnds(out _start, out _end);
	}
	#endregion
}
