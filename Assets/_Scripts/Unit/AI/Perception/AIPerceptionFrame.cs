using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Observer-local immutable perception snapshot for one AI decision tick.
/// Visual channels are AI-0 FROZEN. Sound / Report channels are #9.
/// Built from <see cref="IPerceivedContactRegistry"/> — not from TargetSelector.
/// Selected ≠ Engageable ≠ Fire ≠ this frame. Sound ≠ Observed.
/// </summary>
public readonly struct AIPerceptionFrame
{
	public static readonly AIPerceptionFrame Empty = new AIPerceptionFrame(
		Array.Empty<AIContactKnowledge>(),
		Array.Empty<AIContactKnowledge>(),
		Array.Empty<AIContactKnowledge>(),
		Array.Empty<AIContactKnowledge>(),
		Array.Empty<AIContactKnowledge>(),
		Array.Empty<AIContactKnowledge>(),
		ThreatLevel.None,
		Array.Empty<AISoundContact>(),
		Array.Empty<AIReportContact>());

	public readonly IReadOnlyList<AIContactKnowledge> AllContacts;
	public readonly IReadOnlyList<AIContactKnowledge> VisibleContacts;
	public readonly IReadOnlyList<AIContactKnowledge> RememberedContacts;
	public readonly IReadOnlyList<AIContactKnowledge> StaleContacts;
	public readonly IReadOnlyList<AIContactKnowledge> HostileContacts;
	public readonly IReadOnlyList<AIContactKnowledge> UnknownContacts;
	public readonly ThreatLevel StrongestThreat;
	public readonly IReadOnlyList<AISoundContact> SoundContacts;
	public readonly IReadOnlyList<AIReportContact> ReportContacts;

	public AIPerceptionFrame(
		IReadOnlyList<AIContactKnowledge> _all,
		IReadOnlyList<AIContactKnowledge> _visible,
		IReadOnlyList<AIContactKnowledge> _remembered,
		IReadOnlyList<AIContactKnowledge> _stale,
		IReadOnlyList<AIContactKnowledge> _hostile,
		IReadOnlyList<AIContactKnowledge> _unknown,
		ThreatLevel _strongestThreat)
		: this(
			_all,
			_visible,
			_remembered,
			_stale,
			_hostile,
			_unknown,
			_strongestThreat,
			Array.Empty<AISoundContact>(),
			Array.Empty<AIReportContact>())
	{
	}

	public AIPerceptionFrame(
		IReadOnlyList<AIContactKnowledge> _all,
		IReadOnlyList<AIContactKnowledge> _visible,
		IReadOnlyList<AIContactKnowledge> _remembered,
		IReadOnlyList<AIContactKnowledge> _stale,
		IReadOnlyList<AIContactKnowledge> _hostile,
		IReadOnlyList<AIContactKnowledge> _unknown,
		ThreatLevel _strongestThreat,
		IReadOnlyList<AISoundContact> _sounds,
		IReadOnlyList<AIReportContact> _reports)
	{
		AllContacts = _all ?? Array.Empty<AIContactKnowledge>();
		VisibleContacts = _visible ?? Array.Empty<AIContactKnowledge>();
		RememberedContacts = _remembered ?? Array.Empty<AIContactKnowledge>();
		StaleContacts = _stale ?? Array.Empty<AIContactKnowledge>();
		HostileContacts = _hostile ?? Array.Empty<AIContactKnowledge>();
		UnknownContacts = _unknown ?? Array.Empty<AIContactKnowledge>();
		StrongestThreat = _strongestThreat;
		SoundContacts = _sounds ?? Array.Empty<AISoundContact>();
		ReportContacts = _reports ?? Array.Empty<AIReportContact>();
	}

	public bool TryGetContact(Transform _target, out AIContactKnowledge _knowledge)
	{
		IReadOnlyList<AIContactKnowledge> all = AllContacts;
		if (_target != null && all != null)
		{
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].Target == _target)
				{
					_knowledge = all[i];
					return true;
				}
			}
		}

		_knowledge = default;
		return false;
	}
}
