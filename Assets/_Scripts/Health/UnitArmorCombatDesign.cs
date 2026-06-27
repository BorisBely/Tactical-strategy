/// <summary>
/// Баланс первой версии брони:
/// - цель по живучести плиты: 3-4 винтовочных попадания или 1-2 снайперских;
/// - винтовочный tier: Penetration ниже <see cref="SniperPenetrationThreshold"/> (текущие 5.56 / 7.62);
/// - снайперский tier: Penetration от <see cref="SniperPenetrationThreshold"/> и выше, зарезервировано под будущие патроны;
/// - успешный блок полностью отменяет урон и травму тела, но повреждает броню;
/// - неудачный блок пропускает полный урон в тело и слегка повреждает броню;
/// - тяжёлая броня прикрывает Chest/Abdomen (и Neck от осколков) от Fragment/Explosive;
/// - голова не защищается телом брони — только шлемом (<see cref="UnitHeadEquipment"/>).
/// </summary>
public static class UnitArmorCombatDesign
{
	public const float MaxDurability = 36f;
	public const float DamagedDurabilityRatio = 0.5f;
	public const float SniperPenetrationThreshold = 24f;
	public const float SniperArmorDamageMultiplier = 2.5f;
	public const float FailedBlockArmorDamageMultiplier = 0.5f;

	public const float LightChestBulletBlockChance = 0.55f;

	public const float HeavyChestBulletBlockChance = 0.70f;
	public const float HeavyAbdomenBulletBlockChance = 0.60f;

	public const float HeavyFragmentExplosiveBlockChance = 0.85f;

	public const float LightArmorWeightKg = 8f;
	public const float HeavyArmorWeightKg = 15f;
}
