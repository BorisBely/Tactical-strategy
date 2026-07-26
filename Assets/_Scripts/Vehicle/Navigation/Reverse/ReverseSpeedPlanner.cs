using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Computes desired speed for reverse driving based on curvature, distance, and speed mode.
	/// Separate from ReverseSteeringLimiter — that controls steering, this controls throttle.
	/// </summary>
	public sealed class ReverseSpeedPlanner
	{
		private readonly AnimationCurve m_SpeedCurve;

		public ReverseSpeedPlanner(AnimationCurve _speedCurve = null)
		{
			m_SpeedCurve = _speedCurve ?? new AnimationCurve(
				new Keyframe(0f, 1f), new Keyframe(0.15f, 0.55f), new Keyframe(0.3f, 0.18f));
		}

		public float Compute(
			float _speedFraction,
			float _maxReverseSpeedKmh,
			float _currentSpeedKmh,
			float _curvature,
			float _previewCurvature,
			float _remainingDistance)
		{
			float capKmh = Mathf.Max(1f, _maxReverseSpeedKmh) * Mathf.Clamp01(_speedFraction);
			float maxCurv = Mathf.Max(Mathf.Abs(_curvature), _previewCurvature);
			float curvatureFraction = m_SpeedCurve.Evaluate(maxCurv);
			float arrivalScale = Mathf.Clamp01(_remainingDistance / 10f);
			float targetKmh = capKmh * Mathf.Min(curvatureFraction, arrivalScale);

			float absSpeed = Mathf.Abs(_currentSpeedKmh);
			float launchRamp = Mathf.Clamp01(absSpeed / 3f + 0.15f);
			targetKmh = Mathf.Lerp(Mathf.Min(targetKmh, 6f), targetKmh, launchRamp);

			return targetKmh;
		}
	}
}
