using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.4 bounded exposure profile. No per-sample physics raycast. Does not Move.
/// Unknown threat is conservative, not “safe”.
/// </summary>
public static class TacticalExposureTraversalMath
{
	#region Constants
	public const int DefaultMaxExposureSamples = 8;
	public const float DefaultSampleSpacingMeters = 5f;
	public const float DefaultThreshold01 = 0.6f;
	public const float ExposedFloor01 = 0.25f;
	public const float CoverDashMeters = 4f;
	private const int c_MinSamples = 3;
	private const float c_OccluderRadiusMeters = 1.6f;
	#endregion

	#region Private Fields
	private static readonly List<Vector3> s_Poly = new List<Vector3>(8);
	private static readonly List<float> s_Cum = new List<float>(8);
	private static int s_BuildCount;
	#endregion

	#region Public Properties
	public static int BuildCount => s_BuildCount;
	#endregion

	#region Public Methods
	public static void ResetBuildCount()
	{
		s_BuildCount = 0;
	}

	public static int ResolveSampleCount(float _meters, int _max)
	{
		int cap = Mathf.Max(c_MinSamples, _max);
		int n = Mathf.RoundToInt(_meters / DefaultSampleSpacingMeters) + 1;
		return Mathf.Clamp(n, c_MinSamples, cap);
	}

	public static TacticalExposureRisk Classify(
		float _exposure01,
		float _metersToNextCover)
	{
		float e = Mathf.Clamp01(_exposure01);
		TacticalExposureRisk risk = TacticalExposureRisk.Safe;
		if (e >= 0.75f)
			risk = TacticalExposureRisk.Critical;
		else if (e >= 0.5f)
			risk = TacticalExposureRisk.Dangerous;
		else if (e >= ExposedFloor01)
			risk = TacticalExposureRisk.Exposed;
		if (risk == TacticalExposureRisk.Critical &&
		    _metersToNextCover > 0f &&
		    _metersToNextCover <= CoverDashMeters)
			return TacticalExposureRisk.Dangerous;
		return risk;
	}

	public static float PointExposure01(Vector3 _point, in TacticalRouteSituation _situation)
	{
		float coverMeters = NearestCoverMeters(_point, in _situation);
		bool protectedByCover = coverMeters <= TacticalRouteScoreMath.CoverNearMeters;
		bool known = _situation.HasKnownThreat || HasHostiles(in _situation);
		if (!known)
			return TacticalRouteScoreMath.UnknownExposure;
		float open = OpenThreat01(_point, in _situation);
		if (protectedByCover)
			return 0.12f;
		return open;
	}

	public static TacticalExposureProfileSummary Fill(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation,
		int _maxSamples)
	{
		if (_candidate == null)
			return default;
		if (_candidate.ExposureSamples == null)
			return default;
		if (_candidate.UseAuthoredMetrics)
		{
			if (_candidate.UseAuthoredExposureProfile)
				return Summarize(_candidate, false);
			return new TacticalExposureProfileSummary
			{
				SampleCount = _candidate.ExposureSamples.Count,
				Average01 = _candidate.Exposure01,
				Peak01 = _candidate.PeakExposure01,
				ExposureCost = _candidate.ExposureCost,
				TimeAboveThresholdSeconds = _candidate.TimeAboveThresholdSeconds,
				TimeExposedSeconds = _candidate.TimeExposedSeconds
			};
		}

		_candidate.ExposureSamples.Clear();

		s_BuildCount++;
		BuildPolyline(_candidate);
		float length = s_Cum.Count > 0 ? s_Cum[s_Cum.Count - 1] : 0f;
		if (length < 0.05f)
			length = 0.05f;
		int count = ResolveSampleCount(length, _maxSamples);
		float speed = _situation.WalkSpeedMetersPerSecond > 0.05f
			? _situation.WalkSpeedMetersPerSecond
			: TacticalRouteScoreMath.DefaultWalkSpeed;
		float step = length / Mathf.Max(1, count - 1);
		for (int i = 0; i < count; i++)
		{
			float along = i * step;
			Vector3 point = PointAt(along);
			float coverMeters = NearestCoverMeters(point, in _situation);
			float cover01 = Mathf.Clamp01(Mathf.InverseLerp(
				TacticalRouteScoreMath.CoverFarMeters,
				TacticalRouteScoreMath.CoverNearMeters,
				coverMeters));
			float exposure = PointExposure01(point, in _situation);
			_candidate.ExposureSamples.Add(new TacticalExposureSample
			{
				Position = point,
				DistanceAlongMeters = along,
				SegmentMeters = i == 0 ? 0f : step,
				TravelTimeSeconds = i == 0 ? 0f : step / speed,
				Exposure01 = exposure,
				Cover01 = cover01,
				MetersToNextCover = 0f,
				Risk = TacticalExposureRisk.Safe
			});
		}

		FillForwardCover(_candidate.ExposureSamples);
		for (int i = 0; i < _candidate.ExposureSamples.Count; i++)
		{
			TacticalExposureSample sample = _candidate.ExposureSamples[i];
			sample.Risk = Classify(sample.Exposure01, sample.MetersToNextCover);
			_candidate.ExposureSamples[i] = sample;
		}

		TacticalExposureProfileSummary summary = Summarize(_candidate, false);
		ApplySummary(_candidate, in summary);
		return summary;
	}

	public static Vector3 SamplePosition(TacticalRouteCandidate _candidate, int _index, int _maxSamples)
	{
		if (_candidate == null)
			return default;
		BuildPolyline(_candidate);
		float length = s_Cum.Count > 0 ? s_Cum[s_Cum.Count - 1] : 0f;
		int count = ResolveSampleCount(length, _maxSamples);
		if (_index < 0 || _index >= count)
			return default;
		float step = length / Mathf.Max(1, count - 1);
		return PointAt(_index * step);
	}
	#endregion

	#region Private Methods
	private static bool HasHostiles(in TacticalRouteSituation _situation)
	{
		return _situation.HostilePositions != null && _situation.HostilePositions.Count > 0;
	}

	private static float OpenThreat01(Vector3 _point, in TacticalRouteSituation _situation)
	{
		if (HasHostiles(in _situation))
		{
			float worst = 0.2f;
			for (int i = 0; i < _situation.HostilePositions.Count; i++)
			{
				float vis = IsOccluded(_point, _situation.HostilePositions[i], in _situation)
					? 0.18f
					: 0.92f;
				if (vis > worst)
					worst = vis;
			}

			return worst;
		}

		return 0.75f;
	}

	private static bool IsOccluded(
		Vector3 _point,
		Vector3 _hostile,
		in TacticalRouteSituation _situation)
	{
		if (OccluderHits(_point, _hostile, _situation.CoverHints))
			return true;
		if (_situation.CoverCandidates == null)
			return false;
		for (int i = 0; i < _situation.CoverCandidates.Count; i++)
		{
			if (_situation.CoverCandidates[i] == null)
				continue;
			if (PointHitsSegment(_situation.CoverCandidates[i].Position, _hostile, _point))
				return true;
		}

		return false;
	}

	private static bool OccluderHits(Vector3 _point, Vector3 _hostile, IReadOnlyList<Vector3> _hints)
	{
		if (_hints == null)
			return false;
		for (int i = 0; i < _hints.Count; i++)
		{
			if (PointHitsSegment(_hints[i], _hostile, _point))
				return true;
		}

		return false;
	}

	private static bool PointHitsSegment(Vector3 _occluder, Vector3 _from, Vector3 _to)
	{
		Vector3 ab = _to - _from;
		ab.y = 0f;
		float lenSqr = ab.x * ab.x + ab.z * ab.z;
		if (lenSqr < 0.01f)
			return false;
		Vector3 ap = _occluder - _from;
		ap.y = 0f;
		float t = (ap.x * ab.x + ap.z * ab.z) / lenSqr;
		if (t < 0.12f || t > 0.88f)
			return false;
		Vector3 closest = _from + ab * t;
		return CoverSpatialMath.PlanarDistanceSqr(closest, _occluder) <=
		       c_OccluderRadiusMeters * c_OccluderRadiusMeters;
	}

	private static float NearestCoverMeters(Vector3 _point, in TacticalRouteSituation _situation)
	{
		float best = TacticalRouteScoreMath.CoverFarMeters;
		if (_situation.CoverHints != null)
		{
			for (int i = 0; i < _situation.CoverHints.Count; i++)
			{
				float sqr = CoverSpatialMath.PlanarDistanceSqr(_point, _situation.CoverHints[i]);
				if (sqr < best * best)
					best = Mathf.Sqrt(sqr);
			}
		}

		if (_situation.CoverCandidates != null)
		{
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
			{
				if (_situation.CoverCandidates[i] == null)
					continue;
				float sqr = CoverSpatialMath.PlanarDistanceSqr(_point, _situation.CoverCandidates[i].Position);
				if (sqr < best * best)
					best = Mathf.Sqrt(sqr);
			}
		}

		return best;
	}

	private static void FillForwardCover(List<TacticalExposureSample> _samples)
	{
		if (_samples == null || _samples.Count == 0)
			return;
		float next = -1f;
		for (int i = _samples.Count - 1; i >= 0; i--)
		{
			TacticalExposureSample sample = _samples[i];
			if (sample.Cover01 >= 0.55f)
				next = sample.DistanceAlongMeters;
			sample.MetersToNextCover = next < 0f
				? 99f
				: Mathf.Max(0f, next - sample.DistanceAlongMeters);
			_samples[i] = sample;
		}
	}

	private static TacticalExposureProfileSummary Summarize(
		TacticalRouteCandidate _candidate,
		bool _fromCache)
	{
		var summary = new TacticalExposureProfileSummary { FromCache = _fromCache };
		if (_candidate.ExposureSamples == null || _candidate.ExposureSamples.Count == 0)
			return summary;
		summary.SampleCount = _candidate.ExposureSamples.Count;
		float sum = 0f;
		float peak = 0f;
		float cost = 0f;
		float above = 0f;
		float exposed = 0f;
		for (int i = 0; i < _candidate.ExposureSamples.Count; i++)
		{
			TacticalExposureSample sample = _candidate.ExposureSamples[i];
			sum += sample.Exposure01;
			if (sample.Exposure01 > peak)
				peak = sample.Exposure01;
			cost += sample.Exposure01 * sample.TravelTimeSeconds;
			if (sample.Exposure01 >= DefaultThreshold01)
				above += sample.TravelTimeSeconds;
			if (sample.Exposure01 >= ExposedFloor01)
				exposed += sample.TravelTimeSeconds;
		}

		summary.Average01 = sum / summary.SampleCount;
		summary.Peak01 = peak;
		summary.ExposureCost = cost;
		summary.TimeAboveThresholdSeconds = above;
		summary.TimeExposedSeconds = exposed;
		return summary;
	}

	private static void ApplySummary(
		TacticalRouteCandidate _candidate,
		in TacticalExposureProfileSummary _summary)
	{
		_candidate.PeakExposure01 = _summary.Peak01;
		_candidate.ExposureCost = _summary.ExposureCost;
		_candidate.TimeAboveThresholdSeconds = _summary.TimeAboveThresholdSeconds;
		_candidate.TimeExposedSeconds = _summary.TimeExposedSeconds;
	}

	private static void BuildPolyline(TacticalRouteCandidate _candidate)
	{
		s_Poly.Clear();
		s_Cum.Clear();
		s_Poly.Add(_candidate.Origin);
		s_Cum.Add(0f);
		if (_candidate.Intermediates != null)
		{
			for (int i = 0; i < _candidate.Intermediates.Count; i++)
				Append(_candidate.Intermediates[i].Position);
		}

		Append(_candidate.Destination);
	}

	private static void Append(Vector3 _point)
	{
		Vector3 previous = s_Poly[s_Poly.Count - 1];
		float meters = Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(previous, _point));
		s_Poly.Add(_point);
		s_Cum.Add(s_Cum[s_Cum.Count - 1] + meters);
	}

	private static Vector3 PointAt(float _along)
	{
		if (s_Poly.Count == 0)
			return default;
		if (s_Poly.Count == 1)
			return s_Poly[0];
		float target = Mathf.Max(0f, _along);
		for (int i = 1; i < s_Cum.Count; i++)
		{
			if (s_Cum[i] + 0.0001f < target)
				continue;
			float span = s_Cum[i] - s_Cum[i - 1];
			float t = span <= 0.0001f ? 0f : (target - s_Cum[i - 1]) / span;
			return Vector3.Lerp(s_Poly[i - 1], s_Poly[i], t);
		}

		return s_Poly[s_Poly.Count - 1];
	}
	#endregion
}
