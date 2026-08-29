using System;
using NUnit.Framework;

namespace AI.Tests
{
	/// <summary>
	/// #14B.4 rank balance. Same state machine, different speeds. ArmFatigue does not enter.
	/// Relations, not frozen milliseconds.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessBalanceTests
	{
		#region V Rank mapping
		[Test]
		public void V1_Recruit_StartsNotReady()
		{
			Assert.AreEqual(ReadinessState.NotReady, ReadinessProfile.ForRank(ReadinessRankKind.Recruit).CalmState);
			Assert.AreEqual(ReadinessState.NotReady, ReadinessMath.InitialState(ReadinessRankKind.Recruit));
		}

		[Test]
		public void V2_Soldier_StartsPatrol()
		{
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Soldier));
		}

		[Test]
		public void V3_Corporal_StartsPatrol()
		{
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Corporal));
		}

		[Test]
		public void V4_Veteran_StartsPatrol()
		{
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Veteran));
		}

		[Test]
		public void V5_Elite_StartsPatrol()
		{
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Elite));
		}
		#endregion

		#region W Gunshot mapping
		[Test]
		public void W1_Recruit_Gunshot_LowReady()
		{
			Assert.AreEqual(ReadinessState.LowReady, InstantAfter(ReadinessRankKind.Recruit, ReadinessStimulus.GunshotHeard));
			Assert.AreEqual(ReadinessState.LowReady, ReadinessProfile.ForRank(ReadinessRankKind.Recruit).GunshotState);
		}

		[Test]
		public void W2_Soldier_Gunshot_LowReady()
		{
			Assert.AreEqual(ReadinessState.LowReady, InstantAfter(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard));
		}

		[Test]
		public void W3_Corporal_Gunshot_HighReady()
		{
			Assert.AreEqual(ReadinessState.HighReady, InstantAfter(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard));
		}

		[Test]
		public void W4_Veteran_Gunshot_HighReady()
		{
			Assert.AreEqual(ReadinessState.HighReady, InstantAfter(ReadinessRankKind.Veteran, ReadinessStimulus.GunshotHeard));
		}

		[Test]
		public void W5_Elite_Gunshot_HighReady()
		{
			Assert.AreEqual(ReadinessState.HighReady, InstantAfter(ReadinessRankKind.Elite, ReadinessStimulus.GunshotHeard));
		}

		[Test]
		public void W6_GunshotReadyDuration_EliteFasterThanRecruit()
		{
			float recruit = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Recruit));
			float soldier = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
			float corporal = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Corporal));
			float veteran = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Veteran));
			float elite = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Elite));
			Assert.Greater(recruit, soldier);
			Assert.Greater(soldier, corporal);
			Assert.Greater(corporal, veteran);
			Assert.Greater(veteran, elite);
			Assert.Greater(elite, 0f);
		}

		[Test]
		public void W7_ForRankGunshot_IsTimedNotInstant()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0.01f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			Assert.AreEqual(ReadinessState.LowReady, controller.Context.TransitionTo);
			Assert.Greater(controller.LastRequest.Duration, 0f);
			float afterRaise = controller.LastRequest.Duration + 0.05f;
			Assert.Less(afterRaise, ReadinessMath.EffectiveCalmDownDelay(controller.Profile));
			controller.Tick(afterRaise, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
		}
		#endregion

		#region X ToAim ordering
		[Test]
		public void X1_Elite_HighReadyFasterThanLowReadyFasterThanPatrol()
		{
			AssertWithinRankAimOrder(ReadinessRankKind.Elite);
		}

		[Test]
		public void X2_Soldier_HighReadyFasterThanLowReadyFasterThanPatrol()
		{
			AssertWithinRankAimOrder(ReadinessRankKind.Soldier);
		}

		[Test]
		public void X3_Recruit_HighReadyFasterThanLowReadyFasterThanPatrol()
		{
			AssertWithinRankAimOrder(ReadinessRankKind.Recruit);
		}

		[Test]
		public void X4_PatrolToAim_EliteFasterThanVeteran()
		{
			Assert.Less(PatrolToAim(ReadinessRankKind.Elite), PatrolToAim(ReadinessRankKind.Veteran));
		}

		[Test]
		public void X5_PatrolToAim_VeteranFasterThanCorporal()
		{
			Assert.Less(PatrolToAim(ReadinessRankKind.Veteran), PatrolToAim(ReadinessRankKind.Corporal));
		}

		[Test]
		public void X6_PatrolToAim_CorporalFasterThanSoldier()
		{
			Assert.Less(PatrolToAim(ReadinessRankKind.Corporal), PatrolToAim(ReadinessRankKind.Soldier));
		}

		[Test]
		public void X7_PatrolToAim_SoldierFasterThanRecruit()
		{
			Assert.Less(PatrolToAim(ReadinessRankKind.Soldier), PatrolToAim(ReadinessRankKind.Recruit));
		}

		[Test]
		public void X8_PreAimToAim_FastestWithinRank()
		{
			ReadinessProfile elite = ReadinessProfile.ForRank(ReadinessRankKind.Elite);
			Assert.Less(
				ReadinessMath.AimTransitionDuration(ReadinessState.PreAim, in elite),
				ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in elite));
		}

		[Test]
		public void X9_NotReadyToAim_RecruitSlowerThanElitePatrol()
		{
			ReadinessProfile recruit = ReadinessProfile.ForRank(ReadinessRankKind.Recruit);
			float recruitNotReady = ReadinessMath.AimTransitionDuration(ReadinessState.NotReady, in recruit);
			Assert.Greater(recruitNotReady, PatrolToAim(ReadinessRankKind.Elite));
		}
		#endregion

		#region Y Direct HostileVisible
		[Test]
		public void Y1_Recruit_HostileVisible_DirectAim()
		{
			AssertDirectAim(ReadinessRankKind.Recruit, ReadinessState.NotReady);
		}

		[Test]
		public void Y2_Soldier_HostileVisible_DirectAim()
		{
			AssertDirectAim(ReadinessRankKind.Soldier, ReadinessState.Patrol);
		}

		[Test]
		public void Y3_Corporal_HostileVisible_DirectAim()
		{
			AssertDirectAim(ReadinessRankKind.Corporal, ReadinessState.Patrol);
		}

		[Test]
		public void Y4_Veteran_HostileVisible_DirectAim()
		{
			AssertDirectAim(ReadinessRankKind.Veteran, ReadinessState.Patrol);
		}

		[Test]
		public void Y5_Elite_HostileVisible_DirectAim()
		{
			AssertDirectAim(ReadinessRankKind.Elite, ReadinessState.Patrol);
		}
		#endregion

		#region Z Decay / parameters / stability / fatigue / log
		[Test]
		public void Z1_CalmDownDelay_EliteLongerThanRecruit()
		{
			float recruit = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Recruit));
			float soldier = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
			float corporal = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Corporal));
			float veteran = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Veteran));
			float elite = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Elite));
			Assert.Greater(soldier, recruit);
			Assert.Greater(corporal, soldier);
			Assert.Greater(veteran, corporal);
			Assert.Greater(elite, veteran);
		}

		[Test]
		public void Z2_DecayLadder_Soldier_AimPreAimReadyCalm()
		{
			AssertDecayLadder(ReadinessRankKind.Soldier, ReadinessState.LowReady, ReadinessState.Patrol);
		}

		[Test]
		public void Z3_DecayLadder_Elite_AimPreAimReadyCalm()
		{
			AssertDecayLadder(ReadinessRankKind.Elite, ReadinessState.HighReady, ReadinessState.Patrol);
		}

		[Test]
		public void Z4_DecayLadder_Recruit_EndsNotReady()
		{
			AssertDecayLadder(ReadinessRankKind.Recruit, ReadinessState.LowReady, ReadinessState.NotReady);
		}

		[Test]
		public void Z5_ToReadyToAimDecayResponse_AreOrderedByRank()
		{
			Assert.Less(
				ReadinessProfile.ForRank(ReadinessRankKind.Recruit).ToReadySpeed,
				ReadinessProfile.ForRank(ReadinessRankKind.Soldier).ToReadySpeed);
			Assert.Less(
				ReadinessProfile.ForRank(ReadinessRankKind.Soldier).ToAimSpeed,
				ReadinessProfile.ForRank(ReadinessRankKind.Elite).ToAimSpeed);
			Assert.Less(
				ReadinessProfile.ForRank(ReadinessRankKind.Recruit).DecaySpeed,
				ReadinessProfile.ForRank(ReadinessRankKind.Elite).DecaySpeed);
			Assert.Less(
				ReadinessProfile.ForRank(ReadinessRankKind.Recruit).RankReactionModifier,
				ReadinessProfile.ForRank(ReadinessRankKind.Elite).RankReactionModifier);
		}

		[Test]
		public void Z6_RankReactionModifier_MatchesToAimSpeed()
		{
			ReadinessProfile veteran = ReadinessProfile.ForRank(ReadinessRankKind.Veteran);
			Assert.AreEqual(veteran.ToAimSpeed, veteran.RankReactionModifier);
			Assert.AreEqual(veteran.AimTransitionModifier, veteran.RankReactionModifier);
		}

		[Test]
		public void Z7_RepeatedGunshot_DoesNotLoop()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			int afterFirst = controller.TransitionRequestCount;
			controller.Tick(0.3f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(afterFirst, controller.TransitionRequestCount);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
		}

		[Test]
		public void Z8_RepeatedHostileVisible_DoesNotLoop()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			int afterFirst = controller.TransitionRequestCount;
			controller.Tick(0.3f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(afterFirst, controller.TransitionRequestCount);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void Z9_HostileLost_DoesNotCreateRaiseLoop()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			int raises = controller.TransitionRequestCount;
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.4f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(raises, controller.TransitionRequestCount);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void Z10_ArmFatigue_DoesNotChangeForRankDurations()
		{
			float a = DurationWithFatigue(0f);
			float b = DurationWithFatigue(0.5f);
			float c = DurationWithFatigue(1f);
			Assert.AreEqual(a, b);
			Assert.AreEqual(a, c);
			Assert.Greater(a, 0f);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
		}

		[Test]
		public void Z11_TransitionLog_HasRankDurationAndModifiers()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Veteran), 0f);
			controller.Tick(0.01f, ReadinessStimulus.HostileVisible);
			string payload = controller.LastTransitionPayload;
			Assert.IsTrue(payload.IndexOf("from=Patrol", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("to=Aim", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("rank=Veteran", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("duration=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("reason=HostileVisible", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("profileDuration=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("rankModifier=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("rank=Veteran", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void Z12_Instant_StillSnapsGunshot()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			Assert.AreEqual(0f, controller.LastRequest.Duration);
		}

		[Test]
		public void Z13_ControllerSeconds_ElitePatrolToAimFasterThanRecruit()
		{
			float elite = SecondsUntil(
				ReadinessRankKind.Elite,
				ReadinessStimulus.HostileVisible,
				ReadinessState.Aim,
				ReadinessStimulus.HostileVisible);
			float recruit = SecondsUntil(
				ReadinessRankKind.Recruit,
				ReadinessStimulus.HostileVisible,
				ReadinessState.Aim,
				ReadinessStimulus.HostileVisible);
			Assert.Less(elite, recruit);
		}

		[Test]
		public void Z14_GunshotState_IsNotAim()
		{
			Assert.AreNotEqual(ReadinessState.Aim, ReadinessProfile.ForRank(ReadinessRankKind.Elite).GunshotState);
			Assert.AreNotEqual(ReadinessState.Aim, ReadinessProfile.ForRank(ReadinessRankKind.Recruit).GunshotState);
		}
		#endregion

		#region Private Methods
		private static ReadinessState InstantAfter(ReadinessRankKind _rank, ReadinessStimulus _stimulus)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			controller.Tick(0.2f, _stimulus);
			return controller.CurrentState;
		}

		private static float PatrolToAim(ReadinessRankKind _rank)
		{
			ReadinessProfile profile = ReadinessProfile.ForRank(_rank);
			return ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in profile);
		}

		private static void AssertWithinRankAimOrder(ReadinessRankKind _rank)
		{
			ReadinessProfile profile = ReadinessProfile.ForRank(_rank);
			float high = ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in profile);
			float low = ReadinessMath.AimTransitionDuration(ReadinessState.LowReady, in profile);
			float patrol = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in profile);
			Assert.Less(high, low);
			Assert.Less(low, patrol);
		}

		private static void AssertDirectAim(ReadinessRankKind _rank, ReadinessState _start)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(_rank), 0f);
			Assert.AreEqual(_start, controller.CurrentState);
			controller.Tick(0.01f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(_start, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			Assert.AreEqual(ReadinessState.Aim, controller.Context.TransitionTo);
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.LowReady,
				ReadinessState.HighReady));
			Assert.IsFalse(ReadinessLog.ContainsTransition(
				controller.LogLines,
				ReadinessState.HighReady,
				ReadinessState.PreAim));
			controller.Tick(4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(_start, controller.Context.PreviousState);
			Assert.IsFalse(controller.RequestsFire);
		}

		private static void AssertDecayLadder(
			ReadinessRankKind _rank,
			ReadinessState _ready,
			ReadinessState _calm)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.Instant(_rank), 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(4f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(_ready, controller.CurrentState);
			controller.Tick(6f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(_calm, controller.CurrentState);
		}

		private static float DurationWithFatigue(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.SetArmFatigue(_fatigue, 1f - _fatigue * 0.5f);
			controller.Tick(0.01f, ReadinessStimulus.HostileVisible);
			return controller.LastRequest.Duration;
		}

		private static float SecondsUntil(
			ReadinessRankKind _rank,
			ReadinessStimulus _first,
			ReadinessState _target,
			ReadinessStimulus _hold)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(_rank), 0f);
			controller.Tick(0f, _first);
			if (controller.CurrentState == _target)
				return 0f;

			const float step = 0.02f;
			float t = 0f;
			for (int i = 0; i < 400; i++)
			{
				t += step;
				controller.Tick(t, _hold);
				if (controller.CurrentState == _target)
					return t;
			}

			return t;
		}
		#endregion
	}
}
