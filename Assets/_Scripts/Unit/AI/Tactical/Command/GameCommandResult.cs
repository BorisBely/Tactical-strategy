/// <summary>
/// Outcome of <see cref="GameCommandService.Issue"/>.
/// </summary>
public readonly struct GameCommandResult
{
	public readonly bool Accepted;
	public readonly GameCommandRejectReason Reason;

	private GameCommandResult(bool _accepted, GameCommandRejectReason _reason)
	{
		Accepted = _accepted;
		Reason = _reason;
	}

	public static GameCommandResult Ok()
	{
		return new GameCommandResult(true, GameCommandRejectReason.None);
	}

	public static GameCommandResult Rejected(GameCommandRejectReason _reason)
	{
		return new GameCommandResult(false, _reason);
	}
}
