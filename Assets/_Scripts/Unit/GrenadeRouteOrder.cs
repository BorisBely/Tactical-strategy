using UnityEngine;

/// <summary>
/// Приказ на бросок гранаты, привязанный к якорю на сегменте маршрута.
/// Срабатывает только при достижении якоря (reach-gate).
/// </summary>
[System.Serializable]
public struct GrenadeRouteOrder
{
	/// <summary>Тип гранаты для броска.</summary>
	public GrenadeType Type;

	/// <summary>Мировая позиция цели (куда приземлится граната).</summary>
	public Vector3 TargetPosition;

	/// <summary>Индекс сегмента маршрута (совместимо с прежним RouteWaypointIndex).</summary>
	public int RouteWaypointIndex;

	/// <summary>Нормализованная позиция на сегменте [0..1].</summary>
	public float RouteSegmentT;

	/// <summary>Мировая позиция якоря (маркер + reach).</summary>
	public Vector3 WaypointPosition;
}
