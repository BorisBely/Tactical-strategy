using System.Collections.Generic;

/// <summary>
/// AI-1 FROZEN. Maps frozen <see cref="AIPerceptionFrame"/> onto an in-state action.
/// Does not change <see cref="UnitAIState"/>. Does not call TargetSelector or Combat.
/// Unknown visible is not Hostile. Remembered Hostile is not Engage.
/// </summary>
public static class UnitAIActionResolver
{
	#region Public Methods
	public static UnitAIAction Resolve(UnitAIState _state, in AIPerceptionFrame _frame)
	{
		bool hostileVisible = HasHostileVisible(_frame);
		switch (_state)
		{
			case UnitAIState.Defense:
			case UnitAIState.Attack:
				return hostileVisible ? UnitAIAction.Engage : UnitAIAction.Hold;
			default:
				return UnitAIAction.None;
		}
	}

	public static bool HasHostileVisible(in AIPerceptionFrame _frame)
	{
		return TryGetEngageContact(_frame, out _);
	}

	public static bool TryGetEngageContact(in AIPerceptionFrame _frame, out AIContactKnowledge _knowledge)
	{
		_knowledge = default;
		IReadOnlyList<AIContactKnowledge> all = _frame.AllContacts;
		if (all == null || all.Count == 0)
			return false;

		bool found = false;
		ThreatLevel bestThreat = ThreatLevel.None;
		for (int i = 0; i < all.Count; i++)
		{
			AIContactKnowledge contact = all[i];
			if (!contact.Hostile || !contact.VisibleNow)
				continue;

			if (!found || (int)contact.Threat > (int)bestThreat)
			{
				_knowledge = contact;
				bestThreat = contact.Threat;
				found = true;
			}
		}

		return found;
	}
	#endregion
}
