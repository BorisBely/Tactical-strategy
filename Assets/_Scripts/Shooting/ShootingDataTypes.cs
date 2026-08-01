using System;
using UnityEngine;

/// <summary>
/// Тип калибра для совместимости оружия, патрона и магазина.
/// </summary>
public enum CaliberType
{
	None = 0,
	NineByNineteen = 1,
	Five45By39 = 2,
	Five56By45 = 3,
	TwelveGauge = 4,
	Seven62By39 = 5,
	Seven62By51 = 6,
	Seven62By54R = 7,
	/// <summary>12.7×99 NATO (.50 BMG) — M2 Browning.</summary>
	TwelvePointSevenByNinetyNine = 8,
	/// <summary>40×53mm — MK19.</summary>
	FortyByFiftyThree = 9
}

/// <summary>
/// Платформа анимации перезарядки / bolt-cycle для оружия.
/// </summary>
public enum WeaponAnimationPlatform
{
	/// <summary>M4 / AR и оружие без явной платформы.</summary>
	DefaultM = 0,
	/// <summary>AK-платформа и совместимые автоматы.</summary>
	Ak = 1,
	/// <summary>SVD и совместимые ДМР с AK-style bolt rack.</summary>
	Svd = 2
}

/// <summary>
/// Режим огня оружия.
/// </summary>
public enum WeaponFireMode
{
	SemiAuto = 0,
	FullAuto = 1,
	Burst = 2,
	Auto = 3
}

/// <summary>
/// Баланс выбранного и эффективного режима огня.
/// </summary>
public static class WeaponFireModeUtility
{
	#region Public Methods
	public static WeaponFireMode ResolveEffectiveMode(
		WeaponFireMode _selectedMode,
		float _distanceMeters,
		WeaponFireMode[] _availableModes)
	{
		if (_selectedMode != WeaponFireMode.Auto)
			return IsModeSupported(_selectedMode, _availableModes) ? _selectedMode : ResolveFallbackMode(_selectedMode, _availableModes);

		return ResolveFallbackMode(WeaponFireMode.SemiAuto, _availableModes);
	}

	public static bool IsAutomaticEffectiveMode(WeaponFireMode _mode)
	{
		return _mode == WeaponFireMode.FullAuto || _mode == WeaponFireMode.Burst;
	}

	/// <summary>
	/// Первый выстрел серии Burst/FullAuto: точность как у одиночного, дальше — штрафы автоматического огня.
	/// </summary>
	public static bool IsFirstShotInAutomaticSeries(WeaponFireMode _fireMode, int _burstShotIndex)
	{
		return IsAutomaticEffectiveMode(_fireMode) && _burstShotIndex <= 1;
	}

	public static bool IsModeSupported(WeaponFireMode _mode, WeaponFireMode[] _availableModes)
	{
		if (_availableModes == null || _availableModes.Length == 0)
			return _mode == WeaponFireMode.SemiAuto;

		for (int i = 0; i < _availableModes.Length; i++)
		{
			if (_availableModes[i] == _mode)
				return true;
		}

		return false;
	}

	public static string GetDisplayName(WeaponFireMode _mode)
	{
		return _mode switch
		{
			WeaponFireMode.SemiAuto => "одиночный",
			WeaponFireMode.Burst => "короткая очередь",
			WeaponFireMode.FullAuto => "автоматический",
			WeaponFireMode.Auto => "автовыбор",
			_ => _mode.ToString()
		};
	}
	#endregion

	#region Private Methods
	private static WeaponFireMode ResolveFallbackMode(WeaponFireMode _desiredMode, WeaponFireMode[] _availableModes)
	{
		if (IsModeSupported(_desiredMode, _availableModes) && _desiredMode != WeaponFireMode.Auto)
			return _desiredMode;

		switch (_desiredMode)
		{
			case WeaponFireMode.FullAuto:
				return FirstSupported(_availableModes, WeaponFireMode.FullAuto, WeaponFireMode.Burst, WeaponFireMode.SemiAuto);
			case WeaponFireMode.Burst:
				return FirstSupported(_availableModes, WeaponFireMode.Burst, WeaponFireMode.SemiAuto, WeaponFireMode.FullAuto);
			default:
				return FirstSupported(_availableModes, WeaponFireMode.SemiAuto, WeaponFireMode.Burst, WeaponFireMode.FullAuto);
		}
	}

	private static WeaponFireMode FirstSupported(WeaponFireMode[] _availableModes, params WeaponFireMode[] _preferredModes)
	{
		for (int i = 0; i < _preferredModes.Length; i++)
		{
			if (IsModeSupported(_preferredModes[i], _availableModes))
				return _preferredModes[i];
		}

		return WeaponFireMode.SemiAuto;
	}
	#endregion
}

/// <summary>
/// Режим, определяющий сколько AimProgress нужно накопить перед выстрелом.
/// Внутренний параметр точности; игрок выбирает <see cref="WeaponFireDisciplineMode"/>.
/// </summary>
public enum WeaponAimMode
{
	FullAim = 0,
	QuickAim = 1,
	SnapShot = 2,
	Auto = 3
}

/// <summary>
/// Огневая дисциплина юнита: длина/плотность очередей, паузы и порог прицеливания.
/// Заменяет ручной выбор режимов прицеливания.
/// </summary>
public enum WeaponFireDisciplineMode
{
	Economical = 0,
	Precision = 1,
	Suppressive = 2,
	Auto = 3
}

/// <summary>
/// План одной огневой серии: сколько стрелять, с каким порогом прицела и какой паузой после.
/// </summary>
public readonly struct WeaponFireDisciplinePlan
{
	public readonly WeaponFireDisciplineMode SelectedDiscipline;
	public readonly WeaponFireDisciplineMode EffectiveDiscipline;
	public readonly WeaponFireMode EffectiveFireMode;
	public readonly WeaponAimMode EffectiveAimMode;
	public readonly float RequiredAimProgress01;
	public readonly int SeriesShotCount;
	public readonly float SeriesPauseSeconds;
	public readonly float TargetDistanceMeters;

	public WeaponFireDisciplinePlan(
		WeaponFireDisciplineMode _selectedDiscipline,
		WeaponFireDisciplineMode _effectiveDiscipline,
		WeaponFireMode _effectiveFireMode,
		WeaponAimMode _effectiveAimMode,
		float _requiredAimProgress01,
		int _seriesShotCount,
		float _seriesPauseSeconds,
		float _targetDistanceMeters)
	{
		SelectedDiscipline = _selectedDiscipline;
		EffectiveDiscipline = _effectiveDiscipline;
		EffectiveFireMode = _effectiveFireMode;
		EffectiveAimMode = _effectiveAimMode;
		RequiredAimProgress01 = Mathf.Clamp01(_requiredAimProgress01);
		SeriesShotCount = Mathf.Max(1, _seriesShotCount);
		SeriesPauseSeconds = Mathf.Max(0f, _seriesPauseSeconds);
		TargetDistanceMeters = Mathf.Max(0f, _targetDistanceMeters);
	}
}

/// <summary>
/// Имена и цикл режимов огневой дисциплины.
/// </summary>
public static class WeaponFireDisciplineModeUtility
{
	#region Public Methods
	public static string GetDisplayName(WeaponFireDisciplineMode _mode)
	{
		return _mode switch
		{
			WeaponFireDisciplineMode.Economical => "экономный",
			WeaponFireDisciplineMode.Precision => "точный",
			WeaponFireDisciplineMode.Suppressive => "подавляющий",
			WeaponFireDisciplineMode.Auto => "авто",
			_ => _mode.ToString()
		};
	}

	public static WeaponFireDisciplineMode GetNextMode(WeaponFireDisciplineMode _current)
	{
		return _current switch
		{
			WeaponFireDisciplineMode.Economical => WeaponFireDisciplineMode.Precision,
			WeaponFireDisciplineMode.Precision => WeaponFireDisciplineMode.Suppressive,
			WeaponFireDisciplineMode.Suppressive => WeaponFireDisciplineMode.Auto,
			_ => WeaponFireDisciplineMode.Economical
		};
	}

	public static WeaponAimMode MapToAimMode(WeaponFireDisciplineMode _discipline, float _distanceMeters)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		switch (_discipline)
		{
			case WeaponFireDisciplineMode.Suppressive:
				if (distance <= 35f)
					return WeaponAimMode.SnapShot;
				if (distance <= 90f)
					return WeaponAimMode.QuickAim;
				return WeaponAimMode.FullAim;
			case WeaponFireDisciplineMode.Precision:
				if (distance <= 45f)
					return WeaponAimMode.QuickAim;
				return WeaponAimMode.FullAim;
			default:
				return WeaponAimMode.FullAim;
		}
	}
	#endregion
}

/// <summary>
/// Баланс режимов неполного прицеливания и штрафов к разбросу.
/// </summary>
public static class WeaponAimModeUtility
{
	#region Constants
	public const float SnapShotAimProgress01 = 0.35f;
	public const float QuickAimProgress01 = 0.68f;
	public const float FullAimProgress01 = 1.00f;

	public const float SnapShotMinAimTimeSeconds = 0.11f;
	public const float QuickAimMinAimTimeSeconds = 0.22f;
	public const float FullAimMinAimTimeSeconds = 0.32f;
	private const float c_AbsurdFullAimTimeThresholdSeconds = 0.15f;
	#endregion

	#region Public Methods
	public static WeaponAimMode ResolveEffectiveMode(WeaponAimMode _mode, float _distanceMeters)
	{
		if (_mode != WeaponAimMode.Auto)
			return _mode;

		return WeaponAimMode.FullAim;
	}

	public static float GetBaseRequiredAimProgress01(WeaponAimMode _mode, float _distanceMeters)
	{
		switch (ResolveEffectiveMode(_mode, _distanceMeters))
		{
			case WeaponAimMode.SnapShot:
				return SnapShotAimProgress01;
			case WeaponAimMode.QuickAim:
				return QuickAimProgress01;
			default:
				return FullAimProgress01;
		}
	}

	/// <summary>
	/// Порог AimProgress с учётом минимального времени до выстрела для режима.
	/// </summary>
	public static float GetRequiredAimProgress01(
		WeaponAimMode _mode,
		float _distanceMeters,
		float _fullAimTimeSeconds)
	{
		if (_fullAimTimeSeconds <= 0.01f)
			return GetBaseRequiredAimProgress01(_mode, _distanceMeters);

		float requiredTimeSeconds = GetRequiredAimTimeSeconds(_fullAimTimeSeconds, _mode, _distanceMeters);
		return Mathf.Clamp01(requiredTimeSeconds / _fullAimTimeSeconds);
	}

	public static float GetRequiredAimProgress01(WeaponAimMode _mode, float _distanceMeters) =>
		GetBaseRequiredAimProgress01(_mode, _distanceMeters);

	/// <summary>
	/// Время до выстрела: progress × full aim, затем пол минимального времени режима.
	/// FullAim не замедляется полом, если full aim уже медленнее sanity floor.
	/// </summary>
	public static float GetRequiredAimTimeSeconds(float _fullAimTimeSeconds, WeaponAimMode _mode, float _distanceMeters)
	{
		if (_fullAimTimeSeconds <= 0f)
			return 0f;

		WeaponAimMode effectiveMode = ResolveEffectiveMode(_mode, _distanceMeters);
		if (effectiveMode == WeaponAimMode.FullAim)
		{
			return _fullAimTimeSeconds < c_AbsurdFullAimTimeThresholdSeconds
				? FullAimMinAimTimeSeconds
				: _fullAimTimeSeconds;
		}

		float scaledTimeSeconds = _fullAimTimeSeconds * GetBaseRequiredAimProgress01(_mode, _distanceMeters);
		float minimumTimeSeconds = effectiveMode == WeaponAimMode.SnapShot
			? SnapShotMinAimTimeSeconds
			: QuickAimMinAimTimeSeconds;
		return Mathf.Max(scaledTimeSeconds, minimumTimeSeconds);
	}

	public static string GetDisplayName(WeaponAimMode _mode)
	{
		return _mode switch
		{
			WeaponAimMode.FullAim => "прицельная",
			WeaponAimMode.QuickAim => "быстрое",
			WeaponAimMode.SnapShot => "на вскидку",
			WeaponAimMode.Auto => "авто",
			_ => _mode.ToString()
		};
	}

	public static float GetIncompleteAimSpreadMultiplier(float _aimProgress01) =>
		GetIncompleteAimSpreadMultiplier(_aimProgress01, 25f);

	public static float GetIncompleteAimSpreadMultiplier(float _aimProgress01, float _distanceMeters)
	{
		float baseMultiplier = GetIncompleteAimSpreadMultiplierByProgress(_aimProgress01);
		if (baseMultiplier <= 1f)
			return 1f;

		float excess = baseMultiplier - 1f;
		float distanceScale = GetIncompleteAimDistancePenaltyScale(_distanceMeters);
		return 1f + excess * distanceScale;
	}
	#endregion

	#region Private Methods
	private static float GetIncompleteAimSpreadMultiplierByProgress(float _aimProgress01)
	{
		float progress = Mathf.Clamp01(_aimProgress01);
		if (progress >= FullAimProgress01)
			return 1f;

		if (progress >= 0.85f)
			return Mathf.Lerp(1.15f, 1f, Mathf.InverseLerp(0.85f, FullAimProgress01, progress));
		if (progress >= QuickAimProgress01)
			return Mathf.Lerp(1.45f, 1.15f, Mathf.InverseLerp(QuickAimProgress01, 0.85f, progress));
		if (progress >= SnapShotAimProgress01)
			return Mathf.Lerp(2.20f, 1.45f, Mathf.InverseLerp(SnapShotAimProgress01, QuickAimProgress01, progress));

		return Mathf.Lerp(3.00f, 2.20f, Mathf.InverseLerp(0f, SnapShotAimProgress01, progress));
	}

	private static float GetIncompleteAimDistancePenaltyScale(float _distanceMeters)
	{
		float distance = Mathf.Max(0f, _distanceMeters * 0.2f);
		if (distance <= 10f)
			return 0.60f;
		if (distance <= 25f)
			return Mathf.Lerp(0.60f, 1.00f, Mathf.InverseLerp(10f, 25f, distance));
		if (distance <= 50f)
			return Mathf.Lerp(1.00f, 1.25f, Mathf.InverseLerp(25f, 50f, distance));
		if (distance <= 100f)
			return Mathf.Lerp(1.25f, 1.50f, Mathf.InverseLerp(50f, 100f, distance));

		return 1.50f;
	}
	#endregion
}

/// <summary>
/// Класс оружия для геймплея, баланса и UI.
/// </summary>
public enum WeaponClassType
{
	Unknown = 0,
	Pistol = 1,
	Rifle = 2,
	Shotgun = 3,
	SubmachineGun = 4,
	LightMachineGun = 5,
	/// <summary>Крупнокалиберный пулемёт / орудие турели.</summary>
	HeavyMachineGun = 6,
	/// <summary>Автоматический гранатомёт (MK19 и аналоги).</summary>
	AutomaticGrenadeLauncher = 7
}

/// <summary>
/// Тип магазина для совместимости с оружием.
/// </summary>
public enum MagazineType
{
	None = 0,
	PistolStandard = 1,
	RifleStandard = 2,
	ShotgunTube = 3,
	Drum = 4,
	Internal = 5,
	/// <summary>Магазины СВД (7.62x54R) — не совместимы с болтовой винтовкой.</summary>
	Svd = 6,
	/// <summary>Магазины болтовой винтовки 7.62x54R — не совместимы с СВД.</summary>
	Bolt762x54R = 7,
	/// <summary>Короба M249 5.56x45 — отдельный тип, чтобы не смешивать с барабанами/коробами других платформ.</summary>
	M249Box = 8,
	/// <summary>Короба PKM 7.62x54R — отдельный тип, чтобы не смешивать с барабанами/коробами других платформ.</summary>
	PkmBox = 9,
	/// <summary>Короб ленты M2 Browning 12.7×99.</summary>
	M2Box = 10,
	/// <summary>Короб гранат MK19 40×53.</summary>
	Mk19Box = 11
}

/// <summary>
/// Профиль доступных слотов модулей на оружейной платформе (runtime/UI; сокеты на префабе не трогаем).
/// </summary>
public enum WeaponAttachmentSlotProfile
{
	/// <summary>Все слоты из WeaponDefinition доступны.</summary>
	Full = 0,
	/// <summary>Стоковый АК: дуло + боковая планка (+ магазин).</summary>
	StockAk = 1,
	/// <summary>Тактический АК Mod.1: дуло, верхняя и боковая планка прицела, под стволом, три rail (+ магазин).</summary>
	Mod1Ak = 2,
	/// <summary>M16A: дуло + оптика, без приклада и без rail/underbarrel.</summary>
	M4BasicOpticNoStock = 3,
	/// <summary>M16A4 tactical: дуло, оптика, underbarrel, rail x3; без приклада.</summary>
	M4TacticalNoStock = 4
}

/// <summary>
/// Слот под модуль на корпусе оружия. Магазин отдельно, не является слотом здесь.
/// До трёх планок: три записи <see cref="WeaponAttachmentSlotType.Rail"/> в массиве слотов <c>WeaponDefinition</c>.
/// </summary>
public enum WeaponAttachmentSlotType
{
	/// <summary>Дуло: глушитель / компенсатор / пламегаситель. На оружии один такой слот.</summary>
	Muzzle = 0,
	/// <summary>Под стволом: рукоятка / сошки / подствольный гранатомёт. Один слот.</summary>
	UnderBarrel = 1,
	/// <summary>Планка (над стволом / сбоку): фонарик, ЛЦУ или накладки. Повторяйте значение до 3 раз для трёх физических слотов.</summary>
	Rail = 2,
	/// <summary>Прицел. Один слот.</summary>
	Optic = 3,
	/// <summary>Приклад. Один слот на оружиях, где приклад рассматривается как сменный модуль.</summary>
	Stock = 4,
	/// <summary>Боковая планка (для АК-платформы): прицелы с креплением Side rail. Взаимоисключаем с <see cref="Optic"/>, если оба слота есть на оружии.</summary>
	SideRail = 5
}

/// <summary>
/// Вид модуля. Слот: дульные → <see cref="WeaponAttachmentSlotType.Muzzle"/>; рукоятка/сошки/ПГ → <see cref="WeaponAttachmentSlotType.UnderBarrel"/>;
/// фонарик/ЛЦУ → <see cref="WeaponAttachmentSlotType.Rail"/>; прицел → <see cref="WeaponAttachmentSlotType.Optic"/>; боковая планка АК → <see cref="WeaponAttachmentSlotType.SideRail"/>; приклад → <see cref="WeaponAttachmentSlotType.Stock"/>.
/// </summary>
public enum WeaponAttachmentType
{
	Optic = 0,
	Suppressor = 1,
	Compensator = 2,
	FlashHider = 3,
	Foregrip = 4,
	Bipod = 5,
	UnderBarrelGrenadeLauncher = 6,
	Flashlight = 7,
	/// <summary>ЛЦУ / лазерный целеуказатель (луч).</summary>
	LaserDesignator = 8,
	/// <summary>Сменный приклад, влияющий на управляемость оружия.</summary>
	Stock = 9,
	/// <summary>Накладки на планки, занимают rail-слот и немного улучшают удержание оружия.</summary>
	RailCover = 10
}

/// <summary>
/// Результат попытки сделать один выстрел из текущего оружия.
/// </summary>
public enum WeaponShotAttemptResult
{
	Success = 0,
	NoWeapon = 1,
	NoMagazine = 2,
	EmptyMagazine = 3,
	FireRateLimited = 4,
	NotReady = 5,
	NoVisibleTarget = 6,
	Busy = 7,
	/// <summary>Магазин с патронами есть, но патронник пуст — нужно передёргивание затвора.</summary>
	NeedsBoltCycle = 8,
	/// <summary>Оружие в отказе; выстрел невозможен до снятия клина.</summary>
	MalfunctionStoppage = 9,
	/// <summary>Только что произошёл отказ (щелчок без выстрела).</summary>
	MalfunctionOccurred = 10,
	/// <summary>Оружие в окончательной неисправности (негодно к экипировке).</summary>
	WeaponBroken = 11,
	/// <summary>Прицеливание после отдачи/поворота ещё не завершено.</summary>
	NotAimed = 12,
	/// <summary>Между стволом и целью находится союзник или нейтрал.</summary>
	LineOfFireBlocked = 13
}

/// <summary>Тип текущего отказа (лёгкий / тяжёлый).</summary>
public enum WeaponMalfunctionKind
{
	None = 0,
	Light = 1,
	Heavy = 2
}

/// <summary>Канал, по которому сработал отказ (износ или загрязнение). Одновременно только один.</summary>
public enum WeaponMalfunctionChannel
{
	None = 0,
	Wear = 1,
	Fouling = 2
}

/// <summary>Фаза единого сценария снятия отказа.</summary>
public enum WeaponMalfunctionPhase
{
	None = 0,
	/// <summary>Магазин на месте: до трёх rack с лестницей 50/75/100.</summary>
	PhaseARackWithMag = 1,
	/// <summary>Тяжёлый: снятие магазина и перезарядка с тем же магазином.</summary>
	PhaseBStripAndReinsert = 2
}

/// <summary>Ступень таблицы износа (целостность C) или загрязнения (F).</summary>
public enum WeaponMalfunctionTier
{
	None = 0,
	LightOnly = 1,
	LightOrHeavy = 2,
	HeavyOnly = 3,
	Terminal = 4
}

/// <summary>
/// Описание доступного слота модуля на оружии (платформа <c>WeaponDefinition</c>).
/// </summary>
[Serializable]
public struct WeaponAttachmentSlotDefinition
{
	public WeaponAttachmentSlotType SlotType;
	public bool IsRequired;
	[Tooltip("Зарезервировано. Визуал модулей вешается на сокеты в EquippedWeapon (Muzzle / Optic / Rail / …), не на Barrel и не на Sight Pivot.")]
	public string AnchorChildName;
}

/// <summary>
/// Дистанционный профиль качества прицеливания на диапазоне 0..500 м.
/// Кривые поддерживают любое количество ключей, например 8-10 точек баланса по дистанции.
/// Множитель разброса: меньше 1 = точнее. Множитель времени прицеливания: меньше 1 = быстрее.
/// </summary>
[Serializable]
public sealed class WeaponDistanceAimProfile
{
	#region Constants
	private const float c_MinDistanceMeters = 0f;
	private const float c_MaxDistanceMeters = 500f;
	private const float c_MinMultiplier = 0.01f;
	#endregion

	#region Private Fields
	[Tooltip("Множитель разброса по дистанции 0..500 м. Можно добавить 8-10 ключей. Меньше 1 = точнее, больше 1 = хуже.")]
	[SerializeField] private AnimationCurve m_DispersionMultiplierByDistance = AnimationCurve.Linear(c_MinDistanceMeters, 1f, c_MaxDistanceMeters, 1f);
	[Tooltip("Множитель времени прицеливания по дистанции 0..500 м. Можно добавить 8-10 ключей. Меньше 1 = быстрее, больше 1 = медленнее.")]
	[SerializeField] private AnimationCurve m_AimTimeMultiplierByDistance = AnimationCurve.Linear(c_MinDistanceMeters, 1f, c_MaxDistanceMeters, 1f);
	#endregion

	#region Public Methods
	public float GetDispersionMultiplier(float _distanceMeters)
	{
		return EvaluateMultiplier(m_DispersionMultiplierByDistance, _distanceMeters);
	}

	public float GetAimTimeMultiplier(float _distanceMeters)
	{
		return EvaluateMultiplier(m_AimTimeMultiplierByDistance, _distanceMeters);
	}

	public void SetCurves(AnimationCurve _dispersionMultiplierByDistance, AnimationCurve _aimTimeMultiplierByDistance)
	{
		m_DispersionMultiplierByDistance = _dispersionMultiplierByDistance;
		m_AimTimeMultiplierByDistance = _aimTimeMultiplierByDistance;
	}

	public bool IsFlatDispersionCurve() => IsFlatNeutralCurve(m_DispersionMultiplierByDistance);

	public bool IsFlatAimTimeCurve() => IsFlatNeutralCurve(m_AimTimeMultiplierByDistance);
	#endregion

	#region Private Methods
	private static bool IsFlatNeutralCurve(AnimationCurve _curve)
	{
		if (_curve == null || _curve.length == 0)
			return true;

		for (int i = 0; i < _curve.length; i++)
		{
			if (!Mathf.Approximately(_curve.keys[i].value, 1f))
				return false;
		}

		return true;
	}

	private static float EvaluateMultiplier(AnimationCurve _curve, float _distanceMeters)
	{
		if (_curve == null || _curve.length == 0)
			return 1f;

		float clampedDistance = Mathf.Clamp(_distanceMeters, c_MinDistanceMeters, c_MaxDistanceMeters);
		int count = _curve.length;
		if (count == 1)
			return Mathf.Max(c_MinMultiplier, _curve.keys[0].value);

		Keyframe[] keys = _curve.keys;
		if (clampedDistance <= keys[0].time)
			return Mathf.Max(c_MinMultiplier, keys[0].value);
		if (clampedDistance >= keys[count - 1].time)
			return Mathf.Max(c_MinMultiplier, keys[count - 1].value);

		for (int i = 0; i < count - 1; i++)
		{
			float t0 = keys[i].time;
			float t1 = keys[i + 1].time;
			if (t0 > clampedDistance || clampedDistance > t1)
				continue;

			if (Mathf.Approximately(t1, t0))
				return Mathf.Max(c_MinMultiplier, keys[i].value);

			float t = (clampedDistance - t0) / (t1 - t0);
			return Mathf.Max(c_MinMultiplier, Mathf.Lerp(keys[i].value, keys[i + 1].value, t));
		}

		return Mathf.Max(c_MinMultiplier, keys[count - 1].value);
	}
	#endregion
}

/// <summary>Снимок отдачи и процедурного паттерна для лога выстрела (штраф до добавления за текущий выстрел).</summary>
public readonly struct WeaponShotRecoilLogInfo
{
	public readonly float RecoilPenaltyBeforeShot;
	public readonly float MaxRecoilPenalty;
	public readonly bool IsAtCap;
	public readonly bool PatternApplied;
	public readonly float PatternPitchDegrees;
	public readonly float PatternYawDegrees;
	public readonly float PatternVerticalOffsetMeters;
	public readonly float RecoilAddedPerShot;
	public readonly float RecoveryPerSecond;
	public readonly bool IsRecoveringWhileFiring;
	public readonly float EstimatedNetPenaltyPerSecond;
	public readonly float PitchDegreesPerPenaltyUnit;
	public readonly float RecoilSpreadScale;
	public readonly float RecoilSpreadMultiplier;

	public bool HasPatternData => PatternApplied || RecoilPenaltyBeforeShot > 0.0001f;

	public WeaponShotRecoilLogInfo(
		float _recoilPenaltyBeforeShot,
		float _maxRecoilPenalty,
		bool _isAtCap,
		bool _patternApplied,
		float _patternPitchDegrees,
		float _patternYawDegrees,
		float _patternVerticalOffsetMeters,
		float _recoilAddedPerShot,
		float _recoveryPerSecond,
		bool _isRecoveringWhileFiring,
		float _estimatedNetPenaltyPerSecond,
		float _pitchDegreesPerPenaltyUnit,
		float _recoilSpreadScale,
		float _recoilSpreadMultiplier)
	{
		RecoilPenaltyBeforeShot = _recoilPenaltyBeforeShot;
		MaxRecoilPenalty = _maxRecoilPenalty;
		IsAtCap = _isAtCap;
		PatternApplied = _patternApplied;
		PatternPitchDegrees = _patternPitchDegrees;
		PatternYawDegrees = _patternYawDegrees;
		PatternVerticalOffsetMeters = _patternVerticalOffsetMeters;
		RecoilAddedPerShot = _recoilAddedPerShot;
		RecoveryPerSecond = _recoveryPerSecond;
		IsRecoveringWhileFiring = _isRecoveringWhileFiring;
		EstimatedNetPenaltyPerSecond = _estimatedNetPenaltyPerSecond;
		PitchDegreesPerPenaltyUnit = _pitchDegreesPerPenaltyUnit;
		RecoilSpreadScale = _recoilSpreadScale;
		RecoilSpreadMultiplier = _recoilSpreadMultiplier;
	}
}

/// <summary>Снимок стойки/движения юнита для лога выстрела.</summary>
public readonly struct WeaponShotPostureLogInfo
{
	public readonly string Label;
	public readonly float SpreadMultiplier;
	public readonly float AimTimeMultiplier;
	public readonly float RecoilMultiplier;
	public readonly bool IsSprinting;

	public bool HasValue => !string.IsNullOrEmpty(Label);

	public WeaponShotPostureLogInfo(
		string _label,
		float _spreadMultiplier,
		float _aimTimeMultiplier,
		float _recoilMultiplier,
		bool _isSprinting)
	{
		Label = _label;
		SpreadMultiplier = _spreadMultiplier;
		AimTimeMultiplier = _aimTimeMultiplier;
		RecoilMultiplier = _recoilMultiplier;
		IsSprinting = _isSprinting;
	}
}
