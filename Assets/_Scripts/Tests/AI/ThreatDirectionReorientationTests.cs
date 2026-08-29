using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C.4 Dynamic Threat Reorientation. Facing + ThreatFit. No Move / Release / scan.
	/// </summary>
	[Category("ThreatDirectionReorientation")]
	public sealed class ThreatDirectionReorientationTests
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

		#region A Deadband
		[Test]
		public void A1_FiveDegrees_NotSignificant()
		{
			Assert.IsFalse(ThreatDirectionReorientationMath.IsSignificantChange(
				s_North,
				Slight(5f),
				ThreatDirectionMath.VisualConfidence));
		}

		[Test]
		public void A2_FiveDegrees_NoTacticalChange()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			ThreatDirectionReorientationResult result = reorient.Observe(SlightNorth(5f));
			Assert.IsFalse(result.TacticalChanged);
			Assert.AreEqual(0, reorient.ChangeCount);
		}

		[Test]
		public void A3_FiveDegrees_NoFacingUpdate()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			int facing = reorient.Facing.UpdateCount;
			Assert.IsFalse(reorient.Observe(SlightNorth(5f)).FacingUpdated);
			Assert.AreEqual(facing, reorient.Facing.UpdateCount);
		}

		[Test]
		public void A4_SmallYawOscillation_NoSpam()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			int changes = reorient.ChangeCount;
			int facingLogs = reorient.FacingLogCount;
			reorient.Observe(SlightNorth(4f));
			reorient.Observe(SlightNorth(7f));
			reorient.Observe(SlightNorth(3f));
			Assert.AreEqual(changes, reorient.ChangeCount);
			Assert.AreEqual(facingLogs, reorient.FacingLogCount);
		}
		#endregion

		#region B Significant
		[Test]
		public void B1_NorthToEast_IsSignificant()
		{
			Assert.IsTrue(ThreatDirectionReorientationMath.IsSignificantChange(
				s_North,
				s_East,
				ThreatDirectionMath.VisualConfidence));
		}

		[Test]
		public void B2_NorthToSouth_IsSignificant()
		{
			Assert.IsTrue(ThreatDirectionReorientationMath.IsSignificantChange(
				s_North,
				s_South,
				ThreatDirectionMath.VisualConfidence));
		}

		[Test]
		public void B3_NorthToNorthEast_IsCorrection()
		{
			Assert.IsFalse(ThreatDirectionReorientationMath.IsSignificantChange(
				s_North,
				s_NorthEast,
				ThreatDirectionMath.VisualConfidence));
			ThreatDirectionReorientation reorient = SeededNorth();
			Assert.IsFalse(reorient.Observe(VisualNorthEast()).TacticalChanged);
		}

		[Test]
		public void B4_NorthToEast_EmitsTacticalChange()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			ThreatDirectionReorientationResult result = reorient.Observe(VisualEast());
			Assert.IsTrue(result.TacticalChanged);
			Assert.AreEqual(1, reorient.ChangeCount);
			Assert.Greater(result.AngleDeltaDegrees, 80f);
		}
		#endregion

		#region C Confidence
		[Test]
		public void C1_LowConfidenceEast_NoTacticalReaction()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			ThreatDirectionReorientationResult result = reorient.Observe(WeakEast());
			Assert.IsFalse(result.TacticalChanged);
			Assert.IsFalse(result.FacingUpdated);
			Assert.Greater(reorient.Facing.DesiredFacing.z, 0.9f);
		}

		[Test]
		public void C2_HighConfidenceEast_Reacts()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			ThreatDirectionReorientationResult result = reorient.Observe(VisualEast());
			Assert.IsTrue(result.TacticalChanged);
			Assert.IsTrue(result.FacingUpdated);
			Assert.Greater(reorient.Facing.DesiredFacing.x, 0.9f);
		}

		[Test]
		public void C3_LowConfidence_MathRejectsEvenAtNinetyDegrees()
		{
			Assert.IsFalse(ThreatDirectionReorientationMath.IsSignificantChange(s_North, s_East, 0.2f));
			Assert.IsTrue(ThreatDirectionReorientationMath.IsSignificantChange(
				s_North,
				s_East,
				ThreatDirectionMath.VisualConfidence));
		}
		#endregion

		#region D Facing
		[Test]
		public void D1_SignificantChange_UpdatesDesiredFacing()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			Assert.IsTrue(reorient.Observe(VisualEast()).FacingUpdated);
			Assert.Greater(reorient.Facing.DesiredFacing.x, 0.9f);
		}

		[Test]
		public void D2_NorthToSouth_FacingSouth()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualSouth());
			Assert.Less(reorient.Facing.DesiredFacing.z, -0.9f);
		}

		[Test]
		public void D3_NorthEastCorrection_StillUpdatesFacing()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			ThreatDirectionReorientationResult result = reorient.Observe(VisualNorthEast());
			Assert.IsFalse(result.TacticalChanged);
			Assert.IsTrue(result.FacingUpdated);
			Assert.Greater(reorient.Facing.DesiredFacing.x, 0.5f);
		}

		[Test]
		public void D4_FacingUpdatePayload()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast());
			Assert.IsTrue(reorient.LastFacingPayload.IndexOf("from=N", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastFacingPayload.IndexOf("to=E", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastFacingPayload.IndexOf(
				"reason=ThreatDirectionChanged",
				StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void D5_NorthEastThenEast_FiresTacticalChange()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualNorthEast());
			Assert.AreEqual(0, reorient.ChangeCount);
			Assert.IsTrue(reorient.Observe(VisualEast()).TacticalChanged);
			Assert.AreEqual(1, reorient.ChangeCount);
		}
		#endregion

		#region E Cover
		[Test]
		public void E1_Occupied_DirectionChange_StaysCommitted()
		{
			CoverCandidate current = StandingCover(3, s_Origin, s_North);
			CoverCandidate other = StandingCover(4, s_CoverPos, s_East);
			CoverSituation north = Isolated(ExpectedNorth());
			north.UnitPosition = s_Origin;
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			solver.Decide(in north, new[] { current, other }, in occupying);
			CoverSituation east = Isolated(VisualEast());
			east.UnitPosition = s_Origin;
			TacticalCoverDecision second = solver.Decide(in east, new[] { current, other }, in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(3, second.SelectedCandidateId);
			Assert.IsFalse(second.HasDestination);
		}

		[Test]
		public void E2_ThreatFit_NorthCover_GoodThenPoor()
		{
			CoverCandidate cover = StandingCover(3, s_Origin, s_North);
			ThreatDirectionReorientation reorient = SeededNorth();
			Assert.AreEqual(CoverThreatFit.Good, reorient.Observe(ExpectedNorth(), cover).ThreatFit);
			ThreatDirectionReorientationResult east = reorient.Observe(VisualEast(), cover);
			Assert.AreEqual(CoverThreatFit.Poor, east.ThreatFit);
			Assert.IsTrue(east.ThreatFitChanged);
		}

		[Test]
		public void E3_ThreatFitChange_DoesNotInvalidateCover()
		{
			CoverCandidate cover = StandingCover(3, s_Origin, s_North);
			Assert.IsTrue(CoverScoreMath.IsSelectable(cover));
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast(), cover);
			Assert.IsTrue(CoverScoreMath.IsSelectable(cover));
			Assert.AreEqual(CoverType.Standing, cover.CoverType);
		}

		[Test]
		public void E4_FitPayload()
		{
			CoverCandidate cover = StandingCover(3, s_Origin, s_North);
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast(), cover);
			Assert.IsTrue(reorient.LastFitPayload.IndexOf("cover=C3", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastFitPayload.IndexOf("direction=E", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastFitPayload.IndexOf("fit=Poor", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void E5_Observe_DoesNotIssueMove()
		{
			CoverCandidate cover = StandingCover(3, s_Origin, s_North);
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast(), cover);
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				Isolated(VisualEast()),
				new[] { cover, StandingCover(4, s_CoverPos, s_East) },
				CurrentTacticalPosition.FromCandidate(cover, true));
			Assert.IsFalse(decision.HasDestination);
		}
		#endregion

		#region F Isolation
		[Test]
		public void F1_SearchUntouched()
		{
			Assert.IsFalse(TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false));
		}

		[Test]
		public void F2_AcquireUnchanged()
		{
			Assert.AreEqual(0.6f, TacticalArrivalMath.DefaultAcquireToleranceMeters, 0.0001f);
		}

		[Test]
		public void F3_DoesNotChangeAiStateOrFire()
		{
			var go = new GameObject("ThreatDirectionReorientation_Idle");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatReorientation.Reset();
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
		public void F4_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			int changes = readiness.Context.ChangeCount;
			SeededNorth().Observe(VisualEast());
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(changes, readiness.Context.ChangeCount);
		}

		[Test]
		public void F5_EmptyTicks_NoReorientation()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			var reorient = new ThreatDirectionReorientation(new ThreatDirectionFacingController());
			reorient.Observe(Snapshot(controller));
			int changes = reorient.ChangeCount;
			int facing = reorient.Facing.UpdateCount;
			controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
			controller.Tick(2f, s_Origin, AIPerceptionFrame.Empty);
			reorient.Observe(Snapshot(controller));
			reorient.Observe(Snapshot(controller));
			Assert.AreEqual(changes, reorient.ChangeCount);
			Assert.AreEqual(facing, reorient.Facing.UpdateCount);
		}

		[Test]
		public void F6_WeakSound_DoesNotOverrideVisual()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			controller.ApplyHostileVisible(s_Origin, s_NorthPoint, 1f);
			Assert.IsFalse(controller.ApplyGunshot(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
		}

		[Test]
		public void F7_VisualEast_OverridesNorth()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			controller.ApplyHostileVisible(s_Origin, s_NorthPoint, 1f);
			Assert.IsTrue(controller.ApplyHostileVisible(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}

		[Test]
		public void F8_LiveTick_SetsFacingFromExpected()
		{
			var go = new GameObject("ThreatDirectionReorientation_Facing");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ThreatDirection.Reset();
				ai.ThreatReorientation.Reset();
				Assert.IsTrue(ai.ThreatDirection.ApplyBattleStart(s_Origin, s_NorthPoint, 0f));
				ai.Tick(0f);
				Assert.IsTrue(ai.ThreatFacing.HasDesiredFacing);
				Assert.Greater(ai.ThreatFacing.DesiredFacing.z, 0.9f);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region G Fatigue
		[Test]
		public void G1_HighFatigue_LongerTurn()
		{
			ArmFatigueProfile profile = ArmFatigueProfile.PlayPrototype();
			Assert.Greater(
				ThreatDirectionReorientationMath.TurnDuration(1f, in profile),
				ThreatDirectionReorientationMath.TurnDuration(0f, in profile));
		}

		[Test]
		public void G2_Fatigue_DoesNotChangeDesiredFacing()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast());
			Vector3 facing = reorient.Facing.DesiredFacing;
			ArmFatigueProfile profile = ArmFatigueProfile.PlayPrototype();
			Assert.Greater(
				ThreatDirectionReorientationMath.TurnDuration(1f, in profile),
				ThreatDirectionReorientationMath.TurnDuration(0f, in profile));
			Assert.AreEqual(facing.x, reorient.Facing.DesiredFacing.x, 0.0001f);
			Assert.AreEqual(facing.z, reorient.Facing.DesiredFacing.z, 0.0001f);
		}

		[Test]
		public void G3_TurnDuration_UsesArmFatigueMath()
		{
			ArmFatigueProfile profile = ArmFatigueProfile.PlayPrototype();
			Assert.AreEqual(
				ArmFatigueMath.FinalTurnToTargetTime(0.6f, in profile),
				ThreatDirectionReorientationMath.TurnDuration(0.6f, in profile),
				0.0001f);
		}
		#endregion

		#region H Logs
		[Test]
		public void H1_ChangedChannel()
		{
			Assert.AreEqual("THREAT_DIRECTION_CHANGED", ThreatDirectionReorientationLog.ChangedChannel);
			Assert.AreEqual(UnitActionLog.ThreatDirectionChanged, ThreatDirectionReorientationLog.ChangedChannel);
		}

		[Test]
		public void H2_FacingAndFitChannels()
		{
			Assert.AreEqual("FACING_UPDATE", ThreatDirectionReorientationLog.FacingChannel);
			Assert.AreEqual("COVER_THREAT_FIT", ThreatDirectionReorientationLog.FitChannel);
		}

		[Test]
		public void H3_ChangedPayload()
		{
			ThreatDirectionReorientation reorient = SeededNorth();
			reorient.Observe(VisualEast());
			Assert.IsTrue(reorient.LastChangePayload.IndexOf("from=N", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastChangePayload.IndexOf("to=E", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastChangePayload.IndexOf("confidence=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(reorient.LastChangePayload.IndexOf("delta=", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void H4_CoverScoreUnchangedByReorientation()
		{
			CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
			CoverSituation north = Isolated(ExpectedNorth());
			CoverSituation east = Isolated(VisualEast());
			Assert.AreEqual(
				CoverScoreMath.EvaluateOne(cover, in north, null).Score,
				CoverScoreMath.EvaluateOne(cover, in east, null).Score,
				0.0001f);
		}
		#endregion

		#region Helpers
		private static ThreatDirectionReorientation SeededNorth()
		{
			var reorient = new ThreatDirectionReorientation(new ThreatDirectionFacingController());
			reorient.Observe(ExpectedNorth());
			return reorient;
		}

		private static ThreatDirectionKnowledge Snapshot(ThreatDirectionController _controller)
		{
			_controller.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge);
			return knowledge;
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
			Vector3 slight = Slight(_degrees);
			return new ThreatDirectionKnowledge(
				slight,
				ThreatDirectionCompass.North,
				ThreatDirectionMath.VisualConfidence,
				ThreatDirectionMath.VisualUncertaintyDegrees,
				0f,
				ThreatDirectionSource.Visual,
				ThreatDirectionState.Known);
		}

		private static Vector3 Slight(float _degrees)
		{
			return new Vector3(
				Mathf.Sin(_degrees * Mathf.Deg2Rad),
				0f,
				Mathf.Cos(_degrees * Mathf.Deg2Rad));
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
