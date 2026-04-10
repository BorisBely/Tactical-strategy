using UnityEngine;

/// <summary>
/// Данные магазина: вместимость, тип, снаряжение патронами и влияние на перезарядку/задержки.
/// </summary>
[CreateAssetMenu(fileName = "MagazineDefinition", menuName = "Polygone/Shooting/Magazine Definition", order = 12)]
public sealed class MagazineDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Тип магазина для проверки совместимости с оружием.")]
	[SerializeField] private MagazineType m_MagazineType = MagazineType.None;
	[Tooltip("Калибр патронов, которые можно снаряжать в этот магазин.")]
	[SerializeField] private CaliberType m_SupportedCaliber = CaliberType.None;

	[Header("Capacity")]
	[Tooltip("Максимальное количество патронов, которое вмещает магазин.")]
	[SerializeField, Min(1)] private int m_Capacity = 30;

	[Header("Handling")]
	[Tooltip("Сколько времени занимает загрузка одного патрона в магазин вне боя.")]
	[SerializeField, Min(0.01f)] private float m_RoundLoadTimeSeconds = 0.35f;
	[Tooltip("Как этот магазин меняет скорость его смены в оружии.")]
	[SerializeField, Min(0f)] private float m_ReloadTimeModifier = 1f;

	[Header("Reliability")]
	[Tooltip("Как этот магазин влияет на риск задержки или клина.")]
	[SerializeField, Min(0f)] private float m_JamRiskModifier = 1f;
	#endregion

	#region Public Properties
	public MagazineType MagazineType => m_MagazineType;
	public CaliberType SupportedCaliber => m_SupportedCaliber;
	public int Capacity => m_Capacity;
	public float RoundLoadTimeSeconds => m_RoundLoadTimeSeconds;
	public float ReloadTimeModifier => m_ReloadTimeModifier;
	public float JamRiskModifier => m_JamRiskModifier;
	#endregion
}
