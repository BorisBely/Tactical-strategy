using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сторона юнита или машины. Одна сущность на объект; меняется в рантайме через <see cref="SetTeam"/>.
/// Слой (Unit/Vehicle) — только физика и raycast; сторона живёт здесь, не в Layer.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitTeam : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeamId m_Team = UnitTeamId.Enemy;

	private static readonly List<UnitTeam> s_Active = new List<UnitTeam>(64);
	#endregion

	#region Public Properties
	public UnitTeamId Team => m_Team;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetActiveRegistry()
	{
		s_Active.Clear();
	}

	private void OnEnable()
	{
		if (!s_Active.Contains(this))
			s_Active.Add(this);
	}

	private void OnDisable()
	{
		s_Active.Remove(this);
	}
	#endregion

	#region Public Methods
	public void SetTeam(UnitTeamId _team)
	{
		if (m_Team == _team)
			return;
		m_Team = _team;
	}

	/// <summary>
	/// Living enabled teams. Strips destroyed entries. Order is registration order, not hierarchy.
	/// </summary>
	public static void CopyActive(List<UnitTeam> _buffer)
	{
		if (_buffer == null)
			return;

		_buffer.Clear();
		int write = 0;
		for (int i = 0; i < s_Active.Count; i++)
		{
			UnitTeam team = s_Active[i];
			if (team == null)
				continue;

			s_Active[write++] = team;
			_buffer.Add(team);
		}

		if (write < s_Active.Count)
			s_Active.RemoveRange(write, s_Active.Count - write);
	}
	#endregion
}
