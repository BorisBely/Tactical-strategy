/// <summary>
/// How a target <em>looks</em> to observers (world cue). Not world <see cref="UnitTeam"/> and not
/// committed <see cref="PerceivedIdentity"/>.
/// </summary>
public enum ObservableAffiliation
{
	Unknown = 0,
	Friendly = 1,
	Neutral = 2,
	Hostile = 3
}
