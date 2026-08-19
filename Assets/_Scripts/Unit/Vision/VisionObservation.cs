using UnityEngine;

/// <summary>
/// Result of a single vision detection for one candidate in the current scan frame.
/// Physical facts only — not knowledge / DetectionProgress / PerceivedContact.
/// </summary>
public struct VisionObservation
{
	public Transform Target;
	public Vector3 Position;
	public Vector3 AimPoint;
	public bool HasAimPoint;
	public float DistanceSq;
	public bool IsVisible;

	/// <summary>Horizontal angle (degrees) from observer forward XZ to aim/target point. 0 = center of gaze.</summary>
	public float FovOffsetDegrees;

	/// <summary>Fraction of hit-zone aim samples with LOS (0..1). Legacy collider LOS → 1.</summary>
	public float Exposure01;
}
