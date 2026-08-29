using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.4 Emergency Cover overlay. Destination only. Not Fire. Not Move. Not a new UnitAIState.
	/// </summary>
	public sealed class DynamicCoverEmergencyTests
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
				_destination.Add(StandingCover(1, _bounds.center, Vector3.forward, 1f));
			}
		}

		private sealed class ListSource : ICoverCandidateSource
		{
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(8);

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				for (int i = 0; i < Candidates.Count; i++)
					_destination.Add(Candidates[i]);
			}
		}
		#endregion

		#region A Trigger / query
		[Test]
		public void A1_ImmediateThreat_StartsEval()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var overlay = new EmergencyCoverOverlay();
			overlay.BindCache(cache);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			EmergencyCoverDecision decision = overlay.Update(true, UnitAIState.Idle, in situation);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(EmergencyCoverResult.Selected, decision.Result);
			Assert.IsTrue(decision.HasDestination);
			Assert.IsTrue(decision.Active);
		}

		[Test]
		public void A2_NoThreat_NoQuery()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var overlay = new EmergencyCoverOverlay();
			overlay.BindCache(cache);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			EmergencyCoverDecision decision = overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(0, cache.GenerationCount);
			Assert.IsFalse(decision.Active);
			Assert.IsFalse(decision.HasDestination);
		}

		[Test]
		public void A3_RepeatThreatWhileProtected_NoSecondGenerate()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var overlay = new EmergencyCoverOverlay();
			overlay.BindCache(cache);
			Vector3 protectedPos = CoverSpatialMath.RegionBounds(
				cache.RegionAt(Vector3.zero),
				cache.RegionSizeMeters).center;
			CoverSituation situation = RifleAt(protectedPos, protectedPos + new Vector3(0f, 1.5f, 20f));
			EmergencyCoverDecision first = overlay.Update(true, UnitAIState.Idle, in situation);
			Assert.AreEqual(EmergencyCoverResult.Stay, first.Result);
			Assert.AreEqual(1, cache.GenerationCount);
			EmergencyCoverDecision second = overlay.Update(true, UnitAIState.Idle, in situation);
			Assert.AreEqual(EmergencyCoverResult.Stay, second.Result);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(1, source.GenerateCount);
		}

		[Test]
		public void A4_EmergencyScore_NotTacticalTotal()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			float tactical = CoverScoreMath.PositionScore(candidate, in situation, null);
			float emergency = CoverEmergencyScoreMath.Score(candidate, in situation, null);
			Assert.AreNotEqual(tactical, emergency);
		}
		#endregion

		#region B Stay vs search
		[Test]
		public void B1_CurrentProtected_Stay()
		{
			CoverCandidate occupying = StandingCover(3, Vector3.zero, Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			var solver = new EmergencyCoverSolver();
			EmergencyCoverDecision decision = solver.Decide(
				true, UnitAIState.Idle, in situation, null, occupying);
			Assert.AreEqual(EmergencyCoverResult.Stay, decision.Result);
			Assert.AreEqual(EmergencyCoverReason.CurrentCoverSufficient, decision.Reason);
			Assert.IsFalse(decision.HasDestination);
			Assert.IsFalse(solver.ShouldQueryCandidates(
				true, UnitAIState.Idle, in situation, occupying, null, null));
		}

		[Test]
		public void B2_Insufficient_Search()
		{
			CoverCandidate far = StandingCover(2, new Vector3(5f, 0f, 0f), Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			var solver = new EmergencyCoverSolver();
			EmergencyCoverDecision decision = solver.Decide(
				true, UnitAIState.Idle, in situation, new[] { far }, null);
			Assert.AreEqual(EmergencyCoverResult.Selected, decision.Result);
			Assert.IsTrue(decision.HasDestination);
			Assert.AreEqual(2, decision.SelectedCandidateId);
		}
		#endregion

		#region C Acceptable closest
		[Test]
		public void C1_CloseAcceptable_BeatsFarExcellent()
		{
			CoverCandidate close = StandingCover(1, new Vector3(3f, 0f, 0f), Vector3.forward, 0.55f);
			CoverCandidate far = StandingCover(2, new Vector3(18f, 0f, 0f), Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			CoverEmergencyEvaluation closeEval = CoverEmergencyScoreMath.Evaluate(close, in situation, null);
			CoverEmergencyEvaluation farEval = CoverEmergencyScoreMath.Evaluate(far, in situation, null);
			Assert.IsTrue(closeEval.Acceptable);
			Assert.IsTrue(farEval.Acceptable);
			Assert.Greater(farEval.Score, closeEval.Score);
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new[] { close, far }, null);
			Assert.AreEqual(EmergencyCoverResult.Selected, decision.Result);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}

		[Test]
		public void C2_CloserBelowThreshold_Rejected()
		{
			CoverCandidate closePoor = StandingCover(1, new Vector3(1f, 0f, 0f), Vector3.forward, 0.12f);
			CoverCandidate farGood = StandingCover(2, new Vector3(12f, 0f, 0f), Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			Assert.IsFalse(CoverEmergencyScoreMath.Evaluate(closePoor, in situation, null).Acceptable);
			Assert.IsTrue(CoverEmergencyScoreMath.Evaluate(farGood, in situation, null).Acceptable);
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new[] { closePoor, farGood }, null);
			Assert.AreEqual(2, decision.SelectedCandidateId);
			Assert.AreEqual(EmergencyCoverResult.Selected, decision.Result);
		}
		#endregion

		#region D Selected / fallback / none
		[Test]
		public void D1_AcceptableExists_Selected()
		{
			CoverCandidate candidate = StandingCover(4, new Vector3(4f, 0f, 0f), Vector3.forward, 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 14f));
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Attack, in situation, new[] { candidate }, null);
			Assert.AreEqual(EmergencyCoverResult.Selected, decision.Result);
			Assert.AreEqual(EmergencyCoverReason.ImmediateThreat, decision.Reason);
			Assert.IsTrue(decision.HasDestination);
		}

		[Test]
		public void D2_NoneAcceptable_Fallback()
		{
			CoverCandidate near = StandingCover(1, new Vector3(2f, 0f, 0f), Vector3.forward, 0.12f);
			CoverCandidate far = StandingCover(2, new Vector3(9f, 0f, 0f), Vector3.forward, 0.12f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new[] { near, far }, null);
			Assert.AreEqual(EmergencyCoverResult.Fallback, decision.Result);
			Assert.AreEqual(EmergencyCoverReason.NoAcceptableCandidate, decision.Reason);
			Assert.IsTrue(decision.HasDestination);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}

		[Test]
		public void D3_NoCandidates_NoDestination()
		{
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new CoverCandidate[0], null);
			Assert.AreEqual(EmergencyCoverResult.None, decision.Result);
			Assert.AreEqual(EmergencyCoverReason.NoCandidates, decision.Reason);
			Assert.IsFalse(decision.HasDestination);
		}

		[Test]
		public void D4_CoverTypeNone_NeverAcceptable()
		{
			CoverCandidate open = StandingCover(1, new Vector3(2f, 0f, 0f), Vector3.forward, 1f);
			open.CoverType = CoverType.None;
			open.StandingValid = false;
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			Assert.IsFalse(CoverEmergencyScoreMath.Evaluate(open, in situation, null).Acceptable);
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new[] { open }, null);
			Assert.AreEqual(EmergencyCoverResult.None, decision.Result);
			Assert.IsFalse(decision.HasDestination);
		}
		#endregion

		#region E #11 state unchanged
		[Test]
		public void E1_Attack_ImmediateThreat_StateUnchanged()
		{
			UnitAIController controller = Create();
			try
			{
				BindOpenCover(controller, new Vector3(4f, 0f, 3f));
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				Vector3 attackDest = controller.CurrentContext.Destination;
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(attackDest, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.EmergencyCoverActive);
				Assert.AreNotEqual(attackDest, controller.EmergencyCoverDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E2_Defense_ImmediateThreat_StateUnchanged()
		{
			UnitAIController controller = Create();
			try
			{
				BindOpenCover(controller, new Vector3(3f, 0f, 2f));
				AssertAccepted(controller, TacticalCommand.Defense(P(0f)), UnitAIState.Defense);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.IsTrue(controller.EmergencyCoverActive);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E3_Idle_ImmediateThreat_StateUnchanged()
		{
			UnitAIController controller = Create();
			try
			{
				BindOpenCover(controller, new Vector3(5f, 0f, 1f));
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.IsTrue(controller.EmergencyCoverActive);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E4_Search_ImmediateThreat_KeepsSearch_EmergencyCover()
		{
			UnitAIController controller = Create();
			try
			{
				BindOpenCover(controller, new Vector3(4f, 0f, 2f));
				Vector3 attack = P(8f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(2f)), UnitAIState.Search);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreNotEqual(UnitAISearchCompletionReason.Threat, controller.LastSearchCompletionReason);
				Assert.IsTrue(controller.EmergencyCoverActive);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E5_AttackWalk_NotEmergencyDestination()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				BindOpenCover(controller, new Vector3(4f, 0f, 3f));
				Vector3 attack = P(8f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				Assert.AreEqual(attack, recorder.LastDestination);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(attack, recorder.LastDestination);
				Assert.AreEqual(attack, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.HasEmergencyCoverDestination);
				Assert.AreNotEqual(attack, controller.EmergencyCoverDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E6_Retreat_NoOverlay()
		{
			UnitAIController controller = Create();
			try
			{
				BindOpenCover(controller, new Vector3(4f, 0f, 0f));
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Retreat(P(-6f)), UnitAIState.Retreat);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.IsFalse(controller.EmergencyCoverActive);
				Assert.IsFalse(controller.HasEmergencyCoverDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region F Shared geometry
		[Test]
		public void F1_TwentyUnits_ThreeRegions_ThreeGenerations()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			int decisions = 0;
			Vector3 r1 = Vector3.zero;
			Vector3 r2 = new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
			Vector3 r3 = new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters);
			decisions += FireUnits(cache, r1, 7);
			decisions += FireUnits(cache, r2, 7);
			decisions += FireUnits(cache, r3, 6);
			Assert.AreEqual(3, cache.GenerationCount);
			Assert.AreEqual(3, source.GenerateCount);
			Assert.AreEqual(20, decisions);
		}
		#endregion

		#region Helpers
		private static int FireUnits(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
		{
			int n = 0;
			for (int i = 0; i < _count; i++)
			{
				Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
				var overlay = new EmergencyCoverOverlay();
				overlay.BindCache(_cache);
				CoverSituation situation = RifleAt(pos, pos + new Vector3(0f, 1.5f, 20f));
				overlay.Update(true, UnitAIState.Idle, in situation);
				n++;
			}

			return n;
		}

		private static void BindOpenCover(UnitAIController _controller, Vector3 _cover)
		{
			var source = new ListSource();
			source.Candidates.Add(StandingCover(7, _cover, Vector3.forward, 1f));
			_controller.BindCoverCache(new SharedCoverSpatialCache(source));
		}

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
				GeometryVersion = 1
			};
		}

		private static CoverCandidate StandingCover(int _id, Vector3 _position, Vector3 _normal, float _prot)
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
				StandingProfile = Profile(_prot),
				CrouchProfile = Profile(_prot),
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

		private static Vector3 P(float _x, float _z = 0f)
		{
			return new Vector3(_x, 0f, _z);
		}

		private static void AssertAccepted(UnitAIController _controller, TacticalCommand _command, UnitAIState _state)
		{
			TacticalCommandResult result = _controller.IssueCommand(in _command);
			Assert.IsTrue(result.Accepted, _command.Type + " -> " + _state);
			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static UnitAIController Create()
		{
			return new GameObject("AI134_Emergency").AddComponent<UnitAIController>();
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder) CreateWithRecorder()
		{
			var go = new GameObject("AI134_EmergencyNav");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			return (controller, recorder);
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				Object.DestroyImmediate(_controller.gameObject);
		}
		#endregion
	}
}
