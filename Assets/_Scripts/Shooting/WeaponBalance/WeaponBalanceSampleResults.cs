using UnityEngine;

/// <summary>Recoil channel only — no θ (Phase G4).</summary>
public struct RecoilSampleResult
{
	public float VerticalKickShot1;
	public float HorizontalKickShot1;
	public float RecoveryPerSecond;

	public Vector2 OffsetAfter1;
	public Vector2 OffsetAfter3;
	public Vector2 OffsetAfter5;
	public Vector2 OffsetAfter8;
	public Vector2 OffsetAfter10;

	public float OffsetMagAfter1;
	public float OffsetMagAfter3;
	public float OffsetMagAfter5;
	public float OffsetMagAfter8;
	public float OffsetMagAfter10;

	public float RecoveryAfterPause02;
	public float RecoveryAfterPause04;
	public float RecoveryAfterPause08;

	public float DisplacementMetersAtDistance;
	public float NetDriftPerShot;
	public float MaxAbsYaw;
	public float MeanAbsYaw;
}

/// <summary>Accuracy channel only — θ and spread, no Offset (Phase G5).</summary>
public struct AccuracySampleResult
{
	public float ThetaHalfAngleDegrees;
	public float SpreadDiameterMeters;
}

/// <summary>Auto selector + planner discipline (Phase G6).</summary>
public struct FireControlSampleResult
{
	public static readonly float SelectorThresholdMeters =
		WeaponAutoModeSelectionUtility.AcceptableSpreadDiameterMeters;
	public const float PlannerCapMeters =
		WeaponAutoModeSelectionUtility.HumanTargetWidthMeters * 0.85f;

	public WeaponFireMode SelectedAutoFireMode;
	public WeaponAimMode SelectedAutoAimMode;
	public float PredictedGroupDiameterMeters;
	public bool AutoIsAcceptable;

	public WeaponFireMode PlannerEffectiveFireMode;
	public int PlannerSeriesLength;
	public float PlannerDisplacementMeters;
	public float PlannerResidualOffsetAfterPause;
}
