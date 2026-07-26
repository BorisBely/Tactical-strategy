using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Polygone/Vehicle Physics/Surface Definition", fileName = "Surface_")]
public sealed class SurfacePhysicsDefinition : ScriptableObject
{
	#region Serialized Fields
	[SerializeField, Tooltip("Множитель продольного сцепления (1.0 = асфальт)")]
	private float m_ForwardGripMultiplier = 1f;
	[SerializeField, Tooltip("Множитель бокового сцепления")]
	private float m_LateralGripMultiplier = 1f;
	[SerializeField, Tooltip("Множитель сопротивления качению")]
	private float m_RollingResistanceMultiplier = 1f;
	[SerializeField, Tooltip("Глубина проседания колеса в поверхность (м)")]
	private float m_Sinkage;
	[SerializeField, Range(0f, 1f), Tooltip("Твёрдость деформируемой поверхности (1 = асфальт)")]
	private float m_DeformationHardness = 1f;
	[SerializeField, Tooltip("Амплитуда вибрации от микрорельефа (м)")]
	private float m_Bumpiness;
	[SerializeField, Range(0f, 1f), Tooltip("Мягкость поверхности (влияет на демпфирование подвески)")]
	private float m_Softness;
	[SerializeField, Tooltip("Глубина воды (м, 0 = сухо)")]
	private float m_WaterDepth;
	[SerializeField, Tooltip("Ключевые слова для автоопределения по имени PhysicMaterial")]
	private string[] m_MatchKeywords = Array.Empty<string>();
	#endregion

	#region Public Properties
	public float ForwardGripMultiplier => m_ForwardGripMultiplier;
	public float LateralGripMultiplier => m_LateralGripMultiplier;
	public float RollingResistanceMultiplier => m_RollingResistanceMultiplier;
	public float Sinkage => m_Sinkage;
	public float DeformationHardness => m_DeformationHardness;
	public float Bumpiness => m_Bumpiness;
	public float Softness => m_Softness;
	public float WaterDepth => m_WaterDepth;
	public string[] MatchKeywords => m_MatchKeywords;
	#endregion
}
