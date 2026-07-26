using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct PredictionResult
	{
		public readonly bool IsSafe;
		public readonly float MinClearance;
		public readonly float TimeToCollision;

		public PredictionResult(bool _safe, float _clearance, float _ttc)
		{
			IsSafe = _safe;
			MinClearance = _clearance;
			TimeToCollision = _ttc;
		}

		public static PredictionResult Safe => new PredictionResult(true, float.MaxValue, float.MaxValue);
	}

	/// <summary>
	/// Shared trajectory prediction for ALL driver intents (Forward, Reverse, Parking, Column).
	/// Uses bicycle model to project future arc and checks for collisions.
	/// </summary>
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

			float minClearance = float.MaxValue;
			float ttc = float.MaxValue;

			for (int i = 0; i < _arc.Count; i++)
			{
				float t = i / (float)(_arc.Count - 1) * c_MaxTime;
				if (Physics.CheckSphere(_arc[i], c_SafeRadius, m_ObstacleMask, QueryTriggerInteraction.Ignore))
				{
					if (t < ttc) ttc = t;
				}
			}

			if (ttc < c_MaxTime)
			{
				bool safe = ttc > 0.5f;
				return new PredictionResult(safe, 0f, ttc);
			}

			return PredictionResult.Safe;
		}
	}
}
