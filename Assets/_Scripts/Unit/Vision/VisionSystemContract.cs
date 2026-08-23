/// <summary>
/// Regression + dependency contract for VISION → PERCEPTION → TARGET SELECTION → COMBAT AI.
///
/// States (not equal): Observed (Perception) ≠ Selected (TargetSelector) ≠ Engageable ≠ AI intent.
/// After G1/G2 also: DetectionState ≠ ObservationState; Detected ≠ Selected.
/// Detected + RecentlyLost is valid.
/// After G3: DetectionProgress ≠ IdentityConfidence; Detected + Identity=Unknown is valid;
/// Relationship=Hostile + Threat=Low is valid; PerceivedIdentity ≠ world UnitTeam.
/// After G4: DetectionProgress ≠ LastSeenConfidence ≠ IdentityConfidence.
/// ObservationState.Lost ≠ memory forgotten (confidence → 0). Stale is derived from LastSeenConfidence.
/// After G5: candidate list = PerceivedContacts, not Perception.Observations.
/// Unknown identity is selectable. Forgotten is not. LastKnownPosition ≠ combat AimPoint.
/// GetEngageableSelectedTarget requires LOS-confirmed aim. Selected ≠ Engageable ≠ Fire.
/// After G6: Selected ≠ Engageable ≠ EngagementDecision ≠ Fire.
/// Memory-only selected target → Track, never Fire. LastKnownPosition ≠ combat AimPoint.
/// After G7: Sound/Shared are extra evidence channels on PerceivedContact, not Vision.
/// HasKnowledge includes non-visual confidence. Sound-only / shared-only → Track, never Fire.
/// After G8: LOD / scan tiers are a compute budget, not DetectionProgress and not a Q penalty.
/// Skip-scan must not ApplyVisionFrame(empty). Not scanning this frame ≠ unseen.
/// Coarse range/FOV (with pad) runs before any LOS. Only Detail (T3) may apply a vision frame.
/// Unit.prefab VisionRange = 150 m eye (perception; optic may extend to 300 in Aiming).
/// Reload/misfire retain uses UnitVision.ResolvedMaxRange. Do not bake 18 m as a combat cap.
/// This vision contract is closed at G8. Search / hunt AI is a separate system.
///
/// Allowed combat flow (G6+G7+G8):
/// UnitObservationSource → UnitVision (LOD scheduler, cheap→expensive) → UnitPerception.ApplyVisionFrame
/// Sound/Shared → UnitPerception.ApplySoundEvents / ApplySharedEvents
/// → DetectionProcessor → PerceivedContact (vision + optional sound/shared channels)
/// → TargetSelector (eligibility + score) → SelectedTarget
/// → EngagementDecisionController → Combat execution
///
/// LastObservation / LastSeen* update only on real VisionObservation evidence — never on empty frames.
/// CurrentEvaluation is a frame snapshot, not long-term memory.
/// Identity evidence is VisualIdentityEvidence (world look) mapped by observer side, or a per-observer cue; never target UnitTeam.
/// LastSeenConfidence decays only while not Observed; IdentityConfidence does not decay in G4.
/// SoundConfidence / SharedConfidence decay on their own horizons and do not stop G4.
/// Sound/Shared never write LastObservation, never set ObservationState.Observed,
/// never create AimPoint / LOS / Fire. G7 v1 does not commit Identity from sound/shared.
///
/// Forbidden: TargetSelector enumerating Perception.Observations or SoundEvents as candidates;
/// fake VisionObservation for sound/shared; sound/shared AimPoint/LOS/Fire;
/// LastKnown as fire aim; DetectionProcessor calling Fire or mutating UnitTeam;
/// knowledge/progress/identity/memory fields on VisionObservation;
/// if (target.UnitTeam == Enemy) Identity = Hostile;
/// committing Identity from sound/shared (G7 v1);
/// Search AI in Vision / DetectionProcessor / TargetSelector / EngagementDecisionController;
/// LOD / VisionScanTier / LOS cache on TargetSelector or EngagementDecision;
/// LOD → confidence / Q / DetectionProgress penalty;
/// skip-scan applying an empty vision frame (fake RecentlyLost);
/// baking 18 m as a combat retain cap;
/// treating LastKnown as fire AimPoint;
/// EngagementDecision inside Vision / DetectionProcessor / TargetSelector;
/// EngagementDecisionController calling Fire / StartFiring / hitscan;
/// LastKnown as fire aim; mutating UnitTeam from perception or engagement.
/// </summary>
internal static class VisionSystemContract
{
}
