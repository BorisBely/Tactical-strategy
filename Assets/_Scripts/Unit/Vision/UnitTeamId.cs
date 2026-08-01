/// <summary>
/// Сторона юнита/машины для зрения, выделения и боевой логики.
/// Не привязана к Layer — меняется в рантайме через <see cref="UnitTeam.SetTeam"/>.
/// </summary>
public enum UnitTeamId
{
	Player = 0,
	Enemy = 1,
	Neutral = 2
}
