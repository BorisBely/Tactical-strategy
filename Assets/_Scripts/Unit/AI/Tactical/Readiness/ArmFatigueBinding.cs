using UnityEngine;

/// <summary>
/// Resolves the unit's ReadinessController for physical 14B.6 modifiers.
/// Missing AI → no fatigue (multiplier 1).
/// </summary>
public static class ArmFatigueBinding
{
	#region Public Methods
	public static bool TryGet(Component _host, out ReadinessController _readiness)
	{
		_readiness = null;
		if (_host == null)
			return false;
		if (!_host.TryGetComponent(out UnitAIController ai))
			return false;
		_readiness = ai.Readiness;
		return _readiness != null;
	}

	public static ArmFatigueEffects EffectsOrNeutral(Component _host)
	{
		if (!TryGet(_host, out ReadinessController readiness))
			return ArmFatigueMath.Evaluate(0f, ArmFatigueProfile.PlayPrototype());

		return readiness.FatigueEffects;
	}
	#endregion
}
