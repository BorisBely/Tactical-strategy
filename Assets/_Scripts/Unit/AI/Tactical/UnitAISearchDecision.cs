using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Search uses frozen LastKnown (visual), #9 SoundPosition, or #10 hostile report.
/// Does not write Memory / LastSeen / LastKnown.
/// Useful visual memory = LastSeenConfidence &gt; 0.25 (AI-0). Stale is not enough to search.
/// Hostile combat sound = Gunshot / Explosion from a Hostile emitter.
/// Hostile report = Identity Hostile and Confidence &gt; 0.
/// Attack overlay Search: Search 2.0 gunshot/report immediately; visual memory after lost-visible dwell 1.5 s.
/// Defense overlay Search uses memory / sound / report. ImmediateThreat does not start or finish Search.
/// </summary>
public static class UnitAISearchDecision
{
	#region Constants
	public const float DefaultAreaRadius = 15f;
	public const float InspectDuration = 1f;
	public const float AttackLostVisibleDwellSeconds = 1.5f;
	#endregion

	#region Public Methods
	public static bool ShouldStartSearch(UnitAIState _state, in AIPerceptionFrame _frame)
	{
		return ShouldStartSearch(_state, in _frame, Time.time, float.NegativeInfinity);
	}

	public static bool ShouldStartSearch(
		UnitAIState _state,
		in AIPerceptionFrame _frame,
		float _now,
		float _lastHostileVisibleAt)
	{
		if (_state != UnitAIState.Defense && _state != UnitAIState.Attack)
			return false;
		if (UnitAIActionResolver.HasHostileVisible(_frame))
			return false;
		if (_state == UnitAIState.Attack)
		{
			if (TryGetSearchSound(_frame, out _) || TryGetSearchReport(_frame, out _))
				return true;
			if (_now - _lastHostileVisibleAt < AttackLostVisibleDwellSeconds)
				return false;
			return TryGetSearchContact(_frame, out _);
		}

		return TryGetSearchContact(_frame, out _) ||
		       TryGetSearchSound(_frame, out _) ||
		       TryGetSearchReport(_frame, out _);
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
		return !TryGetSearchContact(_frame, out _) &&
		       !TryGetSearchSound(_frame, out _) &&
		       !TryGetSearchReport(_frame, out _);
	}

	public static bool TryBuildSearchArea(in AIPerceptionFrame _frame, float _now, out UnitAISearchArea _area)
	{
		_area = default;
		_ = _now;
		if (TryGetSearchContact(_frame, out AIContactKnowledge contact))
		{
			_area = new UnitAISearchArea(
				contact.LastKnownPosition,
				DefaultAreaRadius,
				UnitAISearchCue.VisualMemory,
				contact.LastSeenConfidence,
				contact.LastSeenTime);
			return true;
		}

		if (TryGetSearchSound(_frame, out AISoundContact sound))
		{
			_area = new UnitAISearchArea(
				sound.Position,
				DefaultAreaRadius,
				UnitAISearchCue.Sound,
				sound.Confidence,
				sound.Time);
			return true;
		}

		if (TryGetSearchReport(_frame, out AIReportContact report))
		{
			_area = new UnitAISearchArea(
				report.Position,
				DefaultAreaRadius,
				UnitAISearchCue.AllyReport,
				report.Confidence,
				report.Time);
			return true;
		}

		return false;
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

	public static bool TryGetSearchReport(in AIPerceptionFrame _frame, out AIReportContact _report)
	{
		_report = default;
		IReadOnlyList<AIReportContact> reports = _frame.ReportContacts;
		if (reports == null || reports.Count == 0)
			return false;

		bool found = false;
		float bestConfidence = 0f;
		float bestTime = float.MinValue;
		for (int i = 0; i < reports.Count; i++)
		{
			AIReportContact report = reports[i];
			if (report.Identity != PerceivedIdentity.Hostile || report.Confidence <= 0f)
				continue;

			bool better = !found ||
			              report.Confidence > bestConfidence + 0.0001f ||
			              (report.Confidence >= bestConfidence - 0.0001f && report.Time > bestTime);
			if (!better)
				continue;

			_report = report;
			bestConfidence = report.Confidence;
			bestTime = report.Time;
			found = true;
		}

		return found;
	}
	#endregion
}
