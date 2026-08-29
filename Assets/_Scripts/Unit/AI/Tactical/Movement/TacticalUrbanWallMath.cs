using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.3 wall corridor + urban context. No per-candidate raycasts.
/// Uses #13 cover positions/normals or explicit wall anchors.
/// </summary>
public static class TacticalUrbanWallMath
{
	#region Constants
	public const float CoverWallInsetMeters = 0.45f;
	public const float TooCloseMeters = 0.15f;
	public const float PreferredMinMeters = 0.4f;
	public const float PreferredMaxMeters = 2.5f;
	public const float PreferredPeakMeters = 1.5f;
	public const float TooFarMeters = 7f;
	public const int DensityCap = 8;
	#endregion

	#region Public Methods
	public static TacticalUrbanGeometryContext BuildContext(IReadOnlyList<TacticalWallAnchor> _anchors)
	{
		int count = CountValid(_anchors);
		return new TacticalUrbanGeometryContext
		{
			Present = count > 0,
			AnchorCount = count,
			BuildingDensity01 = Mathf.Clamp01(count / (float)DensityCap)
		};
	}

	public static void CollectAnchors(
		in TacticalRouteSituation _situation,
		List<TacticalWallAnchor> _destination)
	{
		_destination.Clear();
		if (_situation.WallAnchors != null && _situation.WallAnchors.Count > 0)
		{
			for (int i = 0; i < _situation.WallAnchors.Count; i++)
				AddUnique(_destination, _situation.WallAnchors[i]);
			return;
		}

		if (_situation.CoverCandidates != null)
		{
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
				AddFromCover(_destination, _situation.CoverCandidates[i]);
		}

		if (_destination.Count > 0 || _situation.CoverCache == null)
			return;
		AddFromCovers(_destination, _situation.CoverCache.GetCandidates(_situation.Origin));
		AddFromCovers(_destination, _situation.CoverCache.GetCandidates(_situation.Destination));
		AddFromCovers(
			_destination,
			_situation.CoverCache.GetCandidates(
				Vector3.Lerp(_situation.Origin, _situation.Destination, 0.5f)));
	}

	public static float WallDistanceMeters(Vector3 _point, in TacticalWallAnchor _anchor)
	{
		Vector3 normal = TacticalWallAnchor.Flatten(_anchor.Normal);
		if (normal.sqrMagnitude < 0.01f)
			return TooFarMeters;
		normal.Normalize();
		Vector3 delta = _point - _anchor.Position;
		delta.y = 0f;
		return Mathf.Abs(Vector3.Dot(delta, normal));
	}

	public static float NearestWallMeters(Vector3 _point, IReadOnlyList<TacticalWallAnchor> _anchors)
	{
		float best = TooFarMeters;
		if (_anchors == null)
			return best;
		for (int i = 0; i < _anchors.Count; i++)
		{
			if (!IsValid(_anchors[i]))
				continue;
			float meters = WallDistanceMeters(_point, _anchors[i]);
			if (meters < best)
				best = meters;
		}

		return best;
	}

	public static float CorridorProximity01(float _wallMeters)
	{
		float meters = Mathf.Max(0f, _wallMeters);
		if (meters <= TooCloseMeters)
			return Mathf.Lerp(0.2f, 0.45f, meters / TooCloseMeters);
		if (meters < PreferredMinMeters)
		{
			float t = (meters - TooCloseMeters) / (PreferredMinMeters - TooCloseMeters);
			return Mathf.Lerp(0.45f, 1f, t);
		}

		if (meters <= PreferredMaxMeters)
			return 1f;
		if (meters >= TooFarMeters)
			return 0f;
		return Mathf.InverseLerp(TooFarMeters, PreferredMaxMeters, meters);
	}

	public static TacticalUrbanRouteSample SampleRoute(
		TacticalRouteCandidate _candidate,
		IReadOnlyList<TacticalWallAnchor> _anchors)
	{
		if (_candidate == null || CountValid(_anchors) == 0)
			return default;
		int samples = 0;
		float proximity = 0f;
		float meters = 0f;
		Accumulate(_candidate.Origin, _anchors, ref proximity, ref meters, ref samples);
		if (_candidate.Intermediates != null)
		{
			Vector3 previous = _candidate.Origin;
			for (int i = 0; i < _candidate.Intermediates.Count; i++)
			{
				Vector3 hop = _candidate.Intermediates[i].Position;
				Accumulate(Vector3.Lerp(previous, hop, 0.5f), _anchors, ref proximity, ref meters, ref samples);
				Accumulate(hop, _anchors, ref proximity, ref meters, ref samples);
				previous = hop;
			}

			Accumulate(
				Vector3.Lerp(previous, _candidate.Destination, 0.5f),
				_anchors,
				ref proximity,
				ref meters,
				ref samples);
		}
		else
		{
			Accumulate(
				Vector3.Lerp(_candidate.Origin, _candidate.Destination, 0.5f),
				_anchors,
				ref proximity,
				ref meters,
				ref samples);
		}

		Accumulate(_candidate.Destination, _anchors, ref proximity, ref meters, ref samples);
		if (samples <= 0)
			return default;
		float wall = Mathf.Clamp01(proximity / samples);
		return new TacticalUrbanRouteSample
		{
			WallProximity01 = wall,
			OpenExposure01 = 1f - wall,
			MeanWallMeters = meters / samples
		};
	}

	public static bool TryProjectCorridorHop(
		in TacticalWallAnchor _anchor,
		Vector3 _origin,
		Vector3 _destination,
		out Vector3 _hop)
	{
		_hop = default;
		Vector3 normal = TacticalWallAnchor.Flatten(_anchor.Normal);
		if (normal.sqrMagnitude < 0.01f)
			return false;
		normal.Normalize();
		Vector3 mid = Vector3.Lerp(_origin, _destination, 0.5f);
		Vector3 delta = mid - _anchor.Position;
		delta.y = 0f;
		float signed = Vector3.Dot(delta, normal);
		Vector3 onWall = mid - normal * signed;
		_hop = onWall + normal * PreferredPeakMeters;
		_hop.y = _origin.y;
		Vector3 forward = _destination - _origin;
		forward.y = 0f;
		float path = forward.magnitude;
		if (path < 2f)
			return false;
		forward /= path;
		float along = Vector3.Dot(_hop - _origin, forward);
		if (along < 1f || along > path - 1f)
			return false;
		if (CoverSpatialMath.PlanarDistanceSqr(_hop, _origin) < 1f)
			return false;
		if (CoverSpatialMath.PlanarDistanceSqr(_hop, _destination) < 1f)
			return false;
		return true;
	}

	public static bool IsValid(in TacticalWallAnchor _anchor)
	{
		return TacticalWallAnchor.Flatten(_anchor.Normal).sqrMagnitude >= 0.01f;
	}
	#endregion

	#region Private Methods
	private static int CountValid(IReadOnlyList<TacticalWallAnchor> _anchors)
	{
		if (_anchors == null)
			return 0;
		int count = 0;
		for (int i = 0; i < _anchors.Count; i++)
		{
			if (IsValid(_anchors[i]))
				count++;
		}

		return count;
	}

	private static void AddFromCovers(
		List<TacticalWallAnchor> _destination,
		IReadOnlyList<CoverCandidate> _covers)
	{
		if (_covers == null)
			return;
		for (int i = 0; i < _covers.Count; i++)
			AddFromCover(_destination, _covers[i]);
	}

	private static void AddFromCover(List<TacticalWallAnchor> _destination, CoverCandidate _cover)
	{
		TacticalWallAnchor anchor = TacticalWallAnchor.FromCover(_cover, CoverWallInsetMeters);
		AddUnique(_destination, anchor);
	}

	private static void AddUnique(List<TacticalWallAnchor> _destination, in TacticalWallAnchor _anchor)
	{
		if (_destination == null || !IsValid(in _anchor))
			return;
		for (int i = 0; i < _destination.Count; i++)
		{
			if (CoverSpatialMath.PlanarDistanceSqr(_destination[i].Position, _anchor.Position) < 0.36f)
				return;
		}

		_destination.Add(_anchor);
	}

	private static void Accumulate(
		Vector3 _point,
		IReadOnlyList<TacticalWallAnchor> _anchors,
		ref float _proximity,
		ref float _meters,
		ref int _samples)
	{
		float meters = NearestWallMeters(_point, _anchors);
		_proximity += CorridorProximity01(meters);
		_meters += meters;
		_samples++;
	}
	#endregion
}
