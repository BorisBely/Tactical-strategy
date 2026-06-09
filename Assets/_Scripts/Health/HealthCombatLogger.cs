using UnityEngine;

/// <summary>
/// Логи после каждого выстрела (отключены). Включить снова через <c>m_LogShots</c> на <see cref="UnitWeaponHitscanShooting"/>.
/// </summary>
public static class HealthCombatLogger
{
	public static void LogAfterShot(
		Object _context,
		string _shooterLabel,
		ItemDefinition _weaponItem,
		AmmoDefinition _ammo,
		Transform _aimTarget,
		float _targetDistanceMeters,
		WeaponShotAccuracyContext _accuracy,
		WeaponShotOutcome _outcome,
		int _projectileIndex,
		int _projectileCount)
	{
	}
}
