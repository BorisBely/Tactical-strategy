using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Stable per-session slots: P01, E01, N01. Targets without <see cref="UnitTeam"/> keep a Tgt_ name.
/// </summary>
public static class UnitActionLogIdentity
{
	#region Private Fields
	private static readonly Dictionary<EntityId, string> s_Slots = new Dictionary<EntityId, string>(64);
	private static int s_Player;
	private static int s_Enemy;
	private static int s_Neutral;
	private static int s_Other;
	#endregion

	#region Public Methods
	public static string Slot(Component _component)
	{
		if (_component == null)
			return "?";
		return Slot(_component.transform);
	}

	public static string Slot(Transform _transform)
	{
		if (_transform == null)
			return "?";

		Transform unitRoot = ResolveUnitRoot(_transform);
		EntityId id = unitRoot.GetEntityId();
		if (s_Slots.TryGetValue(id, out string existing))
			return existing;

		string slot = AssignSlot(unitRoot);
		s_Slots[id] = slot;
		return slot;
	}

	public static string Callsign(Transform _unitRoot)
	{
		if (_unitRoot == null)
			return "?";
		if (_unitRoot.TryGetComponent(out UnitRosterDisplayState roster) &&
		    !string.IsNullOrWhiteSpace(roster.DisplayName))
			return roster.DisplayName;
		return _unitRoot.name;
	}

	public static Transform ResolveUnitRoot(Transform _transform)
	{
		if (_transform == null)
			return null;
		UnitTeam team = _transform.GetComponentInParent<UnitTeam>();
		if (team != null)
			return team.transform;
		return _transform.root != null ? _transform.root : _transform;
	}

	public static string SanitizeFileName(string _name)
	{
		if (string.IsNullOrWhiteSpace(_name))
			return "Unit";
		string safe = _name.Trim();
		foreach (char c in Path.GetInvalidFileNameChars())
			safe = safe.Replace(c, '_');
		safe = safe.Replace(' ', '_');
		if (safe.Length > 48)
			safe = safe.Substring(0, 48);
		return safe;
	}

	public static string TeamFolder(UnitTeamId _team)
	{
		return _team switch
		{
			UnitTeamId.Player => "Player",
			UnitTeamId.Enemy => "Enemy",
			UnitTeamId.Neutral => "Neutral",
			_ => "Other"
		};
	}

	public static void ResetStatics()
	{
		s_Slots.Clear();
		s_Player = 0;
		s_Enemy = 0;
		s_Neutral = 0;
		s_Other = 0;
	}
	#endregion

	#region Private Methods
	private static string AssignSlot(Transform _unitRoot)
	{
		UnitTeam team = _unitRoot.GetComponent<UnitTeam>();
		if (team == null)
		{
			s_Other++;
			string name = SanitizeFileName(_unitRoot.name);
			return "Tgt_" + name;
		}

		int index;
		string prefix;
		switch (team.Team)
		{
			case UnitTeamId.Player:
				s_Player++;
				index = s_Player;
				prefix = "P";
				break;
			case UnitTeamId.Enemy:
				s_Enemy++;
				index = s_Enemy;
				prefix = "E";
				break;
			case UnitTeamId.Neutral:
				s_Neutral++;
				index = s_Neutral;
				prefix = "N";
				break;
			default:
				s_Other++;
				index = s_Other;
				prefix = "X";
				break;
		}

		return prefix + index.ToString("00");
	}
	#endregion
}
