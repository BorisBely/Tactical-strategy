using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14B.3 Readiness ↔ Perception / Combat. Does not change Vision / Identity / TargetSelector / G6 / Cover / Movement.
	/// CombatIntent.Engage ≠ Readiness.Aim. Sound ≠ Fire. LifeGate freezes new transitions.
	/// </summary>
	[Category("Readiness")]
	public sealed class ReadinessIntegrationTests
	{
		#region P HostileVisible from perception
		[Test]
		public void P1_HostileVisible_NotReadyToAim()
		{
			AssertAimFrom(ReadinessRankKind.Recruit, ReadinessState.NotReady);
		}

		[Test]
		public void P2_HostileVisible_PatrolToAim()
		{
			AssertAimFrom(ReadinessRankKind.Soldier, ReadinessState.Patrol);
		}

		[Test]
		public void P3_HostileVisible_LowReadyToAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			controller.Tick(0.4f, HostileVisibleReadinessFrame());
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void P4_HostileVisible_HighReadyToAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Corporal, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			controller.Tick(0.4f, HostileVisibleReadinessFrame());
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void P5_HostileVisible_PreAimToAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(2.2f, HostileVisibleReadinessFrame());
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void P6_ObservedHostile_MapsHostileVisible()
		{
			AIPerceptionFrame frame = HostileVisiblePerception(null);
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, false, false);
			Assert.IsTrue(ReadinessStimulusMath.HasHostileVisible(in frame));
			Assert.IsTrue(mapped.HostileVisible);
			Assert.IsFalse(mapped.GunshotHeard);
			Assert.AreEqual(ReadinessStimulus.HostileVisible, ReadinessStimulusMath.Dominant(in mapped));
		}

		[Test]
		public void P7_UnknownObserved_IsNotHostileVisible()
		{
			AIPerceptionFrame frame = ContactFrame(
				ObservationState.Observed,
				PerceivedIdentity.Unknown,
				PerceivedRelationship.Neutral,
				true,
				false);
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, false, false);
			Assert.IsFalse(mapped.HostileVisible);
			Assert.IsFalse(ReadinessStimulusMath.HasHostileVisible(in frame));
		}

		[Test]
		public void P8_RememberedHostile_IsNotHostileVisible()
		{
			AIPerceptionFrame frame = ContactFrame(
				ObservationState.RecentlyLost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				false,
				true);
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, true, false);
			Assert.IsFalse(mapped.HostileVisible);
			Assert.IsTrue(mapped.HostileLost);
		}
		#endregion

		#region Q Gunshot / Search independence
		[Test]
		public void Q1_Gunshot_RecruitSoldier_LowReady()
		{
			Assert.AreEqual(ReadinessState.LowReady, AfterGunshot(ReadinessRankKind.Recruit));
			Assert.AreEqual(ReadinessState.LowReady, AfterGunshot(ReadinessRankKind.Soldier));
		}

		[Test]
		public void Q2_Gunshot_CorporalPlus_HighReady()
		{
			Assert.AreEqual(ReadinessState.HighReady, AfterGunshot(ReadinessRankKind.Corporal));
			Assert.AreEqual(ReadinessState.HighReady, AfterGunshot(ReadinessRankKind.Veteran));
			Assert.AreEqual(ReadinessState.HighReady, AfterGunshot(ReadinessRankKind.Elite));
		}

		[Test]
		public void Q3_Gunshot_DoesNotBecomeFire_SearchStillIndependent()
		{
			AIPerceptionFrame frame = GunshotPerception();
			Assert.IsTrue(UnitAISearchDecision.ShouldStartSearch(UnitAIState.Defense, in frame));
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in frame, false, false);
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessDecision decision = controller.Tick(0.2f, in mapped);
			Assert.AreEqual(ReadinessState.LowReady, decision.State);
			Assert.IsFalse(decision.RequestsFire);
			Assert.IsFalse(controller.RequestsFire);
		}

		[Test]
		public void Q4_GunshotPlusHostileVisible_GoesAim()
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
			Assert.AreEqual(ReadinessChangeReason.HostileVisible, controller.LastRequest.Reason);
		}
		#endregion

		#region R Decay / retrigger
		[Test]
		public void R1_HostileLost_BeginsDecay_DoesNotSnap()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, HostileVisibleReadinessFrame());
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			controller.Tick(0.7f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			controller.Tick(1.4f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			Assert.IsTrue(ReadinessLog.ContainsDecay(
				controller.LogLines,
				ReadinessState.Aim,
				ReadinessState.PreAim));
			Assert.IsTrue(controller.LastDecayPayload.IndexOf("Aim->PreAim", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void R2_HostileVisibleDuringDecay_CancelsToAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(1.3f, ReadinessStimulus.None);
			Assert.AreEqual(ReadinessState.PreAim, controller.CurrentState);
			controller.Tick(1.4f, HostileVisibleReadinessFrame());
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}
		#endregion

		#region S CombatActivity / CombatIntent / G6
		[Test]
		public void S1_CombatActivity_HoldsDoesNotAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			var frame = new ReadinessFrame { CombatActivity = true };
			controller.Tick(0.2f, in frame);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.HasCombatActivity);
			Assert.AreEqual(0.2f, controller.LastCombatActivityTime);
		}

		[Test]
		public void S2_CombatEvent_IsActivityNotAim()
		{
			Assert.IsTrue(ReadinessCombatActivity.IsCombatEvent(CombatEventType.Gunshot));
			Assert.IsTrue(ReadinessCombatActivity.IsCombatEvent(CombatEventType.Hit));
			Assert.IsFalse(ReadinessCombatActivity.IsCombatEvent(CombatEventType.Death));
			Assert.IsFalse(ReadinessCombatActivity.IsCombatEvent(CombatEventType.Impact));

			AIPerceptionFrame empty = AIPerceptionFrame.Empty;
			ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in empty, false, true);
			Assert.IsTrue(mapped.CombatActivity);
			Assert.IsFalse(mapped.HostileVisible);
			Assert.IsFalse(mapped.GunshotHeard);

			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, in mapped);
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
			Assert.IsTrue(controller.HasCombatActivity);
		}

		[Test]
		public void S3_ImmediateThreat_OnLiveAi_DoesNotAim()
		{
			GameObject go = new GameObject("ReadinessCombatActivity");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.ImmediateThreat = true;
				ai.Tick(0.05f);
				Assert.AreEqual(ReadinessState.Patrol, ai.Readiness.CurrentState);
				Assert.IsTrue(ai.Readiness.HasCombatActivity);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void S4_CombatIntentEngage_DoesNotChangeReadinessByItself()
		{
			GameObject go = new GameObject("ReadinessEngageAlone");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Attack(
					UnitAIStateContext.ForAttack(Vector3.forward, Vector3.forward))));
				ai.Tick(0.05f);
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				combat.ApplyNow();
				Assert.AreEqual(UnitAIState.Attack, ai.CurrentState);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.AreEqual(ReadinessState.Patrol, ai.Readiness.CurrentState);
				Assert.AreEqual(ReadinessChangeReason.Initial, ai.Readiness.Context.LastChangeReason);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void S5_Aim_DoesNotCreateEngage()
		{
			GameObject go = new GameObject("ReadinessAimHold");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.SetPerceptionFrame(HostileVisiblePerception(null));
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.IsTrue(
					ai.Readiness.CurrentState == ReadinessState.Aim ||
					(ai.Readiness.Context.HasPendingTransition &&
					 ai.Readiness.Context.TransitionTo == ReadinessState.Aim));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void S6_Aim_DoesNotCauseG6FireOrShot()
		{
			GameObject go = new GameObject("ReadinessAimNotFire");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.SetPerceptionFrame(HostileVisiblePerception(null));
				ai.Tick(0.05f);
				CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
				combat.ApplyNow();
				EngagementDecisionController g6 = go.GetComponent<EngagementDecisionController>();
				if (g6 != null)
					g6.RefreshDecisionNow();

				Assert.IsFalse(ai.Readiness.RequestsFire);
				Assert.IsFalse(combat.LastPoseRequest.RequestsFire);
				Assert.IsFalse(combat.LastPoseRequest.ChangesG6);
				Assert.IsNull(go.GetComponent<UnitWeaponFireController>());
				if (g6 != null)
				{
					Assert.AreNotEqual(EngagementDecision.Fire, g6.CurrentDecision);
					Assert.AreNotEqual(EngagementDecision.Aim, g6.CurrentDecision);
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region T Priority
		[Test]
		public void T1_Priority_HostileVisibleBeatsGunshot()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, new ReadinessFrame { HostileVisible = true, GunshotHeard = true });
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(
				ReadinessStimulus.HostileVisible,
				ReadinessStimulusMath.Dominant(new ReadinessFrame
				{
					HostileVisible = true,
					GunshotHeard = true,
					CombatActivity = true
				}));
		}

		[Test]
		public void T2_Priority_CombatActivityBeatsGunshot_InDominant_ButGunshotStillRaises()
		{
			var mapped = new ReadinessFrame { GunshotHeard = true, CombatActivity = true };
			Assert.AreEqual(ReadinessStimulus.CombatActivity, ReadinessStimulusMath.Dominant(in mapped));
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, in mapped);
			Assert.AreEqual(ReadinessState.LowReady, controller.CurrentState);
			Assert.IsTrue(controller.HasCombatActivity);
		}

		[Test]
		public void T3_Priority_HighReadyPlusHostileVisible_Aim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Corporal, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.HighReady, controller.CurrentState);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void T4_Priority_AimPlusGunshot_StaysAim()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
		}

		[Test]
		public void T5_Priority_AimPlusHostileLost_BeginsDecay()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			controller.Tick(0.3f, ReadinessStimulus.HostileLost);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.HasCombatActivity);
			Assert.Greater(controller.Context.CalmDownRemaining, 0f);
		}
		#endregion

		#region U LifeGate / logs
		[Test]
		public void U1_Unconscious_ForbidsNewTransitions()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			int changes = controller.Context.ChangeCount;
			string log = controller.LastLogPayload;
			controller.SetAllowed(false);
			controller.Tick(0.4f, ReadinessStimulus.GunshotHeard);
			controller.Tick(0.6f, ReadinessStimulus.HostileVisible);
			Assert.IsFalse(controller.RequestTransition(
				ReadinessState.Patrol,
				ReadinessChangeReason.Calm,
				0.8f));
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.AreEqual(changes, controller.Context.ChangeCount);
			Assert.AreEqual(log, controller.LastLogPayload);
			Assert.IsFalse(controller.Allowed);
		}

		[Test]
		public void U2_Dead_OnLiveAi_ForbidsNewTransitions()
		{
			GameObject go = new GameObject("ReadinessLifeGate");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				ai.SetPerceptionFrame(HostileVisiblePerception(null));
				int changes = ai.Readiness.Context.ChangeCount;
				string log = ai.Readiness.LastLogPayload;
				ai.NotifyLifeState(UnitLifeState.Dead);
				ai.SetPerceptionFrame(GunshotPerception());
				ai.Tick(0.05f);
				Assert.IsFalse(ai.Readiness.Allowed);
				Assert.AreEqual(changes, ai.Readiness.Context.ChangeCount);
				Assert.AreEqual(log, ai.Readiness.LastLogPayload);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void U3_EventLog_HostileVisibleHasTarget_NotEveryTick()
		{
			GameObject target = new GameObject("P12");
			try
			{
				var controller = new ReadinessController();
				controller.Reset(ReadinessRankKind.Soldier, 0f);
				ReadinessFrame frame = ReadinessStimulusMath.FromPerception(
					HostileVisiblePerception(target.transform),
					false,
					false);
				controller.Tick(0.2f, in frame);
				Assert.IsTrue(controller.LastEventPayload.IndexOf("type=HostileVisible", StringComparison.Ordinal) >= 0);
				Assert.IsTrue(controller.LastEventPayload.IndexOf("target=", StringComparison.Ordinal) >= 0);
				Assert.IsTrue(controller.LastTransitionPayload.IndexOf("->Aim", StringComparison.Ordinal) >= 0);
				Assert.IsTrue(controller.LastTransitionPayload.IndexOf("reason=HostileVisible", StringComparison.Ordinal) >= 0);
				string after = controller.LastEventPayload;
				controller.Tick(0.25f, in frame);
				Assert.AreEqual(after, controller.LastEventPayload);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		[Test]
		public void U4_EventLog_GunshotAndDecay()
		{
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
			Assert.IsTrue(ReadinessLog.ContainsEvent(controller.LogLines, ReadinessStimulus.GunshotHeard));
			Assert.IsTrue(controller.LastTransitionPayload.IndexOf("Patrol->LowReady", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastTransitionPayload.IndexOf("reason=GunshotHeard", StringComparison.Ordinal) >= 0);
			controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
			controller.Tick(1.5f, ReadinessStimulus.None);
			Assert.IsTrue(controller.LastDecayPayload.IndexOf("Aim->PreAim", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastDecayPayload.IndexOf("CombatActivityExpired", StringComparison.Ordinal) >= 0);
			string decay = controller.LastDecayPayload;
			controller.Tick(1.55f, ReadinessStimulus.None);
			Assert.AreEqual(decay, controller.LastDecayPayload);
		}

		[Test]
		public void U5_FriendlyObserved_DoesNotAim()
		{
			AIPerceptionFrame frame = ContactFrame(
				ObservationState.Observed,
				PerceivedIdentity.Friendly,
				PerceivedRelationship.Friendly,
				true,
				false);
			var controller = new ReadinessController();
			controller.Reset(ReadinessRankKind.Soldier, 0f);
			controller.Tick(0.2f, ReadinessStimulusMath.FromPerception(in frame, false, false));
			Assert.AreEqual(ReadinessState.Patrol, controller.CurrentState);
		}
		#endregion

		#region Private Methods
		private static void AssertAimFrom(ReadinessRankKind _rank, ReadinessState _expectedStart)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			Assert.AreEqual(_expectedStart, controller.CurrentState);
			controller.Tick(0.2f, HostileVisibleReadinessFrame());
			Assert.AreEqual(ReadinessState.Aim, controller.CurrentState);
			Assert.IsFalse(controller.RequestsFire);
		}

		private static ReadinessState AfterGunshot(ReadinessRankKind _rank)
		{
			var controller = new ReadinessController();
			controller.Reset(_rank, 0f);
			controller.Tick(0.2f, ReadinessStimulusMath.FromPerception(GunshotPerception(), false, false));
			return controller.CurrentState;
		}

		private static ReadinessFrame HostileVisibleReadinessFrame()
		{
			return ReadinessStimulusMath.FromPerception(HostileVisiblePerception(null), false, false);
		}

		private static AIPerceptionFrame HostileVisiblePerception(Transform _target)
		{
			return ContactFrame(
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				true,
				true,
				_target);
		}

		private static AIPerceptionFrame GunshotPerception()
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

		private static AIPerceptionFrame ContactFrame(
			ObservationState _observation,
			PerceivedIdentity _identity,
			PerceivedRelationship _relationship,
			bool _visibleNow,
			bool _hostile,
			Transform _target = null)
		{
			AIContactKnowledge contact = new AIContactKnowledge(
				_target,
				_visibleNow ? DetectionState.Detected : DetectionState.Undetected,
				_observation,
				_identity,
				1f,
				_relationship,
				_hostile ? ThreatLevel.High : ThreatLevel.None,
				Vector3.zero,
				Vector3.zero,
				0f,
				_visibleNow ? 1f : 0.4f,
				_visibleNow,
				_observation == ObservationState.RecentlyLost,
				_observation == ObservationState.Lost,
				true,
				false,
				_identity == PerceivedIdentity.Unknown,
				_identity != PerceivedIdentity.Unknown,
				_relationship == PerceivedRelationship.Friendly,
				_relationship == PerceivedRelationship.Neutral,
				_hostile,
				!_hostile,
				false,
				false,
				_hostile);
			return new AIPerceptionFrame(
				new[] { contact },
				_visibleNow ? new[] { contact } : Array.Empty<AIContactKnowledge>(),
				_visibleNow ? Array.Empty<AIContactKnowledge>() : new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				_hostile ? new[] { contact } : Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				_hostile ? ThreatLevel.High : ThreatLevel.None);
		}
		#endregion
	}
}
