using UnityEngine;

/// <summary>
/// Локальная зона тела для системы травм (8 зон первой версии).
/// </summary>
public enum BodyPartType
{
	Unknown = 0,
	Head = 1,
	Neck = 2,
	Chest = 3,
	Abdomen = 4,
	LeftArm = 5,
	RightArm = 6,
	LeftLeg = 7,
	RightLeg = 8
}

/// <summary>
/// Крупная зона тела для урона, UI и эффектов ранений (legacy HP-система).
/// </summary>
public enum CombatBodyZone
{
	Unknown = 0,
	Head = 1,
	Torso = 2,
	Arms = 3,
	Legs = 4
}

public static class BodyPartTypeUtility
{
	public static CombatBodyZone ToCombatBodyZone(BodyPartType _bodyPart)
	{
		switch (_bodyPart)
		{
			case BodyPartType.Head:
			case BodyPartType.Neck:
				return CombatBodyZone.Head;
			case BodyPartType.Chest:
			case BodyPartType.Abdomen:
				return CombatBodyZone.Torso;
			case BodyPartType.LeftArm:
			case BodyPartType.RightArm:
				return CombatBodyZone.Arms;
			case BodyPartType.LeftLeg:
			case BodyPartType.RightLeg:
				return CombatBodyZone.Legs;
			default:
				return CombatBodyZone.Unknown;
		}
	}

	public static bool IsArm(BodyPartType _bodyPart)
	{
		return _bodyPart == BodyPartType.LeftArm || _bodyPart == BodyPartType.RightArm;
	}

	public static bool IsLeg(BodyPartType _bodyPart)
	{
		return _bodyPart == BodyPartType.LeftLeg || _bodyPart == BodyPartType.RightLeg;
	}

	public static bool IsLimb(BodyPartType _bodyPart)
	{
		return IsArm(_bodyPart) || IsLeg(_bodyPart);
	}

	public static string GetDisplayName(BodyPartType _bodyPart)
	{
		switch (_bodyPart)
		{
			case BodyPartType.Head:
				return LocalizationManager.Get("health.body_part.head", "Голова");
			case BodyPartType.Neck:
				return LocalizationManager.Get("health.body_part.neck", "Шея");
			case BodyPartType.Chest:
				return LocalizationManager.Get("health.body_part.chest", "Грудь");
			case BodyPartType.Abdomen:
				return LocalizationManager.Get("health.body_part.abdomen", "Живот");
			case BodyPartType.LeftArm:
				return LocalizationManager.Get("health.body_part.left_arm", "Левая рука");
			case BodyPartType.RightArm:
				return LocalizationManager.Get("health.body_part.right_arm", "Правая рука");
			case BodyPartType.LeftLeg:
				return LocalizationManager.Get("health.body_part.left_leg", "Левая нога");
			case BodyPartType.RightLeg:
				return LocalizationManager.Get("health.body_part.right_leg", "Правая нога");
			default:
				return LocalizationManager.Get("health.body_part.unknown", "Неизвестно");
		}
	}
}

/// <summary>
/// Зона попадания на дочернем коллайдере юнита.
/// Без этого компонента <see cref="DamageableTarget"/> работает как раньше: единый запас HP без зон тела.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitBodyHitZone : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private BodyPartType m_BodyPart = BodyPartType.Chest;

	[Header("Legacy HP Combat")]
	[SerializeField] private CombatBodyZone m_LegacyZone = CombatBodyZone.Torso;
	[SerializeField, Min(0f)] private float m_DamageMultiplier = 1f;
	[SerializeField] private bool m_IncludeInVision = true;

	[Header("Condition Effects")]
	[SerializeField] private bool m_MarksArmsWounded;
	[SerializeField] private bool m_MarksLegsWounded;
	[SerializeField] private bool m_MarksHeavyPain;
	[SerializeField, Min(0f)] private float m_MinDamageForConditionEffect = 1f;
	#endregion

	#region Public Properties
	public BodyPartType BodyPart => m_BodyPart;
	public CombatBodyZone Zone => m_BodyPart != BodyPartType.Unknown
		? BodyPartTypeUtility.ToCombatBodyZone(m_BodyPart)
		: m_LegacyZone;
	public float DamageMultiplier => m_DamageMultiplier;
	public bool IncludeInVision => m_IncludeInVision;
	#endregion

	#region Unity Lifecycle
#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_BodyPart != BodyPartType.Unknown)
			m_LegacyZone = BodyPartTypeUtility.ToCombatBodyZone(m_BodyPart);

		m_MarksArmsWounded = BodyPartTypeUtility.IsArm(m_BodyPart);
		m_MarksLegsWounded = BodyPartTypeUtility.IsLeg(m_BodyPart);
		m_MarksHeavyPain = m_BodyPart == BodyPartType.Head || m_BodyPart == BodyPartType.Neck;
	}
#endif
	#endregion

	#region Public Methods
	public void ApplyConditionEffects(UnitCombatCondition _condition, float _appliedDamage)
	{
		if (_condition == null || _appliedDamage < m_MinDamageForConditionEffect)
			return;

		if (m_MarksArmsWounded)
			_condition.SetArmsWounded(true);
		if (m_MarksLegsWounded)
			_condition.SetLegsWounded(true);
		if (m_MarksHeavyPain)
			_condition.SetHeavyPain(true);
	}
	#endregion
}
