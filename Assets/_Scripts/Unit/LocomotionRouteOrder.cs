using UnityEngine;

/// <summary>
/// Приказ смены темпа/стойки на якоре маршрута. Срабатывает только при достижении якоря.
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
