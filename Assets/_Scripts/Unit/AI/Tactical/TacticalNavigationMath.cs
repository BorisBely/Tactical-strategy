using UnityEngine;

/// <summary>
/// Planar arrival for Search / Retreat / Flee dest-only points.
/// Cover hops and Attack/Defense Walk use <see cref="TacticalArrivalMath.CoverHopArrivalRadiusMeters"/>
/// so Nav Reached can acquire at 0.60. Search 2.0 uses this radius per candidate; the 15 m value is SearchArea radius.
/// </summary>
public static class TacticalNavigationMath
{
	#region Constants
	public const float DefaultPointArrivalRadius = 1.5f;

	/// <summary>
	/// Nav remaining at which a still-far hop is treated as stuck. Not an acquire disk.
	/// </summary>
	public const float StuckRemainingMeters = 0.05f;
	#endregion

	#region Public Methods
	public static bool IsInsideArrival(Vector3 _unit, Vector3 _destination, float _radius)
	{
		return UnitSearchNavigationMath.PlanarDistance(_unit, _destination) <= Mathf.Max(0f, _radius);
	}

	/// <summary>
	/// Re-issue Walk only after the agent finished pathing and is still short of the hop.
	/// pathPending must not re-issue: IssueNavOrder resets the path, so a pending compute never finishes.
	/// </summary>
	public static bool ShouldReissueStuckWalk(
		bool _insideArrival,
		bool _pathPending,
		bool _hasPath,
		float _remainingDistance)
	{
		if (_insideArrival)
			return false;
		if (_pathPending)
			return false;
		if (!_hasPath)
			return true;
		if (float.IsPositiveInfinity(_remainingDistance))
			return false;
		return _remainingDistance <= StuckRemainingMeters;
	}

	public static bool TryGetPointDestination(
		UnitAIState _state,
		in UnitAIStateContext _context,
		out Vector3 _destination)
	{
		_destination = default;
		if (_state != UnitAIState.Attack &&
		    _state != UnitAIState.Defense &&
		    _state != UnitAIState.Retreat &&
		    _state != UnitAIState.Flee)
			return false;
		if (!_context.HasDestination)
			return false;

		_destination = _context.Destination;
		return true;
	}

	public static UnitNavigationReason ReasonFor(UnitAIState _state)
	{
		switch (_state)
		{
			case UnitAIState.Search:
				return UnitNavigationReason.Search;
			case UnitAIState.Attack:
				return UnitNavigationReason.Attack;
			case UnitAIState.Defense:
				return UnitNavigationReason.Defense;
			case UnitAIState.Retreat:
				return UnitNavigationReason.Retreat;
			case UnitAIState.Flee:
				return UnitNavigationReason.Flee;
			default:
				return UnitNavigationReason.None;
		}
	}
	#endregion
}
