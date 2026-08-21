using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves command recipients. No army cache. Neutral is in neither audience.
/// </summary>
public static class GameCommandRecipientQuery
{
	#region Private Fields
	private static readonly List<RtsUnitMember> s_Selected = new List<RtsUnitMember>(16);
	private static readonly List<UnitTeam> s_Teams = new List<UnitTeam>(64);
	#endregion

	#region Public Methods
	public static int Collect(GameCommandAudience _audience, List<Component> _buffer)
	{
		if (_buffer == null)
			return 0;

		_buffer.Clear();
		switch (_audience)
		{
			case GameCommandAudience.PlayerSelected:
				CollectPlayerSelected(_buffer);
				break;
			case GameCommandAudience.EnemyDebug:
				CollectEnemyDebug(_buffer);
				break;
		}

		return _buffer.Count;
	}

	/// <summary>
	/// Enemy Debug only: add <see cref="UnitAIController"/> when no receiver exists.
	/// Player Selected must not call this.
	/// </summary>
	public static int EnsureEnemyDebugReceivers(IReadOnlyList<Component> _recipients)
	{
		if (_recipients == null || _recipients.Count == 0)
			return 0;

		int attached = 0;
		for (int i = 0; i < _recipients.Count; i++)
		{
			Component recipient = _recipients[i];
			if (recipient == null)
				continue;

			GameObject go = recipient.gameObject;
			if (go.TryGetComponent(out ITacticalCommandReceiver receiver) && receiver != null)
				continue;

			UnitAIController controller = go.AddComponent<UnitAIController>();
			if (controller == null)
				continue;

			controller.DrawSearchHud = false;
			controller.TrySetUseOfForcePolicy(UseOfForceSideCommands.Peek(UnitTeamId.Enemy));
			attached++;
		}

		return attached;
	}
	#endregion

	#region Private Methods
	private static void CollectPlayerSelected(List<Component> _buffer)
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;

		manager.CopyValidSelectedUnits(s_Selected);
		for (int i = 0; i < s_Selected.Count; i++)
		{
			RtsUnitMember member = s_Selected[i];
			if (member != null)
				_buffer.Add(member);
		}
	}

	private static void CollectEnemyDebug(List<Component> _buffer)
	{
		UnitTeam.CopyActive(s_Teams);
		for (int i = 0; i < s_Teams.Count; i++)
		{
			UnitTeam team = s_Teams[i];
			if (team == null || !team.isActiveAndEnabled || team.Team != UnitTeamId.Enemy)
				continue;
			if (!team.gameObject.activeInHierarchy)
				continue;
			if (team.GetComponent<VehicleController>() != null)
				continue;
			if (team.GetComponent<UnitFactionConfigurator>() == null &&
			    team.GetComponent<UnitHealth>() == null)
				continue;
			if (team.TryGetComponent(out UnitHealth health) && health != null && health.IsDead)
				continue;

			_buffer.Add(team);
		}
	}
	#endregion
}
