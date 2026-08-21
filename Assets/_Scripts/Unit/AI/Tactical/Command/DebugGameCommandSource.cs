using UnityEngine;

/// <summary>
/// Stage 6.2 game source for tests and Play. Not an AI debug button and not RTS.
/// Builds <see cref="TacticalCommand"/> with <see cref="TacticalCommandSource.Game"/> and sends it through
/// <see cref="GameCommandService"/>.
/// </summary>
public static class DebugGameCommandSource
{
	#region Public Methods
	public static GameCommandResult Defense(Component _unit, Vector3 _position)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Defense(_position, TacticalCommandSource.Game));
	}

	public static GameCommandResult Attack(Component _unit, Vector3 _position, Transform _target = null)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Attack(_position, _target, TacticalCommandSource.Game));
	}

	public static GameCommandResult Search(Component _unit, Vector3 _position)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Search(_position, TacticalCommandSource.Game));
	}

	public static GameCommandResult Retreat(Component _unit, Vector3 _position)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Retreat(_position, TacticalCommandSource.Game));
	}

	public static GameCommandResult Flee(Component _unit, Vector3 _position)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Flee(_position, TacticalCommandSource.Game));
	}

	public static GameCommandResult Cancel(Component _unit)
	{
		return GameCommandService.Issue(_unit, TacticalCommand.Cancel(TacticalCommandSource.Game));
	}
	#endregion
}
