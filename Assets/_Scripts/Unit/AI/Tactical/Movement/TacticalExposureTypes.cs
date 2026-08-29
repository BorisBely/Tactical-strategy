using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug label for a sample. Scoring uses numbers, not this enum.
/// </summary>
public enum TacticalExposureRisk
{
	Safe = 0,
	Exposed = 1,
	Dangerous = 2,
	Critical = 3
}

/// <summary>
/// One bounded sample along a route. Not a NavMesh speed command.
/// </summary>
public struct TacticalExposureSample
{
	public Vector3 Position;
	public float DistanceAlongMeters;
	public float SegmentMeters;
	public float TravelTimeSeconds;
	public float Exposure01;
	public float Cover01;
	public float MetersToNextCover;
	public TacticalExposureRisk Risk;
}

/// <summary>
/// #14.4 exposure profile. Average is still #14.1 Exposure01. Peak / duration are extra.
/// Prototype, not freeze.
/// </summary>
public struct TacticalExposureProfileSummary
{
	public int SampleCount;
	public float Average01;
	public float Peak01;
	public float ExposureCost;
	public float TimeAboveThresholdSeconds;
	public float TimeExposedSeconds;
	public bool FromCache;
}
