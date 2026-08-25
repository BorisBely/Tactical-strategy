using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Search uses frozen LastKnown (visual) or #9 SoundPosition. Does not write Memory / LastSeen / LastKnown.
/// Useful visual memory = LastSeenConfidence &gt; 0.25 (AI-0). Stale is not enough to search.
/// Hostile combat sound = Gunshot / Explosion from a Hostile emitter.
/// </summary>
public static class UnitAISearchDecision
{
	#region Constants
	public const float DefaultAreaRadius = 15f;
	#endregion

	#region Public Methods
	public static bool ShouldStartSearch(UnitAIState _state, in AIPerceptionFrame _frame)
	{
		if (_state != UnitAIState.Defense && _state != UnitAIState.Attack)
			return false;
		if (UnitAIActionResolver.HasHostileVisible(_frame))
			return false;
		return TryGetSearchContact(_frame, out _) || TryGetSearchSound(_frame, out _);
	}

	public static bool ShouldFinishSearchBecauseFound(UnitAIState _state, in AIPerceptionFrame _frame)
	{
		return _state == UnitAIState.Search && UnitAIActionResolver.HasHostileVisible(_frame);
	}

	public static bool ShouldFinishSearchBecauseMemoryGone(UnitAIState _state, in AIPerceptionFrame _frame)
	{
		if (_state != UnitAIState.Search)
			return false;
		if (UnitAIActionResolver.HasHostileVisible(_frame))
			return false;
		return !TryGetSearchContact(_frame, out _) && !TryGetSearchSound(_frame, out _);
	}

	public static bool TryGetSearchContact(in AIPerceptionFrame _frame, out AIContactKnowledge _knowledge)
	{
		_knowledge = default;
		IReadOnlyList<AIContactKnowledge> all = _frame.AllContacts;
		if (all == null || all.Count == 0)
			return false;

		bool found = false;
		float bestConfidence = 0f;
		float bestSeenTime = float.MinValue;
		for (int i = 0; i < all.Count; i++)
		{
			AIContactKnowledge contact = all[i];
			if (!contact.Hostile || contact.VisibleNow || !contact.HasUsefulMemory)
				continue;

			bool better = !found ||
			              contact.LastSeenConfidence > bestConfidence + 0.0001f ||
			              (contact.LastSeenConfidence >= bestConfidence - 0.0001f &&
			               contact.LastSeenTime > bestSeenTime);
			if (!better)
				continue;

			_knowledge = contact;
			bestConfidence = contact.LastSeenConfidence;
			bestSeenTime = contact.LastSeenTime;
			found = true;
		}

		return found;
	}

	public static bool TryGetSearchSound(in AIPerceptionFrame _frame, out AISoundContact _sound)
	{
		_sound = default;
		IReadOnlyList<AISoundContact> sounds = _frame.SoundContacts;
		if (sounds == null || sounds.Count == 0)
			return false;

		bool found = false;
		float bestConfidence = 0f;
		float bestTime = float.MinValue;
		for (int i = 0; i < sounds.Count; i++)
		{
			AISoundContact cue = sounds[i];
			if (!cue.IsCombatCue)
				continue;

			bool better = !found ||
			              cue.Confidence > bestConfidence + 0.0001f ||
			              (cue.Confidence >= bestConfidence - 0.0001f && cue.Time > bestTime);
			if (!better)
				continue;

			_sound = cue;
			bestConfidence = cue.Confidence;
			bestTime = cue.Time;
			found = true;
		}

		return found;
	}
	#endregion
}
