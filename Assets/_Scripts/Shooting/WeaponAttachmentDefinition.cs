using UnityEngine;

/// <summary>
/// Данные модуля оружия. Пока это только data-слой с модификаторами, без runtime-логики установки.
/// </summary>
[CreateAssetMenu(fileName = "WeaponAttachmentDefinition", menuName = "Polygone/Shooting/Weapon Attachment Definition", order = 13)]
public sealed class WeaponAttachmentDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Тип модуля (соответствует одному из семейств слотов: Muzzle / UnderBarrel / Rail / Optic).")]
	[SerializeField] private WeaponAttachmentType m_AttachmentType = WeaponAttachmentType.Optic;
	[Tooltip("Основной слот на оружии. Используется как fallback, если Compatible Slots пуст.")]
	[SerializeField] private WeaponAttachmentSlotType m_RequiredSlot = WeaponAttachmentSlotType.Optic;
	[Tooltip("Если включено, модуль ставится только на оружие из Compatible Weapons. Если выключено и список пустой — старые assets считаются совместимыми со всем.")]
	[SerializeField] private bool m_UseExplicitWeaponCompatibility;
	[Tooltip("Оружие, с которым совместим модуль. Пустой список = совместим со всеми оружиями старого контента.")]
	[SerializeField] private WeaponDefinition[] m_CompatibleWeapons;
	[Tooltip("Слоты, куда можно ставить модуль. Пустой список = только Required Slot.")]
	[SerializeField] private WeaponAttachmentSlotType[] m_CompatibleSlots;
	[Tooltip("Для Rail-модулей: допустимые физические RailSocket индексы 0..2 (0 сверху тактический ЛЦУ, 1 слева фонарь, 2 справа тактический или компактный ЛЦУ). Пустой список = любой RailSocket.")]
	[SerializeField] private int[] m_CompatibleRailSocketIndices;

	[Header("Modifiers")]
	[Tooltip("Как модуль меняет скорость прицеливания. Значение меньше 1 ускоряет, больше 1 замедляет.")]
	[SerializeField, Min(0f)] private float m_AimTimeModifier = 1f;
	[Tooltip("Как модуль меняет эффективную дальность оружия.")]
	[SerializeField, Min(0f)] private float m_EffectiveRangeModifier = 1f;
	[Tooltip("Абсолютная дальность обзора через кратную оптику (м). 0 или 150 = без бонуса. Clamp 150…300. Не EffectiveRangeModifier.")]
	[SerializeField, Min(0f)] private float m_ScopeVisionRangeMeters;
	[Tooltip("Как модуль меняет накопление отдачи.")]
	[SerializeField, Min(0f)] private float m_RecoilModifier = 1f;
	[Tooltip("Дополнительный множитель отдачи только для одиночного огня. 1 = использовать общий Recoil Modifier без изменений.")]
	[SerializeField, Min(0f)] private float m_SemiAutoRecoilModifier = 1f;
	[Tooltip("Дополнительный множитель отдачи для Burst / FullAuto / Auto. 1 = использовать общий Recoil Modifier без изменений.")]
	[SerializeField, Min(0f)] private float m_AutomaticRecoilModifier = 1f;
	[Tooltip("Как модуль меняет скорость смены магазина в оружии.")]
	[SerializeField, Min(0f)] private float m_ReloadTimeModifier = 1f;
	[Tooltip("Как модуль меняет точность и скорость прицеливания на дистанции 0..500 м.")]
	[SerializeField] private WeaponDistanceAimProfile m_DistanceAimProfile = new WeaponDistanceAimProfile();

	[Header("ЛЦУ")]
	[Tooltip("Улучшенный ЛЦУ: бонус PointAim держится дальше. Игнорируется, если тип не Laser Designator.")]
	[SerializeField] private bool m_IsImprovedLaser;
	[Tooltip("Максимальная дальность красной точки ЛЦУ (м). 0 = 50 м.")]
	[SerializeField, Min(0f)] private float m_LaserDotMaxRangeMeters;
	[Tooltip("Пик множителя разброса в PointAim (близко). Меньше 1 = точнее. 1 = нет бонуса. В Aiming не применяется.")]
	[SerializeField, Min(0.01f)] private float m_LaserPointAimSpreadMultiplier = 1f;
	[Tooltip("Пик множителя времени прицеливания в PointAim. Меньше 1 = быстрее.")]
	[SerializeField, Min(0.01f)] private float m_LaserPointAimAimTimeMultiplier = 1f;
	[Tooltip("Множитель времени прицеливания в Aiming (acquisition). Не даёт бонус к разбросу ADS.")]
	[SerializeField, Min(0.01f)] private float m_LaserAimingAimTimeMultiplier = 1f;
	[Tooltip("Доля бонуса PointAim по дистанции: 1 = полный бонус, 0 = бонус выключен.")]
	[SerializeField] private AnimationCurve m_LaserPointAimEffectByDistance;

	[Header("Weapon condition (за выстрел)")]
	[Tooltip("Множитель накопления износа от патрона за выстрел.")]
	[SerializeField, Min(0f)] private float m_WearPerShotMultiplier = 1f;
	[Tooltip("Множитель накопления загрязнения от патрона за выстрел.")]
	[SerializeField, Min(0f)] private float m_FoulingPerShotMultiplier = 1f;
	[Tooltip("Множитель вероятности клина с каждого выстрела (оба канала).")]
	[SerializeField, Min(0f)] private float m_JamRiskModifier = 1f;

	[Header("Audio")]
	[Tooltip("Множитель громкости основных клипов выстрела оружия при установленном глушителе (1 = без изменений).")]
	[SerializeField, Range(0f, 1f)] private float m_SuppressedFireVolumeMultiplier = 0.35f;
	[Tooltip("Опциональная макс. дистанция слышимости (м) с глушителем. 0 = как у профиля оружия.")]
	[SerializeField] private WeaponFireSoundProfile m_SuppressedFireSoundProfile = new WeaponFireSoundProfile();

	[Header("Визуал на оружии")]
	[Tooltip("Меш модуля в руках: родитель — сокет на EquippedWeapon (дуло / прицел / планка и т.д.), не Barrel и не Sight Pivot.")]
	[SerializeField] private GameObject m_EquippedVisualPrefab;
	#endregion

	#region Public Properties
	public WeaponAttachmentType AttachmentType => m_AttachmentType;
	public WeaponAttachmentSlotType RequiredSlot => m_RequiredSlot;
	public bool UseExplicitWeaponCompatibility => m_UseExplicitWeaponCompatibility;
	public WeaponDefinition[] CompatibleWeapons => m_CompatibleWeapons;
	public WeaponAttachmentSlotType[] CompatibleSlots => m_CompatibleSlots;
	public int[] CompatibleRailSocketIndices => m_CompatibleRailSocketIndices;
	public float AimTimeModifier => m_AimTimeModifier;
	public float EffectiveRangeModifier => m_EffectiveRangeModifier;
	public float ScopeVisionRangeMeters => m_ScopeVisionRangeMeters;
	public float RecoilModifier => m_RecoilModifier;
	public float SemiAutoRecoilModifier => m_SemiAutoRecoilModifier;
	public float AutomaticRecoilModifier => m_AutomaticRecoilModifier;
	public float ReloadTimeModifier => m_ReloadTimeModifier;
	public WeaponDistanceAimProfile DistanceAimProfile => m_DistanceAimProfile;
	public bool IsImprovedLaser =>
		m_AttachmentType == WeaponAttachmentType.LaserDesignator && m_IsImprovedLaser;
	public float LaserDotMaxRangeMeters => m_LaserDotMaxRangeMeters;
	public float LaserPointAimSpreadMultiplier => m_LaserPointAimSpreadMultiplier;
	public float LaserPointAimAimTimeMultiplier => m_LaserPointAimAimTimeMultiplier;
	public float LaserAimingAimTimeMultiplier => m_LaserAimingAimTimeMultiplier;
	public float WearPerShotMultiplier => m_WearPerShotMultiplier;
	public float FoulingPerShotMultiplier => m_FoulingPerShotMultiplier;
	public float JamRiskModifier => m_JamRiskModifier;
	public float SuppressedFireVolumeMultiplier => m_SuppressedFireVolumeMultiplier;
	public WeaponFireSoundProfile SuppressedFireSoundProfile => m_SuppressedFireSoundProfile;
	public GameObject EquippedVisualPrefab => m_EquippedVisualPrefab;
	#endregion

	#region Public Methods
	public void SetScopeVisionRangeMeters(float _meters)
	{
		m_ScopeVisionRangeMeters = Mathf.Max(0f, _meters);
	}

	public bool SupportsWeapon(WeaponDefinition _weaponDefinition)
	{
		if (_weaponDefinition == null)
			return false;
		if (!m_UseExplicitWeaponCompatibility && (m_CompatibleWeapons == null || m_CompatibleWeapons.Length == 0))
			return true;
		if (m_CompatibleWeapons == null || m_CompatibleWeapons.Length == 0)
			return false;

		for (int i = 0; i < m_CompatibleWeapons.Length; i++)
		{
			if (m_CompatibleWeapons[i] == _weaponDefinition)
				return true;
		}

		return false;
	}

	public float GetRecoilModifier(WeaponFireMode _fireMode)
	{
		float fireModeModifier = WeaponFireModeUtility.IsAutomaticEffectiveMode(_fireMode) || _fireMode == WeaponFireMode.Auto
			? m_AutomaticRecoilModifier
			: m_SemiAutoRecoilModifier;
		return Mathf.Max(0.01f, m_RecoilModifier) * Mathf.Max(0.01f, fireModeModifier);
	}

	public bool SupportsSlot(WeaponAttachmentSlotType _slotType)
	{
		if (m_CompatibleSlots == null || m_CompatibleSlots.Length == 0)
			return m_RequiredSlot == _slotType;

		for (int i = 0; i < m_CompatibleSlots.Length; i++)
		{
			if (m_CompatibleSlots[i] == _slotType)
				return true;
		}

		return false;
	}

	public bool SupportsWeaponSlot(WeaponAttachmentSlotType _slotType, int _railSocketIndex)
	{
		if (!SupportsSlot(_slotType))
			return false;
		if (_slotType != WeaponAttachmentSlotType.Rail)
			return true;
		if (m_CompatibleRailSocketIndices == null || m_CompatibleRailSocketIndices.Length == 0)
			return true;

		for (int i = 0; i < m_CompatibleRailSocketIndices.Length; i++)
		{
			if (m_CompatibleRailSocketIndices[i] == _railSocketIndex)
				return true;
		}

		return false;
	}

	public float GetDistanceDispersionMultiplier(float _distanceMeters)
	{
		if (m_AttachmentType == WeaponAttachmentType.LaserDesignator)
			return 1f;

		if (ShouldUseLibraryDistanceCurveFallback())
			return OpticDistanceCurveLibrary.EvaluateDispersionMultiplier(this, _distanceMeters);

		return m_DistanceAimProfile != null
			? m_DistanceAimProfile.GetDispersionMultiplier(_distanceMeters)
			: 1f;
	}

	public float GetDistanceAimTimeMultiplier(float _distanceMeters)
	{
		if (m_AttachmentType == WeaponAttachmentType.LaserDesignator)
			return 1f;

		if (ShouldUseLibraryDistanceCurveFallback())
			return OpticDistanceCurveLibrary.EvaluateAimTimeMultiplier(this, _distanceMeters);

		return m_DistanceAimProfile != null
			? m_DistanceAimProfile.GetAimTimeMultiplier(_distanceMeters)
			: 1f;
	}

	/// <summary>PointAim spread at distance. 1 = no bonus. Does not apply in Aiming/HipFire.</summary>
	public float EvaluateLaserPointAimSpread(float _distanceMeters)
	{
		if (m_AttachmentType != WeaponAttachmentType.LaserDesignator)
			return 1f;

		float peak = m_LaserPointAimSpreadMultiplier;
		if (Mathf.Approximately(peak, 1f))
			peak = RailAttachmentDistanceCurveLibrary.EvaluatePointAimSpreadModifier(this);
		if (Mathf.Approximately(peak, 1f))
			return 1f;

		return Mathf.Lerp(1f, peak, GetLaserPointAimEffect01(_distanceMeters));
	}

	/// <summary>PointAim aim-time at distance. 1 = no bonus.</summary>
	public float EvaluateLaserPointAimAimTime(float _distanceMeters)
	{
		if (m_AttachmentType != WeaponAttachmentType.LaserDesignator)
			return 1f;

		float peak = m_LaserPointAimAimTimeMultiplier;
		if (Mathf.Approximately(peak, 1f))
			peak = RailAttachmentDistanceCurveLibrary.EvaluatePointAimAimTimeModifier(this);
		if (Mathf.Approximately(peak, 1f))
			return 1f;

		return Mathf.Lerp(1f, peak, GetLaserPointAimEffect01(_distanceMeters));
	}

	/// <summary>Aiming acquisition only. 1 = no bonus. Never changes ADS spread.</summary>
	public float EvaluateLaserAimingAimTime()
	{
		if (m_AttachmentType != WeaponAttachmentType.LaserDesignator)
			return 1f;

		float value = m_LaserAimingAimTimeMultiplier;
		if (Mathf.Approximately(value, 1f))
			value = RailAttachmentDistanceCurveLibrary.EvaluateAimingAcquisitionModifier(this);
		return Mathf.Max(0.01f, value);
	}

	private float GetLaserPointAimEffect01(float _distanceMeters)
	{
		if (m_LaserPointAimEffectByDistance != null && m_LaserPointAimEffectByDistance.length > 0)
			return Mathf.Clamp01(m_LaserPointAimEffectByDistance.Evaluate(Mathf.Max(0f, _distanceMeters)));

		return RailAttachmentDistanceCurveLibrary.EvaluatePointAimEffect01(this, _distanceMeters);
	}

	private bool ShouldUseLibraryDistanceCurveFallback()
	{
		if (m_AttachmentType != WeaponAttachmentType.Optic || m_DistanceAimProfile == null)
			return false;

		return m_DistanceAimProfile.IsFlatDispersionCurve()
		       && m_DistanceAimProfile.IsFlatAimTimeCurve();
	}
	#endregion
}
