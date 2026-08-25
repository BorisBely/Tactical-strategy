using System.Collections.Generic;

/// <summary>
/// Reusable buffers for <see cref="AIPerceptionFrameBuilder"/>. Owned per observer so frames do not alias.
/// </summary>
public sealed class AIPerceptionFrameScratch
{
	#region Public Fields
	public readonly List<AIContactKnowledge> All = new List<AIContactKnowledge>(16);
	public readonly List<AIContactKnowledge> Visible = new List<AIContactKnowledge>(8);
	public readonly List<AIContactKnowledge> Remembered = new List<AIContactKnowledge>(8);
	public readonly List<AIContactKnowledge> Stale = new List<AIContactKnowledge>(4);
	public readonly List<AIContactKnowledge> Hostile = new List<AIContactKnowledge>(8);
	public readonly List<AIContactKnowledge> Unknown = new List<AIContactKnowledge>(8);
	public readonly List<AISoundContact> Sounds = new List<AISoundContact>(8);
	public readonly List<AIReportContact> Reports = new List<AIReportContact>(8);
	#endregion

	#region Public Methods
	public void Clear()
	{
		All.Clear();
		Visible.Clear();
		Remembered.Clear();
		Stale.Clear();
		Hostile.Clear();
		Unknown.Clear();
		Sounds.Clear();
		Reports.Clear();
	}
	#endregion
}
