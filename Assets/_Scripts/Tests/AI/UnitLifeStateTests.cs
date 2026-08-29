using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// Alive / Unconscious / Dead is not an AI state. Cover releases on incapacitation.
	/// </summary>
	public sealed class UnitLifeStateTests
	{
		[Test]
		public void Resolve_Null_Alive()
		{
			Assert.AreEqual(UnitLifeState.Alive, UnitLifeStateMath.Resolve(null, null));
			Assert.AreEqual(UnitLifeState.Alive, UnitLifeStateMath.Resolve((Component)null));
		}

		[Test]
		public void Resolve_Dead_BeatsUnconscious()
		{
			var go = new GameObject("Life_Dead");
			try
			{
				UnitHealth health = go.AddComponent<UnitHealth>();
				health.EnterDead();
				Assert.AreEqual(UnitLifeState.Dead, UnitLifeStateMath.Resolve(health, null));
				Assert.IsTrue(UnitLifeStateMath.RequiresCoverRelease(UnitLifeState.Dead));
				Assert.IsFalse(UnitLifeStateMath.AllowsTactical(UnitLifeState.Dead));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void NotifyLifeState_Unconscious_ReleasesOccupiedCover()
		{
			var go = new GameObject("Life_KO_Cover");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				var board = new CoverOccupancyBoard();
				ai.BindCoverOccupancy(board);
				CoverCandidate cover = new CoverCandidate
				{
					CandidateId = 3,
					Position = Vector3.zero,
					Normal = Vector3.forward,
					CoverType = CoverType.Standing,
					StandingValid = true,
					CrouchValid = true,
					NavMeshValid = true,
					GeometryVersion = 1
				};
				int unitId = ai.CoverOccupancyUnitId;
				Assert.IsTrue(board.TryReserve(cover, unitId, 0f).Success);
				Assert.IsTrue(board.ConfirmOccupied(cover, unitId, 0f).Success);
				ai.NotifyLifeState(UnitLifeState.Unconscious);
				Assert.IsTrue(board.IsAvailable(cover, 0f));
				Assert.IsFalse(ai.TacticalMovement.CurrentTacticalPosition.Occupied);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void IssueCommand_Dead_Rejected()
		{
			var go = new GameObject("Life_KO_Cmd");
			try
			{
				UnitHealth health = go.AddComponent<UnitHealth>();
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				health.EnterDead();
				TacticalCommandResult result = ai.IssueCommand(TacticalCommand.Attack(new Vector3(4f, 0f, 0f)));
				Assert.IsFalse(result.Accepted);
				Assert.AreEqual(TacticalCommandRejectReason.UnitUnavailable, result.Reason);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void LifeGate_Dead_DisablesAi()
		{
			var go = new GameObject("Life_Gate");
			try
			{
				UnitHealth health = go.AddComponent<UnitHealth>();
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.enabled = true;
				UnitLifeGate gate = go.AddComponent<UnitLifeGate>();
				gate.Refresh();
				Assert.AreEqual(UnitLifeState.Alive, gate.State);
				health.EnterDead();
				gate.Refresh();
				Assert.AreEqual(UnitLifeState.Dead, gate.State);
				Assert.IsFalse(ai.enabled);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
	}
}
