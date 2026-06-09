using System;
using UnityEngine;

/// <summary>
/// Простая v1-таблица «часть тела + источник → травма с весами».
/// </summary>
public static class InjuryRollTable
{
	private readonly struct WeightedInjury
	{
		public readonly InjuryUiEntry Entry;
		public readonly int Weight;

		public WeightedInjury(InjuryUiEntry _entry, int _weight)
		{
			Entry = _entry;
			Weight = _weight;
		}
	}

	public static InjuryUiEntry Roll(BodyPartType _bodyPart, DamageSourceType _source)
	{
		if (_source != DamageSourceType.Bullet)
			return RollBullet(_bodyPart);

		return RollBullet(_bodyPart);
	}

	public static InjuryUiEntry ResolveFromHitZone(UnitBodyHitZone _hitZone, DamageSourceType _source)
	{
		BodyPartType bodyPart = _hitZone != null ? _hitZone.BodyPart : BodyPartType.Unknown;
		return Roll(bodyPart, _source);
	}

	private static InjuryUiEntry RollBullet(BodyPartType _bodyPart)
	{
		WeightedInjury[] table = GetBulletTable(_bodyPart);
		if (table == null || table.Length == 0)
			return CreateFallbackInjury(_bodyPart);

		int totalWeight = 0;
		for (int i = 0; i < table.Length; i++)
			totalWeight += Mathf.Max(1, table[i].Weight);

		int roll = UnityEngine.Random.Range(0, totalWeight);
		for (int i = 0; i < table.Length; i++)
		{
			roll -= Mathf.Max(1, table[i].Weight);
			if (roll < 0)
				return table[i].Entry;
		}

		return table[table.Length - 1].Entry;
	}

	private static WeightedInjury[] GetBulletTable(BodyPartType _bodyPart)
	{
		switch (_bodyPart)
		{
			case BodyPartType.Head:
				return new[]
				{
					Entry("health.injury.head_wound", "health.condition.moderate_bleeding", "health.injury.head_wound.desc",
						new[] { "health.debuff.aim_penalty" }, 15, 55),
					Entry("health.injury.concussion", "health.condition.internal", "health.injury.concussion.desc",
						new[] { "health.debuff.aim_penalty", "health.debuff.movement_slow" }, 20, 45)
				};

			case BodyPartType.Neck:
				return new[]
				{
					Entry("health.injury.neck_bleeding", "health.condition.moderate_bleeding", "health.injury.neck_bleeding.desc",
						new[] { "health.debuff.oxygen_loss" }, 10, 100)
				};

			case BodyPartType.Chest:
				return new[]
				{
					Entry("health.injury.lung_damage", "health.condition.internal", "health.injury.lung_damage.desc",
						new[] { "health.debuff.oxygen_loss", "health.debuff.no_long_run" }, 20, 60),
					Entry("health.injury.chest_bleeding", "health.condition.moderate_bleeding", "health.injury.chest_bleeding.desc",
						new[] { "health.debuff.movement_slow" }, 30, 40)
				};

			case BodyPartType.Abdomen:
				return new[]
				{
					Entry("health.injury.internal_bleeding", "health.condition.internal", "health.injury.internal_bleeding.desc",
						new[] { "health.debuff.movement_slow", "health.debuff.oxygen_loss" }, 15, 100)
				};

			case BodyPartType.LeftArm:
				return new[]
				{
					Entry("health.injury.left_arm_bleeding", "health.condition.moderate_bleeding", "health.injury.left_arm_bleeding.desc",
						new[] { "health.debuff.aim_penalty", "health.debuff.reload_slow" }, 40, 100)
				};

			case BodyPartType.RightArm:
				return new[]
				{
					Entry("health.injury.arm_bleeding", "health.condition.moderate_bleeding", "health.injury.arm_bleeding.desc",
						new[] { "health.debuff.aim_penalty", "health.debuff.reload_slow" }, 40, 100)
				};

			case BodyPartType.LeftLeg:
				return new[]
				{
					Entry("health.injury.leg_fracture", "health.condition.fracture", "health.injury.leg_fracture.desc",
						new[] { "health.debuff.no_sprint", "health.debuff.movement_slow" }, 30, 55),
					Entry("health.injury.left_leg_bleeding", "health.condition.moderate_bleeding", "health.injury.left_leg_bleeding.desc",
						new[] { "health.debuff.movement_slow" }, 35, 45)
				};

			case BodyPartType.RightLeg:
				return new[]
				{
					Entry("health.injury.right_leg_fracture", "health.condition.fracture", "health.injury.right_leg_fracture.desc",
						new[] { "health.debuff.no_sprint", "health.debuff.movement_slow" }, 30, 55),
					Entry("health.injury.right_leg_bleeding", "health.condition.moderate_bleeding", "health.injury.right_leg_bleeding.desc",
						new[] { "health.debuff.movement_slow" }, 35, 45)
				};

			default:
				return Array.Empty<WeightedInjury>();
		}
	}

	private static WeightedInjury Entry(
		string _statusKey,
		string _conditionKey,
		string _descriptionKey,
		string[] _debuffKeys,
		int _sortPriority,
		int _weight)
	{
		return new WeightedInjury(
			InjuryUiEntry.FromLocalizedKeys(_statusKey, _conditionKey, _descriptionKey, _debuffKeys, _sortPriority),
			_weight);
	}

	private static InjuryUiEntry CreateFallbackInjury(BodyPartType _bodyPart)
	{
		return InjuryUiEntry.FromLocalizedKeys(
			"health.injury.generic_wound",
			"health.condition.moderate_bleeding",
			"health.injury.generic_wound.desc",
			new[] { "health.debuff.movement_slow" },
			_sortPriority: 50);
	}
}
