using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальный реестр юнитов для зрения: игрок и остальные (враги + нейтралы).
/// Ссылки задаются при спавне/расстановке; не ищет объекты по сцене.
/// Tech-debt: GetOpponents currently mixes “who exists” with “who is an opponent”.
/// Spatial / relationship filters belong after GetOpponents (CandidateProvider) — do not expand mix here.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitVisionRegistry : MonoBehaviour
{
	#region Private Fields
	[Header("Runtime Debug")]
	[SerializeField] private List<UnitVision> m_PlayerUnits = new List<UnitVision>(24);
	[SerializeField] private List<UnitVision> m_EnemyUnits = new List<UnitVision>(64);
	[SerializeField] private List<UnitVision> m_NeutralUnits = new List<UnitVision>(64);
	#endregion

	#region Public Methods
	public void Register(UnitVision _unit)
	{
		if (_unit == null)
			return;

		UnitTeam team = _unit.GetComponent<UnitTeam>();
		if (team == null)
		{
			Debug.LogError("UnitVisionRegistry: у юнита нет UnitTeam.", _unit);
			return;
		}

		Unregister(_unit);

		List<UnitVision> list = GetTeamList(team.Team);
		if (list == null)
			return;

		if (list.Contains(_unit))
			return;

		list.Add(_unit);
	}

	public void Unregister(UnitVision _unit)
	{
		if (_unit == null)
			return;

		m_PlayerUnits.Remove(_unit);
		m_EnemyUnits.Remove(_unit);
		m_NeutralUnits.Remove(_unit);
	}

	/// <summary>Кого этот юнит проверяет как потенциальные цели (без рейкастов).</summary>
	public void GetOpponents(UnitTeamId _viewerTeam, List<UnitVision> _outBuffer)
	{
		_outBuffer.Clear();

		if (_viewerTeam == UnitTeamId.Player)
			_outBuffer.AddRange(m_EnemyUnits);
		else if (_viewerTeam == UnitTeamId.Enemy)
			_outBuffer.AddRange(m_PlayerUnits);
	}

	public int PlayerUnitCount => m_PlayerUnits.Count;
	public int EnemyUnitCount => m_EnemyUnits.Count;
	public int NeutralUnitCount => m_NeutralUnits.Count;
	#endregion

	#region Private Methods
	private List<UnitVision> GetTeamList(UnitTeamId _team)
	{
		return _team switch
		{
			UnitTeamId.Player => m_PlayerUnits,
			UnitTeamId.Enemy => m_EnemyUnits,
			UnitTeamId.Neutral => m_NeutralUnits,
			_ => null
		};
	}
	#endregion
}
