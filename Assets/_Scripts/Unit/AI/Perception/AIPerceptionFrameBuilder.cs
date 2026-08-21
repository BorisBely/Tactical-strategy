using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI-0 FROZEN. Builds <see cref="AIPerceptionFrame"/> from observer-local contacts.
/// Does not read UnitTeam, Q, DetectionProgress, Vision LOD, or TargetSelector.
/// </summary>
public static class AIPerceptionFrameBuilder
{
	#region Public Methods
	/// <summary>
	/// Allocates new arrays. Tests and one-shot callers use this so consecutive builds do not alias.
	/// </summary>
	public static AIPerceptionFrame Build(IPerceivedContactRegistry _registry)
	{
		var scratch = new AIPerceptionFrameScratch();
		return Build(_registry, scratch, true);
	}

	/// <summary>
	/// Fills <paramref name="_scratch"/> and returns a frame wrapping those lists. No per-tick arrays.
	/// Caller must not keep the previous frame after the next <see cref="Build(IPerceivedContactRegistry, AIPerceptionFrameScratch)"/>.
	/// </summary>
	public static AIPerceptionFrame Build(IPerceivedContactRegistry _registry, AIPerceptionFrameScratch _scratch)
	{
		return Build(_registry, _scratch, false);
	}
	#endregion

	#region Private Methods
	private static AIPerceptionFrame Build(
		IPerceivedContactRegistry _registry,
		AIPerceptionFrameScratch _scratch,
		bool _copyToArrays)
	{
		if (_scratch == null)
			return AIPerceptionFrame.Empty;

		_scratch.Clear();
		if (_registry == null || _registry.Contacts == null || _registry.Contacts.Count == 0)
			return AIPerceptionFrame.Empty;

		ThreatLevel strongest = ThreatLevel.None;
		foreach (KeyValuePair<Transform, PerceivedContact> pair in _registry.Contacts)
		{
			PerceivedContact contact = pair.Value;
			if (contact == null)
				continue;

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			_scratch.All.Add(knowledge);

			if (knowledge.VisibleNow)
				_scratch.Visible.Add(knowledge);
			else if (knowledge.HasUsefulMemory)
				_scratch.Remembered.Add(knowledge);

			if (knowledge.MemoryStale)
				_scratch.Stale.Add(knowledge);
			if (knowledge.Hostile)
				_scratch.Hostile.Add(knowledge);
			if (knowledge.IdentityUnknown)
				_scratch.Unknown.Add(knowledge);

			if ((int)knowledge.Threat > (int)strongest)
				strongest = knowledge.Threat;
		}

		if (_copyToArrays)
		{
			return new AIPerceptionFrame(
				_scratch.All.ToArray(),
				_scratch.Visible.ToArray(),
				_scratch.Remembered.ToArray(),
				_scratch.Stale.ToArray(),
				_scratch.Hostile.ToArray(),
				_scratch.Unknown.ToArray(),
				strongest);
		}

		return new AIPerceptionFrame(
			_scratch.All,
			_scratch.Visible,
			_scratch.Remembered,
			_scratch.Stale,
			_scratch.Hostile,
			_scratch.Unknown,
			strongest);
	}
	#endregion
}
