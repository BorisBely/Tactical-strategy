using UnityEngine;

/// <summary>
/// Result of a single vision detection for one candidate in the current scan frame.
/// Means only: object was physically detected in this scan. Not EnemyKnowledge / memory / awareness.
/// TargetSelector chooses engage target from observations held by UnitPerception.
/// </summary>
public struct VisionObservation
{
	public Transform Target;
	public Vector3 Position;
	public Vector3 AimPoint;
	public bool HasAimPoint;
	public float DistanceSq;
	public bool IsVisible;
}
