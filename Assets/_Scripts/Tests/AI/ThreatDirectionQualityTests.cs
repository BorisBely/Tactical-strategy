using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C.2 Threat Direction confidence / uncertainty. Weights cover and facing. No new scan.
	/// </summary>
	[Category("ThreatDirectionQuality")]
	public sealed class ThreatDirectionQualityTests
	{
		#region Constants
		private static readonly Vector3 s_Origin = Vector3.zero;
		private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
		private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);
		private static readonly Vector3 s_NorthEastPoint = new Vector3(10f, 0f, 10f);
		private static readonly Vector3 s_North = Vector3.forward;
		private static readonly Vector3 s_East = Vector3.right;
		private static readonly Vector3 s_South = Vector3.back;
		private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
		#endregion

		#region A Source quality
		[Test]
		public void A1_VisualConfidence_GreaterThanSound()
		{
			Assert.Greater(ThreatDirectionMath.VisualConfidence, ThreatDirectionMath.SoundConfidence);
		}

		[Test]
		public void A2_SoundConfidence_GreaterThanReport()
		{
			Assert.Greater(ThreatDirectionMath.SoundConfidence, ThreatDirectionMath.ReportConfidence);
		}

		[Test]
		public void A3_ReportConfidence_GreaterThanExpected()
		{
			Assert.Greater(ThreatDirectionMath.ReportConfidence, ThreatDirectionMath.ExpectedConfidence);
		}

		[Test]
		public void A4_VisualUncertainty_LessThanSound()
		{
			Assert.Less(ThreatDirectionMath.VisualUncertaintyDegrees, ThreatDirectionMath.SoundUncertaintyDegrees);
		}

		[Test]
		public void A5_SoundUncertainty_LessThanReport()
		{
			Assert.Less(ThreatDirectionMath.SoundUncertaintyDegrees, ThreatDirectionMath.ReportUncertaintyDegrees);
		}

		[Test]
		public void A6_ReportUncertainty_LessThanExpected()
		{
			Assert.Less(ThreatDirectionMath.ReportUncertaintyDegrees, ThreatDirectionMath.ExpectedUncertaintyDegrees);
		}

		[Test]
		public void A7_HigherConfidence_LowerUncertainty_AcrossSources()
		{
			ThreatDirectionMath.BaseQuality(ThreatDirectionSource.Visual, out float visualC, out float visualU);
			ThreatDirectionMath.BaseQuality(ThreatDirectionSource.Sound, out float soundC, out float soundU);
			ThreatDirectionMath.BaseQuality(ThreatDirectionSource.AllyReport, out float reportC, out float reportU);
			ThreatDirectionMath.BaseQuality(ThreatDirectionSource.InitialEstimate, out float expectedC, out float expectedU);
			Assert.Greater(visualC, soundC);
			Assert.Less(visualU, soundU);
			Assert.Greater(soundC, reportC);
			Assert.Less(soundU, reportU);
			Assert.Greater(reportC, expectedC);
			Assert.Less(reportU, expectedU);
		}

		[Test]
		public void A8_SourceRank_VisualThenSoundThenReportThenInitial()
		{
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Visual),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Sound));
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Sound),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.AllyReport));
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.AllyReport),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Expected, ThreatDirectionSource.InitialEstimate));
		}
		#endregion

		#region B Aging
		[Test]
		public void B1_Known_NotImmediatelyNone()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			Assert.AreEqual(ThreatDirectionState.Stale, controller.CurrentState);
			Assert.AreNotEqual(ThreatDirectionState.None, controller.CurrentState);
		}

		[Test]
		public void B2_Stale_ConfidenceLowerThanKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			float known = controller.GetThreatConfidence();
			controller.ApplyHostileLost(2f);
			Assert.Less(controller.GetThreatConfidence(), known);
		}

		[Test]
		public void B3_Stale_UncertaintyHigherThanKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			float known = controller.GetThreatUncertainty();
			controller.ApplyHostileLost(2f);
			Assert.Greater(controller.GetThreatUncertainty(), known);
		}

		[Test]
		public void B4_Stale_ConfidenceDecaysOverTime()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			float early = controller.GetThreatConfidence();
			controller.Tick(5f);
			Assert.Less(controller.GetThreatConfidence(), early);
		}

		[Test]
		public void B5_Stale_UncertaintyGrowsOverTime()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			float early = controller.GetThreatUncertainty();
			controller.Tick(5f);
			Assert.Greater(controller.GetThreatUncertainty(), early);
		}

		[Test]
		public void B6_Stale_ToNone_WithoutEstimate()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			controller.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
			Assert.AreEqual(ThreatDirectionState.None, controller.CurrentState);
		}

		[Test]
		public void B7_Expected_DoesNotExpire()
		{
			ThreatDirectionController controller = StartedNorth();
			float conf = controller.GetThreatConfidence();
			float unc = controller.GetThreatUncertainty();
			controller.Tick(120f);
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
			Assert.AreEqual(conf, controller.GetThreatConfidence(), 0.001f);
			Assert.AreEqual(unc, controller.GetThreatUncertainty(), 0.001f);
		}

		[Test]
		public void B8_Expected_IsFallbackAfterActualExpires()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			controller.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.AreEqual(ThreatDirectionMath.ExpectedConfidence, controller.GetThreatConfidence(), 0.001f);
		}
		#endregion

		#region C Override chain
		[Test]
		public void C1_ExpectedThenSoundThenVisual_EndsNorthEast()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.IsTrue(controller.ApplyGunshot(s_Origin, s_EastPoint, 1f));
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
			Assert.IsTrue(controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
			Assert.AreEqual(ThreatDirectionSource.Visual, controller.CurrentSource);
		}

		[Test]
		public void C2_Visual_HasHighestLiveConfidence()
		{
			ThreatDirectionController controller = StartedNorth();
			float expected = controller.GetThreatConfidence();
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			float sound = controller.GetThreatConfidence();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f);
			float visual = controller.GetThreatConfidence();
			Assert.Greater(sound, expected);
			Assert.Greater(visual, sound);
		}

		[Test]
		public void C3_SoundIgnored_WhileVisualKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.IsFalse(controller.ApplyGunshot(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}
		#endregion

		#region D Cover weighting
		[Test]
		public void D1_HighConfidence_StrongerCoverAdjustmentThanExpected()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			float expectedAdj = StampedAdjustment(cover, ExpectedNorth());
			float visualAdj = StampedAdjustment(cover, VisualNorthEastKnowledge(s_North));
			Assert.Greater(Mathf.Abs(visualAdj), Mathf.Abs(expectedAdj));
		}

		[Test]
		public void D2_LowConfidence_StillPrefersCorrectCoverButWeaker()
		{
			CoverEvaluationResult expected = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				ExpectedNorth());
			CoverEvaluationResult visual = EvaluatePair(
				StandingCover(1, s_CoverPos, s_North),
				StandingCover(2, s_CoverPos, s_South),
				VisualNorth());
			Assert.AreEqual(1, expected.Best.Candidate.CandidateId);
			Assert.AreEqual(1, visual.Best.Candidate.CandidateId);
			Assert.Less(Mathf.Abs(expected.Best.ThreatDirectionAdjustment),
				Mathf.Abs(visual.Best.ThreatDirectionAdjustment));
		}

		[Test]
		public void D3_CoverScore_UnchangedByConfidence()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation expected = Isolated(ExpectedNorth());
			CoverSituation visual = Isolated(VisualNorth());
			Assert.AreEqual(
				CoverScoreMath.EvaluateOne(cover, in expected, null).Score,
				CoverScoreMath.EvaluateOne(cover, in visual, null).Score,
				0.0001f);
		}

		[Test]
		public void D4_CoverInfluence_HighGreaterThanLow()
		{
			Assert.Greater(
				ThreatDirectionMath.CoverInfluence(ThreatDirectionMath.VisualConfidence),
				ThreatDirectionMath.CoverInfluence(ThreatDirectionMath.ExpectedConfidence));
			Assert.Greater(
				ThreatDirectionMath.CoverInfluence(0.9f),
				ThreatDirectionMath.CoverInfluence(0.35f));
		}

		[Test]
		public void D5_Occupied_QualityChange_DoesNotForceReposition()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, s_North);
			CoverCandidate other = StandingCover(2, s_CoverPos, s_South);
			CoverSituation expected = Isolated(ExpectedNorth());
			expected.UnitPosition = Vector3.zero;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.Decide(in expected, new[] { current, other }, in occupying);
			CoverSituation visual = Isolated(VisualNorth());
			visual.UnitPosition = Vector3.zero;
			TacticalCoverDecision second = solver.Decide(in visual, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, first.Decision);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(1, second.SelectedCandidateId);
			Assert.IsFalse(second.HasDestination);
		}
		#endregion

		#region E Facing weighting
		[Test]
		public void E1_FacingSlack_VisualTighterThanExpected()
		{
			Assert.Less(
				ThreatDirectionFacingController.FacingSlackDegrees(VisualNorth()),
				ThreatDirectionFacingController.FacingSlackDegrees(ExpectedNorth()));
		}

		[Test]
		public void E2_LowConfidence_TwentyDegreeChange_NoFacingUpdate()
		{
			var facing = new ThreatDirectionFacingController();
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			ThreatDirectionKnowledge slight = DirectionKnowledge(
				new Vector3(Mathf.Sin(20f * Mathf.Deg2Rad), 0f, Mathf.Cos(20f * Mathf.Deg2Rad)),
				ThreatDirectionCompass.North,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected);
			Assert.IsFalse(facing.Notify(
				in slight,
				ThreatDirectionFacingReason.ThreatDirectionChanged,
				0f));
		}

		[Test]
		public void E3_HighConfidence_LargeChange_UpdatesFacing()
		{
			var facing = new ThreatDirectionFacingController();
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			Assert.IsTrue(facing.Notify(
				VisualNorthEast(),
				ThreatDirectionFacingReason.ThreatDirectionChanged,
				0f));
		}

		[Test]
		public void E4_Facing_StaysSectorCenter()
		{
			var facing = new ThreatDirectionFacingController();
			ThreatDirectionKnowledge expected = ExpectedNorth();
			facing.Notify(in expected, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
			Assert.AreEqual(expected.Direction.z, facing.DesiredFacing.z, 0.001f);
		}
		#endregion

		#region F Events / isolation
		[Test]
		public void F1_NoPolling_EnemyMovedWithoutEvent()
		{
			ThreatDirectionController controller = StartedNorth();
			Vector3 before = controller.GetThreatDirection();
			int logs = controller.LogCount;
			controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
			controller.Tick(2f, s_Origin, AIPerceptionFrame.Empty);
			Assert.AreEqual(before, controller.GetThreatDirection());
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.AreEqual(logs, controller.LogCount);
		}

		[Test]
		public void F2_ExpectedTicks_DoNotSpamQualityLog()
		{
			ThreatDirectionController controller = StartedNorth();
			int quality = controller.QualityLogCount;
			controller.Tick(1f);
			controller.Tick(2f);
			controller.Tick(3f);
			Assert.AreEqual(quality, controller.QualityLogCount);
		}

		[Test]
		public void F3_StaleAging_EmitsQualityLogNotEveryFrame()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			int afterLost = controller.QualityLogCount;
			int directionLogs = controller.LogCount;
			controller.Tick(2.05f);
			controller.Tick(2.10f);
			controller.Tick(2.15f);
			Assert.AreEqual(afterLost, controller.QualityLogCount);
			Assert.AreEqual(directionLogs, controller.LogCount);
			controller.Tick(6f);
			Assert.Greater(controller.QualityLogCount, afterLost);
			Assert.AreEqual(directionLogs, controller.LogCount);
		}

		[Test]
		public void F4_UpdateChannel()
		{
			Assert.AreEqual("THREAT_DIRECTION_UPDATE", ThreatDirectionLog.UpdateChannel);
			Assert.AreEqual(UnitActionLog.ThreatDirectionUpdate, ThreatDirectionLog.UpdateChannel);
		}

		[Test]
		public void F5_QualityPayload_HasConfidenceAndUncertainty()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.LastQualityPayload.IndexOf("source=Initial", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastQualityPayload.IndexOf("state=Expected", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastQualityPayload.IndexOf("confidence=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastQualityPayload.IndexOf("uncertainty=", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void F6_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			int changes = readiness.Context.ChangeCount;
			ThreatDirectionController threat = StartedNorth();
			threat.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			threat.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f);
			threat.ApplyHostileLost(3f);
			threat.Tick(5f);
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(changes, readiness.Context.ChangeCount);
		}

		[Test]
		public void F7_DoesNotChangeAiStateOrFire()
		{
			var go = new GameObject("ThreatDirectionQuality_Idle");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatFacing.Reset();
				ai.ThreatDirection.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
				ai.ThreatDirection.ApplyGunshot(s_Origin, s_EastPoint, 1f);
				ai.ThreatDirection.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f);
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
		public void F8_SearchUntouched()
		{
			Assert.IsFalse(TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false));
		}

		[Test]
		public void F9_AcquireUnchanged()
		{
			Assert.AreEqual(0.6f, TacticalArrivalMath.DefaultAcquireToleranceMeters, 0.0001f);
		}

		[Test]
		public void F10_Bind_CopiesConfidenceAndUncertainty()
		{
			CoverSituation situation = Isolated(ExpectedNorth());
			Assert.AreEqual(ThreatDirectionMath.ExpectedConfidence, situation.ThreatConfidence, 0.001f);
			Assert.AreEqual(ThreatDirectionMath.ExpectedUncertaintyDegrees, situation.ThreatUncertaintyDegrees, 0.001f);
			Assert.IsFalse(situation.HasTarget);
		}
		#endregion

		#region Helpers
		private static ThreatDirectionController StartedNorth()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			return controller;
		}

		private static float StampedAdjustment(CoverCandidate _cover, ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = Isolated(_knowledge);
			return ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(_cover, in situation, null),
				in situation).ThreatDirectionAdjustment;
		}

		private static CoverEvaluationResult EvaluatePair(
			CoverCandidate _a,
			CoverCandidate _b,
			ThreatDirectionKnowledge _knowledge)
		{
			CoverSituation situation = Isolated(_knowledge);
			return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
		}

		private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge)
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

		private static ThreatDirectionKnowledge ExpectedNorth()
		{
			return DirectionKnowledge(
				s_North,
				ThreatDirectionCompass.North,
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

		private static ThreatDirectionKnowledge VisualNorthEast()
		{
			return DirectionKnowledge(
				new Vector3(1f, 0f, 1f).normalized,
				ThreatDirectionCompass.NorthEast,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static ThreatDirectionKnowledge VisualNorthEastKnowledge(Vector3 _direction)
		{
			return DirectionKnowledge(
				_direction,
				ThreatDirectionEstimator.CompassFrom(_direction),
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
