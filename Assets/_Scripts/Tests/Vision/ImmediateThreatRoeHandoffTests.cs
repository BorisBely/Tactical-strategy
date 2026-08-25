using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class ImmediateThreatRoeHandoffTests
	{
		private GameObject m_Target;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("ImmediateThreatRoeTarget");
			m_Target.transform.position = new Vector3(15f, 0f, 0f);
			m_Target.AddComponent<UnitTeam>().SetTeam(UnitTeamId.Enemy);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Target != null)
				UnityEngine.Object.DestroyImmediate(m_Target);
		}

		[Test]
		public void FireOrAim_Deny_BecomesIgnore()
		{
			GameObject observer = CreateObserver();
			try
			{
				ArmSelfDefense(observer, false);
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
				Assert.AreNotEqual(EngagementDecision.Aim, engagement.CurrentDecision);
				Assert.IsFalse(engagement.LastForcePermission.Allowed, engagement.LastForcePermission.ToString());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Track_Deny_StaysTrack()
		{
			GameObject observer = CreateObserver();
			try
			{
				ArmSelfDefense(observer, false);
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				ClearSelectedAimPoint(selector);
				engagement.RefreshDecisionNow();
				Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
				Assert.IsFalse(engagement.LastForcePermission.Allowed);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Allow_DoesNotChangeSelection_AndDoesNotCallFire()
		{
			GameObject observer = CreateObserver();
			try
			{
				UnitWeaponFireController fire = observer.AddComponent<UnitWeaponFireController>();
				ArmSelfDefense(observer, true);
				TargetSelector selector = observer.GetComponent<TargetSelector>();
				EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
				Transform selected = selector.SelectedTarget;
				UnitAIController ai = observer.GetComponent<UnitAIController>();
				engagement.RefreshDecisionNow();
				ai.EvaluateForce(true, PerceivedRelationship.Hostile, m_Target.transform);
				Assert.IsTrue(engagement.LastForcePermission.Allowed, engagement.LastForcePermission.ToString());
				Assert.IsTrue(ai.LastForcePermission.Allowed, ai.LastForcePermission.ToString());
				Assert.AreSame(selected, selector.SelectedTarget);
				Assert.AreNotEqual(EngagementDecision.Ignore, engagement.CurrentDecision);
				Assert.IsFalse(fire.IsFiringCommandActive);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(observer);
			}
		}

		private void ArmSelfDefense(GameObject _observer, bool _notifyIncoming)
		{
			UnitTeam observerTeam = _observer.GetComponent<UnitTeam>() ?? _observer.AddComponent<UnitTeam>();
			observerTeam.SetTeam(UnitTeamId.Player);
			UnitAIController ai = _observer.AddComponent<UnitAIController>();
			ai.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense);
			ai.TryApplyCommand(
				UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
			ai.SetPerceptionFrame(HostileVisibleFrame(m_Target.transform));
			ai.Tick(0.05f);

			DetectionProcessor processor = _observer.GetComponent<DetectionProcessor>();
			TargetSelector selector = _observer.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = _observer.GetComponent<EngagementDecisionController>();
			processor.SetAffiliationCue(m_Target.transform, ObservableAffiliation.Hostile);
			Observe(processor, 44);
			selector.SetSelectedTargetForDiagnostics(m_Target.transform, m_Target.transform.position);
			if (_notifyIncoming)
			{
				ImmediateThreatSignal.NotifyIncomingFire(m_Target.transform, _observer.transform);
				ai.Tick(0.05f);
			}

			engagement.RefreshDecisionNow();
		}

		private static void ClearSelectedAimPoint(TargetSelector _selector)
		{
			FieldInfo field = typeof(TargetSelector).GetField(
				"m_HasSelectedAimPoint",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, "TargetSelector.m_HasSelectedAimPoint missing");
			field.SetValue(_selector, false);
		}

		private static GameObject CreateObserver()
		{
			var go = new GameObject("ImmediateThreatRoeObserver");
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
			go.GetComponent<DetectionProcessor>().SetSimulatedTime(0f);
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

		private static AIPerceptionFrame HostileVisibleFrame(Transform _target)
		{
			AIContactKnowledge contact = Knowledge(
				_target,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				true,
				false,
				false);
			return new AIPerceptionFrame(
				new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				contact.Threat);
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
