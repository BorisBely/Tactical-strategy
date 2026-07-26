using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Snapshot of vehicle/environment state shared with all controllers.
	/// Produced by FeedbackSystem each FixedUpdate.
	/// </summary>
	public readonly struct FeedbackState
	{
		public readonly Vector3 Position;
		public readonly Vector3 Forward;
		public readonly Vector3 Right;
		public readonly float Yaw;
		public readonly float SpeedKmh;
		public readonly float SpeedSignedKmh;
		public readonly float VelocitySqr;
		public readonly bool IsReversing;
		public readonly bool IsStopped;
		public readonly bool IsStuck;
		public readonly bool IsAirborne;
		public readonly bool IsUpright;
		public readonly VehicleLocalGeometry.Sample Geometry;
		public readonly VehicleDriverMemory Memory;

		public FeedbackState(
			Vector3 _position,
			Vector3 _forward,
			Vector3 _right,
			float _yaw,
			float _speedKmh,
			float _speedSignedKmh,
			float _velocitySqr,
			bool _isReversing,
			bool _isStopped,
			bool _isStuck,
			bool _isAirborne,
			bool _isUpright,
			VehicleLocalGeometry.Sample _geometry,
			VehicleDriverMemory _memory)
		{
			Position = _position;
			Forward = _forward;
			Right = _right;
			Yaw = _yaw;
			SpeedKmh = _speedKmh;
			SpeedSignedKmh = _speedSignedKmh;
			VelocitySqr = _velocitySqr;
			IsReversing = _isReversing;
			IsStopped = _isStopped;
			IsStuck = _isStuck;
			IsAirborne = _isAirborne;
			IsUpright = _isUpright;
			Geometry = _geometry;
			Memory = _memory;
		}
	}
}
