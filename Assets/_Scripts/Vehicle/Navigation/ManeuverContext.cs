using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>Read-only context used to evaluate maneuver completion.</summary>
	public readonly struct ManeuverContext
	{
		public readonly Vector3 Position;
		public readonly Vector3 Forward;
		public readonly float SpeedKmh;
		public readonly float CompletionDistance;
		public readonly bool IsReversing;

		public ManeuverContext(
			Vector3 _position,
			Vector3 _forward,
			float _speedKmh,
			float _completionDistance,
			bool _isReversing)
		{
			Position = _position;
			Forward = _forward;
			SpeedKmh = _speedKmh;
			CompletionDistance = _completionDistance;
			IsReversing = _isReversing;
		}
	}
}
