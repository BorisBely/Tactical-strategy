/// <summary>
/// Observer-local mapping from world look to identity cue.
/// Does not read the target's <see cref="UnitTeam"/>. Does not commit Identity.
/// </summary>
public static class VisualAffiliationMapping
{
	#region Public Methods
	public static ObservableAffiliation ToCue(VisualAffiliation _look, UnitTeamId _observerSide)
	{
		if (_look == VisualAffiliation.Unknown)
			return ObservableAffiliation.Unknown;

		if (_look == VisualAffiliation.Civilian)
			return ObservableAffiliation.Neutral;

		switch (_observerSide)
		{
			case UnitTeamId.Player:
				return _look == VisualAffiliation.Player
					? ObservableAffiliation.Friendly
					: ObservableAffiliation.Hostile;
			case UnitTeamId.Enemy:
				return _look == VisualAffiliation.Enemy
					? ObservableAffiliation.Friendly
					: ObservableAffiliation.Hostile;
			default:
				return ObservableAffiliation.Neutral;
		}
	}

	/// <summary>
	/// Content factory only. Spawn configs may default look from team; DetectionProcessor must not.
	/// </summary>
	public static VisualAffiliation DefaultLookForTeam(UnitTeamId _team)
	{
		switch (_team)
		{
			case UnitTeamId.Player:
				return VisualAffiliation.Player;
			case UnitTeamId.Enemy:
				return VisualAffiliation.Enemy;
			default:
				return VisualAffiliation.Civilian;
		}
	}
	#endregion
}
