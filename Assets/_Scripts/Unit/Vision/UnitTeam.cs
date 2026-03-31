using UnityEngine;

/// <summary>
/// Сторона юнита. Используется <see cref="UnitVision"/> и <see cref="UnitVisionRegistry"/>.
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
}
