using UnityEngine;

/// <summary>Итог одного hitscan-снаряда для логов и отладки.</summary>
public struct WeaponShotOutcome
{
	public WeaponShotHitResult Result;
	public float HitDistanceMeters;
	public float Damage;
	public string HitColliderName;
	public string HitRootName;
	public BodyPartType BodyPart;
	public CombatBodyZone BodyZone;
	public bool HasDamageableTarget;
	public bool HasUnitHealth;
	public InjuryUiEntry ResolvedInjury;
	public bool HasResolvedInjury;
	public UnitHealth TargetHealth;

	public static WeaponShotOutcome Miss()
	{
		return new WeaponShotOutcome { Result = WeaponShotHitResult.Miss };
	}

	public static WeaponShotOutcome BlockedBySelf(string _colliderName, float _distanceMeters)
	{
		return new WeaponShotOutcome
		{
			Result = WeaponShotHitResult.BlockedBySelf,
			HitColliderName = _colliderName,
			HitDistanceMeters = _distanceMeters
		};
	}
}
