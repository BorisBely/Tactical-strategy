using System;
using NUnit.Framework;

namespace AI.Tests
{
	/// <summary>
	/// #14B.5 hold / step-down decay. Rising stays fast. Falling is slow. ArmFatigue unused.
	/// Instant (14B.0–14B.3) keeps 1 s hold and instant steps.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessPersistenceTests
	{
		#region AA Hold
		[Test]
		public void AA1_ForRank_HostileLost_OneSecond_StaysAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float t = controller.LastCombatActivityTime + 1f;
			controller.Tick(t, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessDecayPhase.Hold, controller.Last.DecayPhase);
		}

		[Test]
		public void AA2_Instant_HostileLost_DoesNotDropBeforeHold()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AA3_HoldRemaining_FromLastActivity()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			ReadinessDecision decision = controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.Greater(decision.CalmDownRemaining, 0.4f);
			Assert.Less(decision.CalmDownRemaining, 0.6f);
		}

		[Test]
		public void AA4_ForRank_BeforeHoldExpires_StaysAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold * 0.8f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.Context.HasPendingTransition);
		}

		[Test]
		public void AA5_CombatActivity_KeepsFullHold()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold * 0.9f, ReadinessStimulus.CombatActivity);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.Greater(controller.Context.CalmDownRemaining, hold * 0.9f);
		}

		[Test]
		public void AA6_HoldLog_OnHostileLost_Once()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			Assert.IsTrue(ReadinessLog.ContainsHold(controller.LogLines, ReadinessState.Aim));
			string hold = controller.LastDecayHoldPayload;
			controller.Tick(controller.LastCombatActivityTime + 0.2f, ReadinessStimulus.None);
			Assert.AreEqual(hold, controller.LastDecayHoldPayload);
		}

		[Test]
		public void AA7_HoldLog_NotEveryTick()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			int before = controller.LogLines.Count;
			float t = controller.LastCombatActivityTime;
			controller.Tick(t + 0.1f, ReadinessStimulus.None);
			controller.Tick(t + 0.2f, ReadinessStimulus.None);
			controller.Tick(t + 0.3f, ReadinessStimulus.None);
			Assert.AreEqual(before, controller.LogLines.Count);
		}

		[Test]
		public void AA8_CombatActivityExpired_StillForceInstantStep()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
		}
		#endregion

		#region AB Step down
		[Test]
		public void AB1_ForRank_AimToPreAim_AfterHold()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			Assert.AreNotEqual(ReadinessState.Patrol, controller.CurrentState);
		}

		[Test]
		public void AB2_ForRank_PreAimToReady_AfterHold()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			StepAfterHold(controller, ReadinessState.LowReady);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
		}

		[Test]
		public void AB3_ForRank_ReadyToCalm_AfterHold()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			StepAfterHold(controller, ReadinessState.LowReady);
			StepAfterHold(controller, ReadinessState.Patrol);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.AreEqual(ReadinessChangeReason.Calm, controller.Context.LastChangeReason);
		}

		[Test]
		public void AB4_ForRank_DoesNotSkipAimToPatrol()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.Aim, controller.Context.PreviousState);
		}

		[Test]
		public void AB5_Instant_LadderUnchanged()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(1.3f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(2.4f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			controller.Tick(3.5f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
		}

		[Test]
		public void AB6_ForRank_ConsecutiveTicks_NoCascade()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float t = controller.LastCombatActivityTime + hold + 0.02f;
			controller.Tick(t, ReadinessStimulus.None);
			ReadinessState afterFirst = controller.CurrentState;
			controller.Tick(t + 0.02f, ReadinessStimulus.None);
			Assert.AreEqual(afterFirst, controller.CurrentState);
			Assert.AreNotEqual(ReadinessState.Patrol, controller.CurrentState);
		}

		[Test]
		public void AB7_ForRank_DecayStepDuration_GreaterThanZero()
		{
			ReadinessProfile soldier = ForSoldier();
			Assert.Greater(ReadinessMath.DecayTransitionDuration(
				ReadinessState.Aim, ReadinessState.PreAim, in soldier), 0f);
			Assert.Greater(soldier.PreAimToReadyDuration, 0f);
			Assert.Greater(soldier.ReadyToCalmDuration, 0f);
		}

		[Test]
		public void AB8_RisingFasterThanFallingHold()
		{
			ReadinessProfile soldier = ForSoldier();
			float raise = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier);
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, in soldier);
			Assert.Greater(hold, raise * 5f);
			Assert.Greater(soldier.HighReadyHoldTime, soldier.ReadyRaiseDuration * 10f);
		}
		#endregion

		#region AC Refresh
		[Test]
		public void AC1_Gunshot_DuringAimHold_ResetsTimer()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float almost = controller.LastCombatActivityTime + hold * 0.9f;
			controller.Tick(almost, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(almost, controller.LastCombatActivityTime);
			controller.Tick(almost + hold * 0.5f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AC2_Gunshot_NearTimeout_StaysAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float almost = controller.LastCombatActivityTime + hold - 0.05f;
			controller.Tick(almost, ReadinessStimulus.GunshotHeard);
			controller.Tick(almost + 0.2f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AC3_Corporal_Gunshot_StaysHighReady()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.Instant(ReadinessRankKind.Corporal), 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			int raises = controller.TransitionRequestCount;
			controller.Tick(0.4f, ReadinessStimulus.HostileLost);
			controller.Tick(0.6f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			Assert.AreEqual(raises, controller.TransitionRequestCount);
		}

		[Test]
		public void AC4_CombatActivity_RefreshesHold()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float almost = controller.LastCombatActivityTime + hold * 0.85f;
			controller.Tick(almost, ReadinessStimulus.CombatActivity);
			controller.Tick(almost + hold * 0.5f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AC5_HostileVisible_RefreshesAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float almost = controller.LastCombatActivityTime + hold * 0.9f;
			controller.Tick(almost, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			controller.Tick(almost + 0.2f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AC6_Gunshot_CancelsPendingDecay()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			float step = ReadinessMath.DecayTransitionDuration(
				ReadinessState.Aim,
				ReadinessState.PreAim,
				controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold + 0.02f, ReadinessStimulus.None);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			Assert.AreEqual(ReadinessState.PreAim, controller.Context.TransitionTo);
			controller.Tick(controller.Context.TransitionStartTime + step * 0.3f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.Context.HasPendingTransition);
		}
		#endregion

		#region AD Reacquire
		[Test]
		public void AD1_Instant_PreAim_HostileVisible_Aim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(2.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.PreAim, controller.Context.PreviousState);
		}

		[Test]
		public void AD2_ForRank_PreAim_HostileVisible_GoesAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			float t = controller.Context.StateEnterTime + 0.05f;
			controller.Tick(t, ReadinessStimulus.HostileVisible);
			Assert.IsTrue(
				controller.CurrentState == ReadinessState.Aim ||
				(controller.Context.HasPendingTransition &&
				 controller.Context.TransitionTo == ReadinessState.Aim));
			FinishRaise(controller, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.PreAim, controller.Context.PreviousState);
		}

		[Test]
		public void AD3_HighReady_HostileVisible_Aim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.Instant(ReadinessRankKind.Corporal), 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.HighReady, controller.Context.PreviousState);
		}

		[Test]
		public void AD4_PendingDecay_HostileVisible_StaysAim()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold + 0.02f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			controller.Tick(controller.Context.TransitionStartTime + 0.05f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.Context.HasPendingTransition);
		}

		[Test]
		public void AD5_Reacquire_DoesNotVisitReady()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			StepAfterHold(controller, ReadinessState.PreAim);
			int before = controller.LogLines.Count;
			FinishRaise(controller, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.PreAim,
				ReadinessState.LowReady));
			Assert.Greater(controller.LogLines.Count, before);
		}
		#endregion

		#region AE Oscillation
		[Test]
		public void AE1_GunshotLostRepeat_StaysReady()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.5f, ReadinessStimulus.HostileLost);
			controller.Tick(0.6f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.7f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
		}

		[Test]
		public void AE2_GunshotLostRepeat_OneRaise()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			int raises = controller.TransitionRequestCount;
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.5f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(raises, controller.TransitionRequestCount);
		}

		[Test]
		public void AE3_NeverPatrolDuringChatter()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			for (int i = 0; i < 8; i++)
			{
				float t = 0.2f + i * 0.15f;
				controller.Tick(t, i % 2 == 0
					? ReadinessStimulus.GunshotHeard
					: ReadinessStimulus.HostileLost);
				Assert.AreNotEqual(ReadinessState.Patrol, controller.CurrentState);
			}
		}

		[Test]
		public void AE4_HostileVisibleLostRepeat_StaysAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			for (int i = 0; i < 6; i++)
			{
				float t = 0.3f + i * 0.1f;
				controller.Tick(t, i % 2 == 0
					? ReadinessStimulus.HostileLost
					: ReadinessStimulus.HostileVisible);
				Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			}
		}
		#endregion

		#region AF Rank structure
		[Test]
		public void AF1_AllRanks_SameDecayLadder()
		{
			AssertLadder(ReadinessRankKind.Recruit, ReadinessState.LowReady, ReadinessState.NotReady);
			AssertLadder(ReadinessRankKind.Soldier, ReadinessState.LowReady, ReadinessState.Patrol);
			AssertLadder(ReadinessRankKind.Corporal, ReadinessState.HighReady, ReadinessState.Patrol);
			AssertLadder(ReadinessRankKind.Veteran, ReadinessState.HighReady, ReadinessState.Patrol);
			AssertLadder(ReadinessRankKind.Elite, ReadinessState.HighReady, ReadinessState.Patrol);
		}

		[Test]
		public void AF2_CalmDownProfile_SameStructure()
		{
			ReadinessCalmDownProfile recruit = ReadinessProfile.ForRank(ReadinessRankKind.Recruit).CalmDownProfile;
			ReadinessCalmDownProfile elite = ReadinessProfile.ForRank(ReadinessRankKind.Elite).CalmDownProfile;
			Assert.AreEqual(recruit.AimHoldTime, elite.AimHoldTime);
			Assert.AreEqual(recruit.PreAimHoldTime, elite.PreAimHoldTime);
			Assert.AreEqual(recruit.LowReadyHoldTime, elite.LowReadyHoldTime);
			Assert.AreEqual(recruit.HighReadyHoldTime, elite.HighReadyHoldTime);
			Assert.AreEqual(recruit.AimToPreAimDuration, elite.AimToPreAimDuration);
		}

		[Test]
		public void AF3_Instant_AllRanks_SameLadder()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Elite, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(4f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			controller.Tick(6f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
		}

		[Test]
		public void AF4_RankCalmDownModifier_DoesNotChangeStructure()
		{
			ReadinessProfile recruit = ReadinessProfile.ForRank(ReadinessRankKind.Recruit);
			ReadinessProfile elite = ReadinessProfile.ForRank(ReadinessRankKind.Elite);
			Assert.AreEqual(
				ReadinessMath.NextDecayState(ReadinessState.Aim, in recruit),
				ReadinessMath.NextDecayState(ReadinessState.Aim, in elite));
			Assert.AreNotEqual(recruit.RankCalmDownModifier, elite.RankCalmDownModifier);
		}
		#endregion

		#region AG Life
		[Test]
		public void AG1_Unconscious_NoDecay()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			int changes = controller.Context.ChangeCount;
			controller.SetAllowed(false);
			controller.Tick(8f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(changes, controller.Context.ChangeCount);
		}

		[Test]
		public void AG2_Dead_NoNewTransition()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.SetAllowed(false);
			Assert.IsFalse(controller.RequestTransition(
				ReadinessState.Patrol,
				ReadinessChangeReason.Calm,
				1f));
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void AG3_Disallowed_Timeout_NoStep()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			int changes = controller.Context.ChangeCount;
			string log = controller.LastLogPayload;
			controller.SetAllowed(false);
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold + 2f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(changes, controller.Context.ChangeCount);
			Assert.AreEqual(log, controller.LastLogPayload);
		}
		#endregion

		#region AH Profile / fatigue / log
		[Test]
		public void AH1_CalmDownProfile_FieldsPresent()
		{
			ReadinessCalmDownProfile calm = ForSoldier().CalmDownProfile;
			Assert.Greater(calm.AimHoldTime, calm.PreAimHoldTime);
			Assert.Greater(calm.HighReadyHoldTime, calm.AimHoldTime);
			Assert.Greater(calm.ReadyToCalmDuration, 0f);
		}

		[Test]
		public void AH2_Soldier_Ladder_InPlayBand()
		{
			float total = ReadinessMath.LadderCalmDownDuration(ForSoldier());
			Assert.Greater(total, 15f);
			Assert.Less(total, 26f);
		}

		[Test]
		public void AH3_Fatigue_DoesNotChangeHold()
		{
			float a = HoldWithFatigue(0f);
			float b = HoldWithFatigue(1f);
			Assert.AreEqual(a, b);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
		}

		[Test]
		public void AH4_Instant_DecayDurationZero()
		{
			ReadinessProfile instant = ReadinessProfile.Instant(ReadinessRankKind.Soldier);
			Assert.AreEqual(0f, ReadinessMath.DecayTransitionDuration(
				ReadinessState.Aim, ReadinessState.PreAim, in instant));
			Assert.AreEqual(1f, instant.AimHoldTime);
		}

		[Test]
		public void AH5_DecayLog_HasTransition()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(1.5f, ReadinessStimulus.None);
			Assert.IsTrue(controller.LastDecayPayload.IndexOf("Aim->PreAim", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastDecayPayload.IndexOf("CombatActivityExpired", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(ReadinessLog.ContainsHold(controller.LogLines, ReadinessState.PreAim) ||
			              ReadinessLog.ContainsHold(controller.LogLines, ReadinessState.Aim));
		}

		[Test]
		public void AH6_DecayPhase_HoldThenStep()
		{
			ReadinessController controller = AimThenLost(ForSoldier());
			Assert.AreEqual(ReadinessDecayPhase.Hold, controller.Last.DecayPhase);
			float hold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
			controller.Tick(controller.LastCombatActivityTime + hold + 0.02f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessDecayPhase.StepDown, controller.Last.DecayPhase);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}
		#endregion

		#region Private Methods
		private static ReadinessProfile ForSoldier() =>
			ReadinessProfile.ForRank(ReadinessRankKind.Soldier);

		private static ReadinessController AimThenLost(ReadinessProfile _profile)
		{
			var controller = new ReadinessController();
			controller.Reset(_profile, 0f);
			controller.Tick(0f, ReadinessStimulus.HostileVisible);
			FinishRaise(controller, ReadinessStimulus.HostileVisible);
			float t = controller.Context.StateEnterTime + 0.01f;
			controller.Tick(t, ReadinessStimulus.HostileLost);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			return controller;
		}

		private static void FinishRaise(ReadinessController _controller, ReadinessStimulus _hold)
		{
			if (_controller.CurrentState == ReadinessState.Aim && !_controller.Context.HasPendingTransition)
				return;

			float t = _controller.Context.HasPendingTransition
				? _controller.Context.TransitionStartTime
				: _controller.Context.StateEnterTime;

			if (!_controller.Context.HasPendingTransition ||
			    _controller.Context.TransitionTo != ReadinessState.Aim)
			{
				t += 0.01f;
				_controller.Tick(t, _hold);
			}

			if (_controller.CurrentState == ReadinessState.Aim && !_controller.Context.HasPendingTransition)
				return;

			t = _controller.Context.TransitionStartTime + _controller.Context.TransitionDuration + 0.05f;
			_controller.Tick(t, _hold);
			Assert.AreEqual(ReadinessState.Aim, _controller.CurrentState);
		}

		private static void StepAfterHold(ReadinessController _controller, ReadinessState _target)
		{
			float hold = ReadinessMath.EffectiveHoldTime(_controller.CurrentState, _controller.Profile);
			float step = ReadinessMath.DecayTransitionDuration(
				_controller.CurrentState,
				_target,
				_controller.Profile);
			float t = _controller.LastCombatActivityTime + hold + 0.05f;
			_controller.Tick(t, ReadinessStimulus.None);
			if (_controller.CurrentState != _target)
			{
				t += step + 0.05f;
				_controller.Tick(t, ReadinessStimulus.None);
			}

			Assert.AreEqual(_target, _controller.CurrentState);
		}

		private static void AssertLadder(
			ReadinessRankKind _rank,
			ReadinessState _ready,
			ReadinessState _calm)
		{
			ReadinessProfile profile = ReadinessProfile.ForRank(_rank);
			Assert.AreEqual(ReadinessState.PreAim, ReadinessMath.NextDecayState(ReadinessState.Aim, in profile));
			Assert.AreEqual(_ready, ReadinessMath.NextDecayState(ReadinessState.PreAim, in profile));
			Assert.AreEqual(_calm, ReadinessMath.NextDecayState(_ready, in profile));
		}

		private static float HoldWithFatigue(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ForSoldier(), 0f);
			controller.SetArmFatigue(_fatigue, 1f - _fatigue * 0.4f);
			controller.Tick(0f, ReadinessStimulus.HostileVisible);
			return ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, controller.Profile);
		}
		#endregion
	}
}
