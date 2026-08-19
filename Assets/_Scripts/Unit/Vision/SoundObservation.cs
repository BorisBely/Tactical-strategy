using UnityEngine;

/// <summary>
/// Physical sound evidence for one emitter. Not VisionObservation.
/// No IsVisible, AimPoint, UnitTeam, Hostile, or Fire.
/// </summary>
public struct SoundObservation
{
	public Transform Source;
	public Vector3 Position;
	public Vector3 Direction;
	public float Loudness;
	public SoundEventType Type;
	public float Time;
	public float SourceConfidence;
}
