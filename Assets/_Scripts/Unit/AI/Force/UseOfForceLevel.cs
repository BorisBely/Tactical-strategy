/// <summary>
/// Rules of engagement. Independent from <see cref="UnitAIState"/>.
/// Does not decide Track / Aim / Fire — that is frozen G6.
/// </summary>
public enum UseOfForceLevel
{
	SelfDefense = 0,
	RestrictedDefense = 1,
	MissionCombat = 2,
	FullEngagement = 3,
	NoFriendlyFire = 4
}
