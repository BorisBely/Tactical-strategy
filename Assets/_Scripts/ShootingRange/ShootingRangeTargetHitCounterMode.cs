/// <summary>
/// Режим счётчика попаданий мишени полигона.
/// </summary>
public enum ShootingRangeTargetHitCounterMode
{
	None = 0,
	One = 1,
	Two = 2,
	Three = 3,
	Five = 5,
	Ten = 10
}

public static class ShootingRangeTargetHitCounterModeUtility
{
	private static readonly ShootingRangeTargetHitCounterMode[] s_CycleOrder =
	{
		ShootingRangeTargetHitCounterMode.None,
		ShootingRangeTargetHitCounterMode.One,
		ShootingRangeTargetHitCounterMode.Two,
		ShootingRangeTargetHitCounterMode.Three,
		ShootingRangeTargetHitCounterMode.Five,
		ShootingRangeTargetHitCounterMode.Ten
	};

	public static bool HasCounter(ShootingRangeTargetHitCounterMode _mode) => _mode != ShootingRangeTargetHitCounterMode.None;

	public static int GetRequiredHits(ShootingRangeTargetHitCounterMode _mode) => (int)_mode;

	public static ShootingRangeTargetHitCounterMode GetNextMode(ShootingRangeTargetHitCounterMode _currentMode)
	{
		for (int i = 0; i < s_CycleOrder.Length; i++)
		{
			if (s_CycleOrder[i] != _currentMode)
				continue;

			int nextIndex = (i + 1) % s_CycleOrder.Length;
			return s_CycleOrder[nextIndex];
		}

		return ShootingRangeTargetHitCounterMode.None;
	}

	public static string GetDisplayLabel(ShootingRangeTargetHitCounterMode _mode)
	{
		return _mode switch
		{
			ShootingRangeTargetHitCounterMode.None => "Off",
			ShootingRangeTargetHitCounterMode.One => "1",
			ShootingRangeTargetHitCounterMode.Two => "2",
			ShootingRangeTargetHitCounterMode.Three => "3",
			ShootingRangeTargetHitCounterMode.Five => "5",
			ShootingRangeTargetHitCounterMode.Ten => "10",
			_ => "Off"
		};
	}
}
