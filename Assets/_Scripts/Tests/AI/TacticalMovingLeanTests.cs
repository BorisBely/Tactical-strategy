using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.8 Moving Lean. Existing executor only. Overlay does not Move. Not a new UnitAIState.
	/// </summary>
	public sealed class TacticalMovingLeanTests
	{
		#region Nested
		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public int SetLeanCount;
			public CoverLeanLevel LastLevel;
			public CoverPeekDirection LastDirection;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				SetLeanCount++;
				LastLevel = _level;
				LastDirection = _direction;
			}
		}
		#endregion

		#region A Opportunity
		[Test]
		public void A1_CornerMovementBenefit_Opportunity()
		{
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(Benefit(CoverPeekDirection.Left));
			Assert.AreEqual(TacticalMovingLeanAction.Lean, decision.Action);
			Assert.IsTrue(decision.Opportunity);
			Assert.AreEqual(TacticalMovingLeanReason.SectorGain, decision.Reason);
		}

		[Test]
		public void A2_NoBenefit_None()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.LeftSmallSufficient = false;
			sit.LeftMediumSufficient = false;
			sit.LeftDeepSufficient = false;
			sit.LeftVisibilityGain = 0f;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(TacticalMovingLeanAction.None, decision.Action);
			Assert.AreEqual(TacticalMovingLeanReason.NoBenefit, decision.Reason);
		}

		[Test]
		public void A3_FarFromCorner_None()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.DistanceToCornerMeters = 12f;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(TacticalMovingLeanAction.None, decision.Action);
			Assert.AreEqual(TacticalMovingLeanReason.FarFromCorner, decision.Reason);
		}
		#endregion

		#region B Direction
		[Test]
		public void B1_Left()
		{
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(Benefit(CoverPeekDirection.Left));
			Assert.AreEqual(CoverPeekDirection.Left, decision.Direction);
		}

		[Test]
		public void B2_Right()
		{
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(Benefit(CoverPeekDirection.Right));
			Assert.AreEqual(CoverPeekDirection.Right, decision.Direction);
		}

		[Test]
		public void B3_Both_PrefersSafer()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.RightAvailable = true;
			sit.RightSmallSufficient = true;
			sit.RightVisibilityGain = 0.41f;
			sit.RightExposure01 = 0.55f;
			sit.LeftExposure01 = 0.12f;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(CoverPeekDirection.Left, decision.Direction);
		}

		[Test]
		public void B4_Neither()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.LeftAvailable = false;
			sit.RightAvailable = false;
			sit.LeftSmallSufficient = false;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(TacticalMovingLeanAction.None, decision.Action);
			Assert.AreEqual(TacticalMovingLeanReason.NoOpportunity, decision.Reason);
		}
		#endregion

		#region C Depth
		[Test]
		public void C1_SmallSufficient_Small()
		{
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(Benefit(CoverPeekDirection.Left));
			Assert.AreEqual(CoverLeanLevel.Small, decision.Depth);
		}

		[Test]
		public void C2_SmallInsufficient_Medium()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.LeftSmallSufficient = false;
			sit.LeftMediumSufficient = true;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(CoverLeanLevel.Medium, decision.Depth);
		}

		[Test]
		public void C3_DeepOnly_Deep()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.LeftSmallSufficient = false;
			sit.LeftMediumSufficient = false;
			sit.LeftDeepSufficient = true;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(CoverLeanLevel.Deep, decision.Depth);
		}
		#endregion

		#region D Transition
		[Test]
		public void D_Normal_MovingLean_Normal()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			TacticalMovingLeanDecision none = overlay.NotifyMovingLean(Far(), executor);
			Assert.AreEqual(TacticalMovingLeanAction.None, none.Action);
			TacticalMovingLeanDecision lean = overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			Assert.AreEqual(TacticalMovingLeanAction.Lean, lean.Action);
			Assert.IsTrue(overlay.MovingLeanActive);
			TacticalMovingLeanSituation passed = Benefit(CoverPeekDirection.Left);
			passed.CornerPassed = true;
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in passed, executor);
			Assert.AreEqual(TacticalMovingLeanAction.Exit, exit.Action);
			Assert.IsFalse(overlay.MovingLeanActive);
			Assert.AreEqual(CoverLeanLevel.None, executor.LastLevel);
		}
		#endregion

		#region E Corner crossing
		[Test]
		public void E_Approach_Lean_Pass_Return()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.NotifyMovingLean(Far(), executor);
			overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			Assert.AreEqual(CoverPeekDirection.Left, executor.LastDirection);
			Assert.AreEqual(CoverLeanLevel.Small, executor.LastLevel);
			TacticalMovingLeanSituation passed = Benefit(CoverPeekDirection.Left);
			passed.CornerPassed = true;
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in passed, executor);
			Assert.AreEqual(TacticalMovingLeanReason.CornerPassed, exit.Reason);
			Assert.IsFalse(overlay.MovingLeanActive);
		}
		#endregion

		#region F Threat
		[Test]
		public void F_ImmediateThreat_CancelsLean()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			TacticalMovingLeanSituation fire = Benefit(CoverPeekDirection.Left);
			fire.ImmediateThreat = true;
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in fire, executor);
			Assert.AreEqual(TacticalMovingLeanAction.Exit, exit.Action);
			Assert.AreEqual(TacticalMovingLeanReason.ImmediateThreat, exit.Reason);
			Assert.AreEqual(CoverLeanLevel.None, executor.LastLevel);
		}
		#endregion

		#region G Replan
		[Test]
		public void G_Replan_CancelsLean()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			TacticalMovingLeanSituation replan = Benefit(CoverPeekDirection.Left);
			replan.Replan = true;
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in replan, executor);
			Assert.AreEqual(TacticalMovingLeanAction.Exit, exit.Action);
			Assert.AreEqual(TacticalMovingLeanReason.Replan, exit.Reason);
			Assert.IsFalse(overlay.MovingLeanActive);
		}
		#endregion

		#region H Arrival
		[Test]
		public void H_Arrival_ExitsMovingLean()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.Update(TacticalRouteMath.Goal(Vector3.zero, new Vector3(4f, 0f, 0f), TacticalMovementMode.Normal, 1f));
			overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			var arrive = new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = new Vector3(4f, 0f, 0f),
				TargetPosition = new Vector3(4f, 0f, 0f),
				HasTargetPosition = true,
				Now = 2f
			};
			overlay.NotifyTacticalArrival(in arrive);
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			Assert.AreEqual(TacticalMovingLeanAction.Exit, exit.Action);
			Assert.AreEqual(TacticalMovingLeanReason.Arrival, exit.Reason);
			Assert.IsFalse(overlay.MovingLeanActive);
		}
		#endregion

		#region Extra
		[Test]
		public void Overlay_UsesExistingExecutor_NotNewController()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.NotifyMovingLean(Benefit(CoverPeekDirection.Right), executor);
			Assert.AreEqual(1, executor.SetLeanCount);
			Assert.AreEqual(CoverPeekDirection.Right, executor.LastDirection);
			Assert.AreEqual(CoverLeanLevel.Small, executor.LastLevel);
		}

		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI148_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				controller.TacticalMovement.NotifyMovingLean(Benefit(CoverPeekDirection.Left));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Performance_NotEveryFrame()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalMovingLeanSituation sit = Far();
			overlay.NotifyMovingLean(in sit);
			for (int i = 0; i < 10; i++)
			{
				sit.Approach = false;
				overlay.NotifyMovingLean(in sit);
			}

			Assert.AreEqual(1, overlay.MovingLeanEvaluationCount);
		}

		[Test]
		public void WallCorridor_DoesNotAutoLean()
		{
			TacticalMovingLeanSituation sit = Far();
			sit.WallCorridor = true;
			sit.HasCorner = true;
			sit.InCorridor = true;
			sit.DistanceToCornerMeters = 1f;
			TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
			Assert.AreEqual(TacticalMovingLeanAction.None, decision.Action);
		}

		[Test]
		public void SameCorner_MovingVsStationary_ShareExecutor()
		{
			var executor = new RecordingLeanExecutor();
			CoverMovementLeanRequest request = new CoverMovementLeanRequest
			{
				Mode = CoverMovementLeanMode.Leaning,
				Direction = CoverPeekDirection.Left,
				Depth = CoverLeanLevel.Small
			};
			CoverMovementLeanContract.Apply(executor, in request);
			Assert.AreEqual(CoverLeanLevel.Small, executor.LastLevel);
			new TacticalMovementOverlay().NotifyMovingLean(Benefit(CoverPeekDirection.Left), executor);
			Assert.AreEqual(CoverPeekDirection.Left, executor.LastDirection);
		}
		#endregion

		#region Helpers
		private static TacticalMovingLeanSituation Benefit(CoverPeekDirection _direction)
		{
			bool left = _direction == CoverPeekDirection.Left;
			return new TacticalMovingLeanSituation
			{
				Present = true,
				Moving = true,
				HasCorner = true,
				InCorridor = true,
				DistanceToCornerMeters = 1.2f,
				Approach = true,
				LeftAvailable = left,
				RightAvailable = !left,
				LeftVisibilityGain = left ? 0.41f : 0f,
				RightVisibilityGain = left ? 0f : 0.41f,
				LeftExposure01 = 0.18f,
				RightExposure01 = 0.18f,
				LeftSmallSufficient = left,
				RightSmallSufficient = !left,
				ExposureWithoutLean = 0.10f
			};
		}

		private static TacticalMovingLeanSituation Far()
		{
			TacticalMovingLeanSituation sit = Benefit(CoverPeekDirection.Left);
			sit.DistanceToCornerMeters = 12f;
			sit.LeftSmallSufficient = false;
			sit.LeftVisibilityGain = 0f;
			return sit;
		}
		#endregion
	}
}
