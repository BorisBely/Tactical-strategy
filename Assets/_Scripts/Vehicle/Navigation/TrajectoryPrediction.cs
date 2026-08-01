using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct PredictionResult
	{
		public readonly bool IsSafe;
		public readonly float MinClearance;
		public readonly float TimeToCollision;
		public readonly float RiskScore;
		public readonly Vector3 CollisionPoint;
		public readonly int CollisionStepIndex;

		public PredictionResult(
			bool _safe,
			float _clearance,
			float _ttc,
			float _riskScore,
			Vector3 _collisionPoint,
			int _collisionStep)
		{
			IsSafe = _safe;
			MinClearance = _clearance;
			TimeToCollision = _ttc;
			RiskScore = _riskScore;
			CollisionPoint = _collisionPoint;
			CollisionStepIndex = _collisionStep;
		}

		public static PredictionResult Safe => new PredictionResult(
			true, float.MaxValue, float.MaxValue, 0f, Vector3.zero, -1);
	}

	public sealed class TrajectoryPrediction
	{
		private readonly LayerMask m_ObstacleMask;
		private const int c_Steps = 8;
		private const float c_MaxTime = 1.5f;
		private const float c_SafeRadius = 1.2f;

		public TrajectoryPrediction(LayerMask _obstacleMask)
		{
			m_ObstacleMask = _obstacleMask;
		}

		public List<Vector3> PredictArc(DriverContext _ctx, float _steerAngleDeg, float _speedKmh, float _duration)
		{
			var arc = new List<Vector3>();
			float dt = _duration / c_Steps;
			float wheelBase = _ctx.WheelBase;
			float yawRad = _ctx.Yaw * Mathf.Deg2Rad;
			Vector3 pos = _ctx.RearAxlePosition;

			for (int i = 0; i <= c_Steps; i++)
			{
				arc.Add(pos);
				float speedMs = Mathf.Max(0.1f, _speedKmh / 3.6f);
				float steerRad = Mathf.Clamp(_steerAngleDeg * Mathf.Deg2Rad, -0.7f, 0.7f);
				float omega = (speedMs / wheelBase) * Mathf.Tan(steerRad);
				yawRad += omega * dt;
				Vector3 fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
				pos += fwd * speedMs * dt;
			}
			return arc;
		}

		public PredictionResult Evaluate(DriverContext _ctx, List<Vector3> _arc)
		{
			if (_arc == null || _arc.Count == 0)
				return PredictionResult.Safe;

			float ttc = float.MaxValue;
			Vector3 collisionPoint = Vector3.zero;
			int collisionStep = -1;

			for (int i = 0; i < _arc.Count; i++)
			{
				float t = i / (float)(_arc.Count - 1) * c_MaxTime;
				if (Physics.CheckSphere(_arc[i], c_SafeRadius, m_ObstacleMask, QueryTriggerInteraction.Ignore))
				{
					if (t < ttc)
					{
						ttc = t;
						collisionPoint = _arc[i];
						collisionStep = i;
					}
				}
			}

			if (ttc < c_MaxTime)
			{
				bool safe = ttc > 0.5f;
				float risk = 1f - Mathf.Clamp01(ttc / 0.5f);
				return new PredictionResult(safe, 0f, ttc, risk, collisionPoint, collisionStep);
			}

			return PredictionResult.Safe;
		}

		public PredictionResult PredictForManeuver(
			Maneuver _maneuver,
			DriverContext _ctx,
			VehicleParameters _params)
		{
			if (_maneuver == null)
				return PredictionResult.Safe;

			switch (_maneuver.Type)
			{
				case VehicleManeuverType.Forward:
				case VehicleManeuverType.Parking:
				case VehicleManeuverType.ApproachWithHeading:
				case VehicleManeuverType.PostTurnAlignment:
					return PredictForward(_ctx, _params);

				case VehicleManeuverType.Reverse:
					return PredictReverse(_ctx, _params);

				case VehicleManeuverType.TurnAround:
				{
					var turn = _maneuver as TurnAroundManeuver;
					float sign = turn != null ? turn.TurnSign : 1f;
					return PredictTurnAround(_ctx, _params, sign);
				}

				default:
					return PredictionResult.Safe;
			}
		}

		public PredictionResult PredictForward(DriverContext _ctx, VehicleParameters _params)
		{
			float speed = Mathf.Max(1f, Mathf.Min(_ctx.SpeedKmh, _params.MaxForwardSpeedKmh * 0.5f));
			var arc = PredictArc(_ctx, 0f, speed, c_MaxTime);
			return Evaluate(_ctx, arc);
		}

		public PredictionResult PredictReverse(DriverContext _ctx, VehicleParameters _params)
		{
			float speed = Mathf.Max(1f, Mathf.Min(_ctx.SpeedKmh, _params.MaxReverseSpeedKmh * 0.5f));

			var revCtx = new DriverContext();
			revCtx.UpdateFrom(new FeedbackState(), _params, default, default);
			revCtx.Position = _ctx.Position;
			revCtx.Forward = -_ctx.Forward;
			revCtx.Right = -_ctx.Right;
			revCtx.Yaw = _ctx.Yaw + 180f;
			if (revCtx.Yaw > 180f) revCtx.Yaw -= 360f;
			revCtx.WheelBase = _ctx.WheelBase;

			var arc = PredictArc(revCtx, 0f, speed, c_MaxTime);
			return Evaluate(revCtx, arc);
		}

		public PredictionResult PredictTurnAround(
			DriverContext _ctx,
			VehicleParameters _params,
			float _turnSign)
		{
			float turnRadius = _params.MinTurningRadius;
			float steerAngleDeg = Mathf.Atan(_params.WheelBase / turnRadius) * Mathf.Rad2Deg * _turnSign;
			float speed = Mathf.Min(_ctx.SpeedKmh, 6f);

			var arc = PredictArc(_ctx, steerAngleDeg, speed, c_MaxTime);
			return Evaluate(_ctx, arc);
		}
	}
}
