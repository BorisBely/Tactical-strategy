using UnityEngine;

/// <summary>
/// Prototype knobs for #13.2. Not a freeze. Not combat damage.
/// </summary>
public sealed class CoverClassificationSettings
{
	#region Public Fields
	public float ProbeDistanceMeters = 3f;
	public float SegmentThreshold = 0.5f;
	public float CornerSpanMeters = 1.2f;
	public float WallProbeMeters = 0.9f;
	public float StandingHeadMeters = 1.60f;
	public float StandingTorsoMeters = 1.30f;
	public float StandingPelvisMeters = 0.95f;
	public float StandingLegsMeters = 0.40f;
	public float CrouchHeadMeters = 0.95f;
	public float CrouchTorsoMeters = 0.70f;
	public float CrouchPelvisMeters = 0.50f;
	public float CrouchLegsMeters = 0.25f;
	#endregion
}
