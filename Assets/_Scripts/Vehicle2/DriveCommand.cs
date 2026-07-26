using UnityEngine;

/// <summary>
/// Raw input command — throttle, steer, brake. No physics.
/// </summary>
public struct DriveCommand
{
	public float Throttle;  // -1..1 (negative = reverse)
	public float Steer;     // -1..1 (negative = left)
	public bool Brake;
}
