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
	Burst = 2
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
	WeaponBroken = 11
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
	#endregion

	#region Private Methods
	private static float EvaluateMultiplier(AnimationCurve _curve, float _distanceMeters)
	{
		if (_curve == null || _curve.length == 0)
			return 1f;

		float clampedDistance = Mathf.Clamp(_distanceMeters, c_MinDistanceMeters, c_MaxDistanceMeters);
		return Mathf.Max(c_MinMultiplier, _curve.Evaluate(clampedDistance));
	}
	#endregion
}
