using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.5 Tactical Cover / Position Switching. Stay / Reposition. Not Fire. Not Move.
	/// </summary>
	public sealed class DynamicCoverTacticalTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				_destination.Add(StandingCover(1, _bounds.center, Vector3.forward));
			}
		}
		#endregion

		#region A Stay — improvement too small
		[Test]
		public void A_SlightlyBetter_Stay()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(
				8f, 8.1f, 1f, true, 1, 2);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.ImprovementTooSmall, decision.Reason);
			Assert.AreEqual(1, decision.SelectedCandidateId);
			Assert.AreEqual(2, decision.BestCandidateId);
			Assert.IsFalse(decision.HasDestination);
		}
		#endregion

		#region B Switch — substantially better
		[Test]
		public void B_SubstantiallyBetter_Reposition()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(
				8f, 10f, 1f, true, 1, 2);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.BetterTacticalPosition, decision.Reason);
			Assert.AreEqual(2, decision.SelectedCandidateId);
			Assert.AreEqual(2, decision.BestCandidateId);
			Assert.IsTrue(decision.HasDestination);
		}
		#endregion

		#region C Equal scores stay
		[Test]
		public void C_EqualScores_Stay()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(
				8f, 8f, 1f, true, 1, 2);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.IsFalse(CoverSwitchMath.ShouldReposition(8f, 8f, 1f));
		}
		#endregion

		#region D Hysteresis
		[Test]
		public void D_StayStayThenSwitch()
		{
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.DecideFromScores(8f, 8.2f, 1f, true, 1, 2);
			TacticalCoverDecision second = solver.DecideFromScores(8f, 8.2f, 1f, true, 1, 2);
			TacticalCoverDecision third = solver.DecideFromScores(8f, 9.5f, 1f, true, 1, 2);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, first.Decision);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, third.Decision);
			Assert.AreEqual(TacticalCoverReason.BetterTacticalPosition, third.Reason);
		}
		#endregion

		#region E Current invalid
		[Test]
		public void E_CurrentInvalid_MustSelectNew()
		{
			CoverCandidate next = StandingCover(7, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { next },
				CurrentTacticalPosition.Invalid);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.CurrentInvalid, decision.Reason);
			Assert.AreEqual(7, decision.SelectedCandidateId);
			Assert.IsTrue(decision.HasDestination);
			Assert.AreEqual(next.Position, decision.Destination);
		}
		#endregion

		#region F GeometryVersion
		[Test]
		public void F_GeometryVersionChange_Reevaluates()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(cache);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(1, overlay.Solver.DecideCount);
			Assert.AreEqual(1, cache.GenerationCount);
			cache.BumpGeometryVersion();
			overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(2, overlay.Solver.DecideCount);
			Assert.AreEqual(2, cache.GenerationCount);
			Assert.AreEqual(2, source.GenerateCount);
		}
		#endregion

		#region G Mission
		[Test]
		public void G_SameSet_DifferentMission_DifferentScores()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(0f, 0f, 10f), Vector3.forward);
			CoverSituation defense = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 30f));
			defense.Mission = CoverMissionIntent.Defense;
			CoverSituation attack = defense;
			attack.Mission = CoverMissionIntent.Attack;
			Assert.AreNotEqual(
				CoverScoreMath.MissionScore(candidate, in defense),
				CoverScoreMath.MissionScore(candidate, in attack));
			TacticalCoverDecision defDecision = new TacticalCoverSolver().Decide(
				in defense,
				new[] { candidate },
				CurrentTacticalPosition.FromCandidate(candidate, true));
			TacticalCoverDecision atkDecision = new TacticalCoverSolver().Decide(
				in attack,
				new[] { candidate },
				CurrentTacticalPosition.FromCandidate(candidate, true));
			Assert.AreNotEqual(defDecision.BestScore, atkDecision.BestScore);
		}
		#endregion

		#region H Target change
		[Test]
		public void H_SignificantTargetChange_Reevaluates()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, Vector3.forward);
			CoverCandidate other = StandingCover(2, new Vector3(6f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			solver.Decide(in situation, new[] { current, other }, in occupying);
			Assert.AreEqual(1, solver.DecideCount);
			situation.TargetPosition = new Vector3(0f, 1.5f, 40f);
			solver.Decide(in situation, new[] { current, other }, in occupying);
			Assert.AreEqual(2, solver.DecideCount);
		}
		#endregion

		#region I Event-only
		[Test]
		public void I_HundredTicksNoEvent_NoRecomputation()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(cache);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(1, overlay.Solver.DecideCount);
			Assert.AreEqual(1, cache.GenerationCount);
			for (int i = 0; i < 100; i++)
				overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(1, overlay.Solver.DecideCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(1, source.GenerateCount);
		}
		#endregion

		#region J Deterministic
		[Test]
		public void J_SameStateHundredTimes_SameSelected()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, Vector3.forward);
			CoverCandidate other = StandingCover(2, new Vector3(5f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			var solver = new TacticalCoverSolver();
			TacticalCoverDecision first = solver.Decide(
				in situation, new[] { current, other }, in occupying);
			int selected = first.SelectedCandidateId;
			for (int i = 0; i < 99; i++)
			{
				TacticalCoverDecision next = solver.Decide(
					in situation, new[] { current, other }, in occupying);
				Assert.AreEqual(selected, next.SelectedCandidateId);
				Assert.IsTrue(next.FromCache);
			}

			Assert.AreEqual(1, solver.DecideCount);
		}
		#endregion

		#region Helpers
		private static CoverSituation RifleAt(Vector3 _unit, Vector3 _target)
		{
			return new CoverSituation
			{
				UnitPosition = _unit,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Hold,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				TargetPosition = _target,
				HasTarget = true,
				SectorForward = Vector3.forward,
				HostileDirection = Vector3.forward,
				GeometryVersion = 1
			};
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
