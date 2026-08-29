/// <summary>
/// Tactical lean depth/side → existing UnitSpineLean.SetLeanLevel.
/// Small/Medium/Deep = Neutral+1/2/3. Not a second lean controller.
/// </summary>
public static class CoverLeanMapping
{
	#region Public Methods
	public static int ToSpineLevel(CoverLeanLevel _level)
	{
		switch (_level)
		{
			case CoverLeanLevel.Small:
				return 1;
			case CoverLeanLevel.Medium:
				return 2;
			case CoverLeanLevel.Deep:
				return 3;
			default:
				return 0;
		}
	}

	public static int ToSpineSide(CoverPeekDirection _direction)
	{
		switch (_direction)
		{
			case CoverPeekDirection.Left:
				return -1;
			case CoverPeekDirection.Right:
				return 1;
			default:
				return 0;
		}
	}

	public static CoverLeanLevel FromSpineLevel(int _level)
	{
		if (_level <= 0)
			return CoverLeanLevel.None;
		if (_level == 1)
			return CoverLeanLevel.Small;
		if (_level == 2)
			return CoverLeanLevel.Medium;
		return CoverLeanLevel.Deep;
	}

	public static CoverPeekDirection FromSpineSide(int _side)
	{
		if (_side < 0)
			return CoverPeekDirection.Left;
		if (_side > 0)
			return CoverPeekDirection.Right;
		return CoverPeekDirection.None;
	}
	#endregion
}
