using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Precision arrival planner: uses priority groups by target side
	/// instead of flat cost comparison across all strategies.
	/// </summary>
	public sealed class ArrivalPlanner
	{
		private readonly ArrivalPlanningSettings m_Settings;
		private readonly List<IArrivalStrategy> m_Strategies;
		public static bool DebugLog = true;

		public ArrivalPlanner(float _turnRadius)
		{
			m_Settings = new ArrivalPlanningSettings(_turnRadius);
			m_Strategies = new List<IArrivalStrategy>
			{
				new DirectArrivalStrategy(),      // [0] Direct
				new ArcArrivalStrategy(),         // [1] Arc
				new ReverseArrivalStrategy(),     // [2] Reverse
				new RepositionArrivalStrategy(),  // [3] Reposition
				new TurnAroundArrivalStrategy()   // [4] TurnAround
			};
		}

		public List<Maneuver> PlanArrival(
			Vector3 _position, float _yaw,
			Vector3 _target, float? _targetHeading)
		{
			float dist = FlatDistance(_position, _target);
			if (dist > m_Settings.PlanningDistance || dist < 0.1f)
			{
				if (DebugLog && dist > 0.1f)
					Debug.Log($"[ArrivalPlanner] too far: {dist:F1}m > {m_Settings.PlanningDistance:F1}m planning distance");
				return null;
			}

			var analysis = ArrivalAnalysis.Compute(_position, _yaw, m_Settings.TurnRadius, _target, _targetHeading);

			if (DebugLog)
				Debug.Log($"[ArrivalPlanner] dist={dist:F1}m angle={analysis.HeadingError:F0} lateral={analysis.LateralOffset:F1}m side={analysis.Side} status={analysis.Status} turnR={m_Settings.TurnRadius:F1}");

			if (analysis.Status == ArrivalStatus.AtGoal)
			{
				if (DebugLog) Debug.Log($"[ArrivalPlanner] => AtGoal — terminal, no arrival plan needed");
				return null;
			}

		var group1 = GetPriorityGroup(analysis);
		var group2 = GetFallbackGroup(analysis);

			ArrivalPlan best = null;
			float bestCost = float.MaxValue;

			foreach (var strategy in group1)
			{
				var plan = strategy.Generate(analysis, m_Settings, _position, _yaw, _target, _targetHeading);
				if (plan == null) continue;

				if (plan.AtGoal)
				{
					if (DebugLog) Debug.Log($"[ArrivalPlanner]   {strategy.Name}: AtGoal — terminal");
					return null;
				}

				if (!plan.Valid)
				{
					if (DebugLog) Debug.Log($"[ArrivalPlanner]   {strategy.Name}: SKIP (not valid)");
					continue;
				}

				float cost = ArrivalCostEvaluator.Evaluate(plan, analysis, m_Settings);
				plan.Cost = cost;
				if (DebugLog) Debug.Log($"[ArrivalPlanner]   {strategy.Name}: cost={cost:F1} [GROUP1]");

				if (cost < bestCost)
				{
					bestCost = cost;
					best = plan;
				}
			}

			if (best != null && best.Valid)
			{
				if (DebugLog) Debug.Log($"[ArrivalPlanner] => CHOSE {best.DebugName} cost={best.Cost:F1} [GROUP1]");
				return new List<Maneuver>(best.Maneuvers);
			}

			if (DebugLog) Debug.Log($"[ArrivalPlanner]   --- GROUP1 empty, trying fallback ---");

			foreach (var strategy in group2)
			{
				var plan = strategy.Generate(analysis, m_Settings, _position, _yaw, _target, _targetHeading);
				if (plan == null || !plan.Valid)
				{
					if (DebugLog) Debug.Log($"[ArrivalPlanner]   {strategy.Name}: SKIP (not valid)");
					continue;
				}

				float cost = ArrivalCostEvaluator.Evaluate(plan, analysis, m_Settings);
				cost *= 1.4f;
				plan.Cost = cost;
				if (DebugLog) Debug.Log($"[ArrivalPlanner]   {strategy.Name}: cost={cost:F1} [GROUP2]");

				if (cost < bestCost)
				{
					bestCost = cost;
					best = plan;
				}
			}

			if (best != null && best.Valid)
			{
				if (DebugLog) Debug.Log($"[ArrivalPlanner] => CHOSE {best.DebugName} cost={best.Cost:F1} [GROUP2]");
				return new List<Maneuver>(best.Maneuvers);
			}

			if (DebugLog) Debug.LogWarning($"[ArrivalPlanner] => NO valid arrival strategy for side={analysis.Side}");
			return null;
		}

		private List<IArrivalStrategy> GetPriorityGroup(ArrivalAnalysis _a)
		{
			switch (_a.Side)
			{
				case TargetSide.Front: return new List<IArrivalStrategy> { m_Strategies[0] };
				case TargetSide.Rear: return new List<IArrivalStrategy> { m_Strategies[2] };
				case TargetSide.Left:
				case TargetSide.Right:
					// Close → Reposition, far → Arc. Same threshold as ArcMinDistance.
					if (_a.Distance < m_Settings.ArcMinDistance)
						return new List<IArrivalStrategy> { m_Strategies[3] }; // Reposition
					else
						return new List<IArrivalStrategy> { m_Strategies[1] }; // Arc
			}
			return new List<IArrivalStrategy>();
		}

		private List<IArrivalStrategy> GetFallbackGroup(ArrivalAnalysis _a)
		{
			switch (_a.Side)
			{
				case TargetSide.Front: return new List<IArrivalStrategy> { m_Strategies[1] };
				case TargetSide.Rear: return new List<IArrivalStrategy> { m_Strategies[4] };
				case TargetSide.Left:
				case TargetSide.Right:
					return new List<IArrivalStrategy> { m_Strategies[1], m_Strategies[3], m_Strategies[4] }; // Arc, Reposition, TurnAround
			}
			return new List<IArrivalStrategy>();
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
