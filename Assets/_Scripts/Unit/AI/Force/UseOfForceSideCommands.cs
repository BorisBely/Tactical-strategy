using UnityEngine;

/// <summary>
/// Applies <see cref="UseOfForceLevel"/> to every unit of a world side.
/// Debug / order helper — not a global static policy. Each unit still owns its controller field.
/// </summary>
public static class UseOfForceSideCommands
{
	#region Private Fields
	private static UseOfForceLevel s_LastPlayer = UseOfForceLevel.SelfDefense;
	private static UseOfForceLevel s_LastEnemy = UseOfForceLevel.SelfDefense;
	private static bool s_HasLastPlayer;
	private static bool s_HasLastEnemy;
	#endregion

	#region Public Methods
	public static int Count(UnitTeamId _side)
	{
		int count = 0;
		UnitTeam[] teams = FindTeams();
		for (int i = 0; i < teams.Length; i++)
		{
			UnitTeam team = teams[i];
			if (team != null && team.Team == _side)
				count++;
		}

		return count;
	}

	public static UseOfForceLevel Peek(UnitTeamId _side)
	{
		if (_side == UnitTeamId.Player && s_HasLastPlayer)
			return s_LastPlayer;
		if (_side == UnitTeamId.Enemy && s_HasLastEnemy)
			return s_LastEnemy;

		UnitAIController first = FindFirst(_side);
		if (first != null)
			return first.CurrentUseOfForceLevel;

		return UseOfForceLevel.SelfDefense;
	}

	public static int Apply(UnitTeamId _side, UseOfForceLevel _level)
	{
		Remember(_side, _level);
		int count = 0;
		UnitTeam[] teams = FindTeams();
		for (int i = 0; i < teams.Length; i++)
		{
			UnitTeam team = teams[i];
			if (team == null || team.Team != _side)
				continue;
			if (!TryGetOrAddController(team.gameObject, out UnitAIController controller))
				continue;
			controller.TrySetUseOfForcePolicy(_level);
			count++;
		}

		return count;
	}

	public static UseOfForceLevel Cycle(UnitTeamId _side)
	{
		UseOfForceLevel next = Next(Peek(_side));
		Apply(_side, next);
		return next;
	}

	public static UseOfForceLevel Next(UseOfForceLevel _level)
	{
		int i = ((int)_level + 1) % 5;
		return (UseOfForceLevel)i;
	}
	#endregion

	#region Private Methods
	private static void Remember(UnitTeamId _side, UseOfForceLevel _level)
	{
		if (_side == UnitTeamId.Player)
		{
			s_LastPlayer = _level;
			s_HasLastPlayer = true;
		}
		else if (_side == UnitTeamId.Enemy)
		{
			s_LastEnemy = _level;
			s_HasLastEnemy = true;
		}
	}

	private static UnitTeam[] FindTeams()
	{
		return Object.FindObjectsByType<UnitTeam>(FindObjectsInactive.Exclude);
	}

	private static UnitAIController FindFirst(UnitTeamId _side)
	{
		UnitTeam[] teams = FindTeams();
		for (int i = 0; i < teams.Length; i++)
		{
			UnitTeam team = teams[i];
			if (team == null || team.Team != _side)
				continue;
			if (team.TryGetComponent(out UnitAIController controller) && controller != null)
				return controller;
		}

		return null;
	}

	private static bool TryGetOrAddController(GameObject _go, out UnitAIController _controller)
	{
		_controller = null;
		if (_go == null)
			return false;
		if (!_go.TryGetComponent(out _controller) || _controller == null)
			_controller = _go.AddComponent<UnitAIController>();
		return _controller != null;
	}
	#endregion
}
