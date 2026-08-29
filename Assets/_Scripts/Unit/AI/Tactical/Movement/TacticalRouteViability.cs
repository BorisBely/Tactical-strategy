using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.1 viability. Reject before score. Not a NavMesh rewrite.
/// </summary>
public static class TacticalRouteViability
{
	#region Public Methods
	public static bool IsFinitePoint(Vector3 _point)
	{
		return !float.IsNaN(_point.x) &&
		       !float.IsNaN(_point.y) &&
		       !float.IsNaN(_point.z) &&
		       !float.IsInfinity(_point.x) &&
		       !float.IsInfinity(_point.y) &&
		       !float.IsInfinity(_point.z);
	}

	public static TacticalRouteRejectReason Classify(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation,
		ITacticalRoutePathProbe _probe)
	{
		if (_candidate == null)
			return TacticalRouteRejectReason.InvalidDestination;
		if (!_situation.HasDestination || !IsFinitePoint(_situation.Destination))
			return TacticalRouteRejectReason.InvalidDestination;
		if (!IsFinitePoint(_candidate.Destination) || !IsFinitePoint(_candidate.Origin))
			return TacticalRouteRejectReason.InvalidDestination;
		if (_probe != null && !_probe.IsDestinationValid(_candidate.Destination))
			return TacticalRouteRejectReason.InvalidDestination;
		if (_candidate.Intermediates != null)
		{
			for (int i = 0; i < _candidate.Intermediates.Count; i++)
			{
				if (!IsFinitePoint(_candidate.Intermediates[i].Position))
					return TacticalRouteRejectReason.Blocked;
			}
		}

		if (_probe != null &&
		    !_probe.IsReachable(_candidate.Origin, _candidate.Destination, _candidate.Intermediates))
			return TacticalRouteRejectReason.Unreachable;
		return TacticalRouteRejectReason.None;
	}

	public static bool IsViable(
		TacticalRouteCandidate _candidate,
		in TacticalRouteSituation _situation,
		ITacticalRoutePathProbe _probe)
	{
		return Classify(_candidate, in _situation, _probe) == TacticalRouteRejectReason.None;
	}
	#endregion
}
