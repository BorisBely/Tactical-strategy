/// <summary>
/// Outcome of <see cref="UnitAIController.IssueCommand"/>.
/// </summary>
public readonly struct TacticalCommandResult
{
	public readonly bool Accepted;
	public readonly TacticalCommandRejectReason Reason;

	private TacticalCommandResult(bool _accepted, TacticalCommandRejectReason _reason)
	{
		Accepted = _accepted;
		Reason = _reason;
	}

	public static TacticalCommandResult Ok()
	{
		return new TacticalCommandResult(true, TacticalCommandRejectReason.None);
	}

	public static TacticalCommandResult Rejected(TacticalCommandRejectReason _reason)
	{
		return new TacticalCommandResult(false, _reason);
	}
}
