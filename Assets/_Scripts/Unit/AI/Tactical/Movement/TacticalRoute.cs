using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14 route. Destination is the goal. Intermediates are how. Not a NavMesh path.
/// </summary>
public sealed class TacticalRoute
{
	#region Private Fields
	private readonly List<TacticalRouteWaypoint> m_Intermediates = new List<TacticalRouteWaypoint>(8);
	private int m_CurrentHopIndex;
	#endregion

	#region Public Properties
	public Vector3 Origin { get; private set; }
	public Vector3 Destination { get; private set; }
	public bool HasDestination { get; private set; }
	public TacticalMovementMode Mode { get; private set; }
	public TacticalRouteKind Kind { get; private set; }
	public IReadOnlyList<TacticalRouteWaypoint> Intermediates => m_Intermediates;
	public int IntermediateCount => m_Intermediates.Count;
	public int CurrentHopIndex => m_CurrentHopIndex;
	public bool IsDirect => Kind == TacticalRouteKind.Direct || m_Intermediates.Count == 0;
	public bool IsOnFinalHop => m_CurrentHopIndex >= m_Intermediates.Count;
	public int HopCount => m_Intermediates.Count + 1;

	public Vector3 CurrentHop
	{
		get
		{
			if (m_CurrentHopIndex < m_Intermediates.Count)
				return m_Intermediates[m_CurrentHopIndex].Position;
			return Destination;
		}
	}

	public TacticalRouteWaypoint CurrentWaypoint
	{
		get
		{
			if (m_CurrentHopIndex < m_Intermediates.Count)
				return m_Intermediates[m_CurrentHopIndex];
			return TacticalRouteWaypoint.At(Destination, TacticalWaypointKind.Destination);
		}
	}
	#endregion

	#region Public Methods
	public void Clear()
	{
		Origin = default;
		Destination = default;
		HasDestination = false;
		Mode = TacticalMovementMode.Normal;
		Kind = TacticalRouteKind.None;
		m_Intermediates.Clear();
		m_CurrentHopIndex = 0;
	}

	public void SetDirect(Vector3 _origin, Vector3 _destination, TacticalMovementMode _mode)
	{
		Origin = _origin;
		Destination = _destination;
		HasDestination = true;
		Mode = _mode;
		Kind = TacticalRouteKind.Direct;
		m_Intermediates.Clear();
		m_CurrentHopIndex = 0;
	}

	public void SetWaypoints(
		Vector3 _origin,
		Vector3 _destination,
		TacticalMovementMode _mode,
		IReadOnlyList<TacticalRouteWaypoint> _intermediates)
	{
		Origin = _origin;
		Destination = _destination;
		HasDestination = true;
		Mode = _mode;
		m_Intermediates.Clear();
		m_CurrentHopIndex = 0;
		if (_intermediates != null)
		{
			for (int i = 0; i < _intermediates.Count; i++)
				m_Intermediates.Add(_intermediates[i]);
		}

		Kind = m_Intermediates.Count > 0 ? TacticalRouteKind.Waypoint : TacticalRouteKind.Direct;
	}

	public bool TryAdvanceHop()
	{
		if (!HasDestination || IsOnFinalHop)
			return false;
		m_CurrentHopIndex++;
		return true;
	}

	public TacticalMovementDecision ToDecision(bool _fromCache)
	{
		return new TacticalMovementDecision
		{
			HasRoute = HasDestination,
			Kind = Kind,
			Mode = Mode,
			Origin = Origin,
			Destination = Destination,
			CurrentHop = CurrentHop,
			IntermediateCount = IntermediateCount,
			FromCache = _fromCache,
			Route = this,
			CurrentHopIndex = m_CurrentHopIndex
		};
	}
	#endregion
}
