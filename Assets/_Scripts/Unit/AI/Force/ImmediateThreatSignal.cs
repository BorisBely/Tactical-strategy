using UnityEngine;

/// <summary>
/// Frozen #7 API. Routes hostile Gunshot/Hit to the victim's <see cref="ImmediateThreatSource"/>.
/// World facts enter through <see cref="CombatEventHub"/>; this type does not publish CombatEvents.
/// Does not fire weapons.
/// </summary>
public static class ImmediateThreatSignal
{
	public static void NotifyIncomingFire(Component _attacker, Transform _aimedTarget)
	{
		Notify(_attacker, _aimedTarget, ImmediateThreatCause.IncomingFire);
	}

	public static void NotifyConfirmedHit(Component _attacker, Component _hitTarget)
	{
		Notify(_attacker, _hitTarget, ImmediateThreatCause.ConfirmedHit);
	}

	public static void NotifyHostileAttack(Component _attacker, Component _victim)
	{
		Notify(_attacker, _victim, ImmediateThreatCause.HostileAttack);
	}

	private static void Notify(Component _attacker, Component _victim, ImmediateThreatCause _cause)
	{
		if (_attacker == null || _victim == null)
			return;

		ImmediateThreatSource source = _victim.GetComponentInParent<ImmediateThreatSource>();
		if (source == null)
		{
			UnitAIController ai = _victim.GetComponentInParent<UnitAIController>();
			if (ai == null)
				return;
			source = ai.EnsureImmediateThreatSource();
		}

		source.NotifyHostileAttack(_attacker, _cause);
	}
}
