using UnityEngine;

/// <summary>
/// Neutral engageability rules shared by TargetSelector and combat consumers.
/// Not owned by Vision — only answers “can this transform be engaged right now?”.
/// </summary>
public static class TargetEngageability
{
	public static bool IsEngageable(Transform _target)
	{
		if (_target == null)
			return false;

		if (!UnitConsciousness.IsTargetableTarget(_target))
			return false;

		if (_target.TryGetComponent(out ShootingRangeTarget rangeTarget))
			return rangeTarget.IsAvailableForTargeting;

		if (_target.TryGetComponent(out DamageableTarget damageable))
			return damageable.IsAlive;

		return true;
	}
}
