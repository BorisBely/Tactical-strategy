using System;
using System.Collections.Generic;

namespace VehicleNavigation
{
	/// <summary>
	/// Immutable result of the DrivingPlanner: an ordered sequence of maneuvers.
	/// </summary>
	public sealed class DrivingPlan
	{
		public static readonly DrivingPlan Empty = new DrivingPlan(Array.Empty<Maneuver>(), "empty");

		public IReadOnlyList<Maneuver> Maneuvers { get; }
		public string Reason { get; }
		public bool IsValid => Maneuvers != null && Maneuvers.Count > 0;

		public DrivingPlan(IReadOnlyList<Maneuver> _maneuvers, string _reason)
		{
			Maneuvers = _maneuvers ?? Array.Empty<Maneuver>();
			Reason = _reason ?? string.Empty;
		}
	}
}
