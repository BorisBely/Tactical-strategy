using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14B.2 Readiness → pose request. Aim ≠ Fire ≠ G6. CombatReadiness executes.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessPoseTests
	{
		#region N Mapping
		[Test]
		public void N1_Mapping_AllReadinessStates()
		{
			Assert.AreEqual(WeaponPoseState.NotReady, ReadinessPoseMath.ToPose(ReadinessState.NotReady));
			Assert.AreEqual(WeaponPoseState.NotReadyPatrol, ReadinessPoseMath.ToPose(ReadinessState.Patrol));
			Assert.AreEqual(WeaponPoseState.LowReady, ReadinessPoseMath.ToPose(ReadinessState.LowReady));
			Assert.AreEqual(WeaponPoseState.HighReady, ReadinessPoseMath.ToPose(ReadinessState.HighReady));
			Assert.AreEqual(WeaponPoseState.PreAim, ReadinessPoseMath.ToPose(ReadinessState.PreAim));
			Assert.AreEqual(WeaponPoseState.Aiming, ReadinessPoseMath.ToPose(ReadinessState.Aim));
		}

		[Test]
		public void N2_PatrolToAim_RequestsAimingWithoutLogicalLadder()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			ReadinessPoseRequest request = controller.PoseRequest;
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.Patrol, controller.Context.PreviousState);
			Assert.AreEqual(WeaponPoseState.Aiming, request.Pose);
			Assert.AreEqual(ReadinessState.Aim, request.State);
			Assert.IsTrue(ReadinessPoseMath.LogicalSkipsIntermediates(
				ReadinessState.Patrol,
				ReadinessState.Aim));
			Assert.IsFalse(request.RequestsFire);
		}

		[Test]
		public void N3_PendingPatrolToAim_AlreadyRequestsAiming()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0.1f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			ReadinessPoseRequest request = controller.PoseRequest;
			Assert.AreEqual(ReadinessState.Aim, request.State);
			Assert.AreEqual(WeaponPoseState.Aiming, request.Pose);
			Assert.Greater(request.Duration, 0f);
			Assert.AreEqual(WeaponPoseState.NotReadyPatrol, request.FromPose);
		}

		[Test]
		public void N4_GunshotRanks_MapToReadyPoses()
		{
			Assert.AreEqual(
				WeaponPoseState.LowReady,
				PoseAfter(ReadinessRankKind.Recruit, ReadinessStimulus.GunshotHeard).Pose);
			Assert.AreEqual(
				WeaponPoseState.LowReady,
				PoseAfter(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard).Pose);
			Assert.AreEqual(
				WeaponPoseState.HighReady,
				PoseAfter(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard).Pose);
			Assert.AreEqual(
				WeaponPoseState.HighReady,
				PoseAfter(ReadinessRankKind.Elite, ReadinessStimulus.GunshotHeard).Pose);
		}

		[Test]
		public void N5_Aim_DoesNotRequestFireOrG6()
		{
			ReadinessPoseRequest request = PoseAfter(
				ReadinessRankKind.Soldier,
				ReadinessStimulus.HostileVisible);
			Assert.AreEqual(WeaponPoseState.Aiming, request.Pose);
			Assert.IsFalse(request.RequestsFire);
			Assert.IsFalse(request.ChangesG6);
			Assert.IsFalse(ReadinessPoseMath.FatigueAffectsPose());
		}

		[Test]
		public void N6_Fatigue_DoesNotChangePoseMapping()
		{
			var a = new ReadinessController();
			a.Reset(ReadinessRankKind.Soldier, 0f);
			a.SetArmFatigue(0f, 1f);
			a.Tick(0.2f, ReadinessStimulus.HostileVisible);
			var b = new ReadinessController();
			b.Reset(ReadinessRankKind.Soldier, 0f);
			b.SetArmFatigue(1f, 0.1f);
			b.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(a.PoseRequest.Pose, b.PoseRequest.Pose);
			Assert.AreEqual(WeaponPoseState.Aiming, a.PoseRequest.Pose);
		}

		[Test]
		public void N7_Decay_MapsPreAimThenReadyThenCalm()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(WeaponPoseState.PreAim, controller.PoseRequest.Pose);
			controller.Tick(4f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(WeaponPoseState.LowReady, controller.PoseRequest.Pose);
			controller.Tick(6f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(WeaponPoseState.NotReadyPatrol, controller.PoseRequest.Pose);
		}
		#endregion

		#region O CombatReadiness executor
		[Test]
		public void O1_Hold_AppliesPatrolPose_DoesNotEngage()
		{
			GameObject go = new GameObject("ReadinessPoseHold");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				Assert.IsNotNull(combat);
				combat.ApplyNow();
				Assert.AreEqual(CombatIntent.Hold, combat.LastAppliedIntent);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.AreEqual(ReadinessState.Patrol, ai.Readiness.CurrentState);
				Assert.AreEqual(WeaponPoseState.NotReadyPatrol, combat.LastPoseRequest.Pose);
				Assert.IsFalse(combat.ReadinessRequested);
				Assert.IsFalse(combat.LastPoseRequest.RequestsFire);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void O2_HoldPlusHostileVisible_PoseAiming_IntentStillHold()
		{
			GameObject go = new GameObject("ReadinessPoseHoldAim");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.SetPerceptionFrame(HostileVisibleFrame());
				ai.Tick(0.05f);
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				EngagementDecision before = EngagementDecision.None;
				EngagementDecisionController g6 = go.GetComponent<EngagementDecisionController>();
				if (g6 != null)
					before = g6.CurrentDecision;
				combat.ApplyNow();
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.AreEqual(CombatIntent.Hold, combat.LastAppliedIntent);
				Assert.AreEqual(WeaponPoseState.Aiming, combat.LastPoseRequest.Pose);
				Assert.IsFalse(combat.LastPoseRequest.RequestsFire);
				Assert.IsFalse(combat.LastPoseRequest.ChangesG6);
				if (g6 != null)
					Assert.AreEqual(before, g6.CurrentDecision);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void O3_Engage_DoesNotUseSecondAutoDriver()
		{
			GameObject go = new GameObject("ReadinessPoseEngage");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
				ai.TryApplyCommand(UnitAICommand.Defense(
					UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
				ai.SetPerceptionFrame(HostileVisibleFrame());
				ai.Tick(0.05f);
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				combat.ApplyNow();
				Assert.AreEqual(CombatIntent.Engage, ai.CurrentCombatIntent);
				Assert.AreEqual(CombatIntent.Engage, combat.LastAppliedIntent);
				Assert.IsTrue(combat.ReadinessRequested);
				Assert.AreEqual(WeaponPoseState.Aiming, combat.LastPoseRequest.Pose);
				Assert.AreNotEqual(WeaponPoseMode.Auto, combat.LastPoseRequest.Mode);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void O4_LifeGate_ForcesNotReadyPose()
		{
			GameObject go = new GameObject("ReadinessPoseLife");
			try
			{
				CombatReadinessController combat = go.AddComponent<CombatReadinessController>();
				combat.ApplyIncapacitatedPose();
				Assert.AreEqual(WeaponPoseState.NotReady, combat.LastPoseRequest.Pose);
				Assert.IsTrue(combat.LastPoseRequest.FromLifeGate);
				Assert.IsTrue(combat.LastPoseLogPayload.IndexOf("reason=LifeGate", StringComparison.Ordinal) >= 0);
				Assert.IsFalse(combat.LastPoseRequest.RequestsFire);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void O5_PoseLog_EventBased()
		{
			GameObject go = new GameObject("ReadinessPoseLog");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				combat.ApplyNow();
				string first = combat.LastPoseLogPayload;
				Assert.IsTrue(first.IndexOf("state=Patrol", StringComparison.Ordinal) >= 0);
				Assert.IsTrue(first.IndexOf("pose=NotReadyPatrol", StringComparison.Ordinal) >= 0);
				combat.ApplyNow();
				Assert.AreEqual(first, combat.LastPoseLogPayload);
				ai.SetPerceptionFrame(HostileVisibleFrame());
				ai.Tick(0.05f);
				combat.ApplyNow();
				Assert.IsTrue(combat.LastPoseLogPayload.IndexOf("pose=Aiming", StringComparison.Ordinal) >= 0);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void O6_Incapacitated_OverridesAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(WeaponPoseState.Aiming, controller.PoseRequest.Pose);
			ReadinessPoseRequest down = ReadinessPoseMath.Incapacitated();
			Assert.AreEqual(WeaponPoseState.NotReady, down.Pose);
			Assert.AreNotEqual(controller.PoseRequest.Pose, down.Pose);
		}
		#endregion

		#region Private Methods
		private static ReadinessPoseRequest PoseAfter(ReadinessRankKind _rank, ReadinessStimulus _stimulus)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			controller.Tick(0.2f, _stimulus);
			return controller.PoseRequest;
		}

		private static AIPerceptionFrame HostileVisibleFrame()
		{
			AIContactKnowledge contact = new AIContactKnowledge(
				null,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				Vector3.zero,
				Vector3.zero,
				0f,
				1f,
				true,
				false,
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
				new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.High);
		}
		#endregion
	}
}
