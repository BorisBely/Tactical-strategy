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
	[Tooltip("Скорострельность для FullAuto и Burst (и для Semi, если Semi Auto Fire Rate Rpm = 0).")]
	[SerializeField, Min(1f)] private float m_FireRateRpm = 600f;
	[Tooltip("Если > 0 — отдельный лимит RPM только для SemiAuto (длиннее пауза между выстрелами при быстром клике ИИ). 0 = использовать Fire Rate Rpm.")]
	[SerializeField, Min(0f)] private float m_SemiAutoFireRateRpm = 0f;
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
	[Tooltip("Сколько единиц накопленной отдачи оружие восстанавливает за секунду.")]
	[SerializeField, Min(0f)] private float m_RecoilRecoveryPerSecond = 3.5f;
	[Tooltip("Длина очереди в режиме Burst.")]
	[SerializeField, Min(2)] private int m_BurstRounds = 3;
	[Tooltip("Пауза между очередями в режиме Burst (сек).")]
	[SerializeField, Min(0f)] private float m_BurstPauseSeconds = 0.12f;

	[Header("Fire Audio")]
	[Tooltip("Звук выстрела (воспроизводит UnitWeaponFireAudio на юните; можно переопределить на патроне).")]
	[SerializeField] private AudioClip m_FireSound;
	[SerializeField, Range(0f, 1f)] private float m_FireSoundVolume = 1f;
	[SerializeField, Range(0f, 0.3f)] private float m_FirePitchVariance = 0.04f;

	[Header("Reload Audio")]
	[Tooltip("Звук снятия магазина — AnimationEvent_EjectCurrentWeaponMagazineToInventory.")]
	[SerializeField] private AudioClip m_ReloadMagOutSound;
	[Tooltip("Звук вставки магазина — AnimationEvent_InsertPendingMagazineIntoWeapon.")]
	[SerializeField] private AudioClip m_ReloadMagInSound;
	[Tooltip("Звук передёргивания затвора — только ивент AnimationEvent_FinishWeaponReload (клип затвора или legacy один клип с досылом в конце).")]
	[SerializeField] private AudioClip m_BoltCycleSound;
	[Tooltip("Если включено: после вставки магазина патрон в патронник досылается ивентом ReloadBoltHoldOpenDelay в конце клипа перезарядки (звук + досыл). Иначе после вставки Animator получает IsCyclingBolt и досыл по FinishWeaponReload в клипе затвора.")]
	[SerializeField] private bool m_HasBoltHoldOpenDelay;
	[Tooltip("Звук затворной задержки / отпускания затвора — только при включённой затворной задержке, ивент ReloadBoltHoldOpenDelay на контроллере перезарядки.")]
	[SerializeField] private AudioClip m_ReloadBoltHoldOpenDelaySound;
	[SerializeField, Range(0f, 1f)] private float m_ReloadSoundsVolume = 1f;

	[Header("Reliability")]
	[Tooltip("Общая надёжность оружия: устойчивость к износу, загрязнению и проблемам в тяжёлых условиях.")]
	[SerializeField, Range(0f, 1f)] private float m_Reliability = 0.8f;
	[Tooltip("Чем больше — тем медленнее накапливается износ (Wear 0…1) от патронов: прибавка = WearPerShot×множители / это значение.")]
	[SerializeField, Min(1f)] private float m_BaseDurability = 2000f;
	[Tooltip("Условный «бюджет» до 100%% загрязнения (Fouling 0…1) при нейтральном патроне 1.0: прибавка = FoulingPerShot×множители / это значение.")]
	[SerializeField, Min(1f)] private float m_BaseFoulingBudget = 1500f;
	[Tooltip("Минимальный нормализованный износ (0…1), с которого канал износа может давать клин (поверх ступеней C). 0 = только таблица C.")]
	[SerializeField, Range(0f, 1f)] private float m_WearJamStartThreshold = 0f;
	[Tooltip("Минимальная загрязнённость (0…1), с которой канал загрязнения может давать клин (поверх ступеней F). 0 = только таблица F.")]
	[SerializeField, Range(0f, 1f)] private float m_FoulingJamStartThreshold = 0f;
	[Tooltip("Общий множитель вероятности клина по каналу износа за выстрел (нагрузка по износу × ступень × патрон/магазин/модули).")]
	[SerializeField, Min(0f)] private float m_WearJamInfluence = 1f;
	[Tooltip("Общий множитель вероятности клина по каналу загрязнения за выстрел.")]
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
	public float SemiAutoFireRateRpm => m_SemiAutoFireRateRpm;
	public float AimTimeSeconds => m_AimTimeSeconds;
	public float ReloadTimeSeconds => m_ReloadTimeSeconds;
	public float EffectiveRangeMeters => m_EffectiveRangeMeters;
	public float BaseShotDispersion => m_BaseShotDispersion;
	public float RecoilPerShot => m_RecoilPerShot;
	public float SemiAutoRecoilMultiplier => m_SemiAutoRecoilMultiplier;
	public float AutoRecoilMultiplier => m_AutoRecoilMultiplier;
	public float RecoilRecoveryPerSecond => m_RecoilRecoveryPerSecond;
	public int BurstRounds => m_BurstRounds;
	public float BurstPauseSeconds => m_BurstPauseSeconds;
	public AudioClip FireSound => m_FireSound;
	public float FireSoundVolume => m_FireSoundVolume;
	public float FirePitchVariance => m_FirePitchVariance;
	public AudioClip ReloadMagOutSound => m_ReloadMagOutSound;
	public AudioClip ReloadMagInSound => m_ReloadMagInSound;
	public AudioClip BoltCycleSound => m_BoltCycleSound;
	public bool HasBoltHoldOpenDelay => m_HasBoltHoldOpenDelay;
	public AudioClip ReloadBoltHoldOpenDelaySound => m_ReloadBoltHoldOpenDelaySound;
	public float ReloadSoundsVolume => m_ReloadSoundsVolume;
	public float Reliability => m_Reliability;
	public float BaseDurability => m_BaseDurability;
	public float BaseFoulingBudget => m_BaseFoulingBudget;
	public float WearJamStartThreshold => m_WearJamStartThreshold;
	public float FoulingJamStartThreshold => m_FoulingJamStartThreshold;
	public float WearJamInfluence => m_WearJamInfluence;
	public float FoulingJamInfluence => m_FoulingJamInfluence;
	#endregion

	#region Static Helpers
	/// <summary>
	/// Тот же вклад в <see cref="EquippedWeaponTransientState.RecoilPenalty"/>, что и после одного выстрела
	/// (совпадает с логикой <see cref="UnitWeaponRecoilController"/> при том же модификаторе обвесов).
	/// </summary>
	public static float ComputeAddedRecoilPenalty(
		WeaponDefinition weaponDefinition,
		WeaponFireMode fireMode,
		AmmoDefinition ammoDefinition,
		float attachmentRecoilModifier = 1f)
	{
		if (weaponDefinition == null)
			return 0f;

		float fireModeMultiplier = fireMode switch
		{
			WeaponFireMode.FullAuto => weaponDefinition.AutoRecoilMultiplier,
			_ => weaponDefinition.SemiAutoRecoilMultiplier
		};

		float ammoModifier = ammoDefinition != null ? ammoDefinition.RecoilModifier : 1f;
		return weaponDefinition.RecoilPerShot * fireModeMultiplier * ammoModifier * attachmentRecoilModifier;
	}
	#endregion
}
