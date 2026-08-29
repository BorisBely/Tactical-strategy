using NUnit.Framework;

namespace AI.Tests
{
	/// <summary>
	/// #14B.0 Readiness contract. Independent axis. Aim ≠ Fire. HostileVisible may skip to Aim.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessContractTests
	{
		#region A Allowed transitions
		[Test]
		public void A1_NotReady_ToPatrol_IsAllowed()
		{
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.NotReady, ReadinessState.Patrol));
		}

		[Test]
		public void A2_Patrol_Raises_AreAllowed()
		{
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.Patrol, ReadinessState.LowReady));
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.Patrol, ReadinessState.HighReady));
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.Patrol, ReadinessState.PreAim));
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.Patrol, ReadinessState.Aim));
		}

		[Test]
		public void A3_ReadyAndPreAim_ToAim_AreAllowed()
		{
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.LowReady, ReadinessState.Aim));
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.HighReady, ReadinessState.Aim));
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.PreAim, ReadinessState.Aim));
		}

		[Test]
		public void A4_Aim_ToPatrol_IsNotAllowed()
		{
			Assert.IsFalse(ReadinessMath.IsAllowed(ReadinessState.Aim, ReadinessState.Patrol));
			Assert.IsFalse(ReadinessMath.IsAllowed(ReadinessState.Aim, ReadinessState.LowReady));
			Assert.IsFalse(ReadinessMath.IsAllowed(ReadinessState.Aim, ReadinessState.NotReady));
		}

		[Test]
		public void A5_NotReady_ToAim_IsAllowed()
		{
			Assert.IsTrue(ReadinessMath.IsAllowed(ReadinessState.NotReady, ReadinessState.Aim));
		}
		#endregion

		#region B Rank initialization
		[Test]
		public void B1_Recruit_StartsNotReady()
		{
			Assert.AreEqual(ReadinessState.NotReady, ReadinessMath.InitialState(ReadinessRankKind.Recruit));
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Recruit, 0f);
			Assert.AreEqual(ReadinessState.NotReady, controller.CurrentState);
			Assert.AreEqual(ReadinessChangeReason.Initial, controller.Context.LastChangeReason);
		}

		[Test]
		public void B2_SoldierAndAbove_StartPatrol()
		{
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Soldier));
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Corporal));
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Veteran));
			Assert.AreEqual(ReadinessState.Patrol, ReadinessMath.InitialState(ReadinessRankKind.Elite));
		}
		#endregion

		#region C Sound reaction
		[Test]
		public void C1_RecruitOrSoldier_Gunshot_LowReady()
		{
			AssertStateAfter(ReadinessRankKind.Recruit, ReadinessStimulus.GunshotHeard, ReadinessState.LowReady);
			AssertStateAfter(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard, ReadinessState.LowReady);
		}

		[Test]
		public void C2_CorporalAndAbove_Gunshot_HighReady()
		{
			AssertStateAfter(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard, ReadinessState.HighReady);
			AssertStateAfter(ReadinessRankKind.Veteran, ReadinessStimulus.GunshotHeard, ReadinessState.HighReady);
			AssertStateAfter(ReadinessRankKind.Elite, ReadinessStimulus.GunshotHeard, ReadinessState.HighReady);
		}
		#endregion

		#region D Direct hostile
		[Test]
		public void D1_AnyState_HostileVisible_GoesToAim()
		{
			AssertHostileToAim(ReadinessRankKind.Recruit, ReadinessState.NotReady);
			AssertHostileToAim(ReadinessRankKind.Soldier, ReadinessState.Patrol);
			AssertHostileToAimFromReady(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard);
			AssertHostileToAimFromReady(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard);

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
		public void D2_NotReady_HostileVisible_Aim()
		{
			AssertStateAfter(ReadinessRankKind.Recruit, ReadinessStimulus.HostileVisible, ReadinessState.Aim);
		}
		#endregion

		#region E No forced intermediate
		[Test]
		public void E1_Patrol_HostileVisible_DoesNotVisitReadyStates()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreNotEqual(ReadinessState.LowReady, controller.Context.PreviousState);
			Assert.AreNotEqual(ReadinessState.HighReady, controller.Context.PreviousState);
			Assert.AreNotEqual(ReadinessState.PreAim, controller.Context.PreviousState);
			Assert.AreEqual(ReadinessState.Patrol, controller.Context.PreviousState);
		}

		[Test]
		public void E2_TimedAimRaise_KeepsStartStateUntilComplete()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0.1f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.Context.HasPendingTransition);
			Assert.AreEqual(ReadinessState.Aim, controller.Context.TransitionTo);
			controller.Tick(1f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ReadinessState.Patrol, controller.Context.PreviousState);
		}
		#endregion

		#region F Aim is not Fire
		[Test]
		public void F1_Aim_DoesNotRequestFire()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessDecision decision = controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, decision.State);
			Assert.IsFalse(decision.RequestsFire);
			Assert.IsFalse(controller.RequestsFire);
		}
		#endregion

		#region G Decay
		[Test]
		public void G1_Aim_WithActivity_StaysAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void G2_Aim_Expired_GoesPreAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
		}

		[Test]
		public void G3_PreAim_Expired_GoesHeardReady()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			controller.Tick(4f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
		}

		[Test]
		public void G4_Ready_Expired_GoesCalm()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			controller.Tick(4f, ReadinessStimulus.CombatActivityExpired);
			controller.Tick(6f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.AreEqual(ReadinessChangeReason.Calm, controller.Context.LastChangeReason);
		}
		#endregion

		#region H Hysteresis
		[Test]
		public void H1_ShortContactLost_DoesNotDropAimToPatrol()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.CombatContactLost);
			controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}
		#endregion

		#region I Arm fatigue placeholder
		[Test]
		public void I1_FatigueValues_DoNotChangeTransition()
		{
			ReadinessDecision zero = TickHostileWithFatigue(0f, 1f);
			ReadinessDecision half = TickHostileWithFatigue(0.5f, 0.5f);
			ReadinessDecision full = TickHostileWithFatigue(1f, 2f);
			Assert.AreEqual(ReadinessState.Aim, zero.State);
			Assert.AreEqual(zero.State, half.State);
			Assert.AreEqual(zero.State, full.State);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());

			ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
			float fromPatrol = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier);
			float fromHigh = ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in soldier);
			Assert.Greater(fromPatrol, fromHigh);
			Assert.AreEqual(
				fromPatrol,
				ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier));
		}
		#endregion

		#region Private Methods
		private static void AssertStateAfter(
			ReadinessRankKind _rank,
			ReadinessStimulus _stimulus,
			ReadinessState _expected)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			controller.Tick(0.2f, _stimulus);
			Assert.AreEqual(_expected, controller.CurrentState);
		}

		private static void AssertHostileToAim(ReadinessRankKind _rank, ReadinessState _start)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			Assert.AreEqual(_start, controller.CurrentState);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		private static void AssertHostileToAimFromReady(ReadinessRankKind _rank, ReadinessStimulus _setup)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			controller.Tick(0.1f, _setup);
			ReadinessState ready = controller.CurrentState;
			Assert.AreNotEqual(ReadinessState.Aim, ready);
			controller.Tick(0.3f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(ready, controller.Context.PreviousState);
		}

		private static ReadinessDecision TickHostileWithFatigue(float _fatigue, float _modifier)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.SetArmFatigue(_fatigue, _modifier);
			return controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		}
		#endregion
	}
}
