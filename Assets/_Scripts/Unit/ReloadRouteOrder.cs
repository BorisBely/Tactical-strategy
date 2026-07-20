using UnityEngine;

/// <summary>
/// Приказ перезарядки оружия на якоре маршрута. Срабатывает только при достижении якоря.
/// </summary>
[System.Serializable]
public struct ReloadRouteOrder
{
	public int RouteSegmentIndex;
	public float RouteSegmentT;
	public Vector3 WaypointPosition;
}
