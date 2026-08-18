using UnityEngine;

namespace VehicleNavigation
{
	public sealed class GoalLimiter : ISpeedLimiter
	{
		private readonly float m_CreepSpeedKmh;
		private readonly float m_CreepDistance;

		public GoalLimiter(float _creepSpeedKmh = 4.5f, float _creepDistance = 3f)
		{
			m_CreepSpeedKmh = _creepSpeedKmh;
			m_CreepDistance = Mathf.Max(0.5f, _creepDistance);
		}

		public SpeedLimitResult GetLimit(NavigationContext _ctx)
		{
			float distanceToEnd = _ctx.RemainingDistance;
			if (distanceToEnd <= 0f)
				return new SpeedLimitResult(0f, StopReason.Goal, 60, true);

			float currentSpeedMs = _ctx.State.SpeedKmh / 3.6f;
			float maxDecel = _ctx.Params.HardBrakeDecelMs2;

			float brakingDistance = (currentSpeedMs * currentSpeedMs) / (2f * maxDecel);

			// Approach/Parking: use normal braking profile until creep zone; only then crawl.
			if (distanceToEnd > brakingDistance + m_CreepDistance)
				return SpeedLimitResult.Unlimited;

			if (distanceToEnd > m_CreepDistance)
			{
				float remainingForDecel = distanceToEnd - m_CreepDistance;
				float safeSpeedMs = Mathf.Sqrt(2f * maxDecel * remainingForDecel);
				float safeSpeedKmh = safeSpeedMs * 3.6f;
				return new SpeedLimitResult(safeSpeedKmh, StopReason.Goal, 60, false);
			}

			if (distanceToEnd > 0.05f)
			{
				float creepProgress = distanceToEnd / m_CreepDistance;
				float desiredKmh = m_CreepSpeedKmh * creepProgress;
				return new SpeedLimitResult(Mathf.Max(0.5f, desiredKmh), StopReason.Goal, 60, false);
			}

			return new SpeedLimitResult(0f, StopReason.Goal, 60, true);
		}
	}
}
