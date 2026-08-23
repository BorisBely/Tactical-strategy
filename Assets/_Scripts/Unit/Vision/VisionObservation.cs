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

	/// <summary>
	/// Fraction of the sensor's tested target surface with LOS (0..1).
	/// Eye detail = weighted hit-zone samples. Far optic cheap = Head/Chest/Abdomen count.
	/// Legacy collider LOS → 1 if any sample hits.
	/// </summary>
	public float Exposure01;

	/// <summary>Which filter produced this observation. Knowledge does not branch on this.</summary>
	public VisionObservationSource Source;
}

public enum VisionObservationSource
{
	Eye = 0,
	Optic = 1
}
