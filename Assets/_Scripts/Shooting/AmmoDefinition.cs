using UnityEngine;

/// <summary>
/// Данные патрона. Именно патрон задаёт поражающие свойства и модификаторы выстрела.
/// </summary>
[CreateAssetMenu(fileName = "AmmoDefinition", menuName = "Polygone/Shooting/Ammo Definition", order = 11)]
public sealed class AmmoDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Калибр этого патрона. Должен совпадать с калибром оружия и магазина.")]
	[SerializeField] private CaliberType m_Caliber = CaliberType.None;

	[Header("Damage")]
	[Tooltip("Базовое поражающее действие патрона по незащищённой цели.")]
	[SerializeField, Min(0f)] private float m_BaseDamage = 20f;
	[Tooltip("Пробивное действие патрона.")]
	[SerializeField, Min(0f)] private float m_Penetration = 10f;
	[Tooltip("Насколько сильно патрон повреждает броню как объект.")]
	[SerializeField, Min(0f)] private float m_ArmorDamage = 5f;

	[Header("Ballistics")]
	[Tooltip("Количество поражающих элементов за один выстрел. Для обычной пули 1, для дроби больше 1.")]
	[SerializeField, Min(1)] private int m_ProjectileCount = 1;
	[Tooltip("Начальная скорость поражающего элемента.")]
	[SerializeField, Min(0.1f)] private float m_Velocity = 400f;
	[Tooltip("Эффективная дальность самого патрона до сильного ухудшения характеристик.")]
	[SerializeField, Min(0.1f)] private float m_EffectiveRangeMeters = 100f;

	[Header("Shot Modifiers")]
	[Tooltip("Как этот патрон меняет разброс текущего выстрела.")]
	[SerializeField, Min(0f)] private float m_SpreadModifier = 1f;
	[Tooltip("Как этот патрон меняет накопление отдачи оружия.")]
	[SerializeField, Min(0f)] private float m_RecoilModifier = 1f;

	[Header("Weapon Condition")]
	[Tooltip("Как этот патрон влияет на накопление износа оружия.")]
	[SerializeField, Min(0f)] private float m_WearModifier = 1f;
	[Tooltip("Как этот патрон влияет на загрязнение оружия.")]
	[SerializeField, Min(0f)] private float m_FoulingModifier = 1f;
	[Tooltip("Как этот патрон меняет риск клина, если оружие уже находится в зоне риска.")]
	[SerializeField, Min(0f)] private float m_JamRiskModifier = 1f;
	#endregion

	#region Public Properties
	public CaliberType Caliber => m_Caliber;
	public float BaseDamage => m_BaseDamage;
	public float Penetration => m_Penetration;
	public float ArmorDamage => m_ArmorDamage;
	public int ProjectileCount => m_ProjectileCount;
	public float Velocity => m_Velocity;
	public float EffectiveRangeMeters => m_EffectiveRangeMeters;
	public float SpreadModifier => m_SpreadModifier;
	public float RecoilModifier => m_RecoilModifier;
	public float WearModifier => m_WearModifier;
	public float FoulingModifier => m_FoulingModifier;
	public float JamRiskModifier => m_JamRiskModifier;
	#endregion
}
