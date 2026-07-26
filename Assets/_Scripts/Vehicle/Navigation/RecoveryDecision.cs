using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class RecoveryDecision
	{
		public RecoveryAction Action { get; set; }
		public float SuggestedSteerSign { get; set; }
		public float SuggestedCruiseSpeedKmh { get; set; }
		public string Reason { get; set; }
	}

	public interface IRecoveryStrategy
	{
		int Priority { get; }
		RecoveryDecision Evaluate(FeedbackState state, VehicleLocalGeometry.Sample geometry, VehicleDriverMemory memory);
	}

	public sealed class UnstuckRockStrategy : IRecoveryStrategy
	{
		public int Priority => 10;
		public RecoveryDecision Evaluate(FeedbackState s, VehicleLocalGeometry.Sample g, VehicleDriverMemory m)
		{
			if (!s.IsStuck) return null;
			return new RecoveryDecision
			{
				Action = RecoveryAction.UnstuckRock,
				SuggestedSteerSign = m.NextUnstuckSteerSign(),
				Reason = "stuck — rocking"
			};
		}
	}

	public sealed class AbortIfTooManyAttemptsStrategy : IRecoveryStrategy
	{
		public int Priority => 1;
		public RecoveryDecision Evaluate(FeedbackState s, VehicleLocalGeometry.Sample g, VehicleDriverMemory m)
		{
			if (m.UnstuckAttempts >= 6)
				return new RecoveryDecision { Action = RecoveryAction.AbortAndStop,
					Reason = $"unstuck {m.UnstuckAttempts} attempts — aborting" };
			return null;
		}
	}

	public sealed class RebuildPathAfterAttemptsStrategy : IRecoveryStrategy
	{
		public int Priority => 2;
		public RecoveryDecision Evaluate(FeedbackState s, VehicleLocalGeometry.Sample g, VehicleDriverMemory m)
		{
			if (m.UnstuckAttempts >= 4)
				return new RecoveryDecision { Action = RecoveryAction.RebuildPath,
					Reason = $"unstuck {m.UnstuckAttempts} attempts — replanning" };
			return null;
		}
	}

	public sealed class ReverseOutStrategy : IRecoveryStrategy
	{
		public int Priority => 3;
		public RecoveryDecision Evaluate(FeedbackState s, VehicleLocalGeometry.Sample g, VehicleDriverMemory m)
		{
			bool frontBlocked = g.FrontClearance < 2f
				&& g.FrontDiagonalLeftClearance < 1.5f
				&& g.FrontDiagonalRightClearance < 1.5f;
			bool rearClear = g.RearClearance > 3f && !g.HasDropBehind;
			if (frontBlocked && rearClear && m.UnstuckAttempts >= 1)
				return new RecoveryDecision { Action = RecoveryAction.ReverseOut,
					SuggestedCruiseSpeedKmh = 5f, Reason = "front blocked, rear clear — backing out" };
			return null;
		}
	}

	public static class RecoveryStrategyRegistry
	{
		private static readonly List<IRecoveryStrategy> s_Strategies = new List<IRecoveryStrategy>
		{
			new AbortIfTooManyAttemptsStrategy(),
			new RebuildPathAfterAttemptsStrategy(),
			new ReverseOutStrategy(),
			new UnstuckRockStrategy()
		};

		public static RecoveryDecision Evaluate(FeedbackState state, VehicleLocalGeometry.Sample geometry, VehicleDriverMemory memory)
		{
			if (!state.IsStuck && !state.IsAirborne)
				return new RecoveryDecision { Action = RecoveryAction.None };

			foreach (var strategy in s_Strategies)
			{
				var decision = strategy.Evaluate(state, geometry, memory);
				if (decision != null && decision.Action != RecoveryAction.None)
					return decision;
			}

			return new RecoveryDecision { Action = RecoveryAction.None };
		}
	}
}
