using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.1 route score. Prototype weights, not freeze. Exposure ≠ visibility.
/// Unknown threat is a moderate penalty, not zero.
/// </summary>
public static class TacticalRouteScoreMath
{
	#region Constants
	public const float DefaultWalkSpeed = 1.5f;
	public const float CoverNearMeters = 2.5f;
	public const float CoverFarMeters = 8f;
	public const float UnknownExposure = 0.35f;
	private const float c_ScoreEpsilon = 0.0001f;
	#endregion

	#region Public Methods
	public static float PolylineMeters(
		Vector3 _origin,
		Vector3 _destination,
		IReadOnlyList<TacticalRouteWaypoint> _hops)
	{
		Vector3 previous = _origin;
		float meters = 0f;
		if (_hops != null)
		{
			for (int i = 0; i < _hops.Count; i++)
			{
				meters += Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(previous, _hops[i].Position));
				previous = _hops[i].Position;
			}
		}

		meters += Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(previous, _destination));
		return meters;
	}

	public static void FillComputedMetrics(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation)
	{
		if (_candidate == null || _candidate.UseAuthoredMetrics)
			return;
		float speed = _situation.WalkSpeedMetersPerSecond > 0.05f
			? _situation.WalkSpeedMetersPerSecond
			: DefaultWalkSpeed;
		_candidate.DistanceMeters = PolylineMeters(
			_candidate.Origin, _candidate.Destination, _candidate.Intermediates);
		_candidate.TravelTimeSeconds = _candidate.DistanceMeters / speed;
		_candidate.Exposure01 = SampleExposure01(_candidate, in _situation);
		_candidate.Cover01 = SampleCover01(_candidate, in _situation);
		_candidate.Danger01 = _situation.HasKnownThreat
			? Mathf.Clamp01(_candidate.Exposure01 * 0.85f + 0.15f)
			: Mathf.Clamp01(UnknownExposure * 0.5f);
		_candidate.MissionProgress01 = SampleMissionProgress01(_candidate, in _situation);
	}

	public static TacticalRouteScoreFactors EvaluateFactors(
		TacticalRouteCandidate _candidate,
		TacticalMovementMode _mode)
	{
		return EvaluateFactors(_candidate, _mode, false);
	}

	public static TacticalRouteScoreFactors EvaluateFactors(
		TacticalRouteCandidate _candidate,
		TacticalMovementMode _mode,
		bool _urbanPresent)
	{
		if (_candidate == null)
			return default;
		float distance = Mathf.Min(3f, _candidate.DistanceMeters / 10f);
		float travel = Mathf.Min(3f, _candidate.TravelTimeSeconds / 8f);
		float exposure = Mathf.Clamp01(_candidate.Exposure01) * 3f;
		float cover = Mathf.Clamp01(_candidate.Cover01) * 3f;
		float danger = Mathf.Clamp01(_candidate.Danger01) * 3f;
		float mission = Mathf.Clamp01(_candidate.MissionProgress01) * 3f;
		float wall = _urbanPresent ? Mathf.Clamp01(_candidate.WallProximity01) : 0f;
		float open = _urbanPresent ? Mathf.Clamp01(_candidate.OpenExposure01) : 0f;
		GetWeights(
			_mode,
			out float wMission,
			out float wCover,
			out float wDistance,
			out float wTravel,
			out float wExposure,
			out float wDanger,
			out float wWall,
			out float wOpen);
		float wallBias = wall * wWall;
		float openPenalty = open * wOpen;
		GetTraversalWeights(_mode, out float wPeakHold, out float wTimeAbove, out float wTimeExposed);
		float peakHold = Mathf.Clamp01(_candidate.PeakExposure01) *
		                 Mathf.Min(2f, _candidate.TimeAboveThresholdSeconds) *
		                 wPeakHold;
		float timeAbove = Mathf.Min(6f, _candidate.TimeAboveThresholdSeconds) * wTimeAbove;
		float timeExposed = Mathf.Min(8f, _candidate.TimeExposedSeconds) * wTimeExposed;
		float total =
			mission * wMission
			+ cover * wCover
			+ wallBias
			- distance * wDistance
			- travel * wTravel
			- exposure * wExposure
			- danger * wDanger
			- openPenalty
			- peakHold
			- timeAbove
			- timeExposed;
		return new TacticalRouteScoreFactors
		{
			MissionProgress = mission * wMission,
			Cover = cover * wCover,
			Distance = distance * wDistance,
			TravelTime = travel * wTravel,
			Exposure = exposure * wExposure,
			Danger = danger * wDanger,
			WallBias = wallBias,
			OpenExposure = openPenalty,
			PeakHold = peakHold,
			TimeAbove = timeAbove,
			TimeExposed = timeExposed,
			Total = total
		};
	}

	public static bool IsBetter(
		in TacticalRouteEvaluation _candidate,
		in TacticalRouteEvaluation _best,
		bool _hasBest)
	{
		if (!_candidate.Viable)
			return false;
		if (!_hasBest)
			return true;
		if (_candidate.Score > _best.Score + c_ScoreEpsilon)
			return true;
		if (_candidate.Score < _best.Score - c_ScoreEpsilon)
			return false;
		float distA = _candidate.Candidate != null ? _candidate.Candidate.DistanceMeters : 0f;
		float distB = _best.Candidate != null ? _best.Candidate.DistanceMeters : 0f;
		if (distA + c_ScoreEpsilon < distB)
			return true;
		if (distB + c_ScoreEpsilon < distA)
			return false;
		int idA = _candidate.Candidate != null ? _candidate.Candidate.CandidateId : 0;
		int idB = _best.Candidate != null ? _best.Candidate.CandidateId : 0;
		return idA < idB;
	}

	public static TacticalRouteSelectReason TieReason(
		in TacticalRouteEvaluation _winner,
		in TacticalRouteEvaluation _other)
	{
		if (!_other.Viable)
			return TacticalRouteSelectReason.OnlyViable;
		if (_winner.Score > _other.Score + c_ScoreEpsilon)
			return TacticalRouteSelectReason.HighestScore;
		float distA = _winner.Candidate != null ? _winner.Candidate.DistanceMeters : 0f;
		float distB = _other.Candidate != null ? _other.Candidate.DistanceMeters : 0f;
		if (distA + c_ScoreEpsilon < distB)
			return TacticalRouteSelectReason.ShorterDistance;
		return TacticalRouteSelectReason.CandidateOrder;
	}
	#endregion

	#region Private Methods
	private static void GetWeights(
		TacticalMovementMode _mode,
		out float _mission,
		out float _cover,
		out float _distance,
		out float _travel,
		out float _exposure,
		out float _danger,
		out float _wall,
		out float _open)
	{
		if (_mode == TacticalMovementMode.Tactical)
		{
			_mission = 1.25f;
			_cover = 1.1f;
			_distance = 0.45f;
			_travel = 0.35f;
			_exposure = 1.3f;
			_danger = 1f;
			_wall = 0.22f;
			_open = 0.26f;
			return;
		}

		if (_mode == TacticalMovementMode.Emergency)
		{
			_mission = 0.7f;
			_cover = 0.25f;
			_distance = 1.35f;
			_travel = 1.15f;
			_exposure = 0.45f;
			_danger = 0.4f;
			_wall = 0.04f;
			_open = 0.05f;
			return;
		}

		_mission = 1f;
		_cover = 0.05f;
		_distance = 1.6f;
		_travel = 1.1f;
		_exposure = 0.08f;
		_danger = 0.08f;
		_wall = 0f;
		_open = 0f;
	}

	private static void GetTraversalWeights(
		TacticalMovementMode _mode,
		out float _peakHold,
		out float _timeAbove,
		out float _timeExposed)
	{
		if (_mode == TacticalMovementMode.Tactical)
		{
			_peakHold = 0.15f;
			_timeAbove = 0.22f;
			_timeExposed = 0.10f;
			return;
		}

		if (_mode == TacticalMovementMode.Emergency)
		{
			_peakHold = 0.05f;
			_timeAbove = 0.08f;
			_timeExposed = 0.04f;
			return;
		}

		_peakHold = 0f;
		_timeAbove = 0f;
		_timeExposed = 0f;
	}

	private static float SampleExposure01(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation)
	{
		int samples = 0;
		float sum = 0f;
		SamplePath(
			_candidate,
			in _situation,
			true,
			ref sum,
			ref samples);
		if (samples <= 0)
			return UnknownExposure;
		return Mathf.Clamp01(sum / samples);
	}

	private static void AccumulateSample(
		Vector3 _point,
		in TacticalRouteSituation _situation,
		ref float _sum,
		ref int _samples)
	{
		float open = UnknownExposure;
		if (NearestCoverMeters(_point, _situation.CoverHints) <= CoverNearMeters)
			open = 0.12f;
		else if (_situation.HasKnownThreat)
			open = 0.75f;
		_sum += open;
		_samples++;
	}

	private static float SampleCover01(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation)
	{
		if (_situation.CoverHints == null || _situation.CoverHints.Count == 0)
			return 0f;
		int samples = 0;
		float sum = 0f;
		SamplePath(
			_candidate,
			in _situation,
			false,
			ref sum,
			ref samples);
		if (samples <= 0)
			return 0f;
		return Mathf.Clamp01(sum / samples);
	}

	private static void SamplePath(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation,
		bool _exposure,
		ref float _sum,
		ref int _samples)
	{
		if (_candidate == null)
			return;
		Vector3 previous = _candidate.Origin;
		AccumulatePoint(previous, in _situation, _exposure, ref _sum, ref _samples);
		if (_candidate.Intermediates != null)
		{
			for (int i = 0; i < _candidate.Intermediates.Count; i++)
			{
				Vector3 hop = _candidate.Intermediates[i].Position;
				AccumulatePoint(Vector3.Lerp(previous, hop, 0.5f), in _situation, _exposure, ref _sum, ref _samples);
				AccumulatePoint(hop, in _situation, _exposure, ref _sum, ref _samples);
				previous = hop;
			}
		}

		AccumulatePoint(
			Vector3.Lerp(previous, _candidate.Destination, 0.5f),
			in _situation,
			_exposure,
			ref _sum,
			ref _samples);
		AccumulatePoint(_candidate.Destination, in _situation, _exposure, ref _sum, ref _samples);
	}

	private static void AccumulatePoint(
		Vector3 _point,
		in TacticalRouteSituation _situation,
		bool _exposure,
		ref float _sum,
		ref int _samples)
	{
		if (_exposure)
			AccumulateSample(_point, in _situation, ref _sum, ref _samples);
		else
			AccumulateCover(_point, _situation.CoverHints, ref _sum, ref _samples);
	}

	private static void AccumulateCover(
		Vector3 _point,
		IReadOnlyList<Vector3> _hints,
		ref float _sum,
		ref int _samples)
	{
		float meters = NearestCoverMeters(_point, _hints);
		float t = Mathf.InverseLerp(CoverFarMeters, CoverNearMeters, meters);
		_sum += Mathf.Clamp01(t);
		_samples++;
	}

	private static float NearestCoverMeters(Vector3 _point, IReadOnlyList<Vector3> _hints)
	{
		if (_hints == null || _hints.Count == 0)
			return CoverFarMeters;
		float best = float.PositiveInfinity;
		for (int i = 0; i < _hints.Count; i++)
		{
			float sqr = CoverSpatialMath.PlanarDistanceSqr(_point, _hints[i]);
			if (sqr < best)
				best = sqr;
		}

		return Mathf.Sqrt(best);
	}

	private static float SampleMissionProgress01(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation)
	{
		if (!_situation.HasObjective)
			return 0.5f;
		float origin = Mathf.Sqrt(
			CoverSpatialMath.PlanarDistanceSqr(_candidate.Origin, _situation.Objective));
		float denom = Mathf.Max(1f, origin);
		float path = 0f;
		int n = 0;
		AddMissionSample(_candidate.Origin, _situation.Objective, ref path, ref n);
		if (_candidate.Intermediates != null)
		{
			for (int i = 0; i < _candidate.Intermediates.Count; i++)
				AddMissionSample(_candidate.Intermediates[i].Position, _situation.Objective, ref path, ref n);
		}

		AddMissionSample(_candidate.Destination, _situation.Objective, ref path, ref n);
		float mean = n > 0 ? path / n : origin;
		return Mathf.Clamp01(1f - mean / denom);
	}

	private static void AddMissionSample(Vector3 _point, Vector3 _objective, ref float _sum, ref int _count)
	{
		_sum += Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(_point, _objective));
		_count++;
	}
	#endregion
}
