using UnityEngine;

/// <summary>
/// AI-0 FROZEN. Immutable AI-facing view of one perceived entity for a single decision tick.
/// Does not expose DetectionProgress, Q, FOV, Exposure, VisionObservation, or UnitTeam.
/// </summary>
public readonly struct AIContactKnowledge
{
	public readonly Transform Target;
	public readonly DetectionState DetectionState;
	public readonly ObservationState ObservationState;
	public readonly PerceivedIdentity Identity;
	public readonly float IdentityConfidence;
	public readonly PerceivedRelationship Relationship;
	public readonly ThreatLevel Threat;
	public readonly Vector3 LastKnownPosition;
	public readonly Vector3 LastSeenPosition;
	public readonly float LastSeenTime;
	public readonly float LastSeenConfidence;

	public readonly bool VisibleNow;
	public readonly bool RecentlyLost;
	public readonly bool Lost;
	public readonly bool HasUsefulMemory;
	public readonly bool MemoryStale;
	public readonly bool IdentityUnknown;
	public readonly bool IdentityKnown;
	public readonly bool Friendly;
	public readonly bool Neutral;
	public readonly bool Hostile;
	public readonly bool ThreatNone;
	public readonly bool ThreatLow;
	public readonly bool ThreatMedium;
	public readonly bool ThreatHigh;

	public AIContactKnowledge(
		Transform _target,
		DetectionState _detectionState,
		ObservationState _observationState,
		PerceivedIdentity _identity,
		float _identityConfidence,
		PerceivedRelationship _relationship,
		ThreatLevel _threat,
		Vector3 _lastKnownPosition,
		Vector3 _lastSeenPosition,
		float _lastSeenTime,
		float _lastSeenConfidence,
		bool _visibleNow,
		bool _recentlyLost,
		bool _lost,
		bool _hasUsefulMemory,
		bool _memoryStale,
		bool _identityUnknown,
		bool _identityKnown,
		bool _friendly,
		bool _neutral,
		bool _hostile,
		bool _threatNone,
		bool _threatLow,
		bool _threatMedium,
		bool _threatHigh)
	{
		Target = _target;
		DetectionState = _detectionState;
		ObservationState = _observationState;
		Identity = _identity;
		IdentityConfidence = _identityConfidence;
		Relationship = _relationship;
		Threat = _threat;
		LastKnownPosition = _lastKnownPosition;
		LastSeenPosition = _lastSeenPosition;
		LastSeenTime = _lastSeenTime;
		LastSeenConfidence = _lastSeenConfidence;
		VisibleNow = _visibleNow;
		RecentlyLost = _recentlyLost;
		Lost = _lost;
		HasUsefulMemory = _hasUsefulMemory;
		MemoryStale = _memoryStale;
		IdentityUnknown = _identityUnknown;
		IdentityKnown = _identityKnown;
		Friendly = _friendly;
		Neutral = _neutral;
		Hostile = _hostile;
		ThreatNone = _threatNone;
		ThreatLow = _threatLow;
		ThreatMedium = _threatMedium;
		ThreatHigh = _threatHigh;
	}

	public static AIContactKnowledge From(PerceivedContact _contact)
	{
		if (_contact == null)
			return default;

		return new AIContactKnowledge(
			_contact.Target,
			_contact.State,
			_contact.ObservationState,
			_contact.Identity,
			_contact.IdentityConfidence,
			_contact.Relationship,
			_contact.Threat,
			_contact.LastKnownPosition,
			_contact.LastSeenPosition,
			_contact.LastSeenTime,
			_contact.LastSeenConfidence,
			AIPerceptionSemantics.IsVisibleNow(_contact),
			AIPerceptionSemantics.IsRecentlyLost(_contact),
			AIPerceptionSemantics.IsLost(_contact),
			AIPerceptionSemantics.HasUsefulMemory(_contact),
			AIPerceptionSemantics.IsMemoryStale(_contact),
			AIPerceptionSemantics.IsIdentityUnknown(_contact),
			AIPerceptionSemantics.IsIdentityKnown(_contact),
			AIPerceptionSemantics.IsFriendly(_contact),
			AIPerceptionSemantics.IsNeutral(_contact),
			AIPerceptionSemantics.IsHostile(_contact),
			AIPerceptionSemantics.IsThreatNone(_contact),
			AIPerceptionSemantics.IsThreatLow(_contact),
			AIPerceptionSemantics.IsThreatMedium(_contact),
			AIPerceptionSemantics.IsThreatHigh(_contact));
	}
}
