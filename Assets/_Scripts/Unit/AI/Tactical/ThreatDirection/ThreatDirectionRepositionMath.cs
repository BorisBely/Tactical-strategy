using UnityEngine;

/// <summary>
/// #14C.5 Stay / FaceOnly / RepositionAllowed. Permission only. Not Move / Reserve / scan.
/// Prototype thresholds, not freeze.
/// </summary>
public enum ThreatDirectionRepositionKind
{
	None = 0,
	FaceOnly = 1,
	Stay = 2,
	RepositionAllowed = 3
}

/// <summary>
/// #14C.5 gate: significant angle + confidence, then cover fit + Stay Committed margin.
/// </summary>
public static class ThreatDirectionRepositionMath
{
	#region Constants
	public const float ThreatRepositionAngleThreshold = 80f;
	public const float ThreatRepositionConfidenceThreshold = 0.75f;
	public const float ThreatRepositionMargin = 0.45f;
	#endregion

	#region Public Methods
	public static bool PassesRepositionGate(float _angleDeltaDegrees, float _confidence)
	{
		return PassesRepositionGate(_angleDeltaDegrees, _confidence, false);
	}

	public static bool PassesRepositionGate(
		float _angleDeltaDegrees,
		float _confidence,
		bool _holdPreviousAllowed)
	{
		if (_confidence < ThreatRepositionConfidenceThreshold)
			return false;
		if (_holdPreviousAllowed)
			return true;
		return _angleDeltaDegrees >= ThreatRepositionAngleThreshold;
	}

	public static bool BeatsCurrent(float _currentScore, float _candidateScore)
	{
		return CoverSwitchMath.ShouldReposition(
			_currentScore,
			_candidateScore,
			ThreatRepositionMargin);
	}

	public static bool HasNoticeableAdvantage(
		float _currentScore,
		float _currentPreference,
		float _bestScore,
		float _bestPreference)
	{
		return HasNoticeableAdvantage(
			_currentScore,
			_currentPreference,
			0f,
			_bestScore,
			_bestPreference,
			0f);
	}

	public static bool HasNoticeableAdvantage(
		float _currentScore,
		float _currentPreference,
		float _currentAdjustment,
		float _bestScore,
		float _bestPreference,
		float _bestAdjustment)
	{
		return BeatsCurrent(_currentScore, _bestScore) ||
		       BeatsCurrent(_currentPreference, _bestPreference) ||
		       BeatsCurrent(_currentAdjustment, _bestAdjustment);
	}

	public static ThreatDirectionRepositionKind Decide(
		float _angleDeltaDegrees,
		float _confidence,
		CoverThreatFit _fit,
		bool _hasOccupying,
		float _currentScore,
		float _currentPreference,
		float _bestScore,
		float _bestPreference,
		int _currentId,
		int _bestId,
		bool _holdPreviousAllowed = false,
		float _currentAdjustment = 0f,
		float _bestAdjustment = 0f,
		CoverThreatFit _bestFit = CoverThreatFit.Unknown)
	{
		bool gate = PassesRepositionGate(_angleDeltaDegrees, _confidence, _holdPreviousAllowed);
		if (!gate)
			return ThreatDirectionRepositionKind.FaceOnly;
		if (!_hasOccupying)
			return ThreatDirectionRepositionKind.FaceOnly;
		if (_fit == CoverThreatFit.Good)
			return ThreatDirectionRepositionKind.Stay;
		if (_currentId != 0 && _currentId == _bestId)
			return ThreatDirectionRepositionKind.Stay;
		if (_fit == CoverThreatFit.Poor && _bestFit == CoverThreatFit.Good)
			return ThreatDirectionRepositionKind.RepositionAllowed;
		if (!HasNoticeableAdvantage(
			    _currentScore,
			    _currentPreference,
			    _currentAdjustment,
			    _bestScore,
			    _bestPreference,
			    _bestAdjustment))
			return ThreatDirectionRepositionKind.Stay;
		return ThreatDirectionRepositionKind.RepositionAllowed;
	}
	#endregion
}
