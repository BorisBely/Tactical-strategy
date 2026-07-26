using System.Collections.Generic;

namespace VehicleNavigation
{
	public sealed class SpeedPlanner
	{
		private readonly List<ISpeedLimiter> m_Limiters = new List<ISpeedLimiter>();

		public SpeedLimitResult ActiveLimit { get; private set; }

		public void Clear()
		{
			m_Limiters.Clear();
			ActiveLimit = SpeedLimitResult.Unlimited;
		}

		public void Register(ISpeedLimiter _limiter)
		{
			m_Limiters.Add(_limiter);
		}

		public float ComputeTargetSpeed(NavigationContext _ctx)
		{
			float target = float.MaxValue;
			SpeedLimitResult active = SpeedLimitResult.Unlimited;

			foreach (ISpeedLimiter limiter in m_Limiters)
			{
				SpeedLimitResult limit = limiter.GetLimit(_ctx);
				if (limit.SpeedKmh < target)
				{
					target = limit.SpeedKmh;
					active = limit;
				}
			}

			// Account for feasibility-based speed recommendation
			if (_ctx.Plan?.Feasibility != null &&
			    _ctx.Plan.Feasibility.RecommendedMaxSpeedKmh > 0f &&
			    _ctx.Plan.Feasibility.RecommendedMaxSpeedKmh < target)
			{
				target = _ctx.Plan.Feasibility.RecommendedMaxSpeedKmh;
			}

			ActiveLimit = active;
			return target;
		}
	}
}
