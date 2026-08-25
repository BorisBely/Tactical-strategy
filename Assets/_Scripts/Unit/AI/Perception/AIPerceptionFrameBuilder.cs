using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds <see cref="AIPerceptionFrame"/> from observer-local contacts.
/// Visual semantics are AI-0 FROZEN. Sound / Report lists are #9 channels, not visual contacts.
/// Does not read Q, DetectionProgress, Vision LOD, or TargetSelector.
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

		Component observer = _registry as Component;
		float now = ResolveNow(_registry);
		ThreatLevel strongest = ThreatLevel.None;
		foreach (KeyValuePair<Transform, PerceivedContact> pair in _registry.Contacts)
		{
			PerceivedContact contact = pair.Value;
			if (contact == null)
				continue;

			if (HasVisualChannel(contact))
			{
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

			if (contact.HasUsefulSound && contact.SoundType != SoundEventType.Unknown)
			{
				_scratch.Sounds.Add(new AISoundContact(
					contact.Target,
					contact.SoundPosition,
					contact.SoundType,
					contact.SoundConfidence,
					contact.SoundTime,
					Mathf.Max(0f, now - contact.SoundTime),
					IsHostileSound(observer, contact)));
			}

			if (contact.HasUsefulShared)
			{
				_scratch.Reports.Add(new AIReportContact(
					contact.SharedReporter,
					contact.Target,
					contact.SharedPosition,
					contact.SharedIdentity,
					contact.SharedConfidence,
					contact.SharedTime,
					Mathf.Max(0f, now - contact.SharedTime)));
			}
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
				strongest,
				_scratch.Sounds.ToArray(),
				_scratch.Reports.ToArray());
		}

		return new AIPerceptionFrame(
			_scratch.All,
			_scratch.Visible,
			_scratch.Remembered,
			_scratch.Stale,
			_scratch.Hostile,
			_scratch.Unknown,
			strongest,
			_scratch.Sounds,
			_scratch.Reports);
	}

	private static float ResolveNow(IPerceivedContactRegistry _registry)
	{
		if (_registry is DetectionProcessor processor)
			return processor.PerceptionClock;
		return Time.time;
	}

	private static bool HasVisualChannel(PerceivedContact _contact)
	{
		if (_contact.ObservationState == ObservationState.Observed ||
		    _contact.ObservationState == ObservationState.RecentlyLost ||
		    _contact.ObservationState == ObservationState.Lost)
			return true;
		return _contact.LastSeenConfidence > 0f;
	}

	private static bool IsHostileSound(Component _observer, PerceivedContact _contact)
	{
		if (_contact.Relationship == PerceivedRelationship.Hostile)
			return true;
		if (_observer == null || _contact.Target == null)
			return false;
		return UnitTeam.AreHostile(_observer, _contact.Target);
	}
	#endregion
}
