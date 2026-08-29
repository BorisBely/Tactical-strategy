using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C.1 Threat Direction → cover orientation & facing. Overlay only. #13/#14 stay frozen.
	/// </summary>
	[Category("ThreatDirectionCover")]
	public sealed class ThreatDirectionCoverTests
	{
		#region Constants
		private static readonly Vector3 s_North = Vector3.forward;
		private static readonly Vector3 s_East = Vector3.right;
		private static readonly Vector3 s_South = Vector3.back;
		private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
		private static readonly Vector3 s_NorthWest = new Vector3(-1f, 0f, 1f).normalized;
		private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
		#endregion

		#region A Adjustment
		[Test]
		public void A1_Alignment_ThreatNorth_NormalNorth_Positive()
		{
			Assert.Greater(ThreatDirectionCoverMath.Alignment(s_North, s_North), 0.99f);
		}

		[Test]
		public void A2_Alignment_ThreatNorth_NormalEast_Side()
		{
			Assert.AreEqual(0f, ThreatDirectionCoverMath.Alignment(s_East, s_North), 0.001f);
		}

		[Test]
		public void A3_Alignment_ThreatNorth_NormalSouth_Negative()
		{
			Assert.Less(ThreatDirectionCoverMath.Alignment(s_South, s_North), -0.99f);
		}

		[Test]
		public void A4_Adjustment_ThreatNorth_NormalNorth_Bonus()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.GoodBonus,
				ThreatDirectionCoverMath.Adjustment(s_North, s_North));
		}

		[Test]
		public void A5_Adjustment_ThreatNorth_NormalEast_Side()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.SideBonus,
				ThreatDirectionCoverMath.Adjustment(s_East, s_North));
		}

		[Test]
		public void A6_Adjustment_ThreatNorth_NormalSouth_Penalty()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.Penalty,
				ThreatDirectionCoverMath.Adjustment(s_South, s_North));
		}

		[Test]
		public void A7_CoverScore_Unchanged_WithOrWithoutThreatDirection()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation bare = IsolatedEastLook(default);
			bare.HasThreatDirection = false;
			CoverSituation withThreat = IsolatedEastLook(ExpectedNorth());
			float without = CoverScoreMath.EvaluateOne(cover, in bare, null).Score;
			float withDir = CoverScoreMath.EvaluateOne(cover, in withThreat, null).Score;
			Assert.AreEqual(without, withDir, 0.0001f);
		}

		[Test]
		public void A8_Bind_DoesNotWriteHostileDirection()
		{
			CoverSituation situation = IsolatedEastLook(default);
			Vector3 hostile = situation.HostileDirection;
			ThreatDirectionCoverMath.Bind(ref situation, ExpectedNorth());
			Assert.AreEqual(hostile.x, situation.HostileDirection.x, 0.0001f);
			Assert.AreEqual(hostile.z, situation.HostileDirection.z, 0.0001f);
			Assert.IsTrue(situation.HasThreatDirection);
			Assert.Greater(situation.ThreatDirection.z, 0.9f);
		}

		[Test]
		public void A9_PreferenceScore_IsScorePlusAdjustment()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			CoverPositionEvaluation evaluation = ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(cover, in situation, null),
				in situation);
			Assert.AreEqual(
				evaluation.Score + evaluation.ThreatDirectionAdjustment,
				evaluation.PreferenceScore,
				0.0001f);
			Assert.AreEqual(
				ThreatDirectionCoverMath.WeightedAdjustment(
					s_North,
					s_North,
					ThreatDirectionMath.ExpectedConfidence),
				evaluation.ThreatDirectionAdjustment,
				0.0001f);
		}

		[Test]
		public void A10_IsBetterPreference_PicksBonusWhenScoresEqual()
		{
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			CoverPositionEvaluation north = Stamped(StandingCover(1, s_CoverPos, s_North), in situation);
			CoverPositionEvaluation south = Stamped(StandingCover(2, s_CoverPos, s_South), in situation);
			Assert.AreEqual(north.Score, south.Score, 0.05f);
			Assert.IsTrue(ThreatDirectionCoverMath.IsBetterPreference(north, south));
			Assert.IsFalse(ThreatDirectionCoverMath.IsBetterPreference(south, north));
		}
		#endregion

		#region B Cover orientation
		[Test]
		public void B1_Expected_WithoutHostile_PrefersNorthCover()
		{
			CoverEvaluationResult result = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				ExpectedNorth());
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.Greater(result.Best.ThreatDirectionAdjustment, 0f);
		}

		[Test]
		public void B2_Expected_FirstPick_SolverSelectsNorthCover()
		{
			TacticalCoverDecision decision = FirstPick(
				new[]
				{
					StandingCover(1, s_CoverPos, s_North),
					StandingCover(2, s_CoverPos, s_South)
				},
				ExpectedNorth());
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}

		[Test]
		public void B3_VisualNorthEast_PrefersNorthEastCoverOverNorthWest()
		{
			CoverEvaluationResult result = EvaluatePair(
				StandingCover(1, s_CoverPos, s_NorthWest),
				StandingCover(2, s_CoverPos, s_NorthEast),
				VisualNorthEast(),
				true);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
		}

		[Test]
		public void B4_Visual_OverridesExpected_OnNextEvaluation()
		{
			CoverCandidate west = StandingCover(1, s_CoverPos, s_NorthWest);
			CoverCandidate east = StandingCover(2, s_CoverPos, s_NorthEast);
			CoverEvaluationResult expected = EvaluatePair(west, east, ExpectedNorth(), true);
			Assert.AreEqual(1, expected.Best.Candidate.CandidateId);
			CoverEvaluationResult visual = EvaluatePair(west, east, VisualNorthEast(), true);
			Assert.AreEqual(2, visual.Best.Candidate.CandidateId);
		}

		[Test]
		public void B5_Occupied_ThreatNorthToNorthEast_StaysCommitted()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_NorthWest);
			CoverCandidate other = StandingCover(2, new Vector3(1.5f, 0f, 0f), s_NorthEast);
			CoverSituation north = IsolatedNorthLook(ExpectedNorth());
			north.UnitPosition = Vector3.zero;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.Decide(in north, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, first.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, first.Reason);
			Assert.AreEqual(1, first.SelectedCandidateId);

			CoverSituation visual = IsolatedNorthLook(VisualNorthEast());
			visual.UnitPosition = Vector3.zero;
			TacticalCoverDecision second = solver.Decide(in visual, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, second.Reason);
			Assert.AreEqual(1, second.SelectedCandidateId);
			Assert.IsFalse(second.HasDestination);
		}

		[Test]
		public void B6_Occupied_BestMayChange_SelectedStays()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_NorthWest);
			CoverCandidate other = StandingCover(2, new Vector3(1.5f, 0f, 0f), s_NorthEast);
			CoverSituation visual = IsolatedNorthLook(VisualNorthEast());
			visual.UnitPosition = Vector3.zero;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in visual,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.AreEqual(1, decision.SelectedCandidateId);
			Assert.AreEqual(2, decision.BestCandidateId);
		}

		[Test]
		public void B7_ThreatOctantChange_IsEventReevaluation()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_East);
			CoverSituation north = IsolatedEastLook(ExpectedNorth());
			north.UnitPosition = Vector3.zero;
			var solver = new TacticalCoverSolver();
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			solver.Decide(in north, new[] { current, other }, in occupying);
			Assert.AreEqual(1, solver.DecideCount);
			CoverSituation east = IsolatedEastLook(VisualEast());
			east.UnitPosition = Vector3.zero;
			solver.Decide(in east, new[] { current, other }, in occupying);
			Assert.AreEqual(2, solver.DecideCount);
		}

		[Test]
		public void B8_NoPolling_SameThreat_CachedDecide()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			situation.UnitPosition = Vector3.zero;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			solver.Decide(in situation, new[] { current, other }, in occupying);
			for (int i = 0; i < 100; i++)
			{
				TacticalCoverDecision next = solver.Decide(
					in situation, new[] { current, other }, in occupying);
				Assert.IsTrue(next.FromCache);
			}

			Assert.AreEqual(1, solver.DecideCount);
		}

		[Test]
		public void B9_RepositionRecommended_UsesCoverScoreNotPreference()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_South);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_North);
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			situation.UnitPosition = Vector3.zero;
			CoverEvaluationResult result = new CoverPositionEvaluator().Evaluate(
				new[] { current, other },
				in situation);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
			Assert.Greater(result.Best.PreferenceScore, result.Current.PreferenceScore);
			Assert.IsFalse(result.RepositionRecommended);
		}

		[Test]
		public void B10_DecideFromScores_IgnoresAdjustment()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(
				5f,
				5f,
				CoverSwitchMath.DefaultSwitchingCost,
				true,
				1,
				2);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}

		[Test]
		public void B11_Search_NotReplacedByThreatDirection()
		{
			Assert.IsFalse(TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false));
		}

		[Test]
		public void B12_AcquireTolerance_Unchanged()
		{
			Assert.AreEqual(0.6f, TacticalArrivalMath.DefaultAcquireToleranceMeters, 0.0001f);
			Assert.AreEqual(
				CoverScoreMath.ArrivalSnapMeters,
				TacticalArrivalMath.DefaultAcquireToleranceMeters);
		}

		[Test]
		public void B13_EvaluatorCache_MissesOnThreatOctantChange()
		{
			CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
			CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
			var evaluator = new CoverPositionEvaluator();
			CoverSituation expected = IsolatedEastLook(ExpectedNorth());
			evaluator.Evaluate(new[] { north, south }, in expected);
			Assert.AreEqual(1, evaluator.EvaluateCount);
			evaluator.Evaluate(new[] { north, south }, in expected);
			Assert.AreEqual(1, evaluator.EvaluateCount);
			CoverSituation visual = IsolatedEastLook(VisualEast());
			evaluator.Evaluate(new[] { north, south }, in visual);
			Assert.AreEqual(2, evaluator.EvaluateCount);
		}
		#endregion

		#region C Facing
		[Test]
		public void C1_Facing_ExpectedNorth_SetsDesiredFacing()
		{
			var facing = new ThreatDirectionFacingController();
			Assert.IsTrue(facing.Notify(
				ExpectedNorth(),
				ThreatDirectionFacingReason.ThreatDirectionChanged,
				90f));
			Assert.IsTrue(facing.HasDesiredFacing);
			Assert.Greater(facing.DesiredFacing.z, 0.9f);
			Assert.AreEqual(1, facing.UpdateCount);
		}

		[Test]
		public void C2_Facing_Deadband_NoUpdate()
		{
			var facing = new ThreatDirectionFacingController();
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			ThreatDirectionKnowledge slight = DirectionKnowledge(
				new Vector3(Mathf.Sin(10f * Mathf.Deg2Rad), 0f, Mathf.Cos(10f * Mathf.Deg2Rad)),
				ThreatDirectionCompass.North,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
			Assert.IsFalse(facing.Notify(
				in slight,
				ThreatDirectionFacingReason.ThreatDirectionChanged,
				0f));
			Assert.AreEqual(1, facing.UpdateCount);
		}

		[Test]
		public void C3_Facing_ChangeAboveDeadband_Updates()
		{
			var facing = new ThreatDirectionFacingController();
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			Assert.IsTrue(facing.Notify(
				VisualNorthEast(),
				ThreatDirectionFacingReason.ThreatDirectionChanged,
				0f));
			Assert.AreEqual(2, facing.UpdateCount);
			Assert.Greater(facing.DesiredFacing.x, 0.5f);
		}

		[Test]
		public void C4_Facing_NoPolling()
		{
			var facing = new ThreatDirectionFacingController();
			ThreatDirectionKnowledge expected = ExpectedNorth();
			facing.Notify(in expected, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			int logs = facing.LogCount;
			for (int i = 0; i < 100; i++)
				Assert.IsFalse(facing.Notify(
					in expected,
					ThreatDirectionFacingReason.ThreatDirectionChanged,
					0f));
			Assert.AreEqual(1, facing.UpdateCount);
			Assert.AreEqual(logs, facing.LogCount);
		}

		[Test]
		public void C5_Facing_UsesSectorCenter()
		{
			ThreatDirectionKnowledge expected = ExpectedNorth();
			Assert.AreEqual(45f, expected.UncertaintyDegrees, 0.001f);
			var facing = new ThreatDirectionFacingController();
			facing.Notify(in expected, ThreatDirectionFacingReason.CoverAcquired, 0f);
			Assert.AreEqual(expected.Direction.z, facing.DesiredFacing.z, 0.001f);
			Assert.AreEqual(0f, ThreatDirectionFacingController.YawFrom(expected.Direction), 0.5f);
		}

		[Test]
		public void C6_Facing_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			int changes = readiness.Context.ChangeCount;
			var facing = new ThreatDirectionFacingController();
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ReadinessChanged, 0f);
			facing.Notify(VisualNorthEast(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(changes, readiness.Context.ChangeCount);
			Assert.AreEqual(ReadinessState.Patrol, readiness.CurrentState);
		}

		[Test]
		public void C7_CoverAcquired_ReasonAccepted()
		{
			var facing = new ThreatDirectionFacingController();
			Assert.IsTrue(facing.Notify(
				ExpectedNorth(),
				ThreatDirectionFacingReason.CoverAcquired,
				0f));
			Assert.AreEqual(1, facing.UpdateCount);
		}

		[Test]
		public void C8_LiveAi_Tick_SetsFacingFromExpected()
		{
			var go = new GameObject("ThreatDirectionCover_Facing");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatFacing.Reset();
				Assert.IsTrue(ai.ThreatDirection.ApplyBattleStart(
					Vector3.zero,
					new Vector3(0f, 0f, 10f),
					0f));
				ai.Tick(0f);
				Assert.IsTrue(ai.ThreatFacing.HasDesiredFacing);
				Assert.Greater(ai.ThreatFacing.DesiredFacing.z, 0.9f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region D Logs
		[Test]
		public void D1_CoverLog_ExpectedNorth()
		{
			string payload = ThreatDirectionCoverLog.FormatCover(ExpectedNorth(), 3, 0.85f);
			Assert.IsTrue(payload.IndexOf("source=Expected", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("dir=N", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("cover=C3", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("adjustment=+0.85", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void D2_FacingLog_VisualNorthEast()
		{
			string payload = ThreatDirectionCoverLog.FormatFacing(VisualNorthEast());
			Assert.IsTrue(payload.IndexOf("dir=NE", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("source=Visual", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void D3_Channels()
		{
			Assert.AreEqual("COVER_DIRECTION", ThreatDirectionCoverLog.CoverChannel);
			Assert.AreEqual("FACING_DIRECTION", ThreatDirectionCoverLog.FacingChannel);
			Assert.AreEqual(UnitActionLog.CoverDirection, ThreatDirectionCoverLog.CoverChannel);
			Assert.AreEqual(UnitActionLog.FacingDirection, ThreatDirectionCoverLog.FacingChannel);
		}

		[Test]
		public void D4_FacingLog_NotEveryNotify()
		{
			var facing = new ThreatDirectionFacingController();
			ThreatDirectionKnowledge expected = ExpectedNorth();
			facing.Notify(in expected, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			int logs = facing.LogCount;
			facing.Notify(in expected, ThreatDirectionFacingReason.CoverAcquired, 0f);
			facing.Notify(in expected, ThreatDirectionFacingReason.ReadinessChanged, 0f);
			Assert.AreEqual(logs, facing.LogCount);
		}

		[Test]
		public void D5_ExpectedUsedWithoutHostile()
		{
			Assert.IsTrue(ExpectedNorth().HasValue);
			Assert.AreEqual(ThreatDirectionState.Expected, ExpectedNorth().State);
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			Assert.IsFalse(situation.HasTarget);
			Assert.IsTrue(situation.HasThreatDirection);
		}
		#endregion

		#region E Independence
		[Test]
		public void E1_EmptyTicks_KnowledgeUnchanged()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(Vector3.zero, new Vector3(0f, 0f, 10f), 0f);
			Vector3 before = controller.GetThreatDirection();
			int logs = controller.LogCount;
			controller.Tick(1f);
			controller.Tick(2f);
			controller.Tick(3f);
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.AreEqual(before.z, controller.GetThreatDirection().z, 0.0001f);
			Assert.AreEqual(logs, controller.LogCount);
		}

		[Test]
		public void E2_ThreatDirection_DoesNotIssueMoveWhenOccupied()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = IsolatedEastLook(ExpectedNorth());
			situation.UnitPosition = Vector3.zero;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.IsFalse(decision.HasDestination);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
		}

		[Test]
		public void E3_ProtectionFactor_UnchangedByThreatBind()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation bare = IsolatedEastLook(default);
			bare.HasThreatDirection = false;
			CoverSituation bound = IsolatedEastLook(ExpectedNorth());
			Assert.AreEqual(
				CoverScoreMath.ProtectionScore(cover, in bare),
				CoverScoreMath.ProtectionScore(cover, in bound));
		}

		[Test]
		public void E4_Knowledge_DoesNotFireOrChangeState()
		{
			var go = new GameObject("ThreatDirectionCover_Idle");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatFacing.Reset();
				ai.ThreatDirection.ApplyBattleStart(Vector3.zero, new Vector3(0f, 0f, 10f), 0f);
				ai.ThreatDirection.ApplyHostileVisible(Vector3.zero, new Vector3(10f, 0f, 10f), 1f);
				ai.Tick(0f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
				Assert.AreNotEqual(UnitAIAction.Engage, ai.CurrentAction);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Helpers
		private static CoverEvaluationResult EvaluatePair(
			CoverCandidate _a,
			CoverCandidate _b,
			ThreatDirectionKnowledge _knowledge,
			bool _northLook = false)
		{
			CoverSituation situation = _northLook
				? IsolatedNorthLook(_knowledge)
				: IsolatedEastLook(_knowledge);
			return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
		}

		private static TacticalCoverDecision FirstPick(
			CoverCandidate[] _candidates,
			ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = IsolatedEastLook(_knowledge);
			return new TacticalCoverSolver().Decide(
				in situation,
				_candidates,
				CurrentTacticalPosition.Invalid);
		}

		private static CoverPositionEvaluation Stamped(
			CoverCandidate _candidate,
			in CoverSituation _situation)
		{
			return ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(_candidate, in _situation, null),
				in _situation);
		}

		private static CoverSituation IsolatedEastLook(ThreatDirectionKnowledge _knowledge)
		{
			var situation = new CoverSituation
			{
				UnitPosition = Vector3.zero,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Hold,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				HasTarget = false,
				SectorForward = s_East,
				HostileDirection = s_East,
				GeometryVersion = 1
			};
			ThreatDirectionCoverMath.Bind(ref situation, in _knowledge);
			return situation;
		}

		private static CoverSituation IsolatedNorthLook(ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = IsolatedEastLook(_knowledge);
			situation.SectorForward = s_North;
			situation.HostileDirection = s_North;
			return situation;
		}

		private static ThreatDirectionKnowledge ExpectedNorth()
		{
			return DirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
		}

		private static ThreatDirectionKnowledge VisualNorthEast()
		{
			return DirectionKnowledge(
				s_NorthEast,
				ThreatDirectionCompass.NorthEast,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge VisualEast()
		{
			return DirectionKnowledge(
				s_East,
				ThreatDirectionCompass.East,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge DirectionKnowledge(
			Vector3 _direction,
			ThreatDirectionCompass _compass,
			ThreatDirectionSource _source,
			ThreatDirectionState _state)
		{
			return new ThreatDirectionKnowledge(
				_direction,
				_compass,
				_state == ThreatDirectionState.Expected
					? ThreatDirectionMath.ExpectedConfidence
					: ThreatDirectionMath.VisualConfidence,
				_state == ThreatDirectionState.Expected
					? ThreatDirectionMath.ExpectedUncertaintyDegrees
					: ThreatDirectionMath.VisualUncertaintyDegrees,
				0f,
				_source,
				_state);
		}

		private static CoverCandidate StandingCover(int _id, Vector3 _position, Vector3 _normal)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = _normal,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = Profile(1f),
				CrouchProfile = Profile(1f),
				GeometryVersion = 1
			};
		}

		private static CoverProtectionProfile Profile(float _value)
		{
			return new CoverProtectionProfile
			{
				Head = _value,
				Torso = _value,
				Pelvis = _value,
				Legs = _value
			};
		}
		#endregion
	}
}
