using UnityEngine;

/// <summary>
/// Приказ пополнения всех магазинов на якоре маршрута. Срабатывает только при достижении якоря.
/// </summary>
[System.Serializable]
public struct MagazineRefillRouteOrder
{
	public int RouteSegmentIndex;
	public float RouteSegmentT;
	public Vector3 WaypointPosition;
}
