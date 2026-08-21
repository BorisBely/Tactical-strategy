/// <summary>
/// Production socket for game orders. Game code must not depend on <see cref="UnitAIController"/>.
/// </summary>
public interface ITacticalCommandReceiver
{
	TacticalCommandResult IssueCommand(in TacticalCommand _command);
}
