/// <summary>
/// Regression + dependency contract for VISION → PERCEPTION → TARGET SELECTION → COMBAT AI.
///
/// States (not equal): Observed (Perception) ≠ Selected (TargetSelector) ≠ Engageable (TargetEngageability) ≠ AI intent.
///
/// Allowed flow:
/// UnitObservationSource → UnitVision
/// VisionCandidateProvider / VisionGeometry / VisibilityChecker → UnitVision
/// UnitVision → UnitPerception.ApplyVisionFrame
/// UnitPerception.PerceptionFrameApplied → TargetSelector.SelectFromPerception
/// TargetSelector + TargetEngageability → Combat / AI / Nav / RTS / Vehicles
///
/// Forbidden: Perception/TargetSelector → UnitVision; Vision scan → TargetSelector.Select*;
/// UnitVision.VisibleTarget / GetEngageableVisibleTarget / VisibleTargetChanged (removed Stage F).
///
/// Stage F complete — architectural freeze before Stage G (detection models / stealth / awareness).
/// Manual smoke checklist: detect → select → fire/aim → nav engage → RTS ForcedPriority → vehicle gunner/passenger.
/// Scan-only stays on UnitVision: RequestImmediateScan, NotifyWeaponReadyChanged, ResolveHalfFovDegreesForScan, DeferNextScan.
///
/// Out of scope (Stage G+): suspicion, awareness tiers, last-known, sound, stealth.
/// </summary>
internal static class VisionSystemContract
{
}
