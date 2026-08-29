using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C.3 Threat Direction → TacticalPositionPreference. Overlay only. #13/#14 stay frozen.
	/// </summary>
	[Category("ThreatDirectionPosition")]
	public sealed class ThreatDirectionPositionTests
	{
		#region Constants
		private static readonly Vector3 s_North = Vector3.forward;
		private static readonly Vector3 s_East = Vector3.right;
		private static readonly Vector3 s_South = Vector3.back;
		private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
		private static readonly Vector3 s_NorthWest = new Vector3(-1f, 0f, 1f).normalized;
		private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
		private static readonly Vector3 s_Origin = Vector3.zero;
		private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
		private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);
		private static readonly Vector3 s_NorthEastPoint = new Vector3(10f, 0f, 10f);
		#endregion

		#region A Direction
		[Test]
		public void A1_DirectionScore_CoverNorth_Positive()
		{
			Assert.Greater(Stamped(StandingCover(1, s_CoverPos, s_North), ExpectedNorth()).DirectionScore, 0f);
		}

		[Test]
		public void A2_DirectionScore_CoverEast_Side()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.SideBonus,
				Stamped(StandingCover(1, s_CoverPos, s_East), ExpectedNorth()).DirectionScore);
		}

		[Test]
		public void A3_DirectionScore_CoverSouth_Negative()
		{
			Assert.Less(Stamped(StandingCover(1, s_CoverPos, s_South), ExpectedNorth()).DirectionScore, 0f);
		}

		[Test]
		public void A4_ExpectedNorth_PrefersNorthCover()
		{
			CoverEvaluationResult result = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				ExpectedNorth());
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.Greater(result.Best.PositionAdjustment, 0f);
		}

		[Test]
		public void A5_EastCover_NotPreferredOverNorth()
		{
			CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
			CoverCandidate east = StandingCover(2, s_CoverPos, s_East);
			CoverSituation situation = IsolatedEqualLook(ExpectedNorth());
			CoverPositionEvaluation northEval = Stamped(north, in situation);
			CoverPositionEvaluation eastEval = Stamped(east, in situation);
			Assert.AreEqual(northEval.Score, eastEval.Score, 0.05f);
			CoverEvaluationResult result = new CoverPositionEvaluator().Evaluate(
				new[] { north, east },
				in situation);
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.Greater(northEval.TacticalPositionPreference, eastEval.TacticalPositionPreference);
		}

		[Test]
		public void A6_CoverScore_Unchanged()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation bare = Isolated(default);
			bare.HasThreatDirection = false;
			CoverSituation withThreat = Isolated(ExpectedNorth());
			Assert.AreEqual(
				CoverScoreMath.EvaluateOne(cover, in bare, null).Score,
				CoverScoreMath.EvaluateOne(cover, in withThreat, null).Score,
				0.0001f);
		}

		[Test]
		public void A7_PreferenceScore_StillScorePlus14C1Adjustment()
		{
			CoverPositionEvaluation evaluation = Stamped(
				StandingCover(1, s_CoverPos, s_North),
				Isolated(ExpectedNorth()));
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
		public void A8_TacticalPositionPreference_IsScorePlusPositionAdjustment()
		{
			CoverPositionEvaluation evaluation = Stamped(
				StandingCover(1, s_CoverPos, s_North),
				Isolated(ExpectedNorth()));
			Assert.AreEqual(
				evaluation.Score + evaluation.PositionAdjustment,
				evaluation.TacticalPositionPreference,
				0.0001f);
		}

		[Test]
		public void A9_PoorNorthCover_DoesNotBeatExcellentSouthCover()
		{
			CoverCandidate excellentSouth = StandingCover(1, s_Origin, s_South, 1f);
			CoverCandidate poorNorth = StandingCover(2, new Vector3(20f, 0f, 0f), s_North, 0.05f);
			CoverEvaluationResult result = EvaluatePair(excellentSouth, poorNorth, ExpectedNorth());
			float excellentScore = Stamped(excellentSouth, Isolated(ExpectedNorth())).Score;
			float poorScore = Stamped(poorNorth, Isolated(ExpectedNorth())).Score;
			Assert.Greater(excellentScore, poorScore + 2f);
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.Less(Stamped(excellentSouth, Isolated(ExpectedNorth())).DirectionScore, 0f);
			Assert.Greater(Stamped(poorNorth, Isolated(ExpectedNorth())).DirectionScore, 0f);
		}

		[Test]
		public void A10_DirectionScore_Equals14C1Adjustment()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.Adjustment(s_North, s_North),
				Stamped(StandingCover(1, s_CoverPos, s_North), ExpectedNorth()).DirectionScore);
		}

		[Test]
		public void A11_FirstPick_SolverSelectsNorthCover()
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
		#endregion

		#region B Confidence
		[Test]
		public void B1_HighConfidence_StrongerAdjustmentThanExpected()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			float expected = Stamped(cover, ExpectedNorth()).PositionAdjustment;
			float visual = Stamped(cover, VisualNorth()).PositionAdjustment;
			Assert.Greater(Mathf.Abs(visual), Mathf.Abs(expected));
		}

		[Test]
		public void B2_LowConfidence_StillPrefersNorth()
		{
			CoverEvaluationResult result = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				LowExpectedNorth());
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.Less(
				Mathf.Abs(result.Best.PositionAdjustment),
				Mathf.Abs(Stamped(StandingCover(1, s_CoverPos, s_North), VisualNorth()).PositionAdjustment));
		}

		[Test]
		public void B3_ConfidenceWeight_IsCoverInfluence()
		{
			CoverPositionEvaluation expected = Stamped(
				StandingCover(1, s_CoverPos, s_North),
				ExpectedNorth());
			CoverPositionEvaluation visual = Stamped(
				StandingCover(1, s_CoverPos, s_North),
				VisualNorth());
			Assert.AreEqual(
				ThreatDirectionMath.CoverInfluence(ThreatDirectionMath.ExpectedConfidence),
				expected.ConfidenceWeight,
				0.0001f);
			Assert.Greater(visual.ConfidenceWeight, expected.ConfidenceWeight);
		}

		[Test]
		public void B4_PositionAdjustment_FollowsFinalFormula()
		{
			CoverPositionEvaluation evaluation = Stamped(
				StandingCover(1, s_CoverPos, s_North),
				ExpectedNorth());
			Assert.AreEqual(
				ThreatDirectionPositionMath.FinalAdjustment(
					evaluation.DirectionScore,
					evaluation.FacingScore,
					evaluation.ConfidenceWeight,
					evaluation.SectorOverlap),
				evaluation.PositionAdjustment,
				0.0001f);
		}

		[Test]
		public void B5_Occupied_ConfidenceChange_DoesNotForceReposition()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation expected = Isolated(ExpectedNorth());
			expected.UnitPosition = s_Origin;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.Decide(in expected, new[] { current, other }, in occupying);
			CoverSituation visual = Isolated(VisualNorth());
			visual.UnitPosition = s_Origin;
			TacticalCoverDecision second = solver.Decide(in visual, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, first.Decision);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(1, second.SelectedCandidateId);
			Assert.IsFalse(second.HasDestination);
		}
		#endregion

		#region C Uncertainty
		[Test]
		public void C1_NarrowCover_OverlapLessThanWide_OnWideThreat()
		{
			float wideThreat = 60f;
			Assert.Less(
				ThreatDirectionPositionMath.SectorOverlap(s_North, 5f, s_North, wideThreat),
				ThreatDirectionPositionMath.SectorOverlap(s_North, 60f, s_North, wideThreat));
		}

		[Test]
		public void C2_WideProtectedSector_BeatsNarrow_WhenScoresEqual()
		{
			CoverCandidate wide = TypedCover(1, s_CoverPos, s_North, CoverType.Partial);
			CoverCandidate narrow = TypedCover(2, s_CoverPos, s_North, CoverType.Corner);
			CoverSituation situation = Isolated(WideExpectedNorth());
			CoverPositionEvaluation wideEval = Stamped(wide, in situation);
			CoverPositionEvaluation narrowEval = Stamped(narrow, in situation);
			Assert.AreEqual(wideEval.Score, narrowEval.Score, 0.05f);
			Assert.Greater(wideEval.SectorOverlap, narrowEval.SectorOverlap);
			Assert.Greater(wideEval.PositionAdjustment, narrowEval.PositionAdjustment);
			Assert.IsTrue(ThreatDirectionCoverMath.IsBetterPreference(wideEval, narrowEval));
		}

		[Test]
		public void C3_TightVisualCone_AlignedStanding_OverlapNearOne()
		{
			Assert.Greater(
				ThreatDirectionPositionMath.SectorOverlap(
					s_North,
					ThreatDirectionPositionMath.StandingProtectedHalfDegrees,
					s_North,
					ThreatDirectionMath.VisualUncertaintyDegrees),
				0.99f);
		}

		[Test]
		public void C4_MisalignedCover_OverlapNearZero()
		{
			Assert.Less(
				ThreatDirectionPositionMath.SectorOverlap(
					s_East,
					ThreatDirectionPositionMath.StandingProtectedHalfDegrees,
					s_North,
					ThreatDirectionMath.ExpectedUncertaintyDegrees),
				0.05f);
		}
		#endregion

		#region D Facing
		[Test]
		public void D1_FacingScore_NorthPositive_SouthNegative()
		{
			Assert.Greater(ThreatDirectionPositionMath.FacingScore(s_North, s_North), 0f);
			Assert.Less(ThreatDirectionPositionMath.FacingScore(s_South, s_North), 0f);
		}

		[Test]
		public void D2_FacingScore_UsesCoverNormalAsExit()
		{
			Assert.AreEqual(
				ThreatDirectionCoverMath.Alignment(s_North, s_North) * ThreatDirectionPositionMath.FacingWeight,
				ThreatDirectionPositionMath.FacingScore(s_North, s_North),
				0.0001f);
		}

		[Test]
		public void D3_AwkwardSouthFacing_WorseThanNorth()
		{
			CoverPositionEvaluation north = Stamped(StandingCover(1, s_CoverPos, s_North), ExpectedNorth());
			CoverPositionEvaluation south = Stamped(StandingCover(2, s_CoverPos, s_South), ExpectedNorth());
			Assert.Greater(north.FacingScore, south.FacingScore);
			Assert.Greater(north.TacticalPositionPreference, south.TacticalPositionPreference);
		}
		#endregion

		#region E Stay / re-evaluation
		[Test]
		public void E1_Occupied_ThreatChanged_StaysCommitted()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_NorthWest);
			CoverCandidate other = StandingCover(2, new Vector3(1.5f, 0f, 0f), s_NorthEast);
			CoverSituation north = IsolatedNorthLook(ExpectedNorth());
			north.UnitPosition = s_Origin;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.Decide(in north, new[] { current, other }, in occupying);
			CoverSituation visual = IsolatedNorthLook(VisualNorthEast());
			visual.UnitPosition = s_Origin;
			TacticalCoverDecision second = solver.Decide(in visual, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, first.Decision);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(1, second.SelectedCandidateId);
			Assert.IsFalse(second.HasDestination);
		}

		[Test]
		public void E2_Occupied_BestMayChange_SelectedStays()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_NorthWest);
			CoverCandidate other = StandingCover(2, new Vector3(1.5f, 0f, 0f), s_NorthEast);
			CoverSituation visual = IsolatedNorthLook(VisualNorthEast());
			visual.UnitPosition = s_Origin;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in visual,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.AreEqual(1, decision.SelectedCandidateId);
			Assert.AreEqual(2, decision.BestCandidateId);
		}

		[Test]
		public void E3_SlightDirectionChange_NoRecalculation()
		{
			CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
			CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
			var evaluator = new CoverPositionEvaluator();
			CoverSituation expected = Isolated(ExpectedNorth());
			evaluator.Evaluate(new[] { north, south }, in expected);
			CoverSituation slight = Isolated(SlightNorth());
			evaluator.Evaluate(new[] { north, south }, in slight);
			Assert.AreEqual(1, evaluator.EvaluateCount);
			Assert.IsFalse(ThreatDirectionPositionMath.IsMaterialDirectionChange(
				ExpectedNorth().Direction,
				SlightNorth().Direction));
		}

		[Test]
		public void E4_MaterialDirectionChange_AllowsRecalculation()
		{
			CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
			CoverCandidate east = StandingCover(2, s_CoverPos, s_East);
			var evaluator = new CoverPositionEvaluator();
			CoverSituation expected = Isolated(ExpectedNorth());
			evaluator.Evaluate(new[] { north, east }, in expected);
			CoverSituation eastThreat = Isolated(VisualEast());
			evaluator.Evaluate(new[] { north, east }, in eastThreat);
			Assert.AreEqual(2, evaluator.EvaluateCount);
			Assert.IsTrue(ThreatDirectionPositionMath.IsMaterialDirectionChange(
				ExpectedNorth().Direction,
				VisualEast().Direction));
		}

		[Test]
		public void E5_MaterialChange_Occupied_RecalcPermitted_ButStay()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_East);
			CoverSituation north = Isolated(ExpectedNorth());
			north.UnitPosition = s_Origin;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			solver.Decide(in north, new[] { current, other }, in occupying);
			Assert.AreEqual(1, solver.DecideCount);
			CoverSituation east = Isolated(VisualEast());
			east.UnitPosition = s_Origin;
			TacticalCoverDecision second = solver.Decide(in east, new[] { current, other }, in occupying);
			Assert.AreEqual(2, solver.DecideCount);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(1, second.SelectedCandidateId);
		}

		[Test]
		public void E6_NoPolling_SameThreat_Cached()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = Isolated(ExpectedNorth());
			situation.UnitPosition = s_Origin;
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
		public void E7_RepositionRecommended_UsesCoverScoreNotPreference()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_South);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_North);
			CoverSituation situation = Isolated(ExpectedNorth());
			situation.UnitPosition = s_Origin;
			CoverEvaluationResult result = new CoverPositionEvaluator().Evaluate(
				new[] { current, other },
				in situation);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
			Assert.Greater(result.Best.TacticalPositionPreference, result.Current.TacticalPositionPreference);
			Assert.IsFalse(result.RepositionRecommended);
		}

		[Test]
		public void E8_DecideFromScores_IgnoresAdjustment()
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
		public void E9_NeedsReevaluation_FalseWhenUnchanged()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = Isolated(ExpectedNorth());
			situation.UnitPosition = s_Origin;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			solver.Decide(in situation, new[] { current, other }, in occupying);
			Assert.IsFalse(solver.NeedsReevaluation(
				in situation, in occupying, new[] { current, other }));
		}
		#endregion

		#region F Isolation
		[Test]
		public void F1_Occupied_DoesNotIssueMove()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = Isolated(ExpectedNorth());
			situation.UnitPosition = s_Origin;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.IsFalse(decision.HasDestination);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
		}

		[Test]
		public void F2_SearchUntouched()
		{
			Assert.IsFalse(TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false));
		}

		[Test]
		public void F3_AcquireUnchanged()
		{
			Assert.AreEqual(0.6f, TacticalArrivalMath.DefaultAcquireToleranceMeters, 0.0001f);
		}

		[Test]
		public void F4_DoesNotChangeAiStateOrFire()
		{
			var go = new GameObject("ThreatDirectionPosition_Idle");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatFacing.Reset();
				ai.ThreatDirection.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
				ai.ThreatDirection.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
				ai.Tick(0f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
				Assert.AreNotEqual(UnitAIAction.Engage, ai.CurrentAction);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void F5_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			int changes = readiness.Context.ChangeCount;
			CoverEvaluationResult unused = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				VisualNorthEast());
			Assert.Greater(unused.Best.PositionAdjustment, float.NegativeInfinity);
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(changes, readiness.Context.ChangeCount);
		}

		[Test]
		public void F6_EmptyTicks_KnowledgeUnchanged()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
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
		public void F7_NoPolling_EnemyMovedWithoutEvent()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			Vector3 before = controller.GetThreatDirection();
			int logs = controller.LogCount;
			controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
			controller.Tick(2f, s_Origin, AIPerceptionFrame.Empty);
			Assert.AreEqual(before, controller.GetThreatDirection());
			Assert.AreEqual(logs, controller.LogCount);
		}

		[Test]
		public void F8_ProtectionFactor_UnchangedByBind()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation bare = Isolated(default);
			bare.HasThreatDirection = false;
			CoverSituation bound = Isolated(ExpectedNorth());
			Assert.AreEqual(
				CoverScoreMath.ProtectionScore(cover, in bare),
				CoverScoreMath.ProtectionScore(cover, in bound));
		}
		#endregion

		#region G Logs
		[Test]
		public void G1_Channel()
		{
			Assert.AreEqual("TACTICAL_POSITION", ThreatDirectionPositionLog.Channel);
			Assert.AreEqual(UnitActionLog.TacticalPosition, ThreatDirectionPositionLog.Channel);
		}

		[Test]
		public void G2_Payload_HasScoresAndAdjustment()
		{
			string payload = ThreatDirectionPositionLog.Format(
				Stamped(StandingCover(3, s_CoverPos, s_North), ExpectedNorth()));
			Assert.IsTrue(payload.IndexOf("cover=C3", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("dirScore=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("facingScore=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("weight=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("overlap=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("adj=", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void G3_VisualNorthEast_PrefersNorthEastCover()
		{
			CoverEvaluationResult result = EvaluatePair(
				StandingCover(1, s_CoverPos, s_NorthWest),
				StandingCover(2, s_CoverPos, s_NorthEast),
				VisualNorthEast(),
				true);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
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
				: Isolated(_knowledge);
			return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
		}

		private static TacticalCoverDecision FirstPick(
			CoverCandidate[] _candidates,
			ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = Isolated(_knowledge);
			return new TacticalCoverSolver().Decide(
				in situation,
				_candidates,
				CurrentTacticalPosition.Invalid);
		}

		private static CoverPositionEvaluation Stamped(
			CoverCandidate _candidate,
			ThreatDirectionKnowledge _knowledge)
		{
			return Stamped(_candidate, Isolated(_knowledge));
		}

		private static CoverPositionEvaluation Stamped(
			CoverCandidate _candidate,
			in CoverSituation _situation)
		{
			return ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(_candidate, in _situation, null),
				in _situation);
		}

		private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge)
		{
			var situation = new CoverSituation
			{
				UnitPosition = s_Origin,
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
			CoverSituation situation = Isolated(_knowledge);
			situation.SectorForward = s_North;
			situation.HostileDirection = s_North;
			return situation;
		}

		private static CoverSituation IsolatedEqualLook(ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = Isolated(_knowledge);
			situation.SectorForward = s_NorthEast;
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

		private static ThreatDirectionKnowledge LowExpectedNorth()
		{
			return new ThreatDirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
				0.3f,
				ThreatDirectionMath.ExpectedUncertaintyDegrees,
				0f,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
		}

		private static ThreatDirectionKnowledge WideExpectedNorth()
		{
			return new ThreatDirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
				ThreatDirectionMath.ExpectedConfidence,
				60f,
				0f,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
		}

		private static ThreatDirectionKnowledge SlightNorth()
		{
			Vector3 slight = new Vector3(
				Mathf.Sin(10f * Mathf.Deg2Rad),
				0f,
				Mathf.Cos(10f * Mathf.Deg2Rad));
			return new ThreatDirectionKnowledge(
				slight,
				ThreatDirectionCompass.North,
				ThreatDirectionMath.ExpectedConfidence,
				ThreatDirectionMath.ExpectedUncertaintyDegrees,
				0f,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
		}

		private static ThreatDirectionKnowledge VisualNorth()
		{
			return DirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
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

		private static ThreatDirectionKnowledge VisualNorthEast()
		{
			return DirectionKnowledge(
				s_NorthEast,
				ThreatDirectionCompass.NorthEast,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge DirectionKnowledge(
			Vector3 _direction,
			ThreatDirectionCompass _compass,
			ThreatDirectionSource _source,
			ThreatDirectionState _state)
		{
			ThreatDirectionMath.QualityAt(_state, _source, 0f, out float confidence, out float uncertainty);
			return new ThreatDirectionKnowledge(
				_direction,
				_compass,
				confidence,
				uncertainty,
				0f,
				_source,
				_state);
		}

		private static CoverCandidate StandingCover(
			int _id,
			Vector3 _position,
			Vector3 _normal,
			float _protection = 1f)
		{
			return TypedCover(_id, _position, _normal, CoverType.Standing, _protection);
		}

		private static CoverCandidate TypedCover(
			int _id,
			Vector3 _position,
			Vector3 _normal,
			CoverType _type,
			float _protection = 1f)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = _normal,
				CoverType = _type,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = Profile(_protection),
				CrouchProfile = Profile(_protection),
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
