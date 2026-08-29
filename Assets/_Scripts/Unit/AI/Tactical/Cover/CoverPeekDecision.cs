/// <summary>
/// Individual peek decision. Not stored on shared geometry.
/// </summary>
public struct CoverPeekDecision
{
	public CoverPeekDecisionKind Kind;
	public CoverPeekDirection Direction;
	public CoverLeanLevel Depth;
	public CoverPeekReason Reason;
	public bool PeekAvailable;
	public bool LeftAvailable;
	public bool RightAvailable;
	public bool VisibleWithoutLean;
	public float VisibilityGain;
	public float Risk;
	public bool FromCache;
	public int CandidateId;
	public CoverPeekOpportunity Opportunity;
	public CoverPeekDebugSnapshot Snapshot;

	public bool RequestsLean => Kind == CoverPeekDecisionKind.Lean && Depth != CoverLeanLevel.None;
}
