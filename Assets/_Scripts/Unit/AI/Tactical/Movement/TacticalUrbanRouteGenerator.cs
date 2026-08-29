using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.3 optional wall-corridor hops. Direct stays first. Not cover-to-cover. Not CQB.
/// </summary>
public static class TacticalUrbanRouteGenerator
{
	#region Constants
	public const int LeftCorridorId = 20;
	public const int RightCorridorId = 21;
	#endregion

	#region Public Methods
	public static int AppendCorridorCandidates(
		in TacticalRouteSituation _situation,
		IReadOnlyList<TacticalWallAnchor> _anchors,
		List<TacticalRouteCandidate> _destination,
		int _max,
		float _diversityMeters)
	{
		if (_destination == null || _anchors == null || _anchors.Count == 0)
			return 0;
		if (_situation.Mode != TacticalMovementMode.Tactical)
			return 0;
		int cap = Mathf.Max(1, _max);
		float diversity = Mathf.Max(0.25f, _diversityMeters);
		int added = 0;
		added += TryAddSide(
			in _situation, _anchors, 1f, LeftCorridorId, diversity, cap, _destination);
		added += TryAddSide(
			in _situation, _anchors, -1f, RightCorridorId, diversity, cap, _destination);
		return added;
	}
	#endregion

	#region Private Methods
	private static int TryAddSide(
		in TacticalRouteSituation _situation,
		IReadOnlyList<TacticalWallAnchor> _anchors,
		float _sideSign,
		int _id,
		float _diversityMeters,
		int _cap,
		List<TacticalRouteCandidate> _destination)
	{
		if (_destination.Count >= _cap)
			return 0;
		Vector3 forward = _situation.Destination - _situation.Origin;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.01f)
			return 0;
		forward.Normalize();
		Vector3 right = Vector3.Cross(Vector3.up, forward);
		if (right.sqrMagnitude < 0.01f)
			right = Vector3.right;
		right.Normalize();
		TacticalWallAnchor best = default;
		float bestDot = -1f;
		for (int i = 0; i < _anchors.Count; i++)
		{
			if (!TacticalUrbanWallMath.IsValid(_anchors[i]))
				continue;
			Vector3 normal = TacticalWallAnchor.Flatten(_anchors[i].Normal).normalized;
			float side = Vector3.Dot(normal, right) * _sideSign;
			if (side <= bestDot)
				continue;
			bestDot = side;
			best = _anchors[i];
		}

		if (bestDot < 0.15f)
			return 0;
		if (!TacticalUrbanWallMath.TryProjectCorridorHop(
			    in best, _situation.Origin, _situation.Destination, out Vector3 hop))
			return 0;
		var candidate = new TacticalRouteCandidate();
		candidate.SetCoverHops(
			_id,
			_situation.Origin,
			_situation.Destination,
			new[] { TacticalRouteWaypoint.At(hop, TacticalWaypointKind.Corridor) });
		for (int i = 0; i < _destination.Count; i++)
		{
			if (!TacticalRouteGenerator.IsDiverseFrom(candidate, _destination[i], _diversityMeters))
				return 0;
		}

		_destination.Add(candidate);
		return 1;
	}
	#endregion
}
