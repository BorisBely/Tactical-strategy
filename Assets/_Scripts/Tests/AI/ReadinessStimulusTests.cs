using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14B.1 stimuli, RequestTransition, decay / hysteresis, mapper, READINESS log.
	/// Does not change 14B.0 assertions. Aim ≠ Fire. No pose.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessStimulusTests
	{
		#region J Stimuli
		[Test]
		public void J1_HostileLost_DoesNotSnapAimToPatrol()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreNotEqual(ReadinessState.Patrol, controller.CurrentState);
		}

		[Test]
		public void J2_CombatActivity_HoldsAimWithoutHostileVisible()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.8f, ReadinessStimulus.CombatActivity);
			controller.Tick(1.6f, ReadinessStimulus.CombatActivity);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasActiveCombatActivity);
		}

		[Test]
		public void J3_GunshotAndHostileVisible_GoesAimNotReady()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			var frame = new ReadinessFrame
			{
				HostileVisible = true,
				GunshotHeard = true
			};
			controller.Tick(0.2f, in frame);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.Patrol, controller.Context.PreviousState);
			Assert.AreEqual(ReadinessState.Aim, controller.LastRequest.ToState);
			Assert.AreEqual(ReadinessChangeReason.HostileVisible, controller.LastRequest.Reason);
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.Patrol,
				ReadinessState.LowReady));
		}

		[Test]
		public void J4_CombatActivity_DoesNotRaisePatrol()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.CombatActivity);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasActiveCombatActivity);
		}

		[Test]
		public void J5_Gunshot_DoesNotLowerAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.RequestsFire);
		}
		#endregion

		#region K Transition request
		[Test]
		public void K1_RequestTransition_RecordsFromToDuration()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			Assert.IsTrue(controller.RequestTransition(
				ReadinessState.Aim,
				ReadinessChangeReason.HostileVisible,
				0.1f));
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.AreEqual(ReadinessState.Patrol, controller.LastRequest.FromState);
			Assert.AreEqual(ReadinessState.Aim, controller.LastRequest.ToState);
			Assert.Greater(controller.LastRequest.Duration, 0f);
			Assert.AreEqual(ReadinessChangeReason.HostileVisible, controller.LastRequest.Reason);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			Assert.AreEqual(1, controller.TransitionRequestCount);
		}

		[Test]
		public void K2_AimDuration_HighReadyFasterThanLowReadyFasterThanPatrol()
		{
			ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
			float fromHigh = ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in soldier);
			float fromLow = ReadinessMath.AimTransitionDuration(ReadinessState.LowReady, in soldier);
			float fromPatrol = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier);
			Assert.Greater(fromLow, fromHigh);
			Assert.Greater(fromPatrol, fromLow);
			Assert.AreEqual(soldier.HighReadyToAimDuration / soldier.AimTransitionModifier, fromHigh);
			Assert.AreEqual(soldier.PatrolToAimDuration / soldier.AimTransitionModifier, fromPatrol);
		}

		[Test]
		public void K3_PatrolHostileVisible_IsOneRequestNotLadder()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			int before = controller.TransitionRequestCount;
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(before + 1, controller.TransitionRequestCount);
			Assert.AreEqual(ReadinessState.Patrol, controller.LastRequest.FromState);
			Assert.AreEqual(ReadinessState.Aim, controller.LastRequest.ToState);
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.Patrol,
				ReadinessState.LowReady));
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.LowReady,
				ReadinessState.HighReady));
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.HighReady,
				ReadinessState.PreAim));
			Assert.IsTrue(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.Patrol,
				ReadinessState.Aim));
		}

		[Test]
		public void K4_ProfileAliases_MatchGunshotPolicy()
		{
			ReadinessProfile recruit = ReadinessProfile.ForRank(ReadinessRankKind.Recruit);
			ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
			ReadinessProfile corporal = ReadinessProfile.ForRank(ReadinessRankKind.Corporal);
			Assert.AreEqual(ReadinessState.LowReady, recruit.GunshotReadyState);
			Assert.AreEqual(ReadinessState.LowReady, soldier.GunshotReadyState);
			Assert.AreEqual(ReadinessState.HighReady, corporal.GunshotReadyState);
			Assert.AreEqual(soldier.NotReadyAimDuration, soldier.NotReadyToAimDuration);
			Assert.AreEqual(soldier.PreAimAimDuration, soldier.PreAimToAimDuration);
		}
		#endregion

		#region L Decay hysteresis retrigger
		[Test]
		public void L1_Decay_DoesNotCascadeThreeRungsInConsecutiveTicks()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(1.3f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(1.4f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(2.4f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			Assert.AreEqual(ReadinessChangeReason.CalmDown, controller.Context.LastChangeReason);
			controller.Tick(3.5f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.AreEqual(ReadinessChangeReason.Calm, controller.Context.LastChangeReason);
		}

		[Test]
		public void L2_RetriggerHostileVisible_CancelsDecayAndReturnsAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.5f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasActiveCombatActivity);

			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(2.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.PreAim, controller.Context.PreviousState);
		}

		[Test]
		public void L3_CalmDownRemaining_CountsAfterHostileLost()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			ReadinessDecision decision = controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, decision.State);
			Assert.IsFalse(decision.HasActiveCombatActivity);
			Assert.Greater(decision.CalmDownRemaining, 0.4f);
			Assert.Less(decision.CalmDownRemaining, 0.6f);
		}
		#endregion

		#region M Mapper and log
		[Test]
		public void M1_Mapper_HostileVisible_WinsPriority()
		{
			AIPerceptionFrame frame = HostileVisibleFrame();
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, false, false);
			Assert.IsTrue(mapped.HostileVisible);
			Assert.IsFalse(mapped.HostileLost);
			Assert.AreEqual(ReadinessStimulus.HostileVisible, ReadinessStimulusMath.Dominant(in mapped));
		}

		[Test]
		public void M2_Mapper_Gunshot_IsNotFire()
		{
			AIPerceptionFrame frame = GunshotFrame();
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, false, false);
			Assert.IsTrue(mapped.GunshotHeard);
			Assert.IsFalse(mapped.HostileVisible);
			Assert.AreEqual(ReadinessStimulus.GunshotHeard, ReadinessStimulusMath.Dominant(in mapped));

			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessDecision decision = controller.Tick(0.2f, in mapped);
			Assert.AreEqual(ReadinessState.LowReady, decision.State);
			Assert.IsFalse(decision.RequestsFire);
		}

		[Test]
		public void M3_Mapper_HostileLost_IsEdgeNotCalm()
		{
			AIPerceptionFrame empty = AIPerceptionFrame.Empty;
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in empty, true, false);
			Assert.IsTrue(mapped.HostileLost);
			Assert.IsFalse(mapped.HostileVisible);
			Assert.AreEqual(ReadinessStimulus.HostileLost, ReadinessStimulusMath.Dominant(in mapped));
		}

		[Test]
		public void M4_Log_InitialAndTransitions_AreEventBased()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			Assert.AreEqual("state=Patrol reason=Initial", controller.LastLogPayload);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("transition=Patrol->LowReady", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("reason=GunshotHeard", StringComparison.Ordinal) >= 0);
			string afterGunshot = controller.LastLogPayload;
			controller.Tick(0.25f, ReadinessStimulus.None);
			Assert.AreEqual(afterGunshot, controller.LastLogPayload);
		}

		[Test]
		public void M5_ArmFatigue_StillDoesNotChangeResult()
		{
			var a = new ReadinessController();
			a.Reset(ReadinessRankKind.Soldier, 0f);
			a.SetArmFatigue(0f, 1f);
			ReadinessDecision zero = a.Tick(0.2f, ReadinessStimulus.HostileVisible);

			var b = new ReadinessController();
			b.Reset(ReadinessRankKind.Soldier, 0f);
			b.SetArmFatigue(1f, 0.1f);
			ReadinessDecision full = b.Tick(0.2f, ReadinessStimulus.HostileVisible);

			Assert.AreEqual(ReadinessState.Aim, zero.State);
			Assert.AreEqual(zero.State, full.State);
			Assert.AreEqual(a.LastRequest.Duration, b.LastRequest.Duration);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
		}

		[Test]
		public void M6_RankAssetIndex_MatchesDisplayOrder()
		{
			Assert.AreEqual(ReadinessRankKind.Recruit, ReadinessMath.RankFromAssetIndex(0));
			Assert.AreEqual(ReadinessRankKind.Soldier, ReadinessMath.RankFromAssetIndex(1));
			Assert.AreEqual(ReadinessRankKind.Corporal, ReadinessMath.RankFromAssetIndex(2));
			Assert.AreEqual(ReadinessRankKind.Veteran, ReadinessMath.RankFromAssetIndex(3));
			Assert.AreEqual(ReadinessRankKind.Elite, ReadinessMath.RankFromAssetIndex(4));
			Assert.AreEqual(ReadinessRankKind.Soldier, ReadinessMath.RankFromAssetIndex(-1));
		}
		#endregion

		#region Private Methods
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

		private static AIPerceptionFrame GunshotFrame()
		{
			var sound = new AISoundContact(
				null,
				Vector3.zero,
				SoundEventType.Gunshot,
				1f,
				0f,
				0f,
				true);
			return new AIPerceptionFrame(
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None,
				new[] { sound },
				Array.Empty<AIReportContact>());
		}
		#endregion
	}
}
