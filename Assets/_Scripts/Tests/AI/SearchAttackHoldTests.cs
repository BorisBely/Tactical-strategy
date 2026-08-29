using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// Occupy execution patch: Search hysteresis, ImmediateThreat HoldState, Occupied hold, LIFE release.
	/// Does not retune CoverScore / PathScore / 0.60. Does not reopen #10/#11/#13/#14/#15.
	/// </summary>
	public sealed class SearchAttackHoldTests
	{
		#region A Search hysteresis
		[Test]
		public void A_LostVisual_HalfSecond_NoSearch()
		{
			Transform target = new GameObject("Hold_A05").transform;
			try
			{
				AIPerceptionFrame frame = VisualLost(target, Vector3.right * 10f);
				Assert.IsFalse(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Attack, in frame, 1.5f, 1f));
			}
			finally
			{
				Object.DestroyImmediate(target.gameObject);
			}
		}

		[Test]
		public void A_LostVisual_OnePointFiveSeconds_Search()
		{
			Transform target = new GameObject("Hold_A15").transform;
			try
			{
				AIPerceptionFrame frame = VisualLost(target, Vector3.right * 10f);
				Assert.IsTrue(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Attack, in frame, 1.5f, 0f));
			}
			finally
			{
				Object.DestroyImmediate(target.gameObject);
			}
		}
		#endregion

		#region B ImmediateThreat
		[Test]
		public void B_Search_ImmediateThreat_StaysSearch()
		{
			var go = new GameObject("Hold_B");
			Transform target = new GameObject("Hold_B_Target").transform;
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(
					UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 8f, Vector3.forward))));
				controller.SetPerceptionFrame(VisualLost(target, new Vector3(19f, 0f, 0f)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);

				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreNotEqual(UnitAISearchCompletionReason.Threat, controller.LastSearchCompletionReason);
				Assert.AreEqual(UnitAIPriorityDecision.HoldState, controller.LastPriorityEvaluation.Decision);
			}
			finally
			{
				Object.DestroyImmediate(go);
				Object.DestroyImmediate(target.gameObject);
			}
		}
		#endregion

		#region C Cover persistence
		[Test]
		public void C_Occupied_DoesNotSwapOnBetterScore()
		{
			CoverCandidate current = Standing(1, Vector3.zero, 0.12f);
			CoverCandidate better = Standing(2, new Vector3(6f, 0f, 0f), 1f);
			CoverSituation situation = new CoverSituation
			{
				UnitPosition = Vector3.zero,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Attack,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				TargetPosition = new Vector3(0f, 1.5f, 18f),
				HasTarget = true,
				SectorForward = Vector3.forward,
				HostileDirection = Vector3.forward,
				GeometryVersion = 1
			};
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, better },
				in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, decision.Reason);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}
		#endregion

		#region D Cover + KO
		[Test]
		public void D_Occupied_Unconscious_Released_NoTactical()
		{
			var go = new GameObject("Hold_D");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Attack(
					UnitAIStateContext.ForAttack(new Vector3(4f, 0f, 0f), Vector3.forward))));
				var board = new CoverOccupancyBoard();
				ai.BindCoverOccupancy(board);
				CoverCandidate cover = Standing(2, Vector3.zero, 1f);
				int unitId = ai.CoverOccupancyUnitId;
				Assert.IsTrue(board.TryReserve(cover, unitId, 0f).Success);
				Assert.IsTrue(board.ConfirmOccupied(cover, unitId, 0f).Success);
				ai.TacticalMovement.BindOccupancy(board, unitId);
				ai.NotifyLifeState(UnitLifeState.Unconscious);
				Assert.IsTrue(board.IsAvailable(cover, 0f));
				Assert.IsFalse(ai.TacticalMovement.CurrentTacticalPosition.Occupied);
				Assert.IsFalse(UnitLifeStateMath.AllowsTactical(UnitLifeState.Unconscious));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Helpers
		private static AIPerceptionFrame VisualLost(Transform _target, Vector3 _lastKnown)
		{
			var contact = new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				ObservationState.RecentlyLost,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_lastKnown,
				_lastKnown,
				12.5f,
				0.95f,
				false,
				true,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				false,
				true);
			return new AIPerceptionFrame(
				new[] { contact },
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None);
		}

		private static CoverCandidate Standing(int _id, Vector3 _position, float _protection)
		{
			var profile = new CoverProtectionProfile
			{
				Head = _protection,
				Torso = _protection,
				Pelvis = _protection,
				Legs = _protection
			};
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = Vector3.forward,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = profile,
				CrouchProfile = profile,
				GeometryVersion = 1
			};
		}
		#endregion
	}
}
