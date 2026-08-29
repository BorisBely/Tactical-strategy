using System.Globalization;
using UnityEngine;

/// <summary>
/// #14B.6 event log. ARM_FATIGUE thresholds and ARM_FATIGUE_EFFECT. Not every tick.
/// </summary>
public static class ArmFatigueLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.ArmFatigue;
	public static string EffectChannel => UnitActionLog.ArmFatigueEffect;
	public static string ReadinessEffectChannel => UnitActionLog.ReadinessEffect;
	#endregion

	#region Public Methods
	public static string FormatThreshold(int _band)
	{
		switch (_band)
		{
			case 1:
				return "threshold=0.25";
			case 2:
				return "threshold=0.50";
			case 3:
				return "threshold=0.75";
			case 4:
				return "max";
			default:
				return "threshold=0";
		}
	}

	public static string FormatRecoveryStart()
	{
		return "recovery-start";
	}

	public static string FormatValue(float _fatigue)
	{
		return "value=" + ArmFatigueMath.Clamp01(_fatigue).ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static string FormatEffect(in ArmFatigueEffects _effects)
	{
		return "fatigue=" + _effects.Fatigue.ToString("0.##", CultureInfo.InvariantCulture) +
		       " aimMultiplier=" + _effects.AimTimeMultiplier.ToString("0.###", CultureInfo.InvariantCulture) +
		       " recoilMultiplier=" + _effects.RecoilControlModifier.ToString("0.###", CultureInfo.InvariantCulture) +
		       " turnMultiplier=" + _effects.TurnTimeMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
	}

	public static void Emit(Component _actor, string _payload)
	{
		EmitOn(Channel, _actor, _payload);
	}

	public static void EmitEffect(Component _actor, string _payload)
	{
		EmitOn(EffectChannel, _actor, _payload);
	}

	public static void EmitReadinessEffect(Component _actor, string _payload)
	{
		EmitOn(ReadinessEffectChannel, _actor, _payload);
	}
	#endregion

	#region Private Methods
	private static void EmitOn(string _channel, Component _actor, string _payload)
	{
		if (!UnitActionLog.Enabled || string.IsNullOrEmpty(_payload))
			return;

		UnitActionLog.Write(_actor, _channel, _payload);
		string prefix = _actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty;
		UnitActionLog.Timeline(_channel, prefix + _payload);
	}
	#endregion
}
