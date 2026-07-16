using UnityEngine;

/// <summary>
/// Приказ на бросок гранаты, привязанный к waypoint на маршруте.
/// Юнит выполняет бросок при прохождении этой точки, не останавливаясь.
/// </summary>
[System.Serializable]
public struct GrenadeRouteOrder
{
	/// <summary>Тип гранаты для броска.</summary>
	public GrenadeType Type;

	/// <summary>Мировая позиция цели (куда приземлится граната).</summary>
	public Vector3 TargetPosition;

	/// <summary>Индекс waypoint в очереди маршрута юнита, на котором срабатывает бросок.</summary>
	public int RouteWaypointIndex;

	/// <summary>Мировая позиция waypoint (для отображения маркера).</summary>
	public Vector3 WaypointPosition;
}
