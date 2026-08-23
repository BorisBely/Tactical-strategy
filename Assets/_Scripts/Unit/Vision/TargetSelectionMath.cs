using UnityEngine;

/// <summary>
/// Pure G5 selection score. Higher is better.
/// Observed beats remembered; confidence then threat then inverse distance.
/// LastKnown is used only as a distance hint — never as a combat aim point.
/// </summary>
public static class TargetSelectionMath
{
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

		Vector3 believed = ResolveBelievedPosition(_contact);
		float dist = Vector3.Distance(
			new Vector3(_origin.x, 0f, _origin.z),
			new Vector3(believed.x, 0f, believed.z));
		float distance = Mathf.Max(0f, _policy.DistanceWeight) / (1f + Mathf.Max(0f, dist));

		float stalePenalty = 0f;
		if (MemoryDecayMath.IsStale(_contact.LastSeenConfidence, _policy.StaleThreshold))
			stalePenalty = Mathf.Max(0f, _policy.StalePenalty);

		return observed + confidence + threat + hostile + distance - stalePenalty;
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
}
