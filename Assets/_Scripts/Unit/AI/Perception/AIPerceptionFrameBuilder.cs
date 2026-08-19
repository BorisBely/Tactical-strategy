using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI-0 FROZEN. Builds <see cref="AIPerceptionFrame"/> from observer-local contacts.
/// Does not read UnitTeam, Q, DetectionProgress, Vision LOD, or TargetSelector.
/// </summary>
public static class AIPerceptionFrameBuilder
{
	public static AIPerceptionFrame Build(IPerceivedContactRegistry _registry)
	{
		if (_registry == null || _registry.Contacts == null || _registry.Contacts.Count == 0)
			return AIPerceptionFrame.Empty;

		var all = new List<AIContactKnowledge>(_registry.Contacts.Count);
		var visible = new List<AIContactKnowledge>();
		var remembered = new List<AIContactKnowledge>();
		var stale = new List<AIContactKnowledge>();
		var hostile = new List<AIContactKnowledge>();
		var unknown = new List<AIContactKnowledge>();
		ThreatLevel strongest = ThreatLevel.None;

		foreach (KeyValuePair<Transform, PerceivedContact> pair in _registry.Contacts)
		{
			PerceivedContact contact = pair.Value;
			if (contact == null)
				continue;

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			all.Add(knowledge);

			if (knowledge.VisibleNow)
				visible.Add(knowledge);
			else if (knowledge.HasUsefulMemory)
				remembered.Add(knowledge);

			if (knowledge.MemoryStale)
				stale.Add(knowledge);
			if (knowledge.Hostile)
				hostile.Add(knowledge);
			if (knowledge.IdentityUnknown)
				unknown.Add(knowledge);

			if ((int)knowledge.Threat > (int)strongest)
				strongest = knowledge.Threat;
		}

		return new AIPerceptionFrame(
			all.ToArray(),
			visible.ToArray(),
			remembered.ToArray(),
			stale.ToArray(),
			hostile.ToArray(),
			unknown.ToArray(),
			strongest);
	}
}
