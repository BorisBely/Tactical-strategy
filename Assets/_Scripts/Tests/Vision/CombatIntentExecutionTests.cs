using System;
using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class CombatIntentExecutionTests
	{
		private GameObject m_Target;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("CombatIntentTarget");
			m_Target.transform.position = new Vector3(15f, 0f, 0f);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Target != null)
				UnityEngine.Object.DestroyImmediate(m_Target);
		}

		[Test]
		public void MissingSource_DoesNotVetoG6()
		{
			GameObject observer = CreateObserver();
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				Observe(processor, 20);
				selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
				engagement.RefreshDecisionNow();

				Assert.IsFalse(engagement.CombatIntentGateApplied);
				Assert.AreNotEqual(EngagementDecision.Ignore, engagement.CurrentDecision);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Hold_VetoesFireAndAim()
		{
			GameObject observer = CreateObserver();
			try
			{
				UnitAIController ai = observer.AddComponent<UnitAIController>();
				ai.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				Observe(processor, 20);
				StampHostileContact(processor, m_Target.transform);
				selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
				engagement.RefreshDecisionNow();

				Assert.AreEqual(UseOfForceLevel.MissionCombat, ai.CurrentUseOfForceLevel);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.IsTrue(engagement.ForceGateApplied);
				Assert.IsTrue(engagement.CombatIntentGateApplied);
				Assert.AreEqual(CombatIntent.Hold, engagement.LastCombatIntent);
				Assert.IsTrue(
					engagement.LastForcePermission.Allowed,
					engagement.LastForcePermission.ToString());
				Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
				Assert.AreNotEqual(EngagementDecision.Aim, engagement.CurrentDecision);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Engage_DoesNotCallFire_AndDoesNotVetoG6()
		{
			GameObject observer = CreateObserver();
			try
			{
				UnitAIController ai = observer.AddComponent<UnitAIController>();
				ai.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
				ai.ImmediateThreat = false;
				Assert.IsTrue(ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward))));
				ai.SetPerceptionFrame(HostileVisibleFrame(m_Target.transform));
				ai.Tick(0.05f);
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				UnitWeaponFireController fire = observer.GetComponent<UnitWeaponFireController>();
				Observe(processor, 20);
				StampHostileContact(processor, m_Target.transform);
				selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
				engagement.RefreshDecisionNow();

				Assert.AreEqual(UseOfForceLevel.MissionCombat, ai.CurrentUseOfForceLevel);
				Assert.AreEqual(CombatIntent.Engage, ai.CurrentCombatIntent);
				Assert.IsTrue(engagement.ForceGateApplied);
				Assert.IsTrue(engagement.CombatIntentGateApplied);
				Assert.AreEqual(CombatIntent.Engage, engagement.LastCombatIntent);
				Assert.IsTrue(
					engagement.LastForcePermission.Allowed,
					engagement.LastForcePermission.ToString());
				Assert.AreNotEqual(EngagementDecision.Ignore, engagement.CurrentDecision);
				if (fire != null)
					Assert.IsFalse(fire.IsFiringCommandActive);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void AiDefenseHostile_PublishesEngage()
		{
			var go = new GameObject("CombatIntentAi");
			Transform hostile = new GameObject("CombatIntentHostile").transform;
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.IsTrue(ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward))));
				ai.SetPerceptionFrame(HostileVisibleFrame(hostile));
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIAction.Engage, ai.CurrentAction);
				Assert.AreEqual(CombatIntent.Engage, ai.CurrentCombatIntent);
				Assert.AreSame(hostile, ai.CurrentEngageTarget);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
				if (hostile != null)
					UnityEngine.Object.DestroyImmediate(hostile.gameObject);
			}
		}

		[Test]
		public void AiDefenseUnknown_PublishesHold()
		{
			var go = new GameObject("CombatIntentUnknownAi");
			Transform unknown = new GameObject("CombatIntentUnknown").transform;
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				Assert.IsTrue(ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward))));
				ai.SetPerceptionFrame(VisibleUnknownFrame(unknown));
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIAction.Hold, ai.CurrentAction);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
				Assert.IsNull(ai.CurrentEngageTarget);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
				if (unknown != null)
					UnityEngine.Object.DestroyImmediate(unknown.gameObject);
			}
		}

		[Test]
		public void AiDefenseFriendly_PublishesHold()
		{
			var go = new GameObject("CombatIntentFriendlyAi");
			Transform friendly = new GameObject("CombatIntentFriendly").transform;
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				Assert.IsTrue(ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward))));
				ai.SetPerceptionFrame(VisibleFriendlyFrame(friendly));
				ai.Tick(0.05f);
				Assert.AreEqual(UnitAIAction.Hold, ai.CurrentAction);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
				if (friendly != null)
					UnityEngine.Object.DestroyImmediate(friendly.gameObject);
			}
		}

		[Test]
		public void HostileLost_PublishesHold()
		{
			var go = new GameObject("CombatIntentLostAi");
			Transform hostile = new GameObject("CombatIntentLostHostile").transform;
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				Assert.IsTrue(ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward))));
				ai.SetPerceptionFrame(HostileVisibleFrame(hostile));
				ai.Tick(0.05f);
				Assert.AreEqual(CombatIntent.Engage, ai.CurrentCombatIntent);
				ai.SetPerceptionFrame(AIPerceptionFrame.Empty);
				ai.Tick(0.05f);
				Assert.AreEqual(CombatIntent.Hold, ai.CurrentCombatIntent);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
				if (hostile != null)
					UnityEngine.Object.DestroyImmediate(hostile.gameObject);
			}
		}

		[Test]
		public void TwoControllers_IndependentIntent()
		{
			var aGo = new GameObject("CombatIntentA");
			var bGo = new GameObject("CombatIntentB");
			Transform hostile = new GameObject("CombatIntentSharedHostile").transform;
			try
			{
				UnitAIController a = aGo.AddComponent<UnitAIController>();
				UnitAIController b = bGo.AddComponent<UnitAIController>();
				a.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
				b.TryApplyCommand(
					UnitAICommand.Idle());
				a.SetPerceptionFrame(HostileVisibleFrame(hostile));
				b.SetPerceptionFrame(HostileVisibleFrame(hostile));
				a.Tick(0.05f);
				b.Tick(0.05f);
				Assert.AreEqual(CombatIntent.Engage, a.CurrentCombatIntent);
				Assert.AreEqual(CombatIntent.Hold, b.CurrentCombatIntent);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(aGo);
				UnityEngine.Object.DestroyImmediate(bGo);
				if (hostile != null)
					UnityEngine.Object.DestroyImmediate(hostile.gameObject);
			}
		}

		[Test]
		public void Mismatch_IsObserved_NotAutoFixed()
		{
			GameObject observer = CreateObserver();
			Transform other = new GameObject("CombatIntentOther").transform;
			try
			{
				UnitAIController ai = observer.AddComponent<UnitAIController>();
				ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
				ai.SetPerceptionFrame(HostileVisibleFrame(m_Target.transform));
				ai.Tick(0.05f);
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				engagement.RefreshDecisionNow();
				selector.SetSelectedTargetForDiagnostics(other, other.position);
				engagement.RefreshDecisionNow();
				Assert.AreSame(m_Target.transform, ai.CurrentEngageTarget);
				Assert.AreSame(other, selector.SelectedTarget);
				Assert.AreNotSame(ai.CurrentEngageTarget, selector.SelectedTarget);
				Assert.IsTrue(engagement.EngageTargetMismatch);
				Assert.AreSame(other, selector.SelectedTarget);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
				if (other != null)
					UnityEngine.Object.DestroyImmediate(other.gameObject);
			}
		}

		[Test]
		public void SelfDefense_StillVetoesAimFire_WhenEngage()
		{
			GameObject observer = CreateObserver();
			try
			{
				UnitAIController ai = observer.AddComponent<UnitAIController>();
				ai.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense);
				ai.ImmediateThreat = false;
				ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
				ai.SetPerceptionFrame(HostileVisibleFrame(m_Target.transform));
				ai.Tick(0.05f);
				Assert.AreEqual(CombatIntent.Engage, ai.CurrentCombatIntent);

				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				processor.SetAffiliationCue(m_Target.transform, ObservableAffiliation.Hostile);
				Observe(processor, 44);
				selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
				engagement.RefreshDecisionNow();
				Assert.AreEqual(CombatIntent.Engage, engagement.LastCombatIntent);
				Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
				Assert.AreNotEqual(EngagementDecision.Aim, engagement.CurrentDecision);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void SelfDefense_ImmediateThreat_DoesNotVetoG6_AndDoesNotChangeSelection()
		{
			GameObject observer = CreateObserver();
			try
			{
				UnitTeam observerTeam = observer.GetComponent<UnitTeam>() ?? observer.AddComponent<UnitTeam>();
				observerTeam.SetTeam(UnitTeamId.Player);
				UnitTeam targetTeam = m_Target.GetComponent<UnitTeam>() ?? m_Target.AddComponent<UnitTeam>();
				targetTeam.SetTeam(UnitTeamId.Enemy);

				UnitAIController ai = observer.AddComponent<UnitAIController>();
				ai.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense);
				ai.TryApplyCommand(
					UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
				ai.SetPerceptionFrame(HostileVisibleFrame(m_Target.transform));
				ai.Tick(0.05f);

				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				processor.SetAffiliationCue(m_Target.transform, ObservableAffiliation.Hostile);
				Observe(processor, 44);
				selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
				Transform selectedBefore = selector.SelectedTarget;

				ImmediateThreatSignal.NotifyIncomingFire(m_Target.transform, observer.transform);
				ai.Tick(0.05f);
				engagement.RefreshDecisionNow();

				Assert.IsTrue(ai.ImmediateThreat);
				Assert.IsTrue(engagement.LastForcePermission.Allowed, engagement.LastForcePermission.ToString());
				Assert.AreSame(selectedBefore, selector.SelectedTarget);
				Assert.AreNotEqual(EngagementDecision.Ignore, engagement.CurrentDecision);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		private static GameObject CreateObserver()
		{
			var go = new GameObject("CombatIntentObserver");
			go.SetActive(false);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			if (go.GetComponent<DetectionProcessor>() == null)
				go.AddComponent<DetectionProcessor>();
			if (go.GetComponent<TargetSelector>() == null)
				go.AddComponent<TargetSelector>();
			if (go.GetComponent<EngagementDecisionController>() == null)
				go.AddComponent<EngagementDecisionController>();
			go.SetActive(true);
			DetectionProcessor processor = go.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			return go;
		}

		private void Observe(DetectionProcessor _processor, int _ticks)
		{
			float now = 0f;
			Vector3 position = m_Target.transform.position;
			for (int i = 0; i < _ticks; i++)
			{
				_processor.ApplySyntheticObservation(m_Target.transform, 15f, 0f, 1f, position);
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}
		}

		private static void StampHostileContact(DetectionProcessor _processor, Transform _target)
		{
			Assert.IsTrue(_processor.TryGetContact(_target, out PerceivedContact contact), "contact missing");
			contact.Identity = PerceivedIdentity.Hostile;
			contact.Relationship = PerceivedRelationship.Hostile;
		}

		private static AIPerceptionFrame HostileVisibleFrame(Transform _target)
		{
			return Frame(VisibleHostile(_target));
		}

		private static AIPerceptionFrame VisibleUnknownFrame(Transform _target)
		{
			return Frame(VisibleUnknown(_target));
		}

		private static AIPerceptionFrame VisibleFriendlyFrame(Transform _target)
		{
			return Frame(VisibleFriendly(_target));
		}

		private static AIPerceptionFrame Frame(AIContactKnowledge _contact)
		{
			return new AIPerceptionFrame(
				new[] { _contact },
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				_contact.Threat);
		}

		private static AIContactKnowledge VisibleHostile(Transform _target)
		{
			return Knowledge(
				_target,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				true,
				false,
				false);
		}

		private static AIContactKnowledge VisibleUnknown(Transform _target)
		{
			return Knowledge(
				_target,
				PerceivedIdentity.Unknown,
				PerceivedRelationship.Unknown,
				ThreatLevel.None,
				false,
				false,
				false);
		}

		private static AIContactKnowledge VisibleFriendly(Transform _target)
		{
			return Knowledge(
				_target,
				PerceivedIdentity.Friendly,
				PerceivedRelationship.Friendly,
				ThreatLevel.None,
				false,
				true,
				false);
		}

		private static AIContactKnowledge Knowledge(
			Transform _target,
			PerceivedIdentity _identity,
			PerceivedRelationship _relationship,
			ThreatLevel _threat,
			bool _hostile,
			bool _friendly,
			bool _neutral)
		{
			return new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				_identity,
				_identity == PerceivedIdentity.Unknown ? 0f : 1f,
				_relationship,
				_threat,
				_target.position,
				_target.position,
				0f,
				1f,
				true,
				false,
				false,
				true,
				false,
				_identity == PerceivedIdentity.Unknown,
				_identity != PerceivedIdentity.Unknown,
				_friendly,
				_neutral,
				_hostile,
				_threat == ThreatLevel.None,
				_threat == ThreatLevel.Low,
				_threat == ThreatLevel.Medium,
				_threat == ThreatLevel.High);
		}
	}
}
