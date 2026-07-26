using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Precision arrival planner: generates 5 candidate arrival plans,
	/// evaluates cost, returns the best. Only activates close to target.
	/// </summary>
	public sealed class ArrivalPlanner
	{
		private readonly ArrivalPlanningSettings m_Settings;
		private readonly List<IArrivalStrategy> m_Strategies;

		public ArrivalPlanner(float _turnRadius)
		{
			m_Settings = new ArrivalPlanningSettings(_turnRadius);
			m_Strategies = new List<IArrivalStrategy>
			{
				new DirectArrivalStrategy(),
				new ArcArrivalStrategy(),
				new ReverseArrivalStrategy(),
				new RepositionArrivalStrategy(),
				new TurnAroundArrivalStrategy()
			};
		}

		/// <summary>
		/// Returns replacement maneuvers for the final approach, or null if too far.
		/// </summary>
		public List<Maneuver> PlanArrival(
			Vector3 _position, float _yaw,
			Vector3 _target, float? _targetHeading)
		{
			float dist = FlatDistance(_position, _target);
			if (dist > m_Settings.PlanningDistance || dist < 0.1f)
				return null;

			var analysis = ArrivalAnalysis.Compute(_position, _yaw, m_Settings.TurnRadius, _target, _targetHeading);

			ArrivalPlan best = null;
			float bestCost = float.MaxValue;

			foreach (var strategy in m_Strategies)
			{
				var plan = strategy.Generate(analysis, m_Settings, _position, _yaw, _target, _targetHeading);
				if (plan == null || !plan.Valid) continue;

				float cost = ArrivalCostEvaluator.Evaluate(plan, analysis, m_Settings);
				plan.Cost = cost;

				if (cost < bestCost)
				{
					bestCost = cost;
					best = plan;
				}
			}

			if (best != null && best.Valid)
			{
				Debug.Log($"[ArrivalPlanner] best={best.DebugName} cost={best.Cost:F1} dist={dist:F1} angle={analysis.HeadingError:F0}° lateral={analysis.LateralOffset:F1}");
				return new List<Maneuver>(best.Maneuvers);
			}

			return null;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
