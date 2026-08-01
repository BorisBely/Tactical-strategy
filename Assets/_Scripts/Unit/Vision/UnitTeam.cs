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
	#endregion

	#region Public Properties
	public UnitTeamId Team => m_Team;
	#endregion

	#region Public Methods
	public void SetTeam(UnitTeamId _team)
	{
		if (m_Team == _team)
			return;
		m_Team = _team;
	}
	#endregion
}
