using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 6.2 production channel. Resolves a unit, checks receiver presence, forwards <see cref="TacticalCommand"/>.
/// Does not know the transition table, does not call Fire / Navigate, does not merge repeated Attack.
/// </summary>
public static class GameCommandService
{
	#region Public Methods
	public static GameCommandResult Issue(GameObject _unit, in TacticalCommand _command)
	{
		if (_unit == null)
			return Reject(null, in _command, GameCommandRejectReason.InvalidUnit);
		return Issue(_unit.transform, in _command);
	}

	public static GameCommandResult Issue(Component _unit, in TacticalCommand _command)
	{
		if (_unit == null)
			return Reject(null, in _command, GameCommandRejectReason.InvalidUnit);
		return Issue(_unit.transform, in _command);
	}

	public static GameCommandResult Issue(Transform _unit, in TacticalCommand _command)
	{
		if (_unit == null)
			return Reject(null, in _command, GameCommandRejectReason.InvalidUnit);

		Transform root = UnitActionLogIdentity.ResolveUnitRoot(_unit);
		if (root == null)
			return Reject(_unit, in _command, GameCommandRejectReason.InvalidUnit);

		Log("issue", root, in _command, GameCommandRejectReason.None);

		if (!root.gameObject.activeInHierarchy)
			return Reject(root, in _command, GameCommandRejectReason.InvalidUnit, false);

		if (root.TryGetComponent(out UnitHealth health) && health.IsDead)
			return Reject(root, in _command, GameCommandRejectReason.InvalidUnit, false);

		if (!root.TryGetComponent(out ITacticalCommandReceiver receiver) || receiver == null)
			return Reject(root, in _command, GameCommandRejectReason.NoAI, false);

		TacticalCommandResult inner = receiver.IssueCommand(in _command);
		if (!inner.Accepted)
			return Reject(root, in _command, MapReason(inner.Reason), false);

		Log("accepted", root, in _command, GameCommandRejectReason.None);
		return GameCommandResult.Ok();
	}

	/// <summary>
	/// Same <paramref name="_command"/> to each recipient via <see cref="Issue"/>. Empty list → 0, no throw.
	/// Does not merge repeated Attack. Returns the number Accepted.
	/// </summary>
	public static int IssueMany(
		IReadOnlyList<Component> _recipients,
		in TacticalCommand _command,
		List<GameCommandResult> _results = null)
	{
		if (_recipients == null || _recipients.Count == 0)
			return 0;

		int accepted = 0;
		for (int i = 0; i < _recipients.Count; i++)
		{
			GameCommandResult result = Issue(_recipients[i], in _command);
			if (_results != null)
				_results.Add(result);
			if (result.Accepted)
				accepted++;
		}

		return accepted;
	}
	#endregion

	#region Private Methods
	private static GameCommandResult Reject(
		Component _actor,
		in TacticalCommand _command,
		GameCommandRejectReason _reason,
		bool _logIssue = true)
	{
		if (_logIssue)
			Log("issue", _actor, in _command, GameCommandRejectReason.None);
		Log("rejected", _actor, in _command, _reason);
		return GameCommandResult.Rejected(_reason);
	}

	private static void Log(
		string _verb,
		Component _actor,
		in TacticalCommand _command,
		GameCommandRejectReason _reason)
	{
		if (!UnitActionLog.Enabled)
			return;

		string slot = _actor != null ? UnitActionLog.Slot(_actor) : "none";
		string pos = _command.HasPosition ? UnitActionLog.Vec(_command.Position) : "none";
		string tgt = _command.Target != null ? UnitActionLog.Slot(_command.Target) : "none";
		string payload =
			_verb +
			" unit=" + slot +
			" type=" + _command.Type +
			" pos=" + pos +
			" tgt=" + tgt +
			" source=" + _command.Source;
		if (_reason != GameCommandRejectReason.None)
			payload += " reason=" + _reason;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.GameCmd, payload);
		UnitActionLog.Timeline(UnitActionLog.GameCmd, payload);
	}

	private static GameCommandRejectReason MapReason(TacticalCommandRejectReason _reason)
	{
		switch (_reason)
		{
			case TacticalCommandRejectReason.None:
				return GameCommandRejectReason.None;
			case TacticalCommandRejectReason.InvalidStateTransition:
				return GameCommandRejectReason.InvalidStateTransition;
			case TacticalCommandRejectReason.InvalidCommandData:
				return GameCommandRejectReason.InvalidCommandData;
			case TacticalCommandRejectReason.MissingDestination:
				return GameCommandRejectReason.MissingDestination;
			default:
				return GameCommandRejectReason.InvalidCommandData;
		}
	}
	#endregion
}
