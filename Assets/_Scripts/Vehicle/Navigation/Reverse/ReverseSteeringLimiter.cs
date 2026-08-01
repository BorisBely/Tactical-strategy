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
			float before = _steer;
			float clamped = Mathf.Clamp(_steer, -limit, limit);
			if (Mathf.Abs(before) > 0.01f && Mathf.Abs(clamped) < Mathf.Abs(before) * 0.8f)
				Debug.Log($"[RevSteerLimit] limiting steer: {before:F3}→{clamped:F3} (limit={limit:F2} at speed={_speedKmh:F1}km/h)");
			return clamped;
		}
	}
}
