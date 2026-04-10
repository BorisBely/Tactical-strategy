using UnityEngine;

/// <summary>
/// Базовые данные оружейной платформы без runtime-состояния.
/// </summary>
[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Polygone/Shooting/Weapon Definition", order = 10)]
public sealed class WeaponDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Класс оружия для логики, баланса и UI: пистолет, винтовка, дробовик и т.д.")]
	[SerializeField] private WeaponClassType m_WeaponClass = WeaponClassType.Rifle;

	[Header("Compatibility")]
	[Tooltip("Калибр патронов, с которыми это оружие совместимо.")]
	[SerializeField] private CaliberType m_SupportedCaliber = CaliberType.None;
	[Tooltip("Тип магазина, который можно вставить в это оружие.")]
	[SerializeField] private MagazineType m_SupportedMagazineType = MagazineType.None;
	[Tooltip("Режимы огня, которые поддерживает это оружие.")]
	[SerializeField] private WeaponFireMode[] m_AvailableFireModes =
	{
		WeaponFireMode.SemiAuto
	};
	[Tooltip("Режим огня, который выбирается по умолчанию при инициализации оружия.")]
	[SerializeField] private WeaponFireMode m_DefaultFireMode = WeaponFireMode.SemiAuto;
	[Tooltip("Слоты модулей, доступные на этой оружейной платформе.")]
	[SerializeField] private WeaponAttachmentSlotDefinition[] m_AttachmentSlots;

	[Header("Combat")]
	[Tooltip("Теоретическая скорострельность оружия в выстрелах в минуту.")]
	[SerializeField, Min(1f)] private float m_FireRateRpm = 600f;
	[Tooltip("Сколько времени нужно, чтобы выйти на полноценное качество прицеливания.")]
	[SerializeField, Min(0.01f)] private float m_AimTimeSeconds = 0.28f;
	[Tooltip("Базовое время смены магазина в этом оружии до модификаторов магазина и модулей.")]
	[SerializeField, Min(0.1f)] private float m_ReloadTimeSeconds = 2.2f;
	[Tooltip("Дистанция, до которой дальность сама по себе не даёт дополнительный штраф к стрельбе.")]
	[SerializeField, Min(0.1f)] private float m_EffectiveRangeMeters = 100f;
	[Tooltip("Базовый разброс оружейной платформы до модификаторов патрона, стойки, движения и отдачи.")]
	[SerializeField, Min(0f)] private float m_BaseShotDispersion = 1f;
	[Tooltip("Базовое накопление штрафа отдачи после одного выстрела.")]
	[SerializeField, Min(0f)] private float m_RecoilPerShot = 1f;
	[Tooltip("Множитель накопления отдачи при одиночной стрельбе.")]
	[SerializeField, Min(0f)] private float m_SemiAutoRecoilMultiplier = 0.85f;
	[Tooltip("Множитель накопления отдачи при автоматическом огне.")]
	[SerializeField, Min(0f)] private float m_AutoRecoilMultiplier = 1.25f;

	[Header("Reliability")]
	[Tooltip("Общая надёжность оружия: устойчивость к износу, загрязнению и проблемам в тяжёлых условиях.")]
	[SerializeField, Range(0f, 1f)] private float m_Reliability = 0.8f;
	[Tooltip("Порог износа, после которого у оружия вообще становится возможен клин.")]
	[SerializeField, Range(0f, 1f)] private float m_WearJamStartThreshold = 0.7f;
	[Tooltip("Порог загрязнённости, после которого у оружия вообще становится возможен клин.")]
	[SerializeField, Range(0f, 1f)] private float m_FoulingJamStartThreshold = 0.55f;
	[Tooltip("Насколько сильно износ после порога влияет на риск клина.")]
	[SerializeField, Min(0f)] private float m_WearJamInfluence = 1f;
	[Tooltip("Насколько сильно загрязнение после порога влияет на риск клина.")]
	[SerializeField, Min(0f)] private float m_FoulingJamInfluence = 1f;
	#endregion

	#region Public Properties
	public WeaponClassType WeaponClass => m_WeaponClass;
	public CaliberType SupportedCaliber => m_SupportedCaliber;
	public MagazineType SupportedMagazineType => m_SupportedMagazineType;
	public WeaponFireMode[] AvailableFireModes => m_AvailableFireModes;
	public WeaponFireMode DefaultFireMode => m_DefaultFireMode;
	public WeaponAttachmentSlotDefinition[] AttachmentSlots => m_AttachmentSlots;
	public float FireRateRpm => m_FireRateRpm;
	public float AimTimeSeconds => m_AimTimeSeconds;
	public float ReloadTimeSeconds => m_ReloadTimeSeconds;
	public float EffectiveRangeMeters => m_EffectiveRangeMeters;
	public float BaseShotDispersion => m_BaseShotDispersion;
	public float RecoilPerShot => m_RecoilPerShot;
	public float SemiAutoRecoilMultiplier => m_SemiAutoRecoilMultiplier;
	public float AutoRecoilMultiplier => m_AutoRecoilMultiplier;
	public float Reliability => m_Reliability;
	public float WearJamStartThreshold => m_WearJamStartThreshold;
	public float FoulingJamStartThreshold => m_FoulingJamStartThreshold;
	public float WearJamInfluence => m_WearJamInfluence;
	public float FoulingJamInfluence => m_FoulingJamInfluence;
	#endregion
}
