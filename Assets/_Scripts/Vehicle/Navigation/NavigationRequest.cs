using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct NavigationRequest
	{
		public readonly Vector3 Destination;
		public readonly float? HeadingYaw;
		public readonly GoalHeadingSource HeadingSource;
		public readonly VehicleSpeedMode SpeedMode;

		public readonly ArrivalFacingMode FacingMode;
		public readonly bool AllowReverse;
		public readonly bool AllowTurnAround;
		public readonly bool AllowRepath;
		public readonly float MinArrivalDistance;
		public readonly float MinArrivalHeading;

		public bool HasHeading => HeadingYaw.HasValue;
		public bool HasAdvisoryHeading =>
			HeadingSource == GoalHeadingSource.SoftPathTangent && HeadingYaw.HasValue;
		public bool RequiresPosePlanning => HeadingSource == GoalHeadingSource.RequiredExplicit;
		public bool RequiresExplicitHeadingArrival => RequiresPosePlanning;

		public static NavigationRequest FromPosition(Vector3 _destination, VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, null, GoalHeadingSource.None, _speedMode,
				ArrivalFacingMode.None, true, true, true, 0.45f, 5f);
		}

		public static NavigationRequest FromPositionAndHeading(
			Vector3 _destination,
			float _headingYaw,
			VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, _headingYaw, GoalHeadingSource.RequiredExplicit, _speedMode,
				ArrivalFacingMode.FaceHeading, true, true, true, 0.45f, 8f);
		}

		public static NavigationRequest FromOrder(VehicleMoveOrder _order)
		{
			float? heading = _order.HasDesiredHeading ? _order.DesiredHeadingYaw : (float?)null;
			var source = _order.HasDesiredHeading
				? GoalHeadingSource.RequiredExplicit
				: GoalHeadingSource.None;
			return new NavigationRequest(
				_order.Destination,
				heading,
				source,
				_order.SpeedMode,
				_order.FacingMode,
				_order.AllowReverse,
				_order.AllowTurnAround,
				true,
				0.45f,
				5f);
		}

		public NavigationRequest WithPathTangentHeading(float _headingYaw)
		{
			if (HeadingSource == GoalHeadingSource.RequiredExplicit)
				return this;

			return new NavigationRequest(
				Destination,
				_headingYaw,
				GoalHeadingSource.SoftPathTangent,
				SpeedMode,
				FacingMode,
				AllowReverse,
				AllowTurnAround,
				AllowRepath,
				MinArrivalDistance,
				MinArrivalHeading);
		}

		private NavigationRequest(
			Vector3 _destination,
			float? _headingYaw,
			GoalHeadingSource _headingSource,
			VehicleSpeedMode _speedMode,
			ArrivalFacingMode _facingMode,
			bool _allowReverse,
			bool _allowTurnAround,
			bool _allowRepath,
			float _minArrivalDistance,
			float _minArrivalHeading)
		{
			Destination = _destination;
			HeadingYaw = _headingYaw;
			HeadingSource = _headingSource;
			SpeedMode = _speedMode;
			FacingMode = _facingMode;
			AllowReverse = _allowReverse;
			AllowTurnAround = _allowTurnAround;
			AllowRepath = _allowRepath;
			MinArrivalDistance = _minArrivalDistance;
			MinArrivalHeading = _minArrivalHeading;
		}
	}
}
