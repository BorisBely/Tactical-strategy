using System;

/// <summary>
/// Тип калибра для совместимости оружия, патрона и магазина.
/// </summary>
public enum CaliberType
{
	None = 0,
	NineByNineteen = 1,
	Five45By39 = 2,
	Five56By45 = 3,
	TwelveGauge = 4
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
/// Слот аксессуара на оружии.
/// </summary>
public enum WeaponAttachmentSlotType
{
	Optic = 0,
	Muzzle = 1,
	Laser = 2,
	Foregrip = 3,
	Stock = 4
}

/// <summary>
/// Категория модуля оружия.
/// </summary>
public enum WeaponAttachmentType
{
	Optic = 0,
	MuzzleDevice = 1,
	Laser = 2,
	Foregrip = 3,
	Stock = 4
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
/// Описание доступного слота модуля на оружии.
/// </summary>
[Serializable]
public struct WeaponAttachmentSlotDefinition
{
	public WeaponAttachmentSlotType SlotType;
	public bool IsRequired;
}
