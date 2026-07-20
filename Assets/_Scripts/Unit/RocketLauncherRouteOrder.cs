using UnityEngine;

/// <summary>
/// Приказ выстрела из рюкзачного гранатомёта (H) на якоре маршрута.
/// Срабатывает только при достижении якоря.
/// </summary>
[System.Serializable]
public struct RocketLauncherRouteOrder
{
	public int BagIndex;
	public ItemInstanceState LauncherInstance;
	public int RouteSegmentIndex;
	public float RouteSegmentT;
	public Vector3 WaypointPosition;
}
