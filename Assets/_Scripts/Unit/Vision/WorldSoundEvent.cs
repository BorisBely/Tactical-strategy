using UnityEngine;

/// <summary>
/// World sound fact for perception. Not acoustics, not headphones, not Q.
/// </summary>
public struct WorldSoundEvent
{
	public Transform Source;
	public Vector3 Position;
	public SoundEventType Type;
	public float Strength;
	public float AudibleRangeMeters;
	public float Time;
}
