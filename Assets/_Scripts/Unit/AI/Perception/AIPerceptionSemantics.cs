/// <summary>
/// AI-0 FROZEN. Perception flags over frozen <see cref="PerceivedContact"/> — not new Vision fields.
/// Vision = perception. AI = decision. Do not retune Detection / Memory / Identity here.
/// </summary>
public static class AIPerceptionSemantics
{
	public static float StaleThreshold => MemoryDecayMath.DefaultStaleThreshold;

	public static bool IsVisibleNow(PerceivedContact _contact)
	{
		return _contact != null &&
		       _contact.State == DetectionState.Detected &&
		       _contact.ObservationState == ObservationState.Observed;
	}

	public static bool IsRecentlyLost(PerceivedContact _contact)
	{
		return _contact != null && _contact.ObservationState == ObservationState.RecentlyLost;
	}

	public static bool IsLost(PerceivedContact _contact)
	{
		return _contact != null && _contact.ObservationState == ObservationState.Lost;
	}

	public static bool HasUsefulMemory(PerceivedContact _contact)
	{
		return _contact != null && _contact.LastSeenConfidence > StaleThreshold;
	}

	public static bool IsMemoryStale(PerceivedContact _contact)
	{
		return _contact != null && MemoryDecayMath.IsStale(_contact.LastSeenConfidence);
	}

	public static bool IsIdentityUnknown(PerceivedContact _contact)
	{
		return _contact != null && _contact.Identity == PerceivedIdentity.Unknown;
	}

	public static bool IsIdentityKnown(PerceivedContact _contact)
	{
		return _contact != null && _contact.Identity != PerceivedIdentity.Unknown;
	}

	public static bool IsFriendly(PerceivedContact _contact)
	{
		return _contact != null && _contact.Relationship == PerceivedRelationship.Friendly;
	}

	public static bool IsNeutral(PerceivedContact _contact)
	{
		return _contact != null && _contact.Relationship == PerceivedRelationship.Neutral;
	}

	public static bool IsHostile(PerceivedContact _contact)
	{
		return _contact != null && _contact.Relationship == PerceivedRelationship.Hostile;
	}

	public static bool IsThreatNone(PerceivedContact _contact)
	{
		return _contact != null && _contact.Threat == ThreatLevel.None;
	}

	public static bool IsThreatLow(PerceivedContact _contact)
	{
		return _contact != null && _contact.Threat == ThreatLevel.Low;
	}

	public static bool IsThreatMedium(PerceivedContact _contact)
	{
		return _contact != null && _contact.Threat == ThreatLevel.Medium;
	}

	public static bool IsThreatHigh(PerceivedContact _contact)
	{
		return _contact != null && _contact.Threat == ThreatLevel.High;
	}
}
