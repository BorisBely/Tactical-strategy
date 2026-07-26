using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Reverse pure-pursuit controller.
	/// Works from the REAR AXLE (not vehicle center), computing curvature
	/// for backward driving using proper geometry — not just inverting a sign.
	/// </summary>
	public sealed class ReversePursuit
	{
		public struct Output
		{
			public float DesiredCurvature;
			public float DesiredSpeedKmh;
			public float DistanceToEnd;
			public Vector3 PursuitTarget;
			public float LookBehindDist;
		}

		private readonly ReverseSteeringLimiter m_SteeringLimiter;
		private readonly AnimationCurve m_SpeedCurve;
		private readonly ReverseSpeedPlanner m_SpeedPlanner;

		private float m_SmoothedCurvature;

		public ReversePursuit(AnimationCurve _speedCurve = null, AnimationCurve _steerLimit = null)
		{
			m_SpeedCurve = _speedCurve ?? new AnimationCurve(
				new Keyframe(0f, 1f), new Keyframe(0.15f, 0.55f), new Keyframe(0.3f, 0.18f));
			m_SteeringLimiter = new ReverseSteeringLimiter(_steerLimit);
			m_SpeedPlanner = new ReverseSpeedPlanner(m_SpeedCurve);
		}

		public Output Tick(DriverContext _ctx, ReversePath _path, float _speedFraction)
		{
			var result = new Output();
			if (!_path.IsValid || _path.IsComplete)
				return result;

			Vector3 rearAxle = _ctx.RearAxlePosition;

			float lookBehind = ComputeLookBehind(_ctx.SpeedKmh);
			result.LookBehindDist = lookBehind;

			Vector3 target = _path.GetLookBehind(rearAxle, lookBehind);
			result.PursuitTarget = target;

			Vector3 toTarget = target - rearAxle;
			toTarget.y = 0f;
			float dist = toTarget.magnitude;
			result.DistanceToEnd = _path.RemainingDistance;

			float curvature = 0f;
			if (dist > 0.05f && lookBehind > 0.05f)
			{
				Vector3 toTargetDir = toTarget / dist;

				Vector3 travelDir = -_ctx.Forward;
				float cross = Vector3.Cross(travelDir, toTargetDir).y;
				float crossTrack = cross * dist;
				curvature = 2f * crossTrack / (lookBehind * lookBehind);

				float closeness = 1f - Mathf.Clamp01(result.DistanceToEnd / 6f);
				float maxCurv = Mathf.Lerp(0.35f, 0.12f, closeness);
				curvature = Mathf.Clamp(curvature, -maxCurv, maxCurv);
			}

			float steerFraction = Mathf.Abs(curvature) / 0.35f;
			float limitedFraction = m_SteeringLimiter.GetAllowedFraction(_ctx.SpeedKmh);
			if (steerFraction > limitedFraction)
				curvature = Mathf.Sign(curvature) * limitedFraction * 0.35f;

			m_SmoothedCurvature = Mathf.Lerp(m_SmoothedCurvature, curvature, 0.3f);
			result.DesiredCurvature = m_SmoothedCurvature;

			float previewCurv = PreviewCurvature(_path, lookBehind);
			result.DesiredSpeedKmh = m_SpeedPlanner.Compute(
				_speedFraction,
				_ctx.MaxReverseSpeedKmh,
				_ctx.SpeedKmh,
				result.DesiredCurvature,
				previewCurv,
				result.DistanceToEnd);

			return result;
		}

		public void Reset()
		{
			m_SmoothedCurvature = 0f;
		}

		private static float ComputeLookBehind(float _speedKmh)
		{
			float s = Mathf.Max(0f, _speedKmh);
			return Mathf.Clamp(3f + s * 0.25f, 2f, 8f);
		}

		private static float PreviewCurvature(ReversePath _path, float _lookDist)
		{
			if (!_path.IsValid)
				return 0f;
			int end = Mathf.Min(_path.CurrentSegment + 4, _path.Points.Count - 1);
			float maxC = 0f;
			for (int i = _path.CurrentSegment; i < end && i < _path.Points.Count - 1; i++)
			{
				float c = _path.CurvatureAt(i);
				if (c > maxC) maxC = c;
			}
			return maxC;
		}
	}
}
