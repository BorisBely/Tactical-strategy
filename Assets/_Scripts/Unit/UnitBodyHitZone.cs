using UnityEngine;

/// <summary>
/// Опциональная зона попадания на дочернем коллайдере юнита.
/// Без этого компонента <see cref="DamageableTarget"/> работает как раньше: единый запас HP без зон тела.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitBodyHitZone : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private CombatBodyZone m_Zone = CombatBodyZone.Torso;
	[SerializeField, Min(0f)] private float m_DamageMultiplier = 1f;

	[Header("Condition Effects")]
	[SerializeField] private bool m_MarksArmsWounded;
	[SerializeField] private bool m_MarksLegsWounded;
	[SerializeField] private bool m_MarksHeavyPain;
	[SerializeField, Min(0f)] private float m_MinDamageForConditionEffect = 1f;
	#endregion

	#region Public Properties
	public CombatBodyZone Zone => m_Zone;
	public float DamageMultiplier => m_DamageMultiplier;
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

/// <summary>
/// Крупная зона тела для урона, UI и будущих эффектов ранений.
/// </summary>
public enum CombatBodyZone
{
	Unknown = 0,
	Head = 1,
	Torso = 2,
	Arms = 3,
	Legs = 4
}
