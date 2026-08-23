/// <summary>Locomotion context for balance matrix (not full locomotion sim).</summary>
public enum WeaponBalanceMovement
{
	Idle = 0,
	Walk = 1,
	Sprint = 2
}

/// <summary>Stance for balance matrix. Prone skipped by config.</summary>
public enum WeaponBalanceStance
{
	Standing = 0,
	Crouch = 1
}

public enum WeaponBalanceBandLevel
{
	Unknown = 0,
	Low = 1,
	Medium = 2,
	High = 3
}

public enum WeaponBalanceVerdict
{
	Pass = 0,
	Warn = 1,
	Fail = 2
}
