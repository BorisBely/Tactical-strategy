using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct NavigationRequest
	{
		public readonly Vector3 Destination;
		public readonly float? HeadingYaw;
		public readonly VehicleSpeedMode SpeedMode;

		public readonly ArrivalFacingMode FacingMode;
		public readonly bool AllowReverse;
		public readonly bool AllowTurnAround;
		public readonly bool AllowRepath;
		public readonly float MinArrivalDistance;
		public readonly float MinArrivalHeading;

		public bool HasHeading => HeadingYaw.HasValue;

		public static NavigationRequest FromPosition(Vector3 _destination, VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, null, _speedMode,
				ArrivalFacingMode.None, true, true, true, 0.6f, 8f);
		}

		public static NavigationRequest FromPositionAndHeading(
			Vector3 _destination,
			float _headingYaw,
			VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, _headingYaw, _speedMode,
				ArrivalFacingMode.FaceHeading, true, true, true, 0.6f, 8f);
		}

		public static NavigationRequest FromOrder(VehicleMoveOrder _order)
		{
			float? heading = _order.HasDesiredHeading ? _order.DesiredHeadingYaw : (float?)null;
			return new NavigationRequest(
				_order.Destination,
				heading,
				_order.SpeedMode,
				_order.FacingMode,
				_order.AllowReverse,
				_order.AllowTurnAround,
				true,
				0.6f,
				8f);
		}

		private NavigationRequest(
			Vector3 _destination,
			float? _headingYaw,
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
