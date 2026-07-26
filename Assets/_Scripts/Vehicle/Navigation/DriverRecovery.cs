using UnityEngine;

namespace VehicleNavigation
{
	public enum RecoveryReason
	{
		None,
		Stuck,
		SteeringSaturated,
		AngleTooLarge,
		PredictionUnsafe,
		PathBlocked
	}

	public enum RecoveryAction
	{
		None,
		StopAndReplan,
		TurnAround,
		ThreePointTurn,
		Abort,
		UnstuckRock,
		ReverseOut,
		CreepAside,
		RebuildPath,
		AbortAndStop
	}

	/// <summary>
	/// Shared recovery system for ALL driver intents.
	/// Detects when a maneuver has failed and decides what to do about it.
	/// </summary>
	public sealed class DriverRecovery
	{
		private const float c_SteerSaturatedTime = 2f;
		private const float c_AngleTooLarge = 80f;
		private const float c_StuckTime = 3f;

		private float m_SteerSaturatedTimer;
		private TrajectoryPrediction m_Prediction;

		public void Reset()
		{
			m_SteerSaturatedTimer = 0f;
		}

		public void BindPrediction(TrajectoryPrediction _pred)
		{
			m_Prediction = _pred;
		}

		public (RecoveryReason reason, RecoveryAction action) Evaluate(DriverContext _ctx, DriverIntent _intent, float _dt, ReversePath _path = null)
		{
			if (_ctx.IsStuck)
				return (RecoveryReason.Stuck, RecoveryAction.TurnAround);

			if (Mathf.Abs(_ctx.CurrentSteerAngle) > _ctx.MaxSteeringAngleDeg * 0.95f
			    && _ctx.SpeedKmh < 2f)
			{
				m_SteerSaturatedTimer += _dt;
				if (m_SteerSaturatedTimer > c_SteerSaturatedTime)
					return (RecoveryReason.SteeringSaturated, RecoveryAction.ThreePointTurn);
			}
			else
			{
				m_SteerSaturatedTimer = Mathf.Max(0f, m_SteerSaturatedTimer - _dt * 0.5f);
			}

			if (_path != null && _path.IsValid)
			{
				Vector3 segDir = _path.Points[_path.CurrentSegment].Tangent;
				Vector3 travelDir = _intent == DriverIntent.Reverse ? -_ctx.Forward : _ctx.Forward;
				float angle = Vector3.Angle(travelDir, segDir);
				if (angle > c_AngleTooLarge)
					return (RecoveryReason.AngleTooLarge, RecoveryAction.TurnAround);
			}

			if (_intent == DriverIntent.Reverse
			    && Physics.Raycast(_ctx.RearAxlePosition, -_ctx.Forward, 1.5f, ~0, QueryTriggerInteraction.Ignore))
				return (RecoveryReason.PathBlocked, RecoveryAction.Abort);

			return (RecoveryReason.None, RecoveryAction.None);
		}
	}
}

