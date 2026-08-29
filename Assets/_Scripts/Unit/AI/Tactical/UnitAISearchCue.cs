/// <summary>
/// Why SearchArea was chosen. Visual memory uses LastKnown. Sound uses SoundPosition.
/// Ally report uses Report.Position. They are not the same knowledge source.
/// </summary>
public enum UnitAISearchCue
{
	None = 0,
	VisualMemory = 1,
	Sound = 2,
	AllyReport = 3
}
