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
	Seven62By39 = 5
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
/// </summary>
public enum WeaponAimMode
{
	FullAim = 0,
	QuickAim = 1,
	SnapShot = 2,
	Auto = 3
}

/// <summary>
/// Баланс режимов неполного прицеливания и штрафов к разбросу.
/// </summary>
public static class WeaponAimModeUtility
{
	#region Constants
	public const float SnapShotAimProgress01 = 0.25f;
	public const float QuickAimProgress01 = 0.60f;
	public const float FullAimProgress01 = 1.00f;
	#endregion

	#region Public Methods
	public static WeaponAimMode ResolveEffectiveMode(WeaponAimMode _mode, float _distanceMeters)
	{
		if (_mode != WeaponAimMode.Auto)
			return _mode;

		return WeaponAimMode.FullAim;
	}

	public static float GetRequiredAimProgress01(WeaponAimMode _mode, float _distanceMeters)
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

	/// <summary>Время до выстрела при линейном накоплении AimProgress: полное время × порог режима.</summary>
	public static float GetRequiredAimTimeSeconds(float _fullAimTimeSeconds, WeaponAimMode _mode, float _distanceMeters)
	{
		return Mathf.Max(0f, _fullAimTimeSeconds * GetRequiredAimProgress01(_mode, _distanceMeters));
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

	public static float GetIncompleteAimSpreadMultiplier(float _aimProgress01)
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
	LightMachineGun = 5
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
	Internal = 5
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
	NotAimed = 12
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
/// Дистанционный профиль качества прицеливания на диапазоне 0..100 м.
/// Кривые поддерживают любое количество ключей, например 8-10 точек баланса по дистанции.
/// Множитель разброса: меньше 1 = точнее. Множитель времени прицеливания: меньше 1 = быстрее.
/// </summary>
[Serializable]
public sealed class WeaponDistanceAimProfile
{
	#region Constants
	private const float c_MinDistanceMeters = 0f;
	private const float c_MaxDistanceMeters = 100f;
	private const float c_MinMultiplier = 0.01f;
	#endregion

	#region Private Fields
	[Tooltip("Множитель разброса по дистанции 0..100 м. Можно добавить 8-10 ключей. Меньше 1 = точнее, больше 1 = хуже.")]
	[SerializeField] private AnimationCurve m_DispersionMultiplierByDistance = AnimationCurve.Linear(c_MinDistanceMeters, 1f, c_MaxDistanceMeters, 1f);
	[Tooltip("Множитель времени прицеливания по дистанции 0..100 м. Можно добавить 8-10 ключей. Меньше 1 = быстрее, больше 1 = медленнее.")]
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
