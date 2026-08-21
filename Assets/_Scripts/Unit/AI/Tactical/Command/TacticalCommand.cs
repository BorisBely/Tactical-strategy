using UnityEngine;

/// <summary>
/// Stage 6.1 game order. Says what and where. Does not say how to fight or walk.
/// Maps to <see cref="UnitAIState"/> only after <see cref="UnitAIController.IssueCommand"/> accepts it.
/// </summary>
public readonly struct TacticalCommand
{
	public readonly TacticalCommandType Type;
	public readonly Vector3 Position;
	public readonly bool HasPosition;
	public readonly Transform Target;
	public readonly TacticalCommandSource Source;

	private TacticalCommand(
		TacticalCommandType _type,
		Vector3 _position,
		bool _hasPosition,
		Transform _target,
		TacticalCommandSource _source)
	{
		Type = _type;
		Position = _position;
		HasPosition = _hasPosition;
		Target = _target;
		Source = _source;
	}

	public static TacticalCommand Defense(Vector3 _position, TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Defense, _position, true, null, _source);
	}

	public static TacticalCommand Attack(
		Vector3 _position,
		Transform _target = null,
		TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Attack, _position, true, _target, _source);
	}

	public static TacticalCommand Search(Vector3 _position, TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Search, _position, true, null, _source);
	}

	public static TacticalCommand Retreat(Vector3 _position, TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Retreat, _position, true, null, _source);
	}

	public static TacticalCommand Flee(Vector3 _position, TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Flee, _position, true, null, _source);
	}

	public static TacticalCommand Cancel(TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(TacticalCommandType.Cancel, default, false, null, _source);
	}

	/// <summary>
	/// Raw payload. Named factories are the game API. Use this for invalid-data tests.
	/// </summary>
	public static TacticalCommand Create(
		TacticalCommandType _type,
		Vector3 _position,
		bool _hasPosition,
		Transform _target = null,
		TacticalCommandSource _source = TacticalCommandSource.Test)
	{
		return new TacticalCommand(_type, _position, _hasPosition, _target, _source);
	}
}
