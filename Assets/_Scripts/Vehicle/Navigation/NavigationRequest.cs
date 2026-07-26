using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// High-level order given to a vehicle. Knows nothing about physics or steering.
	/// </summary>
	public readonly struct NavigationRequest
	{
		public readonly Vector3 Destination;
		public readonly float? HeadingYaw;
		public readonly VehicleSpeedMode SpeedMode;

		public bool HasHeading => HeadingYaw.HasValue;

		public static NavigationRequest FromPosition(Vector3 _destination, VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, null, _speedMode);
		}

		public static NavigationRequest FromPositionAndHeading(
			Vector3 _destination,
			float _headingYaw,
			VehicleSpeedMode _speedMode)
		{
			return new NavigationRequest(_destination, _headingYaw, _speedMode);
		}

		private NavigationRequest(Vector3 _destination, float? _headingYaw, VehicleSpeedMode _speedMode)
		{
			Destination = _destination;
			HeadingYaw = _headingYaw;
			SpeedMode = _speedMode;
		}
	}
}
