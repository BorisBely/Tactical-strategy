using UnityEngine;

/// <summary>
/// Planar arrival for Attack / Defense / Retreat / Flee points. Search keeps its own 15 m area.
/// </summary>
public static class TacticalNavigationMath
{
	#region Constants
	public const float DefaultPointArrivalRadius = 1.5f;
	#endregion

	#region Public Methods
	public static bool IsInsideArrival(Vector3 _unit, Vector3 _destination, float _radius)
	{
		return UnitSearchNavigationMath.PlanarDistance(_unit, _destination) <= Mathf.Max(0f, _radius);
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
