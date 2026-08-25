using UnityEngine;

/// <summary>
/// #8 → #7 adapter. Maps world Gunshot/Hit onto the frozen ImmediateThreat Signal API.
/// Impact and Death are world facts; they do not set ImmediateThreat.
/// Does not write perception or call Fire.
/// </summary>
public static class ImmediateThreatCombatEventBridge
{
	#region Public Methods
	public static void Handle(CombatEvent _evt)
	{
		if (_evt.Instigator == null || _evt.Target == null)
			return;

		switch (_evt.Type)
		{
			case CombatEventType.Gunshot:
				ImmediateThreatSignal.NotifyIncomingFire(_evt.Instigator, _evt.Target.transform);
				return;
			case CombatEventType.Hit:
				ImmediateThreatSignal.NotifyConfirmedHit(_evt.Instigator, _evt.Target);
				return;
			default:
				return;
		}
	}
	#endregion
}
