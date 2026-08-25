/// <summary>
/// Why SearchPosition was chosen. Visual memory uses LastKnown. Sound uses SoundPosition.
/// They are not the same knowledge source.
/// </summary>
public enum UnitAISearchCue
{
	None = 0,
	VisualMemory = 1,
	Sound = 2
}
