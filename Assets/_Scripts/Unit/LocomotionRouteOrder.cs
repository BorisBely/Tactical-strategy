using UnityEngine;

/// <summary>
/// Приказ смены темпа/стойки на якоре маршрута. Срабатывает при достижении/проходе якоря.
/// Бег из меню маршрута держится до следующего приказа; блокирующие приказы (граната, РПГ,
/// перезарядка, набивка, wait, facing, шаг/присед) снимают бег и оставляют шаг на остаток маршрута.
/// </summary>
[System.Serializable]
public struct LocomotionRouteOrder
{
	public UnitClickToMove.MoveTier MoveTier;
	public LocomotionStance Stance;
	public int RouteSegmentIndex;
	public float RouteSegmentT;
	public Vector3 WaypointPosition;
}
