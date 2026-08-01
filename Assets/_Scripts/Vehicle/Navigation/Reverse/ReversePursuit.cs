using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ReversePursuit
	{
		public struct Output
		{
			public float DesiredCurvature;
			public float DistanceToEnd;
			public Vector3 PursuitTarget;
			public float LookBehindDist;
		}

		private static readonly AnimationCurve s_LookByDist = new AnimationCurve(
			new Keyframe(0f, 0.35f), new Keyframe(2f, 0.6f),
			new Keyframe(4f, 1.2f),  new Keyframe(10f, 3f));

		private static readonly AnimationCurve s_LookBySpeed = new AnimationCurve(
			new Keyframe(0f, 1f), new Keyframe(5f, 2f),
			new Keyframe(15f, 4f), new Keyframe(30f, 6f));

		private readonly ReverseSteeringLimiter m_SteeringLimiter;

		private float m_SmoothedCurvature;

		public ReversePursuit(AnimationCurve _steerLimit = null)
		{
			m_SteeringLimiter = new ReverseSteeringLimiter(_steerLimit);
		}

		public Output Tick(DriverContext _ctx, ReversePath _path, float _speedFraction)
		{
			var result = new Output();
			if (!_path.IsValid || _path.IsComplete)
				return result;

			Vector3 rearAxle = _ctx.GetControlPoint(DriverIntent.Reverse);

			float lookBehind = ComputeLookBehind(_ctx.SpeedKmh, _path.RemainingDistance);
			result.LookBehindDist = lookBehind;

			Vector3 target = _path.GetLookBehind(rearAxle, lookBehind);
			result.PursuitTarget = target;

			Vector3 toTarget = target - rearAxle;
			toTarget.y = 0f;
			float dist = toTarget.magnitude;
			result.DistanceToEnd = _path.RemainingDistance;

			float curvature = 0f;
			float crossTrack = 0f;
			float rawCurvature = 0f;
			if (dist > 0.05f && lookBehind > 0.05f)
			{
				Vector3 toTargetDir = toTarget / dist;
				Vector3 travelDir = -_ctx.Forward;
				float cross = Vector3.Cross(travelDir, toTargetDir).y;
				crossTrack = cross * dist;
				rawCurvature = 2f * crossTrack / (lookBehind * lookBehind);
				curvature = rawCurvature;

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

			if (Time.frameCount % 30 == 0)
			{
				int closestSeg = _path.FindClosestSegment(rearAxle);
				float desiredCurv = result.DesiredCurvature;
				float steerTarget = Mathf.Atan(_ctx.WheelBase * desiredCurv) * Mathf.Rad2Deg;
				float steerLimited = m_SteeringLimiter.ClampSteer(steerTarget / _ctx.MaxSteeringAngleDeg, _ctx.SpeedKmh) * _ctx.MaxSteeringAngleDeg;
				float expectedR = Mathf.Abs(desiredCurv) > 0.0001f ? 1f / Mathf.Abs(desiredCurv) : 999f;
				float actualR = _ctx.SpeedKmh > 0.3f ? (_ctx.SpeedKmh / 3.6f) / (Mathf.Abs(_ctx.YawRate) * Mathf.Deg2Rad + 0.0001f) : 999f;

				Debug.Log($"[RevPursuit] rearAxle=({rearAxle.x:F2},{rearAxle.z:F2}) target=({target.x:F2},{target.z:F2}) " +
					$"dist={dist:F2}m lookBehind={lookBehind:F2}m crossTrack={crossTrack:F3} " +
					$"rawCurv={rawCurvature:F4} curv={curvature:F4} smoothed={m_SmoothedCurvature:F4} " +
					$"steerFrac={steerFraction:F2}/{limitedFraction:F2} closestSeg={closestSeg} remaining={result.DistanceToEnd:F1}m " +
					$"speed={_ctx.SpeedKmh:F1}km/h eqR={expectedR:F1}m actR={actualR:F1}m yawRate={_ctx.YawRate:F1}°/s");
			}

			return result;
		}

		public void Reset()
		{
			m_SmoothedCurvature = 0f;
		}

		private static float ComputeLookBehind(float _speedKmh, float _remainingDist)
		{
			return Mathf.Max(
				s_LookByDist.Evaluate(_remainingDist),
				s_LookBySpeed.Evaluate(Mathf.Abs(_speedKmh)));
		}
	}
}
