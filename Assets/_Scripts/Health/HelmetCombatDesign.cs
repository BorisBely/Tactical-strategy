using UnityEngine;

/// <summary>
/// Баллистика шлемов: один бросок на попадание пули в голову, без прочности/HP.
/// Снайперские патроны снижают шанс поглощения.
/// </summary>
public static class HelmetCombatDesign
{
	public const float SniperBlockChanceMultiplier = 0.30f;

	/// <summary>Шлем экипажа — минимальная баллистика.</summary>
	public const float CrewBlockChance = 0.18f;

	/// <summary>Кевларовый шлем (базовый).</summary>
	public const float Kevlar1BlockChance = 0.50f;

	/// <summary>Кевларовый с модификациями.</summary>
	public const float Kevlar2BlockChance = 0.55f;

	/// <summary>Тактический шлем.</summary>
	public const float TacticalBlockChance = 0.45f;

	public const float DefaultBlockChance = 0.25f;

	public static float ResolveBlockChance(ItemDefinition _helmet, AmmoDefinition _ammo)
	{
		if (_helmet == null || _helmet.EquipmentKind != EquipmentKind.Helmet || _ammo == null)
			return 0f;

		float chance = _helmet.GetHeadBulletBlockChance();
		if (_ammo.Penetration >= UnitArmorCombatDesign.SniperPenetrationThreshold)
			chance *= SniperBlockChanceMultiplier;

		return Mathf.Clamp01(chance);
	}

	public static float ResolveDefaultBlockChance(string _localizationKey)
	{
		if (string.IsNullOrWhiteSpace(_localizationKey))
			return DefaultBlockChance;

		switch (_localizationKey)
		{
			case "item.helmet.crew":
				return CrewBlockChance;
			case "item.helmet.kevlar_1":
				return Kevlar1BlockChance;
			case "item.helmet.kevlar_2":
				return Kevlar2BlockChance;
			case "item.helmet.tactical":
				return TacticalBlockChance;
			default:
				return DefaultBlockChance;
		}
	}
}
