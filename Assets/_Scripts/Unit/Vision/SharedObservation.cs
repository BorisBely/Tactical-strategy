using UnityEngine;

/// <summary>
/// Reported knowledge from another observer. Not VisionObservation and not LOS.
/// Subject is the reported entity key; SourceUnit is who told us.
/// </summary>
public struct SharedObservation
{
	public Transform Subject;
	public Transform SourceUnit;
	public Vector3 Position;
	public float Time;
	public float SourceConfidence;
	public SharedInformationType InformationType;
	public float FreshnessSeconds;
	public PerceivedIdentity ReportedIdentity;
}
