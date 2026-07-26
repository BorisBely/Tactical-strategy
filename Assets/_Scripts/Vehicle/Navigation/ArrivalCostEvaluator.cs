using UnityEngine;

namespace VehicleNavigation
{
	public static class ArrivalCostEvaluator
	{
		private const float Weight_Distance = 1.2f;
		private const float Weight_Reverse = 8f;
		private const float Weight_Heading = 0.3f;
		private const float Weight_Maneuvers = 5f;
		private const float Weight_Precision = 2f;

		public static float Evaluate(ArrivalPlan _plan, ArrivalAnalysis _analysis, ArrivalPlanningSettings _settings)
		{
			if (_plan == null || !_plan.Valid)
				return float.MaxValue;

			float cost = 0f;

			cost += _analysis.Distance * Weight_Distance;
			cost += Mathf.Abs(_analysis.HeadingError) * Weight_Heading;
			cost += _plan.Maneuvers.Count * Weight_Maneuvers;

			bool hasReverse = false;
			foreach (var m in _plan.Maneuvers)
			{
				if (m == null) continue;
				if (m.Type == VehicleManeuverType.Reverse || m is ReverseIntentManeuver)
					hasReverse = true;
			}
			if (hasReverse)
				cost += Weight_Reverse;

			if (_analysis.Distance < _settings.PrecisionActivationDistance)
				cost += Weight_Precision;

			return cost;
		}
	}
}
