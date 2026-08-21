/// <summary>
/// How a target looks in the world. Not committed Identity and not world UnitTeam.
/// Observer maps this to <see cref="ObservableAffiliation"/> via <see cref="VisualAffiliationMapping"/>.
/// </summary>
public enum VisualAffiliation
{
	Unknown = 0,
	Player = 1,
	Enemy = 2,
	Civilian = 3
}
