using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальный реестр юнитов для зрения: игрок и остальные (враги + нейтралы).
/// Ссылки задаются при спавне/расстановке; не ищет объекты по сцене.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitVisionRegistry : MonoBehaviour
{
	#region Private Fields
	private readonly List<UnitVision> m_PlayerUnits = new List<UnitVision>(24);
	private readonly List<UnitVision> m_OtherUnits = new List<UnitVision>(128);
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

		List<UnitVision> list = team.Team == UnitTeamId.Player ? m_PlayerUnits : m_OtherUnits;
		if (list.Contains(_unit))
			return;
		list.Add(_unit);
	}

	public void Unregister(UnitVision _unit)
	{
		if (_unit == null)
			return;
		m_PlayerUnits.Remove(_unit);
		m_OtherUnits.Remove(_unit);
	}

	/// <summary>Кого этот юнит проверяет как потенциальные цели (без рейкастов).</summary>
	public void GetOpponents(UnitTeamId _viewerTeam, List<UnitVision> _outBuffer)
	{
		_outBuffer.Clear();
		if (_viewerTeam == UnitTeamId.Player)
			_outBuffer.AddRange(m_OtherUnits);
		else
			_outBuffer.AddRange(m_PlayerUnits);
	}

	public int PlayerUnitCount => m_PlayerUnits.Count;
	public int OtherUnitCount => m_OtherUnits.Count;
	#endregion
}
