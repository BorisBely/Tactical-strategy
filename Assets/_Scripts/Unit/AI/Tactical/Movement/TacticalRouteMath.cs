using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14 route factories. Destination is never replaced by a hop.
/// </summary>
public static class TacticalRouteMath
{
	#region Constants
	public const float OriginQuantizeMeters = 0.75f;
	#endregion

	#region Public Methods
	public static TacticalMovementGoal Goal(
		Vector3 _origin,
		Vector3 _destination,
		TacticalMovementMode _mode,
		float _now = 0f)
	{
		return new TacticalMovementGoal
		{
			Origin = _origin,
			Destination = _destination,
			HasDestination = true,
			Context = TacticalRouteContext.Single(_mode),
			Now = _now
		};
	}

	public static bool DestinationUnchanged(in TacticalMovementDecision _decision, Vector3 _goal)
	{
		if (!_decision.HasRoute)
			return false;
		return CoverSpatialMath.PlanarDistanceSqr(_decision.Destination, _goal) <= 0.0001f;
	}

	public static int Quantize(float _meters)
	{
		return Mathf.RoundToInt(_meters / OriginQuantizeMeters);
	}

	public static void CopyWaypoints(
		IReadOnlyList<TacticalRouteWaypoint> _source,
		List<TacticalRouteWaypoint> _destination)
	{
		_destination.Clear();
		if (_source == null)
			return;
		for (int i = 0; i < _source.Count; i++)
			_destination.Add(_source[i]);
	}
	#endregion
}
