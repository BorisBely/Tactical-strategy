using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Limits steering angle based on current speed during reverse driving.
	/// Prevents the car from "folding" at higher reverse speeds.
	/// </summary>
	public sealed class ReverseSteeringLimiter
	{
		private readonly AnimationCurve m_LimitCurve;

		public ReverseSteeringLimiter(AnimationCurve _curve = null)
		{
			m_LimitCurve = _curve ?? new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(5f, 0.9f),
				new Keyframe(10f, 0.7f),
				new Keyframe(15f, 0.5f),
				new Keyframe(20f, 0.3f));
		}

		public float GetAllowedFraction(float _speedKmh)
		{
			return Mathf.Clamp01(m_LimitCurve.Evaluate(Mathf.Abs(_speedKmh)));
		}

		public float ClampSteer(float _steer, float _speedKmh)
		{
			float limit = GetAllowedFraction(_speedKmh);
			return Mathf.Clamp(_steer, -limit, limit);
		}
	}
}
