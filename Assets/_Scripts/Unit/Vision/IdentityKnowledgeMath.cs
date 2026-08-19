using UnityEngine;

/// <summary>
/// Pure G3 identity / relationship / threat math. Shared by DetectionProcessor and tests.
/// Does not reference UnitTeam. IdentityConfidence is independent from DetectionProgress.
/// RecentlyLost/Lost hold IdentityConfidence. G4 decays LastSeenConfidence only.
/// Block C CLOSED / VERIFIED. Vision Freeze / AI Handoff: IdentifyTime 4 s, commit 0.50. Threat High≤25 m, Medium≤80 m.
/// </summary>
public static class IdentityKnowledgeMath
{
	/// <summary>Block C CLOSED: IdentifyTime ≫ AcquireTime (0.35 s). Time to conf=1 at Q=1.</summary>
	public const float DefaultIdentifyTimeSeconds = 4f;

	/// <summary>Block C CLOSED: commit PerceivedIdentity at this confidence.</summary>
	public const float DefaultCommitThreshold = 0.5f;

	/// <summary>
	/// Stepwise 40×0.05 s can land a hair below 0.50 while F3 still prints 0.500.
	/// Slack 0.001 commits that case. 0.49 stays Unknown; 1.99 s (0.4975) stays Unknown.
	/// </summary>
	public const float CommitFloatSlack = 0.001f;

	/// <summary>Hostile closer than this → Threat High.</summary>
	public const float DefaultThreatHighMeters = 25f;

	/// <summary>Hostile closer than this (and beyond High) → Threat Medium; else Low.</summary>
	public const float DefaultThreatMediumMeters = 80f;

	/// <summary>
	/// Grow identity confidence only while observed with a non-Unknown cue.
	/// Otherwise hold. G4 does not decay IdentityConfidence (only LastSeenConfidence).
	/// </summary>
	public static float IntegrateConfidence(
		float _current,
		float _visibilityQuality,
		float _dt,
		bool _isObserved,
		ObservableAffiliation _cue,
		float _identifyTimeSeconds = DefaultIdentifyTimeSeconds)
	{
		if (!_isObserved || _cue == ObservableAffiliation.Unknown || _dt <= 0f)
			return Mathf.Clamp01(_current);

		float rate = 1f / Mathf.Max(0.1f, _identifyTimeSeconds);
		return Mathf.Clamp01(_current + Mathf.Clamp01(_visibilityQuality) * rate * _dt);
	}

	/// <summary>
	/// Seconds of Q=1 observation from conf=0 to reach commit. IdentifyTime=4 → 2.0 s.
	/// </summary>
	public static float SecondsToCommit(
		float _identifyTimeSeconds = DefaultIdentifyTimeSeconds,
		float _visibilityQuality = 1f)
	{
		float q = Mathf.Clamp01(_visibilityQuality);
		if (q <= 0.0001f)
			return float.PositiveInfinity;
		return DefaultCommitThreshold * Mathf.Max(0.1f, _identifyTimeSeconds) / q;
	}

	public static PerceivedIdentity MapCue(ObservableAffiliation _cue)
	{
		switch (_cue)
		{
			case ObservableAffiliation.Friendly:
				return PerceivedIdentity.Friendly;
			case ObservableAffiliation.Neutral:
				return PerceivedIdentity.Neutral;
			case ObservableAffiliation.Hostile:
				return PerceivedIdentity.Hostile;
			default:
				return PerceivedIdentity.Unknown;
		}
	}

	/// <summary>
	/// Committed identity vs a new valid cue. Unknown cue never conflicts (hold).
	/// </summary>
	public static bool CueConflictsWithCommitted(
		PerceivedIdentity _committed,
		ObservableAffiliation _cue)
	{
		if (_committed == PerceivedIdentity.Unknown || _cue == ObservableAffiliation.Unknown)
			return false;
		return MapCue(_cue) != _committed;
	}

	public static bool HasReachedCommitThreshold(float _confidence)
	{
		return _confidence + CommitFloatSlack >= DefaultCommitThreshold;
	}

	public static PerceivedIdentity ResolveIdentity(
		float _confidence,
		ObservableAffiliation _cue,
		PerceivedIdentity _previous)
	{
		if (!HasReachedCommitThreshold(_confidence))
			return PerceivedIdentity.Unknown;

		if (_cue == ObservableAffiliation.Unknown)
			return _previous;

		return MapCue(_cue);
	}

	public static PerceivedRelationship ResolveRelationship(PerceivedIdentity _identity)
	{
		switch (_identity)
		{
			case PerceivedIdentity.Friendly:
				return PerceivedRelationship.Friendly;
			case PerceivedIdentity.Neutral:
				return PerceivedRelationship.Neutral;
			case PerceivedIdentity.Hostile:
				return PerceivedRelationship.Hostile;
			default:
				return PerceivedRelationship.Unknown;
		}
	}

	/// <summary>
	/// Hostile + close → High/Medium; Hostile + far → Low. Non-hostile → None.
	/// Neutral is None (not Low): threat is hostility, not “someone is nearby”.
	/// </summary>
	public static ThreatLevel EvaluateThreat(
		PerceivedRelationship _relationship,
		float _distanceMeters,
		float _highMeters = DefaultThreatHighMeters,
		float _mediumMeters = DefaultThreatMediumMeters)
	{
		if (_relationship != PerceivedRelationship.Hostile)
			return ThreatLevel.None;

		float dist = Mathf.Max(0f, _distanceMeters);
		float high = Mathf.Max(0.1f, _highMeters);
		float medium = Mathf.Max(high, _mediumMeters);

		if (dist <= high)
			return ThreatLevel.High;
		if (dist <= medium)
			return ThreatLevel.Medium;
		return ThreatLevel.Low;
	}

	public static void ApplyToContact(
		PerceivedContact _contact,
		bool _isObserved,
		ObservableAffiliation _cue,
		float _dt,
		float _identifyTimeSeconds = DefaultIdentifyTimeSeconds)
	{
		if (_contact == null)
			return;

		if (_isObserved && CueConflictsWithCommitted(_contact.Identity, _cue))
		{
			_contact.IdentityConfidence = 0f;
			_contact.Identity = PerceivedIdentity.Unknown;
			_contact.Relationship = PerceivedRelationship.Unknown;
		}

		float q = _isObserved ? _contact.CurrentEvaluation.VisibilityQuality : 0f;
		_contact.IdentityConfidence = IntegrateConfidence(
			_contact.IdentityConfidence, q, _dt, _isObserved, _cue, _identifyTimeSeconds);

		if (_isObserved)
		{
			_contact.Identity = ResolveIdentity(_contact.IdentityConfidence, _cue, _contact.Identity);
			if (_contact.Identity != PerceivedIdentity.Unknown)
				_contact.Relationship = ResolveRelationship(_contact.Identity);
		}

		float distance = Mathf.Sqrt(Mathf.Max(0f, _contact.LastObservation.DistanceSq));
		_contact.Threat = EvaluateThreat(_contact.Relationship, distance);
	}
}
