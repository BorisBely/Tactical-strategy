using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies <see cref="IUnitTacticalCommand"/> to every living unit of a world side.
/// Debug / scenario helper — each unit still owns its <see cref="UnitAIController"/>.
/// </summary>
public static class TacticalSideCommands
{
	#region Constants
	private const float c_SpreadMeters = 1.35f;
	#endregion

	#region Private Fields
	private static readonly List<UnitAIController> s_Buffer = new List<UnitAIController>(32);
	private static readonly List<UnitTeam> s_Teams = new List<UnitTeam>(64);
	#endregion

	#region Public Methods
	public static int Idle(UnitTeamId _side)
	{
		Collect(_side);
		int count = 0;
		for (int i = 0; i < s_Buffer.Count; i++)
		{
			if (s_Buffer[i].SetIdle())
				count++;
		}

		return count;
	}

	public static int Defense(UnitTeamId _side, Vector3 _point)
	{
		return ApplyPoint(_side, _point, false, (_ai, _dest) => _ai.SetDefense(_dest));
	}

	public static int Attack(UnitTeamId _side, Vector3 _point, Transform _target)
	{
		Collect(_side);
		int count = 0;
		int n = s_Buffer.Count;
		Vector3 center = _target != null ? _target.position : _point;
		for (int i = 0; i < n; i++)
		{
			Vector3 dest = Spread(center, i, n);
			if (s_Buffer[i].SetAttack(dest, _target))
				count++;
		}

		return count;
	}

	public static int Search(UnitTeamId _side, Vector3 _point)
	{
		return ApplyPoint(_side, _point, true, (_ai, _dest) => _ai.SetSearch(_dest));
	}

	public static int SearchFromMemory(UnitTeamId _side)
	{
		Collect(_side);
		int count = 0;
		for (int i = 0; i < s_Buffer.Count; i++)
		{
			if (s_Buffer[i].SetSearch())
				count++;
		}

		return count;
	}

	public static int Retreat(UnitTeamId _side, Vector3 _point)
	{
		return ApplyPoint(_side, _point, true, (_ai, _dest) => _ai.SetRetreat(_dest));
	}

	public static int Flee(UnitTeamId _side, Vector3 _point)
	{
		return ApplyPoint(_side, _point, true, (_ai, _dest) => _ai.SetFlee(_dest));
	}

	public static string Describe(UnitTeamId _side)
	{
		int idle = 0;
		int defense = 0;
		int attack = 0;
		int search = 0;
		int retreat = 0;
		int flee = 0;
		int missing = 0;

		UnitTeam.CopyActive(s_Teams);
		for (int i = 0; i < s_Teams.Count; i++)
		{
			UnitTeam team = s_Teams[i];
			if (team == null || team.Team != _side)
				continue;
			if (!team.TryGetComponent(out UnitAIController ai) || ai == null)
			{
				missing++;
				continue;
			}

			switch (ai.CurrentState)
			{
				case UnitAIState.Defense:
					defense++;
					break;
				case UnitAIState.Attack:
					attack++;
					break;
				case UnitAIState.Search:
					search++;
					break;
				case UnitAIState.Retreat:
					retreat++;
					break;
				case UnitAIState.Flee:
					flee++;
					break;
				default:
					idle++;
					break;
			}
		}

		return "Idle " + idle +
		       "  Def " + defense +
		       "  Atk " + attack +
		       "  Srch " + search +
		       "  Ret " + retreat +
		       "  Flee " + flee +
		       (missing > 0 ? "  —" + missing : string.Empty);
	}
	#endregion

	#region Private Methods
	private static int ApplyPoint(
		UnitTeamId _side,
		Vector3 _point,
		bool _spread,
		Func<UnitAIController, Vector3, bool> _apply)
	{
		Collect(_side);
		int count = 0;
		int n = s_Buffer.Count;
		for (int i = 0; i < n; i++)
		{
			Vector3 dest = _spread ? Spread(_point, i, n) : _point;
			if (_apply(s_Buffer[i], dest))
				count++;
		}

		return count;
	}

	private static void Collect(UnitTeamId _side)
	{
		s_Buffer.Clear();
		UnitTeam.CopyActive(s_Teams);
		for (int i = 0; i < s_Teams.Count; i++)
		{
			UnitTeam team = s_Teams[i];
			if (team == null || team.Team != _side)
				continue;
			if (!TryGetOrAddController(team.gameObject, _side, out UnitAIController controller))
				continue;
			s_Buffer.Add(controller);
		}
	}

	private static bool TryGetOrAddController(GameObject _go, UnitTeamId _side, out UnitAIController _controller)
	{
		_controller = null;
		if (_go == null)
			return false;

		bool added = !_go.TryGetComponent(out _controller) || _controller == null;
		if (added)
			_controller = _go.AddComponent<UnitAIController>();
		if (_controller == null)
			return false;

		_controller.DrawSearchHud = false;
		if (added)
			_controller.TrySetUseOfForcePolicy(UseOfForceSideCommands.Peek(_side));

		return true;
	}

	private static Vector3 Spread(Vector3 _center, int _index, int _count)
	{
		if (_count <= 1)
			return _center;

		float angle = _index / (float)_count * Mathf.PI * 2f;
		return _center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * c_SpreadMeters;
	}
	#endregion
}
