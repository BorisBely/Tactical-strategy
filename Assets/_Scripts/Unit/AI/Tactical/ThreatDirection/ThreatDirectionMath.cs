using UnityEngine;

/// <summary>
/// #14C ranks, prototype confidence / uncertainty, expiry windows. Numbers are not freeze.
/// </summary>
public static class ThreatDirectionMath
{
	#region Constants
	public const float ExpectedConfidence = 0.5f;
	public const float ExpectedUncertaintyDegrees = 45f;
	public const float VisualConfidence = 0.9f;
	public const float VisualUncertaintyDegrees = 15f;
	public const float SoundConfidence = 0.7f;
	public const float SoundUncertaintyDegrees = 30f;
	public const float ReportConfidence = 0.6f;
	public const float ReportUncertaintyDegrees = 35f;
	public const float VisualStaleToFallbackSeconds = 8f;
	public const float SoundKnownToStaleSeconds = 4f;
	public const float SoundStaleToFallbackSeconds = 4f;
	public const float ReportKnownToStaleSeconds = 5f;
	public const float ReportStaleToFallbackSeconds = 5f;
	public const float StaleConfidenceFactor = 0.75f;
	public const float StaleUncertaintyExtraDegrees = 15f;
	public const float DecayConfidencePerSecond = 0.04f;
	public const float DecayUncertaintyPerSecond = 4f;
	public const float MinStaleConfidence = 0.15f;
	public const float MaxUncertaintyDegrees = 90f;
	public const float DirectionEpsilonSqr = 0.0001f;
	public const float CoverInfluenceLowConfidence = 0.2f;
	public const float CoverInfluenceHighConfidence = 0.9f;
	#endregion

	#region Public Methods
	public static int SourceRank(ThreatDirectionState _state, ThreatDirectionSource _source)
	{
		if (_state == ThreatDirectionState.None)
			return -1;
		if (_state == ThreatDirectionState.Expected || _source == ThreatDirectionSource.InitialEstimate)
			return 0;
		if (_source == ThreatDirectionSource.AllyReport)
			return 1;
		if (_source == ThreatDirectionSource.Sound)
			return 2;
		return 3;
	}

	public static bool CanOverride(
		ThreatDirectionState _currentState,
		ThreatDirectionSource _currentSource,
		ThreatDirectionSource _incoming)
	{
		if (_incoming == ThreatDirectionSource.Visual)
			return true;
		return SourceRank(ThreatDirectionState.Known, _incoming) >
		       SourceRank(_currentState, _currentSource);
	}

	public static void BaseQuality(
		ThreatDirectionSource _source,
		out float _confidence,
		out float _uncertaintyDegrees)
	{
		switch (_source)
		{
			case ThreatDirectionSource.Visual:
				_confidence = VisualConfidence;
				_uncertaintyDegrees = VisualUncertaintyDegrees;
				return;
			case ThreatDirectionSource.Sound:
				_confidence = SoundConfidence;
				_uncertaintyDegrees = SoundUncertaintyDegrees;
				return;
			case ThreatDirectionSource.AllyReport:
				_confidence = ReportConfidence;
				_uncertaintyDegrees = ReportUncertaintyDegrees;
				return;
			default:
				_confidence = ExpectedConfidence;
				_uncertaintyDegrees = ExpectedUncertaintyDegrees;
				return;
		}
	}

	public static void QualityAt(
		ThreatDirectionState _state,
		ThreatDirectionSource _source,
		float _staleAge,
		out float _confidence,
		out float _uncertaintyDegrees)
	{
		BaseQuality(_source, out _confidence, out _uncertaintyDegrees);
		if (_state != ThreatDirectionState.Stale)
			return;

		float age = Mathf.Max(0f, _staleAge);
		_confidence = Mathf.Max(
			MinStaleConfidence,
			_confidence * StaleConfidenceFactor - DecayConfidencePerSecond * age);
		_uncertaintyDegrees = Mathf.Min(
			MaxUncertaintyDegrees,
			_uncertaintyDegrees + StaleUncertaintyExtraDegrees + DecayUncertaintyPerSecond * age);
	}

	public static float KnownToStaleSeconds(ThreatDirectionSource _source)
	{
		switch (_source)
		{
			case ThreatDirectionSource.Sound:
				return SoundKnownToStaleSeconds;
			case ThreatDirectionSource.AllyReport:
				return ReportKnownToStaleSeconds;
			default:
				return float.PositiveInfinity;
		}
	}

	public static float CoverInfluence(float _confidence)
	{
		return Mathf.Clamp01(Mathf.InverseLerp(
			CoverInfluenceLowConfidence,
			CoverInfluenceHighConfidence,
			_confidence));
	}

	public static int QualityBand(float _confidence)
	{
		return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(_confidence) * 4f), 0, 4);
	}

	public static int QualityLogKey(float _confidence, float _uncertaintyDegrees)
	{
		int confidence = Mathf.RoundToInt(Mathf.Clamp01(_confidence) * 10f);
		int uncertainty = Mathf.RoundToInt(Mathf.Max(0f, _uncertaintyDegrees) / 5f);
		return (confidence << 8) | uncertainty;
	}

	public static int ConsumerQualityBand(in CoverSituation _situation)
	{
		if (!_situation.HasThreatDirection)
			return -1;
		return QualityBand(_situation.ThreatConfidence);
	}

	public static float StaleToFallbackSeconds(ThreatDirectionSource _source)
	{
		switch (_source)
		{
			case ThreatDirectionSource.Visual:
				return VisualStaleToFallbackSeconds;
			case ThreatDirectionSource.Sound:
				return SoundStaleToFallbackSeconds;
			case ThreatDirectionSource.AllyReport:
				return ReportStaleToFallbackSeconds;
			default:
				return VisualStaleToFallbackSeconds;
		}
	}
	#endregion
}
