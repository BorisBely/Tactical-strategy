using UnityEngine;

/// <summary>
/// #14.8 moving-lean action. Not a <see cref="UnitAIState"/>. Not a new LeanController.
/// </summary>
public enum TacticalMovingLeanAction
{
	None = 0,
	Lean = 1,
	Exit = 2
}

/// <summary>
/// Why moving lean started, skipped, or ended. Thresholds are prototype, not freeze.
/// </summary>
public enum TacticalMovingLeanReason
{
	None = 0,
	NoOpportunity = 1,
	NoBenefit = 2,
	FarFromCorner = 3,
	AlreadyVisible = 4,
	SectorGain = 5,
	TraversalCost = 6,
	NotMoving = 7,
	NotInCorridor = 8,
	CornerPassed = 9,
	OpportunityEnded = 10,
	ImmediateThreat = 11,
	Replan = 12,
	Arrival = 13
}

/// <summary>
/// Authored / inferred snapshot for one moving-lean decision.
/// Overlay does not Move. #13.7 still owns the executor.
/// </summary>
public struct TacticalMovingLeanSituation
{
	public bool Present;
	public bool Moving;
	public bool HasCorner;
	public bool InCorridor;
	public float DistanceToCornerMeters;
	public bool VisibleWithoutLean;
	public bool LeftAvailable;
	public bool RightAvailable;
	public float LeftVisibilityGain;
	public float RightVisibilityGain;
	public float LeftExposure01;
	public float RightExposure01;
	public bool LeftSmallSufficient;
	public bool LeftMediumSufficient;
	public bool LeftDeepSufficient;
	public bool RightSmallSufficient;
	public bool RightMediumSufficient;
	public bool RightDeepSufficient;
	public float ExposureWithoutLean;
	public float TraversalCost01;
	public bool ImmediateThreat;
	public bool Replan;
	public bool Arrived;
	public bool RouteChanged;
	public bool TargetChanged;
	public bool Approach;
	public bool CornerPassed;
	public bool WallCorridor;
	public bool CurrentlyLeaning;
	public CoverPeekDirection CurrentDirection;
	public CoverLeanLevel CurrentDepth;
}

/// <summary>
/// One moving-lean choice. Pose is applied through <see cref="CoverMovementLeanContract"/>.
/// </summary>
public struct TacticalMovingLeanDecision
{
	public TacticalMovingLeanAction Action;
	public CoverPeekDirection Direction;
	public CoverLeanLevel Depth;
	public TacticalMovingLeanReason Reason;
	public bool Opportunity;
	public float VisibilityGain;
	public float ExposureChange;
	public float TraversalCost01;
	public CoverMovementLeanRequest Request;
	public bool FromCache;

	public bool RequestsLean =>
		Action == TacticalMovingLeanAction.Lean && Depth != CoverLeanLevel.None;
}
