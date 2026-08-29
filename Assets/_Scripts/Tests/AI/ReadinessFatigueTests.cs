using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14B.6 ArmFatigue. Load / recover / freeze. Three independent physical multipliers.
	/// Does not become a ReadinessState. Does not retune Vision / G6 / Cover / Movement.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessFatigueTests
	{
		#region F Load / recover math
		[Test]
		public void F1_Clamp_BelowZero()
		{
			Assert.AreEqual(0f, ArmFatigueMath.Clamp01(-0.4f));
		}

		[Test]
		public void F2_Clamp_AboveOne()
		{
			Assert.AreEqual(1f, ArmFatigueMath.Clamp01(1.8f));
		}

		[Test]
		public void F3_Patrol_LoadZero()
		{
			Assert.AreEqual(0f, ArmFatigueMath.LoadRate(ReadinessState.Patrol, Play()));
		}

		[Test]
		public void F4_NotReady_LoadZero()
		{
			Assert.AreEqual(0f, ArmFatigueMath.LoadRate(ReadinessState.NotReady, Play()));
		}

		[Test]
		public void F5_LoadOrder_Low_lt_High()
		{
			ArmFatigueProfile profile = Play();
			Assert.Greater(
				ArmFatigueMath.LoadRate(ReadinessState.HighReady, in profile),
				ArmFatigueMath.LoadRate(ReadinessState.LowReady, in profile));
		}

		[Test]
		public void F6_LoadOrder_High_lt_PreAim()
		{
			ArmFatigueProfile profile = Play();
			Assert.Greater(
				ArmFatigueMath.LoadRate(ReadinessState.PreAim, in profile),
				ArmFatigueMath.LoadRate(ReadinessState.HighReady, in profile));
		}

		[Test]
		public void F7_LoadOrder_PreAim_lt_Aim()
		{
			ArmFatigueProfile profile = Play();
			Assert.Greater(
				ArmFatigueMath.LoadRate(ReadinessState.Aim, in profile),
				ArmFatigueMath.LoadRate(ReadinessState.PreAim, in profile));
		}

		[Test]
		public void F8_Firing_MaxesStateLoad()
		{
			ArmFatigueProfile profile = Play();
			float aim = ArmFatigueMath.EffectiveLoadRate(ReadinessState.Aim, false, in profile);
			float firingAim = ArmFatigueMath.EffectiveLoadRate(ReadinessState.Aim, true, in profile);
			float firingPatrol = ArmFatigueMath.EffectiveLoadRate(ReadinessState.Patrol, true, in profile);
			Assert.Greater(firingAim, aim);
			Assert.AreEqual(profile.LoadRateFiring, firingPatrol);
			Assert.AreEqual(firingAim, firingPatrol);
		}

		[Test]
		public void F9_RecoveryWhenUnloaded()
		{
			ArmFatigueProfile profile = Play();
			bool loaded;
			float next = ArmFatigueMath.Step(0.4f, 1f, ReadinessState.Patrol, false, true, in profile, out loaded);
			Assert.IsFalse(loaded);
			Assert.Less(next, 0.4f);
			Assert.Greater(next, 0f);
		}

		[Test]
		public void F10_NoRecoveryWhileLoaded()
		{
			ArmFatigueProfile profile = Play();
			bool loaded;
			float next = ArmFatigueMath.Step(0.2f, 1f, ReadinessState.Aim, false, true, in profile, out loaded);
			Assert.IsTrue(loaded);
			Assert.Greater(next, 0.2f);
		}

		[Test]
		public void F11_Disabled_NoAccumulate()
		{
			ArmFatigueProfile profile = ArmFatigueProfile.Disabled();
			bool loaded;
			float next = ArmFatigueMath.Step(0.3f, 2f, ReadinessState.Aim, true, true, in profile, out loaded);
			Assert.IsFalse(loaded);
			Assert.AreEqual(0.3f, next);
		}
		#endregion

		#region G Physical effects
		[Test]
		public void F12_AimTime_Increases()
		{
			ArmFatigueProfile profile = Play();
			Assert.Greater(
				ArmFatigueMath.FinalAimTime(1f, 1f, in profile),
				ArmFatigueMath.FinalAimTime(1f, 0f, in profile));
			Assert.AreEqual(1f, ArmFatigueMath.FinalAimTime(1f, 0f, in profile));
			Assert.AreEqual(profile.FatigueAimMultiplier, ArmFatigueMath.FinalAimTime(1f, 1f, in profile));
		}

		[Test]
		public void F13_RecoilControl_Decreases()
		{
			ArmFatigueProfile profile = Play();
			Assert.Less(
				ArmFatigueMath.EffectiveRecoilControl(50f, 1f, in profile),
				ArmFatigueMath.EffectiveRecoilControl(50f, 0f, in profile));
			Assert.AreEqual(50f, ArmFatigueMath.EffectiveRecoilControl(50f, 0f, in profile));
		}

		[Test]
		public void F14_TurnTime_Increases()
		{
			ArmFatigueProfile profile = Play();
			Assert.Greater(
				ArmFatigueMath.FinalTurnToTargetTime(1f, in profile),
				ArmFatigueMath.FinalTurnToTargetTime(0f, in profile));
			Assert.AreEqual(profile.BaseTurnToTargetTime, ArmFatigueMath.FinalTurnToTargetTime(0f, in profile));
		}

		[Test]
		public void F15_Multipliers_Independent()
		{
			ArmFatigueEffects effects = ArmFatigueMath.Evaluate(1f, Play());
			Assert.AreNotEqual(effects.AimTimeMultiplier, effects.RecoilControlModifier);
			Assert.AreNotEqual(effects.AimTimeMultiplier, effects.TurnTimeMultiplier);
			Assert.AreNotEqual(effects.RecoilControlModifier, effects.TurnTimeMultiplier);
			Assert.Greater(effects.AimTimeMultiplier, 1f);
			Assert.Less(effects.RecoilControlModifier, 1f);
			Assert.Greater(effects.TurnTimeMultiplier, 1f);
		}

		[Test]
		public void F16_DoesNotAffectReadinessState()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsReadinessState());
		}

		[Test]
		public void F17_DoesNotAffectPerception()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsPerception());
		}

		[Test]
		public void F18_DoesNotAffectG6()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsG6());
		}

		[Test]
		public void F19_DoesNotAffectCover()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsCover());
		}

		[Test]
		public void F20_DoesNotAffectMovement()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsMovement());
		}

		[Test]
		public void F21_LogicalDuration_StillIgnoresFatigue()
		{
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
			ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
			Assert.AreEqual(
				ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier),
				ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier));
		}

		[Test]
		public void F22_ArmLoadMultiplier_IsOne()
		{
			Assert.AreEqual(1f, Play().ArmLoadMultiplier);
			Assert.AreEqual(1f, ReadinessProfile.ForRank(ReadinessRankKind.Soldier).ArmFatigue.ArmLoadMultiplier);
		}

		[Test]
		public void F23_RankModifiers_AreOne()
		{
			ReadinessRankKind[] ranks =
			{
				ReadinessRankKind.Recruit,
				ReadinessRankKind.Soldier,
				ReadinessRankKind.Corporal,
				ReadinessRankKind.Veteran,
				ReadinessRankKind.Elite
			};
			for (int i = 0; i < ranks.Length; i++)
			{
				ArmFatigueProfile profile = ReadinessProfile.ForRank(ranks[i]).ArmFatigue;
				Assert.AreEqual(1f, profile.FatigueLoadModifier);
				Assert.AreEqual(1f, profile.FatigueRecoveryModifier);
			}
		}

		[Test]
		public void F24_ThresholdBands()
		{
			Assert.AreEqual(0, ArmFatigueMath.ThresholdBand(0f));
			Assert.AreEqual(1, ArmFatigueMath.ThresholdBand(0.25f));
			Assert.AreEqual(2, ArmFatigueMath.ThresholdBand(0.5f));
			Assert.AreEqual(3, ArmFatigueMath.ThresholdBand(0.75f));
			Assert.AreEqual(4, ArmFatigueMath.ThresholdBand(1f));
		}

		[Test]
		public void F25_LogFormat_ThresholdRecoveryEffect()
		{
			Assert.AreEqual("threshold=0.25", ArmFatigueLog.FormatThreshold(1));
			Assert.AreEqual("threshold=0.50", ArmFatigueLog.FormatThreshold(2));
			Assert.AreEqual("threshold=0.75", ArmFatigueLog.FormatThreshold(3));
			Assert.AreEqual("max", ArmFatigueLog.FormatThreshold(4));
			Assert.AreEqual("recovery-start", ArmFatigueLog.FormatRecoveryStart());
			ArmFatigueEffects effects = ArmFatigueMath.Evaluate(0.5f, Play());
			string payload = ArmFatigueLog.FormatEffect(in effects);
			Assert.IsTrue(payload.IndexOf("fatigue=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("aimMultiplier=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("recoilMultiplier=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(payload.IndexOf("turnMultiplier=", StringComparison.Ordinal) >= 0);
		}
		#endregion

		#region H Controller
		[Test]
		public void F26_Instant_DoesNotAccumulate()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(5f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(0f, controller.ArmFatigue);
		}

		[Test]
		public void F27_ForRank_AimAccumulates()
		{
			ReadinessController controller = ForRankInAim();
			float before = controller.ArmFatigue;
			float t = controller.Context.StateEnterTime + 2f;
			controller.Tick(t, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.Greater(controller.ArmFatigue, before);
			Assert.Greater(controller.ArmFatigue, 0f);
		}

		[Test]
		public void F28_Firing_FasterThanAim()
		{
			ReadinessController aim = ForRankInAim();
			ReadinessController firing = ForRankInAim();
			float t = Mathf.Max(aim.Context.StateEnterTime, firing.Context.StateEnterTime) + 1f;
			aim.Tick(t, ReadinessStimulus.HostileVisible);
			var fireFrame = new ReadinessFrame { HostileVisible = true, Firing = true };
			firing.Tick(t, in fireFrame);
			Assert.Greater(firing.ArmFatigue, aim.ArmFatigue);
		}

		[Test]
		public void F29_Patrol_Recovers()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.RequestTransition(ReadinessState.HighReady, ReadinessChangeReason.Gunshot, 0f);
			controller.Tick(0.3f, ReadinessStimulus.None);
			controller.Tick(2f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			float loaded = controller.ArmFatigue;
			Assert.Greater(loaded, 0f);
			controller.RequestTransition(ReadinessState.Patrol, ReadinessChangeReason.Calm, 2f);
			controller.Tick(2.8f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.Less(controller.ArmFatigue, loaded);
			Assert.Greater(controller.ArmFatigue, 0f);
		}

		[Test]
		public void F30_Clamp_OnController()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.SetArmFatigue(-1f, 1f);
			Assert.AreEqual(0f, controller.ArmFatigue);
			controller.SetArmFatigue(2f, 1f);
			Assert.AreEqual(1f, controller.ArmFatigue);
		}

		[Test]
		public void F31_Fatigue_DoesNotChangeState()
		{
			Assert.AreEqual(ReadinessState.Aim, TickHostileWithFatigue(0f).CurrentState);
			Assert.AreEqual(ReadinessState.Aim, TickHostileWithFatigue(1f).CurrentState);
		}

		[Test]
		public void F32_Fatigue_DoesNotChangeLogicalDuration()
		{
			float a = DurationWithFatigue(0f);
			float b = DurationWithFatigue(1f);
			Assert.AreEqual(a, b);
			Assert.Greater(a, 0f);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
		}

		[Test]
		public void F33_StateChange_DoesNotZeroFatigue()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.SetArmFatigue(0.4f, 1f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			Assert.AreEqual(0.4f, controller.ArmFatigue);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(0.4f, controller.ArmFatigue);
		}

		[Test]
		public void F34_LifeGate_FreezesFatigue()
		{
			ReadinessController controller = ForRankInAim();
			float t = controller.Context.StateEnterTime + 0.5f;
			controller.Tick(t, ReadinessStimulus.HostileVisible);
			float frozen = controller.ArmFatigue;
			Assert.Greater(frozen, 0f);
			int changes = controller.Context.ChangeCount;
			controller.SetAllowed(false);
			controller.Tick(t + 8f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(frozen, controller.ArmFatigue);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(changes, controller.Context.ChangeCount);
			controller.SetAllowed(true);
			controller.Tick(t + 8.05f, ReadinessStimulus.HostileVisible);
			Assert.Greater(controller.ArmFatigue, frozen);
		}

		[Test]
		public void F35_Logs_NotEveryTick()
		{
			ReadinessController controller = ForRankInAim();
			float t = controller.Context.StateEnterTime;
			for (int i = 0; i < 8; i++)
			{
				t += 0.05f;
				controller.Tick(t, ReadinessStimulus.HostileVisible);
			}

			Assert.Less(controller.ArmFatigue, 0.25f);
			Assert.AreEqual(string.Empty, controller.LastFatiguePayload);
			Assert.AreEqual(string.Empty, controller.LastFatigueEffectPayload);
		}

		[Test]
		public void F36_Logs_ThresholdAndRecoveryStart()
		{
			ReadinessController controller = ForRankInAim();
			float t = controller.Context.StateEnterTime;
			string last = controller.LastFatiguePayload;
			int events = 0;
			for (int i = 0; i < 40 && events < 1; i++)
			{
				t += 0.1f;
				controller.Tick(t, ReadinessStimulus.HostileVisible);
				if (controller.LastFatiguePayload != last)
				{
					last = controller.LastFatiguePayload;
					events++;
				}
			}

			Assert.AreEqual("threshold=0.25", controller.LastFatiguePayload);
			Assert.IsTrue(controller.LastFatigueEffectPayload.IndexOf("aimMultiplier=", StringComparison.Ordinal) >= 0);

			var recover = new ReadinessController();
			recover.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			recover.RequestTransition(ReadinessState.HighReady, ReadinessChangeReason.Gunshot, 0f);
			recover.Tick(0.3f, ReadinessStimulus.None);
			recover.Tick(2f, ReadinessStimulus.None);
			recover.RequestTransition(ReadinessState.Patrol, ReadinessChangeReason.Calm, 2f);
			recover.Tick(2.8f, ReadinessStimulus.None);
			Assert.AreEqual("recovery-start", recover.LastFatiguePayload);
		}

		[Test]
		public void F37_Binding_WithoutAi_Neutral()
		{
			GameObject go = new GameObject("FatigueBindingNone");
			try
			{
				ArmFatigueEffects effects = ArmFatigueBinding.EffectsOrNeutral(go.transform);
				Assert.AreEqual(0f, effects.Fatigue);
				Assert.AreEqual(1f, effects.AimTimeMultiplier);
				Assert.AreEqual(1f, effects.RecoilControlModifier);
				Assert.AreEqual(1f, effects.TurnTimeMultiplier);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void F38_Binding_WithAi_Applies()
		{
			GameObject go = new GameObject("FatigueBindingAi");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ArmFatigueEffects effects = ArmFatigueBinding.EffectsOrNeutral(ai);
				Assert.AreEqual(1f, effects.Fatigue);
				Assert.Greater(effects.AimTimeMultiplier, 1f);
				Assert.Less(effects.RecoilControlModifier, 1f);
				Assert.Greater(effects.TurnTimeMultiplier, 1f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void F39_CombatIntentAndAiState_Unchanged()
		{
			GameObject go = new GameObject("FatigueIndependence");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.AreEqual(ReadinessState.Patrol, ai.Readiness.CurrentState);
				Assert.IsFalse(ai.Readiness.RequestsFire);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void F40_RecoilAdded_WorsensWithFatigue()
		{
			GameObject go = new GameObject("FatigueRecoilStats");
			try
			{
				UnitCombatStats stats = go.AddComponent<UnitCombatStats>();
				stats.ApplySkills(50f, 50f, 50f);
				float baseline = stats.GetRecoilAddedMultiplier();
				float tiredControl = ArmFatigueMath.EffectiveRecoilControl(stats.RecoilControl, 1f, Play());
				float tired = stats.GetRecoilAddedMultiplier(tiredControl);
				Assert.Greater(tired, baseline);
				Assert.Less(tiredControl, stats.RecoilControl);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Private Methods
		private static ArmFatigueProfile Play() => ArmFatigueProfile.PlayPrototype();

		private static ReadinessController ForRankInAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0f, ReadinessStimulus.HostileVisible);
			if (controller.CurrentState != ReadinessState.Aim || controller.Context.HasPendingTransition)
			{
				float t = controller.Context.TransitionStartTime + controller.Context.TransitionDuration + 0.05f;
				controller.Tick(t, ReadinessStimulus.HostileVisible);
			}

			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.Context.HasPendingTransition);
			return controller;
		}

		private static ReadinessController TickHostileWithFatigue(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.SetArmFatigue(_fatigue, 1f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			return controller;
		}

		private static float DurationWithFatigue(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.SetArmFatigue(_fatigue, 1f);
			controller.Tick(0.01f, ReadinessStimulus.HostileVisible);
			return controller.LastRequest.Duration;
		}
		#endregion
	}
}
