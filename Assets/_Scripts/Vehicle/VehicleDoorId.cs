/// <summary>Двери машины (передние/задние, левые/правые).</summary>
public enum VehicleDoorId : byte
{
	FrontLeft = 0,
	FrontRight = 1,
	RearLeft = 2,
	RearRight = 3
}

/// <summary>Ограничение стороны посадки из меню.</summary>
public enum VehicleBoardSide : byte
{
	Any = 0,
	Left = 1,
	Right = 2
}
