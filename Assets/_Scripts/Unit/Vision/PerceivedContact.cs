using UnityEngine;

/// <summary>
/// Frozen Vision knowledge. AI-0 reads this via <see cref="AIPerceptionFrame"/>, not by mining Q / DetectionProgress.
/// Do not add orders / search / tactics fields here. Vision ≠ decision.
/// </summary>
public sealed class PerceivedContact
{
	public Transform Target;
	public DetectionState State;
	public float DetectionProgress;

	/// <summary>
	/// Last real VisionObservation. Never overwritten by empty frames.
	/// </summary>
	public VisionObservation LastObservation;

	/// <summary>Current-frame quality snapshot (not long-term memory).</summary>
	public DetectionEvaluation CurrentEvaluation;

	public ObservationState ObservationState;
	public float LastSeenTime;
	public Vector3 LastSeenPosition;

	/// <summary>Where this observer currently believes the entity is. May equal LastSeenPosition.</summary>
	public Vector3 LastKnownPosition;

	/// <summary>0..1 trust in LastKnownPosition. Independent from DetectionProgress and IdentityConfidence.</summary>
	public float LastSeenConfidence;

	/// <summary>Committed identity. Unknown until IdentityConfidence reaches commit threshold.</summary>
	public PerceivedIdentity Identity;

	/// <summary>0..1 identification confidence. Independent from <see cref="DetectionProgress"/>.</summary>
	public float IdentityConfidence;

	/// <summary>Observer relationship. Not a copy of world UnitTeam.</summary>
	public PerceivedRelationship Relationship;

	/// <summary>Threat band. Hostile ≠ automatically High. Does not gate fire.</summary>
	public ThreatLevel Threat;

	public float SoundConfidence;
	public float SoundConfidenceInitial;
	public float SoundTime;
	public Vector3 SoundPosition;

	public float SharedConfidence;
	public float SharedConfidenceInitial;
	public float SharedTime;
	public Vector3 SharedPosition;

	public float VisibilityQuality => CurrentEvaluation.VisibilityQuality;

	public bool HasVisualEvidence => ObservationState == ObservationState.Observed;

	public bool HasSoundEvidence => SoundConfidence > 0f;

	public bool HasSharedEvidence => SharedConfidence > 0f;

	public bool HasNonVisualKnowledge => HasSoundEvidence || HasSharedEvidence;

	public bool EvidenceIsMixed => HasVisualEvidence && HasNonVisualKnowledge;

	public bool HasKnowledge =>
		LastSeenConfidence > 0f || HasSoundEvidence || HasSharedEvidence;

	public bool HasMemory => LastSeenConfidence > 0f;

	public bool IsMemoryForgotten => LastSeenConfidence <= 0f;

	public bool IsMemoryStale(float _staleThreshold = MemoryDecayMath.DefaultStaleThreshold)
	{
		return MemoryDecayMath.IsStale(LastSeenConfidence, _staleThreshold);
	}
}
