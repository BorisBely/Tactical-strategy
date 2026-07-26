using UnityEngine;

/// <summary>
/// Snapshot of one wheel's state for the current frame.
/// Read-only data — no logic inside.
/// </summary>
public struct WheelState
{
	public bool HasContact;
	public Vector3 ContactPoint;
	public Vector3 ContactNormal;
	public float Compression;        // 0=fully extended, 1=fully compressed
	public float CompressionSpeed;   // m/s, positive = compressing
	public float SuspensionForce;    // N, upward on body
	public float SlipRatio;
	public float SidewaysSlip;
	public float Rpm;
	public float MotorTorque;
	public float BrakeTorque;
	public float SteerAngle;
	public float SteerAngleDeg;
	public Vector3 WorldCenter;
	public float SuspensionCompression; // 0=full droop, 1=full compression      // wheel hub world position
}
