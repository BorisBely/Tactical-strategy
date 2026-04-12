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
	Busy = 7
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
