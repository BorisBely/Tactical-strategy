/// <summary>
/// #14B.1 maps perception / combat flags onto <see cref="ReadinessFrame"/>.
/// #14B.3 uses the frozen Hostile+Observed contact (same as Engage), not a second Vision.
/// Does not write <see cref="ReadinessState"/>. Sound is not Fire.
/// </summary>
public static class ReadinessStimulusMath
{
	#region Public Methods
	public static bool HasGunshot(in AIPerceptionFrame _frame)
	{
		var sounds = _frame.SoundContacts;
		if (sounds == null)
			return false;

		for (int i = 0; i < sounds.Count; i++)
		{
			if (sounds[i].Type == SoundEventType.Gunshot)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Observed + Relationship Hostile. Same contact as <see cref="UnitAIActionResolver"/> Engage.
	/// Identity is not a second definition: frozen Hostile is Relationship.
	/// </summary>
	public static bool HasHostileVisible(in AIPerceptionFrame _frame)
	{
		return TryGetHostileVisible(in _frame, out _);
	}

	public static bool TryGetHostileVisible(in AIPerceptionFrame _frame, out AIContactKnowledge _knowledge)
	{
		_knowledge = default;
		if (!UnitAIActionResolver.TryGetEngageContact(in _frame, out AIContactKnowledge contact))
			return false;
		if (contact.ObservationState != ObservationState.Observed)
			return false;

		_knowledge = contact;
		return true;
	}

	public static ReadinessFrame FromPerception(
		in AIPerceptionFrame _frame,
		bool _previousHostileVisible,
		bool _immediateThreat)
	{
		bool visible = TryGetHostileVisible(in _frame, out AIContactKnowledge contact);
		string target = null;
		if (visible && contact.Target != null)
			target = UnitActionLog.Slot(contact.Target);

		return new ReadinessFrame
		{
			HostileVisible = visible,
			HostileLost = _previousHostileVisible && !visible,
			GunshotHeard = HasGunshot(in _frame),
			CombatActivity = _immediateThreat,
			CombatActivityExpired = false,
			StimulusTarget = target
		};
	}

	/// <summary>
	/// Diagnostic collapse. The controller uses flags, not this, so Gunshot can still raise
	/// when CombatActivity is also set. HostileVisible always wins the raise.
	/// Priority: HostileVisible &gt; CombatActivity &gt; GunshotHeard &gt; Calm / decay.
	/// </summary>
	public static ReadinessStimulus Dominant(in ReadinessFrame _frame)
	{
		if (_frame.HostileVisible)
			return ReadinessStimulus.HostileVisible;
		if (_frame.CombatActivity)
			return ReadinessStimulus.CombatActivity;
		if (_frame.GunshotHeard)
			return ReadinessStimulus.GunshotHeard;
		if (_frame.CombatActivityExpired)
			return ReadinessStimulus.CombatActivityExpired;
		if (_frame.HostileLost)
			return ReadinessStimulus.HostileLost;
		return ReadinessStimulus.None;
	}
	#endregion
}
