using UnityEngine;

/// <summary>
/// Stage 17: one-shot ally report. Not a copied contact, not Observed, not AimPoint.
/// </summary>
public struct WorldAllyReportEvent
{
	public Transform Reporter;
	public Transform Subject;
	public Vector3 Position;
	public PerceivedIdentity ReportedIdentity;
	public float Confidence;
	public float RangeMeters;
	public float Time;
}
