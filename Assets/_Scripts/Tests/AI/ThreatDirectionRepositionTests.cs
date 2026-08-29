using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C.5 Threat Direction → Reposition Decision. Permission only. #13/#14 stay frozen.
	/// </summary>
	[Category("ThreatDirectionReposition")]
	public sealed class ThreatDirectionRepositionTests
	{
		#region Constants
		private static readonly Vector3 s_North = Vector3.forward;
		private static readonly Vector3 s_East = Vector3.right;
		private static readonly Vector3 s_South = Vector3.back;
		private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
		private static readonly Vector3 s_Origin = Vector3.zero;
		private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
		private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
		private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);
		#endregion

		#region A FaceOnly
		[Test]
		public void A1_FiveDegrees_FaceOnly()
		{
			Assert.AreEqual(
				ThreatDirectionRepositionKind.FaceOnly,
				DecidePair(SlightNorth(5f), 5f).Kind);
		}

		[Test]
		public void A2_NorthEast_FaceOnly()
		{
			Assert.AreEqual(
				ThreatDirectionRepositionKind.FaceOnly,
				DecidePair(VisualNorthEast(), 45f).Kind);
		}

		[Test]
		public void A3_SmallYaw_NoLogSpam()
		{
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			CoverSituation situation = IsolatedEqualLook(SlightNorth(4f));
			decision.Evaluate(SlightNorth(4f), current, new[] { current, other }, in situation, 4f);
			int logs = decision.LogCount;
			decision.Evaluate(SlightNorth(7f), current, new[] { current, other }, in situation, 7f);
			decision.Evaluate(SlightNorth(3f), current, new[] { current, other }, in situation, 3f);
			Assert.AreEqual(logs, decision.LogCount);
			Assert.AreEqual(ThreatDirectionRepositionKind.FaceOnly, decision.LastKind);
		}

		[Test]
		public void A4_NoOccupying_FaceOnlyEvenAtNinety()
		{
			var decision = new ThreatDirectionReposition();
			ThreatDirectionRepositionResult result = decision.Evaluate(
				VisualEast(),
				null,
				new[] { StandingCover(2, s_Origin, s_East) },
				IsolatedEqualLook(VisualEast()),
				90f);
			Assert.AreEqual(ThreatDirectionRepositionKind.FaceOnly, result.Kind);
			Assert.IsFalse(decision.AllowsCoverReevaluation);
		}
		#endregion

		#region B Confidence
		[Test]
		public void B1_LowConfidenceEast_FaceOnly()
		{
			Assert.AreEqual(
				ThreatDirectionRepositionKind.FaceOnly,
				DecidePair(WeakEast(), 90f).Kind);
		}

		[Test]
		public void B2_HighConfidenceEast_RepositionAllowed()
		{
			Assert.AreEqual(
				ThreatDirectionRepositionKind.RepositionAllowed,
				DecidePair(VisualEast(), 90f).Kind);
		}

		[Test]
		public void B3_MathGate_RejectsLowConfidenceAtNinety()
		{
			Assert.IsFalse(ThreatDirectionRepositionMath.PassesRepositionGate(90f, 0.2f));
			Assert.IsTrue(ThreatDirectionRepositionMath.PassesRepositionGate(
				90f,
				ThreatDirectionMath.VisualConfidence));
		}

		[Test]
		public void B4_ExpectedConfidence_BelowThreshold()
		{
			Assert.IsFalse(ThreatDirectionRepositionMath.PassesRepositionGate(
				180f,
				ThreatDirectionMath.ExpectedConfidence));
		}
		#endregion

		#region C Stay
		[Test]
		public void C1_GoodFit_Stay()
		{
			CoverCandidate east = StandingCover(1, s_Origin, s_East);
			CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			ThreatDirectionRepositionResult result = new ThreatDirectionReposition().Evaluate(
				VisualEast(),
				east,
				new[] { east, south },
				in situation,
				90f);
			Assert.AreEqual(ThreatDirectionRepositionKind.Stay, result.Kind);
			Assert.AreEqual(CoverThreatFit.Good, result.ThreatFit);
		}

		[Test]
		public void C2_SingleCover_StayEvenIfPoor()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			ThreatDirectionRepositionResult result = new ThreatDirectionReposition().Evaluate(
				VisualEast(),
				current,
				new[] { current },
				in situation,
				90f);
			Assert.AreEqual(ThreatDirectionRepositionKind.Stay, result.Kind);
			Assert.AreEqual(CoverThreatFit.Poor, result.ThreatFit);
		}

		[Test]
		public void C3_TinyScoreDelta_Stay()
		{
			Assert.AreEqual(
				ThreatDirectionRepositionKind.Stay,
				ThreatDirectionRepositionMath.Decide(
					90f,
					ThreatDirectionMath.VisualConfidence,
					CoverThreatFit.Poor,
					true,
					5f,
					5f,
					5.01f,
					5.01f,
					1,
					2));
		}

		[Test]
		public void C4_NorthEast_NotStayReposition()
		{
			Assert.AreNotEqual(
				ThreatDirectionRepositionKind.RepositionAllowed,
				DecidePair(VisualNorthEast(), 45f).Kind);
		}
		#endregion

		#region D RepositionAllowed
		[Test]
		public void D1_NorthToEast_PoorCover_RepositionAllowed()
		{
			ThreatDirectionRepositionResult result = DecidePair(VisualEast(), 90f);
			Assert.AreEqual(ThreatDirectionRepositionKind.RepositionAllowed, result.Kind);
			Assert.AreEqual(CoverThreatFit.Poor, result.ThreatFit);
			Assert.AreEqual(1, result.CurrentCandidateId);
			Assert.AreEqual(2, result.BestCandidateId);
		}

		[Test]
		public void D2_NorthToSouth_RepositionAllowed()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate south = StandingCover(2, s_Origin, s_South);
			CoverSituation situation = IsolatedEqualLook(VisualSouth());
			ThreatDirectionRepositionResult result = new ThreatDirectionReposition().Evaluate(
				VisualSouth(),
				current,
				new[] { current, south },
				in situation,
				180f);
			Assert.AreEqual(ThreatDirectionRepositionKind.RepositionAllowed, result.Kind);
		}

		[Test]
		public void D3_AllowsCoverReevaluation_OnlyWhenAllowed()
		{
			ThreatDirectionReposition face = new ThreatDirectionReposition();
			DecideOn(face, VisualNorthEast(), 45f);
			Assert.IsFalse(face.AllowsCoverReevaluation);
			ThreatDirectionReposition move = new ThreatDirectionReposition();
			DecideOn(move, VisualEast(), 90f);
			Assert.IsTrue(move.AllowsCoverReevaluation);
		}

		[Test]
		public void D4_HoldAfterAngleConsumed()
		{
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			DecideOn(decision, VisualEast(), 90f);
			Assert.IsTrue(decision.AllowsCoverReevaluation);
			int logs = decision.LogCount;
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			ThreatDirectionRepositionResult held = decision.Evaluate(
				VisualEast(),
				current,
				new[] { current, other },
				in situation,
				0f);
			Assert.AreEqual(ThreatDirectionRepositionKind.RepositionAllowed, held.Kind);
			Assert.AreEqual(logs, decision.LogCount);
		}
		#endregion

		#region E Margin / oscillation
		[Test]
		public void E1_NoticeableAdvantage_Required()
		{
			Assert.IsFalse(ThreatDirectionRepositionMath.HasNoticeableAdvantage(5f, 5f, 5.01f, 5.01f));
			Assert.IsTrue(ThreatDirectionRepositionMath.HasNoticeableAdvantage(5f, 5f, 6f, 6f));
			Assert.AreEqual(
				ThreatDirectionRepositionKind.RepositionAllowed,
				ThreatDirectionRepositionMath.Decide(
					90f,
					ThreatDirectionMath.VisualConfidence,
					CoverThreatFit.Poor,
					true,
					5f,
					5f,
					5.2f,
					5.2f,
					1,
					2,
					false,
					0f,
					0f,
					CoverThreatFit.Good));
		}

		[Test]
		public void E2_MarginMatchesCoverSwitch()
		{
			Assert.AreEqual(
				CoverSwitchMath.DefaultSwitchingCost,
				ThreatDirectionRepositionMath.ThreatRepositionMargin,
				0.0001f);
		}

		[Test]
		public void E3_RepeatSameInputs_NoOscillation()
		{
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			DecideOn(decision, VisualEast(), 90f);
			int logs = decision.LogCount;
			int decides = decision.DecideCount;
			DecideOn(decision, VisualEast(), 90f);
			DecideOn(decision, VisualEast(), 90f);
			Assert.AreEqual(logs, decision.LogCount);
			Assert.Greater(decision.DecideCount, decides);
			Assert.AreEqual(ThreatDirectionRepositionKind.RepositionAllowed, decision.LastKind);
		}

		[Test]
		public void E4_AngleThreshold_Eighty()
		{
			Assert.AreEqual(80f, ThreatDirectionRepositionMath.ThreatRepositionAngleThreshold);
			Assert.IsFalse(ThreatDirectionRepositionMath.PassesRepositionGate(
				45f,
				ThreatDirectionMath.VisualConfidence));
			Assert.IsTrue(ThreatDirectionRepositionMath.PassesRepositionGate(
				90f,
				ThreatDirectionMath.VisualConfidence));
		}
		#endregion

		#region F Occupied not released
		[Test]
		public void F1_Occupied_NotReleasedByDecision()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(current, 11, 0f).Success);
			Assert.IsTrue(board.ConfirmOccupied(current, 11, 0f).Success);
			DecidePair(VisualEast(), 90f);
			Assert.IsTrue(board.TryGetHeld(11, 0f, out CoverReservation held));
			Assert.AreEqual(CoverOccupancy.Occupied, held.State);
			Assert.AreEqual(1, held.CandidateId);
		}

		[Test]
		public void F2_OccupiedSolver_WithoutFlag_StaysCommitted()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			situation.UnitPosition = s_Origin;
			situation.ThreatRepositionAllowed = false;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, decision.Reason);
			Assert.AreEqual(1, decision.SelectedCandidateId);
			Assert.IsFalse(decision.HasDestination);
		}

		[Test]
		public void F3_OccupiedSolver_WithFlag_Repositions()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			situation.UnitPosition = s_Origin;
			situation.ThreatRepositionAllowed = true;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.BetterTacticalPosition, decision.Reason);
			Assert.AreEqual(2, decision.SelectedCandidateId);
			Assert.IsTrue(decision.HasDestination);
		}

		[Test]
		public void F4_OccupiedSolver_FlagWithoutAdvantage_Stays()
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			situation.UnitPosition = s_Origin;
			situation.ThreatRepositionAllowed = true;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current },
				CurrentTacticalPosition.FromCandidate(current, true));
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}
		#endregion

		#region G Isolation
		[Test]
		public void G1_DoesNotIssueMove()
		{
			ThreatDirectionRepositionResult result = DecidePair(VisualEast(), 90f);
			Assert.AreEqual(ThreatDirectionRepositionKind.RepositionAllowed, result.Kind);
		}

		[Test]
		public void G2_SearchUntouched()
		{
			Assert.IsFalse(TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false));
		}

		[Test]
		public void G3_AcquireUnchanged()
		{
			Assert.AreEqual(0.6f, TacticalArrivalMath.DefaultAcquireToleranceMeters, 0.0001f);
		}

		[Test]
		public void G4_DoesNotChangeAiStateOrFire()
		{
			var go = new GameObject("ThreatDirectionReposition_Idle");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatReorientation.Reset();
				ai.ThreatReposition.Reset();
				ai.ThreatDirection.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
				ai.ThreatDirection.ApplyHostileVisible(s_Origin, s_EastPoint, 1f);
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
		public void G5_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			int changes = readiness.Context.ChangeCount;
			DecidePair(VisualEast(), 90f);
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(changes, readiness.Context.ChangeCount);
		}

		[Test]
		public void G6_CoverScoreUnchanged()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation north = IsolatedEqualLook(ExpectedNorth());
			CoverSituation east = IsolatedEqualLook(VisualEast());
			Assert.AreEqual(
				CoverScoreMath.EvaluateOne(cover, in north, null).Score,
				CoverScoreMath.EvaluateOne(cover, in east, null).Score,
				0.0001f);
			DecidePair(VisualEast(), 90f);
			Assert.AreEqual(
				CoverScoreMath.EvaluateOne(cover, in north, null).Score,
				CoverScoreMath.EvaluateOne(cover, in east, null).Score,
				0.0001f);
		}

		[Test]
		public void G7_RepositionRecommended_StillCoverScore()
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
		public void G8_EmptyTicks_NoDecisionSpam()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			controller.TryGetThreatDirection(out ThreatDirectionKnowledge expected);
			CoverSituation situation = IsolatedEqualLook(expected);
			decision.Evaluate(expected, current, new[] { current }, in situation, 0f);
			int logs = decision.LogCount;
			controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
			controller.Tick(2f, s_Origin, AIPerceptionFrame.Empty);
			controller.TryGetThreatDirection(out expected);
			decision.Evaluate(expected, current, new[] { current }, in situation, 0f);
			decision.Evaluate(expected, current, new[] { current }, in situation, 0f);
			Assert.AreEqual(logs, decision.LogCount);
		}

		[Test]
		public void G9_WeakSound_DoesNotOverrideVisualNorth()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			controller.ApplyHostileVisible(s_Origin, s_NorthPoint, 1f);
			Assert.IsFalse(controller.ApplyGunshot(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			controller.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge);
			Assert.AreNotEqual(
				ThreatDirectionRepositionKind.RepositionAllowed,
				DecidePair(knowledge, 0f).Kind);
		}

		[Test]
		public void G10_VisualEast_OverridesAndAllows()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			controller.ApplyHostileVisible(s_Origin, s_EastPoint, 1f);
			controller.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge);
			Assert.AreEqual(ThreatDirectionCompass.East, knowledge.Compass);
			Assert.AreEqual(
				ThreatDirectionRepositionKind.RepositionAllowed,
				DecidePair(knowledge, 90f).Kind);
		}
		#endregion

		#region H Live wiring
		[Test]
		public void H1_LiveTick_SetsPermissionWhenOccupiedPoor()
		{
			var go = new GameObject("ThreatDirectionReposition_Live");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatReorientation.Reset();
				ai.ThreatReposition.Reset();
				CoverCandidate current = StandingCover(1, s_Origin, s_North);
				CoverCandidate other = StandingCover(2, s_Origin, s_East);
				ai.ThreatDirection.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
				ai.ThreatDirection.TryGetThreatDirection(out ThreatDirectionKnowledge north);
				ai.ThreatReorientation.Observe(north, current, 0f);
				ai.ThreatDirection.ApplyHostileVisible(s_Origin, s_EastPoint, 1f);
				ai.ThreatDirection.TryGetThreatDirection(out ThreatDirectionKnowledge east);
				CoverSituation situation = IsolatedEqualLook(east);
				ai.ThreatReposition.Evaluate(
					east,
					current,
					new[] { current, other },
					in situation,
					90f);
				Assert.IsTrue(ai.ThreatRepositionAllowed);
				situation.ThreatRepositionAllowed = ai.ThreatRepositionAllowed;
				situation.UnitPosition = s_Origin;
				TacticalCoverDecision cover = new TacticalCoverSolver().Decide(
					in situation,
					new[] { current, other },
					CurrentTacticalPosition.FromCandidate(current, true));
				Assert.AreEqual(TacticalCoverDecisionKind.Reposition, cover.Decision);
				Assert.AreEqual(2, cover.SelectedCandidateId);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void H2_LiveTick_NorthEast_NoRepositionRequest()
		{
			var go = new GameObject("ThreatDirectionReposition_NE");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatReposition.Reset();
				CoverCandidate current = StandingCover(1, s_Origin, s_North);
				CoverCandidate other = StandingCover(2, s_Origin, s_East);
				CoverSituation situation = IsolatedEqualLook(VisualNorthEast());
				ai.ThreatReposition.Evaluate(
					VisualNorthEast(),
					current,
					new[] { current, other },
					in situation,
					45f);
				Assert.IsFalse(ai.ThreatRepositionAllowed);
				Assert.IsFalse(ai.HasTacticalRepositionRequest);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void H3_Channel()
		{
			Assert.AreEqual("THREAT_REPOSITION", ThreatDirectionRepositionLog.Channel);
			Assert.AreEqual(UnitActionLog.ThreatReposition, ThreatDirectionRepositionLog.Channel);
		}

		[Test]
		public void H4_Payload()
		{
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			DecideOn(decision, VisualEast(), 90f);
			Assert.IsTrue(decision.LastPayload.IndexOf("kind=RepositionAllowed", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(decision.LastPayload.IndexOf("fit=Poor", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(decision.LastPayload.IndexOf("current=C1", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(decision.LastPayload.IndexOf("best=C2", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(decision.LastPayload.IndexOf("delta=90", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void H5_FaceOnlyPayload()
		{
			ThreatDirectionReposition decision = new ThreatDirectionReposition();
			DecideOn(decision, VisualNorthEast(), 45f);
			Assert.IsTrue(decision.LastPayload.IndexOf("kind=FaceOnly", StringComparison.Ordinal) >= 0);
		}
		#endregion

		#region Helpers
		private static ThreatDirectionRepositionResult DecidePair(
			ThreatDirectionKnowledge _knowledge,
			float _angle)
		{
			return DecideOn(new ThreatDirectionReposition(), _knowledge, _angle);
		}

		private static ThreatDirectionRepositionResult DecideOn(
			ThreatDirectionReposition _decision,
			ThreatDirectionKnowledge _knowledge,
			float _angle)
		{
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			if (_knowledge.Compass == ThreatDirectionCompass.South)
				other = StandingCover(2, s_Origin, s_South);
			CoverSituation situation = IsolatedEqualLook(_knowledge);
			situation.UnitPosition = s_Origin;
			return _decision.Evaluate(
				_knowledge,
				current,
				new[] { current, other },
				in situation,
				_angle);
		}

		private static CoverSituation IsolatedEqualLook(ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = Isolated(_knowledge);
			situation.SectorForward = s_NorthEast;
			return situation;
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

		private static ThreatDirectionKnowledge ExpectedNorth()
		{
			return DirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
		}

		private static ThreatDirectionKnowledge VisualEast()
		{
			return DirectionKnowledge(
				s_East,
				ThreatDirectionCompass.East,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge VisualSouth()
		{
			return DirectionKnowledge(
				s_South,
				ThreatDirectionCompass.South,
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

		private static ThreatDirectionKnowledge WeakEast()
		{
			return new ThreatDirectionKnowledge(
				s_East,
				ThreatDirectionCompass.East,
				0.2f,
				ThreatDirectionMath.SoundUncertaintyDegrees,
				0f,
				ThreatDirectionSource.Sound,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge SlightNorth(float _degrees)
		{
			Vector3 slight = new Vector3(
				Mathf.Sin(_degrees * Mathf.Deg2Rad),
				0f,
				Mathf.Cos(_degrees * Mathf.Deg2Rad));
			return new ThreatDirectionKnowledge(
				slight,
				ThreatDirectionCompass.North,
				ThreatDirectionMath.VisualConfidence,
				ThreatDirectionMath.VisualUncertaintyDegrees,
				0f,
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
				StandingProfile = new CoverProtectionProfile
				{
					Head = 1f,
					Torso = 1f,
					Pelvis = 1f,
					Legs = 1f
				},
				CrouchProfile = new CoverProtectionProfile
				{
					Head = 1f,
					Torso = 1f,
					Pelvis = 1f,
					Legs = 1f
				},
				GeometryVersion = 1
			};
		}
		#endregion
	}
}
