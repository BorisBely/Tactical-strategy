using System.Linq;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// One segment of a maneuver plan: type + waypoints + metadata.
	/// Separates geometry (waypoints) from logic (Maneuver subclass).
	/// </summary>
	public sealed class ManeuverPlanSegment
	{
		public VehicleManeuverType ManeuverType { get; }
		public Vector3[] Waypoints { get; }
		public float DesiredHeadingYaw { get; }
		public bool HasDesiredHeading { get; }
		public bool AllowReverse { get; }
		public bool AllowRepathMidSegment { get; }
		public float SegmentLength { get; }
		public bool IsArrivalSegment { get; }
		public float SpeedScale { get; }
		public float? LookAheadOverride { get; }

		public ManeuverPlanSegment(
			VehicleManeuverType _type,
			Vector3[] _waypoints,
			float _desiredHeadingYaw = 0f,
			bool _hasDesiredHeading = false,
			bool _allowReverse = false,
			bool _allowRepath = false,
			float _speedScale = 1f,
			float? _lookAheadOverride = null,
			bool _isArrival = false)
		{
			ManeuverType = _type;
			Waypoints = _waypoints ?? new Vector3[0];
			DesiredHeadingYaw = _desiredHeadingYaw;
			HasDesiredHeading = _hasDesiredHeading;
			AllowReverse = _allowReverse;
			AllowRepathMidSegment = _allowRepath;
			SpeedScale = _speedScale;
			LookAheadOverride = _lookAheadOverride;
			IsArrivalSegment = _isArrival;
			SegmentLength = CalculateLength();
		}

		public static ManeuverPlanSegment FromManeuver(Maneuver _m)
		{
			if (_m == null)
				return null;

			float heading = 0f;
			bool hasHeading = false;
			if (_m is ParkingManeuver p) { heading = p.TargetHeadingYaw; hasHeading = true; }
			if (_m is ApproachWithHeadingManeuver a) { heading = a.TargetHeadingYaw; hasHeading = true; }

			return new ManeuverPlanSegment(
				_m.Type,
				_m.Waypoints?.ToArray() ?? new Vector3[0],
				heading,
				hasHeading,
				_m.AllowReverse,
				false,
				_m.SpeedScale,
				_m.LookAheadOverride,
				_m.IsArrivalManeuver);
		}

		private float CalculateLength()
		{
			if (Waypoints == null || Waypoints.Length < 2)
				return 0f;
			float len = 0f;
			for (int i = 0; i < Waypoints.Length - 1; i++)
			{
				Vector3 a = Waypoints[i]; a.y = 0f;
				Vector3 b = Waypoints[i + 1]; b.y = 0f;
				len += Vector3.Distance(a, b);
			}
			return len;
		}
	}
}
