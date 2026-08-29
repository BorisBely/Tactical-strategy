using System;
using UnityEngine;

public enum ContactSelectionRejectReason
{
	None = 0,
	NullContact = 1,
	NoTarget = 2,
	Forgotten = 3,
	NotWorldEngageable = 4,
	Friendly = 5,
	NeutralIdentity = 6,
	UnknownDisallowed = 7,
	StaleDisallowed = 8
}

/// <summary>
/// G5 selection policy. Identity/Relationship/Threat are modifiers, not a Hostile gate.
/// Unknown is selectable by default until VisualIdentityEvidence is committed.
/// </summary>
[Serializable]
public struct ContactSelectionPolicy
{
	public bool ExcludeFriendly;
	public bool ExcludeNeutralIdentity;
	public bool AllowUnknown;
	public bool StaleEligible;
	public float StaleThreshold;
	public float ObservedBonus;
	public float ConfidenceWeight;
	public float ThreatWeight;
	public float DistanceWeight;
	public float StalePenalty;
	public float HostileBonus;
	public float SwitchThreshold;
	public float WeaponSuitabilityWeight;
	public float MissionBonus;

	public static ContactSelectionPolicy CreateDefault()
	{
		return new ContactSelectionPolicy
		{
			ExcludeFriendly = true,
			ExcludeNeutralIdentity = true,
			AllowUnknown = true,
			StaleEligible = true,
			StaleThreshold = MemoryDecayMath.DefaultStaleThreshold,
			ObservedBonus = 10f,
			ConfidenceWeight = 2f,
			ThreatWeight = 1f,
			DistanceWeight = 1f,
			StalePenalty = 3f,
			HostileBonus = 0.5f,
			SwitchThreshold = TargetSwitchMath.DefaultSwitchThreshold,
			WeaponSuitabilityWeight = TargetSelectionMath.DefaultWeaponSuitabilityWeight,
			MissionBonus = TargetSelectionMath.DefaultMissionBonus
		};
	}
}

/// <summary>
/// Pure G5 eligibility. Does not reference UnitVision / Combat / UnitTeam / detection math.
/// World engageability is passed in so this stays testable without consciousness/damage components.
/// </summary>
public static class ContactSelectionEligibility
{
	public static bool Evaluate(
		PerceivedContact _contact,
		bool _isWorldEngageable,
		ContactSelectionPolicy _policy,
		out ContactSelectionRejectReason _reason)
	{
		_reason = ContactSelectionRejectReason.None;
		if (_contact == null)
		{
			_reason = ContactSelectionRejectReason.NullContact;
			return false;
		}

		if (_contact.Target == null)
		{
			_reason = ContactSelectionRejectReason.NoTarget;
			return false;
		}

		if (!_contact.HasKnowledge)
		{
			_reason = ContactSelectionRejectReason.Forgotten;
			return false;
		}

		if (!_isWorldEngageable)
		{
			_reason = ContactSelectionRejectReason.NotWorldEngageable;
			return false;
		}

		if (_policy.ExcludeFriendly &&
		    (_contact.Identity == PerceivedIdentity.Friendly ||
		     _contact.Relationship == PerceivedRelationship.Friendly))
		{
			_reason = ContactSelectionRejectReason.Friendly;
			return false;
		}

		if (_policy.ExcludeNeutralIdentity && _contact.Identity == PerceivedIdentity.Neutral)
		{
			_reason = ContactSelectionRejectReason.NeutralIdentity;
			return false;
		}

		if (!_policy.AllowUnknown && _contact.Identity == PerceivedIdentity.Unknown)
		{
			_reason = ContactSelectionRejectReason.UnknownDisallowed;
			return false;
		}

		if (!_policy.StaleEligible &&
		    MemoryDecayMath.IsStale(_contact.LastSeenConfidence, _policy.StaleThreshold))
		{
			_reason = ContactSelectionRejectReason.StaleDisallowed;
			return false;
		}

		return true;
	}
}
