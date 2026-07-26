using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// One atomic maneuver in a driving plan. Subclasses only describe *what* to do;
	/// waypoint generation is handled by ManeuverPlanner so the maneuver stays data-only.
	/// </summary>
	public abstract class Maneuver
	{
		public abstract VehicleManeuverType Type { get; }

		/// <summary>Local waypoints for Pursuit.</summary>
		public IReadOnlyList<Vector3> Waypoints => m_Waypoints;

		/// <summary>If true, the FSM may use reverse gear while executing this maneuver.</summary>
		public bool AllowReverse { get; protected set; }

		/// <summary>Speed multiplier relative to the current speed mode cap.</summary>
		public float SpeedScale { get; protected set; } = 1f;

		/// <summary>Optional fixed look-ahead distance for this maneuver.</summary>
		public float? LookAheadOverride { get; protected set; }

		/// <summary>True if this maneuver is only used for final alignment / parking.</summary>
		public bool IsArrivalManeuver { get; protected set; }

		private Vector3[] m_Waypoints = Array.Empty<Vector3>();
		private IReadOnlyList<Vector3> m_WaypointsView;

		protected Maneuver()
		{
			m_WaypointsView = m_Waypoints;
		}

		public void SetWaypoints(Vector3[] _waypoints)
		{
			m_Waypoints = _waypoints ?? Array.Empty<Vector3>();
			m_WaypointsView = m_Waypoints;
		}

		public virtual bool IsComplete(ManeuverContext _ctx)
		{
			if (m_Waypoints == null || m_Waypoints.Length == 0)
				return true;

			Vector3 last = m_Waypoints[m_Waypoints.Length - 1];
			return FlatDistance(_ctx.Position, last) <= _ctx.CompletionDistance;
		}

		protected static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
