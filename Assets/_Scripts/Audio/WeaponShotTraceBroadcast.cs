using System;

/// <summary>
/// Глобальная рассылка трасс hitscan-выстрелов для систем, не привязанных к конкретному юниту
/// (например, звук пролёта пули у камеры).
/// </summary>
public static class WeaponShotTraceBroadcast
{
	#region Events
	public static event Action<WeaponShotTraceInfo> TracePublished;
	#endregion

	#region Internal Methods
	internal static void Publish(WeaponShotTraceInfo _trace)
	{
		TracePublished?.Invoke(_trace);
	}
	#endregion
}
