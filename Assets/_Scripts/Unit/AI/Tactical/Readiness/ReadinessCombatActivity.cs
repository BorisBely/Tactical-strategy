/// <summary>
/// #14B.3 shared combat-activity context. Hold / decay clock, not a raise to Aim.
/// Sources: HostileVisible, GunshotHeard, combat event (ImmediateThreat / Gunshot / Hit).
/// Not Suppression / UnderFire / Wound.
/// </summary>
public static class ReadinessCombatActivity
{
	#region Public Methods
	public static bool FromSources(bool _hostileVisible, bool _gunshotHeard, bool _combatEvent)
	{
		return _hostileVisible || _gunshotHeard || _combatEvent;
	}

	public static bool FromFrame(in ReadinessFrame _frame)
	{
		return FromSources(_frame.HostileVisible, _frame.GunshotHeard, _frame.CombatActivity);
	}

	/// <summary>
	/// Combat-bus facts that hold activity. Gunshot here is not <see cref="ReadinessStimulus.GunshotHeard"/>
	/// (that comes from the sound channel). Death is LifeGate, not activity.
	/// </summary>
	public static bool IsCombatEvent(CombatEventType _type)
	{
		switch (_type)
		{
			case CombatEventType.Gunshot:
			case CombatEventType.Hit:
				return true;
			default:
				return false;
		}
	}

	public static bool IsCombatEvent(in CombatEvent _evt)
	{
		return IsCombatEvent(_evt.Type);
	}
	#endregion
}
