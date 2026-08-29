using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dedup and cap without tactical quality. Prototype; API of the cache does not depend on this.
/// </summary>
public static class CoverSpatialReduce
{
	#region Constants
	private const float c_NormalAlignDot = 0.5f;
	#endregion

	#region Public Methods
	public static void Deduplicate(List<CoverCandidate> _candidates, float _radiusMeters)
	{
		if (_candidates == null || _candidates.Count <= 1)
			return;

		_candidates.Sort(CompareXz);
		float radius = Mathf.Max(0f, _radiusMeters);
		float radiusSqr = radius * radius;
		var kept = new List<CoverCandidate>(_candidates.Count);
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null)
				continue;

			bool duplicate = false;
			for (int k = 0; k < kept.Count; k++)
			{
				if (IsNearDuplicate(candidate, kept[k], radiusSqr))
				{
					MergeGeometryFlags(kept[k], candidate);
					duplicate = true;
					break;
				}
			}

			if (!duplicate)
				kept.Add(candidate);
		}

		_candidates.Clear();
		_candidates.AddRange(kept);
	}

	public static void ReduceToSpatiallyDiverse(List<CoverCandidate> _candidates, int _maxCount)
	{
		if (_candidates == null)
			return;

		int maxCount = Mathf.Max(1, _maxCount);
		if (_candidates.Count <= maxCount)
			return;

		_candidates.Sort(CompareXz);
		var selected = new List<CoverCandidate>(maxCount);
		var remaining = new List<CoverCandidate>(_candidates);
		selected.Add(remaining[0]);
		remaining.RemoveAt(0);

		while (selected.Count < maxCount && remaining.Count > 0)
		{
			int bestIndex = 0;
			float bestMin = MinPlanarDistanceSqr(remaining[0], selected);
			for (int i = 1; i < remaining.Count; i++)
			{
				float minDist = MinPlanarDistanceSqr(remaining[i], selected);
				bool farther = minDist > bestMin + 0.0001f;
				bool tieBreak = Mathf.Abs(minDist - bestMin) <= 0.0001f &&
				                CompareXz(remaining[i], remaining[bestIndex]) < 0;
				if (farther || tieBreak)
				{
					bestMin = minDist;
					bestIndex = i;
				}
			}

			selected.Add(remaining[bestIndex]);
			remaining.RemoveAt(bestIndex);
		}

		selected.Sort(CompareXz);
		_candidates.Clear();
		_candidates.AddRange(selected);
	}

	public static int CompareXz(CoverCandidate _a, CoverCandidate _b)
	{
		if (ReferenceEquals(_a, _b))
			return 0;
		if (_a == null)
			return 1;
		if (_b == null)
			return -1;

		int x = _a.Position.x.CompareTo(_b.Position.x);
		if (x != 0)
			return x;
		int z = _a.Position.z.CompareTo(_b.Position.z);
		if (z != 0)
			return z;
		return _a.CandidateId.CompareTo(_b.CandidateId);
	}

	public static void MergeGeometryFlags(CoverCandidate _kept, CoverCandidate _incoming)
	{
		if (_kept == null || _incoming == null)
			return;
		_kept.EdgeSeed |= _incoming.EdgeSeed;
		_kept.OpeningSeed |= _incoming.OpeningSeed;
		_kept.EdgeValid |= _incoming.EdgeValid;
		_kept.OpeningValid |= _incoming.OpeningValid;
		_kept.WindowValid |= _incoming.WindowValid;
		_kept.CornerValid |= _incoming.CornerValid;
		_kept.Capabilities |= _incoming.Capabilities;
		if (_incoming.EdgeSeed && _incoming.EdgeDirection.sqrMagnitude > _kept.EdgeDirection.sqrMagnitude)
			_kept.EdgeDirection = _incoming.EdgeDirection;
		if (_incoming.OpeningWidth > _kept.OpeningWidth)
		{
			_kept.OpeningWidth = _incoming.OpeningWidth;
			_kept.OpeningAxis = _incoming.OpeningAxis;
			_kept.OpeningCenter = _incoming.OpeningCenter;
			_kept.LeftOffset = _incoming.LeftOffset;
			_kept.RightOffset = _incoming.RightOffset;
		}

		_kept.HasFrame |= _incoming.HasFrame;
		_kept.HasTransparentPane |= _incoming.HasTransparentPane;
		if (_incoming.WindowWidth > _kept.WindowWidth)
		{
			_kept.WindowWidth = _incoming.WindowWidth;
			_kept.WindowAxis = _incoming.WindowAxis;
			_kept.WindowCenter = _incoming.WindowCenter;
		}

		_kept.CornerSeed |= _incoming.CornerSeed;
		if (_incoming.CornerFacing.sqrMagnitude > _kept.CornerFacing.sqrMagnitude)
		{
			_kept.CornerFacing = _incoming.CornerFacing;
			_kept.CornerNormalA = _incoming.CornerNormalA;
			_kept.CornerNormalB = _incoming.CornerNormalB;
			_kept.CornerVertex = _incoming.CornerVertex;
			_kept.CornerOrientation = _incoming.CornerOrientation;
		}
	}
	#endregion

	#region Private Methods
	private static bool IsNearDuplicate(CoverCandidate _a, CoverCandidate _b, float _radiusSqr)
	{
		if (CoverSpatialMath.PlanarDistanceSqr(_a.Position, _b.Position) > _radiusSqr)
			return false;
		return Vector3.Dot(_a.Normal, _b.Normal) > c_NormalAlignDot;
	}

	private static float MinPlanarDistanceSqr(CoverCandidate _candidate, List<CoverCandidate> _selected)
	{
		float min = float.MaxValue;
		for (int i = 0; i < _selected.Count; i++)
		{
			float d = CoverSpatialMath.PlanarDistanceSqr(_candidate.Position, _selected[i].Position);
			if (d < min)
				min = d;
		}

		return min;
	}
	#endregion
}
