using UnityEngine;

/// <summary>
/// Pure G5 selection score. Higher is better.
/// Observed beats remembered; confidence then threat then inverse distance.
/// LastKnown is used only as a distance hint — never as a combat aim point.
/// #12 does not rewrite this formula. Weapon / mission are additive modifiers after Score.
/// </summary>
public static class TargetSelectionMath
{
	#region Constants
	public const float DefaultWeaponSuitabilityWeight = 0.35f;
	public const float DefaultMissionBonus = 0.6f;
	#endregion

	#region Public Methods
	public static float Score(
		PerceivedContact _contact,
		Vector3 _origin,
		ContactSelectionPolicy _policy)
	{
		if (_contact == null)
			return 0f;

		float observed = _contact.ObservationState == ObservationState.Observed
			? Mathf.Max(0f, _policy.ObservedBonus)
			: 0f;
		float belief = Mathf.Max(
			Mathf.Clamp01(_contact.LastSeenConfidence),
			Mathf.Clamp01(_contact.SoundConfidence),
			Mathf.Clamp01(_contact.SharedConfidence));
		float confidence = belief * Mathf.Max(0f, _policy.ConfidenceWeight);
		float threatT = (float)_contact.Threat / (float)ThreatLevel.High;
		float threat = Mathf.Clamp01(threatT) * Mathf.Max(0f, _policy.ThreatWeight);
		float hostile = (_contact.Identity == PerceivedIdentity.Hostile ||
		                 _contact.Relationship == PerceivedRelationship.Hostile)
			? Mathf.Max(0f, _policy.HostileBonus)
			: 0f;

		float dist = HorizontalDistance(_origin, ResolveBelievedPosition(_contact));
		float distance = Mathf.Max(0f, _policy.DistanceWeight) / (1f + Mathf.Max(0f, dist));

		float stalePenalty = 0f;
		if (MemoryDecayMath.IsStale(_contact.LastSeenConfidence, _policy.StaleThreshold))
			stalePenalty = Mathf.Max(0f, _policy.StalePenalty);

		return observed + confidence + threat + hostile + distance - stalePenalty;
	}

	/// <summary>
	/// G5 Score plus small #12 modifiers. Does not change Score itself.
	/// Weapon suitability is a nudge, not a role system. Mission is a bonus, not ForcedPriority.
	/// </summary>
	public static float ScoreWithModifiers(
		PerceivedContact _contact,
		Vector3 _origin,
		ContactSelectionPolicy _policy,
		WeaponClassType _weaponClass,
		float _effectiveRangeMeters,
		Transform _missionTarget)
	{
		float score = Score(_contact, _origin, _policy);
		if (_contact == null)
			return score;

		float dist = HorizontalDistance(_origin, ResolveBelievedPosition(_contact));
		score += WeaponSuitabilityBonus(
			_weaponClass,
			dist,
			_effectiveRangeMeters,
			_policy.WeaponSuitabilityWeight);

		if (_missionTarget != null && _contact.Target == _missionTarget)
			score += Mathf.Max(0f, _policy.MissionBonus);

		return score;
	}

	/// <summary>
	/// Small distance preference by weapon class. Sniper far ↑, shotgun near ↑.
	/// Weight 0 keeps Score unchanged. Does not retune EffectiveRange / WorkingRange.
	/// </summary>
	public static float WeaponSuitabilityBonus(
		WeaponClassType _weaponClass,
		float _distanceMeters,
		float _effectiveRangeMeters,
		float _weight)
	{
		float weight = Mathf.Max(0f, _weight);
		if (weight <= 0f || _weaponClass == WeaponClassType.Unknown)
			return 0f;

		float distance = Mathf.Max(0f, _distanceMeters);
		float range = Mathf.Max(1f, _effectiveRangeMeters);

		switch (_weaponClass)
		{
			case WeaponClassType.Shotgun:
			case WeaponClassType.Pistol:
			case WeaponClassType.SubmachineGun:
				return weight * (1f - Mathf.Clamp01(distance / Mathf.Max(8f, range * 0.15f)));
			case WeaponClassType.SniperRifle:
				return weight * Mathf.Clamp01(distance / (range * 0.6f));
			default:
				float peak = range * 0.45f;
				return weight * (1f - Mathf.Clamp01(Mathf.Abs(distance - peak) / range));
		}
	}

	public static Vector3 ResolveBelievedPosition(PerceivedContact _contact)
	{
		if (_contact == null)
			return Vector3.zero;
		if (_contact.HasUsefulVisualMemory)
		{
			if (_contact.LastKnownPosition.sqrMagnitude > 0.0001f)
				return _contact.LastKnownPosition;
			return _contact.LastSeenPosition;
		}

		if (_contact.HasUsefulSound)
			return _contact.SoundPosition;

		if (_contact.HasUsefulShared)
			return _contact.SharedPosition;

		if (_contact.LastKnownPosition.sqrMagnitude > 0.0001f)
			return _contact.LastKnownPosition;
		return _contact.LastSeenPosition;
	}

	public static bool TryGetObservedAimPoint(PerceivedContact _contact, out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		if (_contact == null || _contact.ObservationState != ObservationState.Observed)
			return false;
		if (!_contact.LastObservation.HasAimPoint || !_contact.LastObservation.IsVisible)
			return false;
		_aimPoint = _contact.LastObservation.AimPoint;
		return true;
	}
	#endregion

	#region Private Methods
	private static float HorizontalDistance(Vector3 _origin, Vector3 _believed)
	{
		return Vector3.Distance(
			new Vector3(_origin.x, 0f, _origin.z),
			new Vector3(_believed.x, 0f, _believed.z));
	}
	#endregion
}
