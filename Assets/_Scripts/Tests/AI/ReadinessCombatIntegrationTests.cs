using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14B.7 Readiness × ArmFatigue × Combat. Closed path only. Fatigue does not drive Readiness.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessCombatIntegrationTests
	{
		#region C AimTime through combat
		[Test]
		public void C1_AimTime_FreshShortest()
		{
			Assert.Less(AimAt(0f), AimAt(0.5f));
		}

		[Test]
		public void C2_AimTime_HalfLongerThanFresh()
		{
			Assert.Less(AimAt(0.5f), AimAt(1f));
		}

		[Test]
		public void C3_AimTime_FullLongest()
		{
			Assert.Greater(AimAt(1f), AimAt(0f));
		}

		[Test]
		public void C4_AimTime_OrderThroughAimProgress()
		{
			Assert.Less(AimAt(0f), AimAt(0.5f));
			Assert.Less(AimAt(0.5f), AimAt(1f));
		}

		[Test]
		public void C5_LogicalHostileVisible_DurationUnchanged()
		{
			float a = LogicalAimDuration(0f);
			float b = LogicalAimDuration(1f);
			Assert.AreEqual(a, b);
			Assert.Greater(a, 0f);
			Assert.IsFalse(ReadinessMath.FatigueAffectsResult());
		}

		[Test]
		public void C6_HostileVisible_StillAim_AtHighFatigue()
		{
			Assert.AreEqual(ReadinessState.Aim, InstantAim(1f).CurrentState);
		}

		[Test]
		public void C7_Pose_StillAiming_AtHighFatigue()
		{
			Assert.AreEqual(WeaponPoseState.Aiming, InstantAim(1f).PoseRequest.Pose);
		}
		#endregion

		#region D Turn through aiming
		[Test]
		public void C8_Turn_FreshFasterThanHalf()
		{
			Assert.Less(TurnAt(0f), TurnAt(0.5f));
		}

		[Test]
		public void C9_Turn_HalfFasterThanFull()
		{
			Assert.Less(TurnAt(0.5f), TurnAt(1f));
		}

		[Test]
		public void C10_Turn_ThroughAimingComponent()
		{
			Assert.Less(TurnAt(0f), TurnAt(1f));
		}

		[Test]
		public void C11_Turn_NinetyDegrees_ScalesWithFatigue()
		{
			float fresh = TurnDelta(0f, 90f);
			float tired = TurnDelta(1f, 90f);
			Assert.Greater(tired, fresh);
		}
		#endregion

		#region E Recoil through RecoilOffset path
		[Test]
		public void C12_RecoilControl_DecreasesWithFatigue()
		{
			Assert.Greater(ControlAt(0f), ControlAt(0.5f));
			Assert.Greater(ControlAt(0.5f), ControlAt(1f));
		}

		[Test]
		public void C13_RecoveryMultiplier_DecreasesWithFatigue()
		{
			Assert.Greater(RecoveryAt(0f), RecoveryAt(1f));
		}

		[Test]
		public void C14_RecoilOffset_RecoversSlowerWhenTired()
		{
			float freshLeft = CombatFatigueProbe.RemainingRecoilAfter(
				RecoveryAt(0f), CombatFatigueProbe.RecoilProbeSeconds);
			float tiredLeft = CombatFatigueProbe.RemainingRecoilAfter(
				RecoveryAt(1f), CombatFatigueProbe.RecoilProbeSeconds);
			Assert.Greater(tiredLeft, freshLeft);
		}

		[Test]
		public void C15_RecoilOffset_UsesExistingRecover()
		{
			Vector2 start = new Vector2(4f, 0f);
			Vector2 next = WeaponRecoilMath.Recover(start, 2f, 0.5f);
			Assert.Less(next.magnitude, start.magnitude);
			Assert.Greater(next.magnitude, 0f);
		}

		[Test]
		public void C16_RecoilAdded_WorsensWhenTired()
		{
			GameObject go = LiveHost("C16");
			try
			{
				UnitCombatStats stats = go.GetComponent<UnitCombatStats>();
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(0f, 1f);
				float fresh = stats.GetRecoilAddedMultiplier(
					CombatFatigueProbe.SampleEffectiveRecoilControl(go.transform));
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(1f, 1f);
				float tired = stats.GetRecoilAddedMultiplier(
					CombatFatigueProbe.SampleEffectiveRecoilControl(go.transform));
				Assert.Greater(tired, fresh);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region F Accumulation from real load
		[Test]
		public void C17_PatrolIdle_DoesNotAccumulate()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(2f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.AreEqual(0f, controller.ArmFatigue);
		}

		[Test]
		public void C18_LowReady_AccumulatesMoreThanPatrol()
		{
			Assert.Greater(LoadIn(ReadinessState.LowReady, 1f), LoadIn(ReadinessState.Patrol, 1f));
		}

		[Test]
		public void C19_HighReady_AccumulatesMoreThanLow()
		{
			Assert.Greater(LoadIn(ReadinessState.HighReady, 1f), LoadIn(ReadinessState.LowReady, 1f));
		}

		[Test]
		public void C20_Aim_AccumulatesMoreThanHighReady()
		{
			Assert.Greater(LoadIn(ReadinessState.Aim, 1f), LoadIn(ReadinessState.HighReady, 1f));
		}

		[Test]
		public void C21_Firing_AccumulatesMoreThanAim()
		{
			ReadinessController aim = ForRankInAim();
			ReadinessController firing = ForRankInAim();
			float t = aim.Context.StateEnterTime + 1f;
			aim.Tick(t, ReadinessStimulus.HostileVisible);
			firing.Tick(t, new ReadinessFrame { HostileVisible = true, Firing = true });
			Assert.Greater(firing.ArmFatigue, aim.ArmFatigue);
		}

		[Test]
		public void C22_IdleInScene_IsPatrolLoad()
		{
			Assert.AreEqual(0f, ArmFatigueMath.LoadRate(ReadinessState.Patrol, Play()));
		}

		[Test]
		public void C23_GunshotReady_Accumulates()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0f, ReadinessStimulus.GunshotHeard);
			FinishTo(controller, ReadinessState.LowReady, ReadinessStimulus.GunshotHeard);
			float t = controller.Context.StateEnterTime + 1.5f;
			controller.Tick(t, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			Assert.Greater(controller.ArmFatigue, 0f);
		}

		[Test]
		public void C24_HostileVisibleAim_AccumulatesMoreThanGunshotReady()
		{
			Assert.Greater(LoadIn(ReadinessState.Aim, 1.5f), LoadIn(ReadinessState.LowReady, 1.5f));
		}

		[Test]
		public void C25_Firefight_GrowsFromAimLoad()
		{
			ReadinessController controller = ForRankInAim();
			float before = controller.ArmFatigue;
			controller.Tick(
				controller.Context.StateEnterTime + 2f,
				new ReadinessFrame { HostileVisible = true, Firing = true });
			Assert.Greater(controller.ArmFatigue, before);
		}
		#endregion

		#region G Recovery and interrupt
		[Test]
		public void C26_NoLoad_Recovers()
		{
			var controller = RecoveringSoldier();
			float t1 = controller.ArmFatigue;
			controller.Tick(3.5f, ReadinessStimulus.None);
			Assert.Less(controller.ArmFatigue, t1);
		}

		[Test]
		public void C27_Recovery_DoesNotJumpToZero()
		{
			var controller = RecoveringSoldier();
			controller.Tick(3.2f, ReadinessStimulus.None);
			Assert.Greater(controller.ArmFatigue, 0f);
		}

		[Test]
		public void C28_HostileLost_KeepsAimLoad()
		{
			ReadinessController controller = ForRankInAim();
			float before = controller.ArmFatigue;
			controller.Tick(controller.Context.StateEnterTime + 0.2f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.GreaterOrEqual(controller.ArmFatigue, before);
		}

		[Test]
		public void C29_Patrol_IsTheRecoveryState()
		{
			var controller = RecoveringSoldier();
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.Greater(controller.ArmFatigue, 0f);
		}

		[Test]
		public void C30_Recovery_InterruptedByHostileVisible()
		{
			var controller = RecoveringSoldier();
			float recovered = controller.ArmFatigue;
			controller.Tick(3.4f, ReadinessStimulus.HostileVisible);
			FinishTo(controller, ReadinessState.Aim, ReadinessStimulus.HostileVisible);
			controller.Tick(controller.Context.StateEnterTime + 1f, ReadinessStimulus.HostileVisible);
			Assert.Greater(controller.ArmFatigue, recovered);
		}

		[Test]
		public void C31_ReAim_DoesNotResetFatigue()
		{
			var controller = RecoveringSoldier();
			float recovered = controller.ArmFatigue;
			controller.Tick(3.4f, ReadinessStimulus.HostileVisible);
			FinishTo(controller, ReadinessState.Aim, ReadinessStimulus.HostileVisible);
			Assert.Greater(controller.ArmFatigue, recovered * 0.5f);
			Assert.Greater(controller.ArmFatigue, 0.01f);
		}

		[Test]
		public void C32_CombatLoad_ResumesFromCurrentValue()
		{
			var controller = RecoveringSoldier();
			float recovered = controller.ArmFatigue;
			controller.Tick(3.4f, ReadinessStimulus.HostileVisible);
			Assert.Greater(controller.ArmFatigue, 0.01f);
			Assert.Less(controller.ArmFatigue, 1f);
			Assert.Greater(recovered, 0.01f);
		}
		#endregion

		#region H Isolation
		[Test]
		public void C33_UnitAIState_Unchanged()
		{
			GameObject go = LiveHost("C33");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C34_CombatIntent_Unchanged()
		{
			GameObject go = LiveHost("C34");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C35_G6_NotDrivenByFatigue()
		{
			GameObject go = LiveHost("C35");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				EngagementDecisionController g6 = go.GetComponent<EngagementDecisionController>();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				if (g6 != null)
				{
					g6.RefreshDecisionNow();
					Assert.AreNotEqual(EngagementDecision.Fire, g6.CurrentDecision);
					Assert.AreNotEqual(EngagementDecision.Aim, g6.CurrentDecision);
				}

				Assert.IsFalse(ArmFatigueMath.AffectsG6());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C36_TargetSelector_Unchanged()
		{
			GameObject go = LiveHost("C36");
			try
			{
				TargetSelector selector = go.GetComponent<TargetSelector>();
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(1f, 1f);
				go.GetComponent<UnitAIController>().Tick(0.05f);
				if (selector != null)
					Assert.IsNull(selector.SelectedTarget);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C37_RoE_Unchanged()
		{
			GameObject go = LiveHost("C37");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				UseOfForceLevel before = ai.CurrentUseOfForceLevel;
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				Assert.AreEqual(before, ai.CurrentUseOfForceLevel);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C38_Cover_NotRequested()
		{
			GameObject go = LiveHost("C38");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				ai.Readiness.SetArmFatigue(1f, 1f);
				ai.Tick(0.05f);
				Assert.IsNull(ai.CoverOccupancy);
				Assert.IsFalse(ArmFatigueMath.AffectsCover());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C39_RequestsFire_False()
		{
			Assert.IsFalse(InstantAim(1f).RequestsFire);
		}

		[Test]
		public void C40_Movement_NotAffected()
		{
			Assert.IsFalse(ArmFatigueMath.AffectsMovement());
			Assert.IsFalse(ArmFatigueMath.AffectsPerception());
			Assert.IsFalse(ArmFatigueMath.AffectsReadinessState());
		}
		#endregion

		#region I LifeGate / rank / chain
		[Test]
		public void C41_Unconscious_FreezesFatigue()
		{
			ReadinessController controller = ForRankInAim();
			float t = controller.Context.StateEnterTime + 0.4f;
			controller.Tick(t, ReadinessStimulus.HostileVisible);
			float frozen = controller.ArmFatigue;
			controller.SetAllowed(false);
			controller.Tick(t + 5f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(frozen, controller.ArmFatigue);
		}

		[Test]
		public void C42_Dead_FreezesCombatSamples()
		{
			GameObject go = LiveHost("C42");
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				ai.Readiness.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), Time.time);
				ai.Readiness.SetArmFatigue(0.4f, 1f);
				float frozen = CombatFatigueProbe.SampleAimTimeSeconds(go.transform);
				ai.NotifyLifeState(UnitLifeState.Dead);
				ai.Tick(0.2f);
				Assert.IsFalse(ai.Readiness.Allowed);
				Assert.AreEqual(0.4f, ai.Readiness.ArmFatigue);
				Assert.AreEqual(frozen, CombatFatigueProbe.SampleAimTimeSeconds(go.transform));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void C43_Revive_DoesNotDumpFatigue()
		{
			ReadinessController controller = ForRankInAim();
			float t = controller.Context.StateEnterTime + 0.3f;
			controller.Tick(t, ReadinessStimulus.HostileVisible);
			float frozen = controller.ArmFatigue;
			controller.SetAllowed(false);
			controller.Tick(t + 8f, ReadinessStimulus.HostileVisible);
			controller.SetAllowed(true);
			controller.Tick(t + 8.05f, ReadinessStimulus.HostileVisible);
			Assert.Less(controller.ArmFatigue - frozen, 0.05f);
		}

		[Test]
		public void C44_FiveRanks_SameFatigueOverlay()
		{
			ReadinessRankKind[] ranks =
			{
				ReadinessRankKind.Recruit,
				ReadinessRankKind.Soldier,
				ReadinessRankKind.Corporal,
				ReadinessRankKind.Veteran,
				ReadinessRankKind.Elite
			};
			float previous = 0f;
			for (int i = 0; i < ranks.Length; i++)
			{
				ReadinessProfile profile = ReadinessProfile.ForRank(ranks[i]);
				Assert.AreEqual(1f, profile.ArmFatigue.FatigueLoadModifier);
				float aim = ArmFatigueMath.FinalAimTime(1f, 0.5f, profile.ArmFatigue);
				if (i > 0)
					Assert.AreEqual(previous, aim);
				previous = aim;
			}
		}

		[Test]
		public void C45_EliteRaise_StillFasterThanRecruit_AtSameFatigue()
		{
			ReadinessProfile recruit = ReadinessProfile.ForRank(ReadinessRankKind.Recruit);
			ReadinessProfile elite = ReadinessProfile.ForRank(ReadinessRankKind.Elite);
			float recruitRaise = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in recruit);
			float eliteRaise = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in elite);
			Assert.Greater(recruitRaise, eliteRaise);
			Assert.AreEqual(
				ArmFatigueMath.AimTimeMultiplier(0.5f, recruit.ArmFatigue),
				ArmFatigueMath.AimTimeMultiplier(0.5f, elite.ArmFatigue));
		}

		[Test]
		public void C46_ChainLog_HasFatigueValue()
		{
			var controller = InstantAim(0.5f);
			Assert.IsTrue(controller.LastFatigueValuePayload.IndexOf("value=", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void C47_ChainLog_HasReadinessEffect()
		{
			var controller = InstantAim(0.5f);
			Assert.IsTrue(controller.LastReadinessEffectPayload.IndexOf("aimMultiplier=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastReadinessEffectPayload.IndexOf("recoilMultiplier=", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastReadinessEffectPayload.IndexOf("turnMultiplier=", StringComparison.Ordinal) >= 0);
		}
		#endregion

		#region Private Methods
		private static ArmFatigueProfile Play() => ArmFatigueProfile.PlayPrototype();

		private static GameObject LiveHost(string _name)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitAIController>();
			UnitCombatStats stats = go.AddComponent<UnitCombatStats>();
			stats.ApplySkills(50f, 50f, 50f);
			go.AddComponent<UnitWeaponAimProgressController>();
			go.AddComponent<UnitWeaponAiming>();
			go.AddComponent<UnitWeaponRecoilController>();
			go.GetComponent<UnitAIController>().EnsureStarted();
			return go;
		}

		private static float AimAt(float _fatigue)
		{
			GameObject go = LiveHost("AimAt");
			try
			{
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(_fatigue, 1f);
				return CombatFatigueProbe.SampleAimTimeSeconds(go.transform);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static float TurnAt(float _fatigue)
		{
			return TurnDelta(_fatigue, CombatFatigueProbe.DefaultTurnDeltaDegrees);
		}

		private static float TurnDelta(float _fatigue, float _degrees)
		{
			GameObject go = LiveHost("TurnAt");
			try
			{
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(_fatigue, 1f);
				return CombatFatigueProbe.SampleTurnSeconds(go.transform, _degrees);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static float ControlAt(float _fatigue)
		{
			GameObject go = LiveHost("ControlAt");
			try
			{
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(_fatigue, 1f);
				return CombatFatigueProbe.SampleEffectiveRecoilControl(go.transform);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static float RecoveryAt(float _fatigue)
		{
			GameObject go = LiveHost("RecoveryAt");
			try
			{
				go.GetComponent<UnitAIController>().Readiness.SetArmFatigue(_fatigue, 1f);
				return CombatFatigueProbe.SampleSkillRecoveryMultiplier(go.transform);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static float LogicalAimDuration(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.SetArmFatigue(_fatigue, 1f);
			controller.Tick(0.01f, ReadinessStimulus.HostileVisible);
			return controller.LastRequest.Duration;
		}

		private static ReadinessController InstantAim(float _fatigue)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.SetArmFatigue(_fatigue, 1f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			return controller;
		}

		private static ReadinessController ForRankInAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.Tick(0f, ReadinessStimulus.HostileVisible);
			FinishTo(controller, ReadinessState.Aim, ReadinessStimulus.HostileVisible);
			return controller;
		}

		private static void FinishTo(
			ReadinessController _controller,
			ReadinessState _target,
			ReadinessStimulus _hold)
		{
			if (_controller.CurrentState == _target && !_controller.Context.HasPendingTransition)
				return;

			float t = _controller.Context.HasPendingTransition
				? _controller.Context.TransitionStartTime + _controller.Context.TransitionDuration + 0.05f
				: _controller.Context.StateEnterTime + 0.05f;
			_controller.Tick(t, _hold);
			Assert.AreEqual(_target, _controller.CurrentState);
		}

		private static float LoadIn(ReadinessState _state, float _seconds)
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			if (_state != ReadinessState.Patrol)
			{
				ReadinessChangeReason reason = _state == ReadinessState.Aim
					? ReadinessChangeReason.HostileVisible
					: ReadinessChangeReason.Gunshot;
				controller.RequestTransition(_state, reason, 0f);
				ReadinessStimulus hold = _state == ReadinessState.Aim
					? ReadinessStimulus.HostileVisible
					: ReadinessStimulus.None;
				FinishTo(controller, _state, hold);
			}

			float start = controller.ArmFatigue;
			float t = controller.Context.StateEnterTime + _seconds;
			ReadinessStimulus keep = _state == ReadinessState.Aim
				? ReadinessStimulus.HostileVisible
				: ReadinessStimulus.None;
			controller.Tick(t, keep);
			return controller.ArmFatigue - start;
		}

		private static ReadinessController RecoveringSoldier()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
			controller.RequestTransition(ReadinessState.HighReady, ReadinessChangeReason.Gunshot, 0f);
			controller.Tick(0.3f, ReadinessStimulus.None);
			controller.Tick(2f, ReadinessStimulus.None);
			controller.RequestTransition(ReadinessState.Patrol, ReadinessChangeReason.Calm, 2f);
			controller.Tick(2.8f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.Greater(controller.ArmFatigue, 0f);
			return controller;
		}
		#endregion
	}
}
