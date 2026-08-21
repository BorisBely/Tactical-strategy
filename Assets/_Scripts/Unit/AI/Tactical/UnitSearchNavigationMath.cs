using UnityEngine;

/// <summary>
/// Planar Search-area check. Success of Search is Hostile+VisibleNow, not this radius.
/// </summary>
public static class UnitSearchNavigationMath
{
	#region Public Methods
	public static float PlanarDistance(Vector3 _a, Vector3 _b)
	{
		_a.y = 0f;
		_b.y = 0f;
		return Vector3.Distance(_a, _b);
	}

	public static bool IsInsideSearchArea(Vector3 _unit, Vector3 _searchPosition, float _radius)
	{
		return PlanarDistance(_unit, _searchPosition) <= Mathf.Max(0f, _radius);
	}
	#endregion
}
