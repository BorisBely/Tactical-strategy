using System;
using System.Collections.Generic;

namespace VehicleNavigation
{
	public sealed class DrivingPlan
	{
		public static readonly DrivingPlan Empty = new DrivingPlan(
			Array.Empty<Maneuver>(), "empty");

		public IReadOnlyList<Maneuver> Maneuvers { get; }
		public string Reason { get; }
		public bool IsValid => Maneuvers != null && Maneuvers.Count > 0;
		public VehicleDrivingMode DrivingMode { get; }
		public float TotalCost { get; }

		public FeasibilityResult Feasibility { get; }
		public IReadOnlyList<ManeuverPlanSegment> Segments { get; private set; }

		public float EstimatedDistance { get; private set; }
		public float ReverseDistance { get; private set; }
		public int TurnCount { get; private set; }
		public float Risk { get; private set; }
		public ArrivalFallbackDecision FallbackDecision { get; set; }

		public DrivingPlan(
			IReadOnlyList<Maneuver> _maneuvers,
			string _reason,
			VehicleDrivingMode _mode = VehicleDrivingMode.Forward,
			float _totalCost = 0f,
			FeasibilityResult _feasibility = null)
		{
			Maneuvers = _maneuvers ?? Array.Empty<Maneuver>();
			Reason = _reason ?? string.Empty;
			DrivingMode = _mode;
			TotalCost = _totalCost;
			Feasibility = _feasibility ?? FeasibilityResult.Valid;
		}

		public void BuildSegments()
		{
			var list = new List<ManeuverPlanSegment>();
			float totalDist = 0f;
			float revDist = 0f;
			int turns = 0;

			foreach (var m in Maneuvers)
			{
				if (m == null) continue;
				var seg = ManeuverPlanSegment.FromManeuver(m);
				if (seg == null) continue;

				list.Add(seg);
				totalDist += seg.SegmentLength;
				if (seg.AllowReverse) revDist += seg.SegmentLength;
				if (seg.ManeuverType == VehicleManeuverType.TurnAround ||
				    seg.ManeuverType == VehicleManeuverType.ThreePointTurn)
					turns++;
			}

			Segments = list;
			EstimatedDistance = totalDist;
			ReverseDistance = revDist;
			TurnCount = turns;
			Risk = Feasibility?.RiskScore ?? 0f;
		}
	}
}
