using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	[TestFixture]
	public sealed class CombatEventTests
	{
		#region Private Fields
		private int m_ListenerHits;
		private CombatEvent m_LastHeard;
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			CombatEventHub.ResetForTests();
			WorldSoundHub.ResetForTests();
			m_ListenerHits = 0;
			m_LastHeard = default;
		}

		[TearDown]
		public void TearDown()
		{
			CombatEventHub.ResetForTests();
			WorldSoundHub.ResetForTests();
		}
		#endregion

		#region Tests
		[Test]
		public void Publish_DeliversFact_WithoutRegisteringSoundKnowledge()
		{
			GameObject observer = CreateObserver();
			GameObject shooter = CreateActor("Shooter", UnitTeamId.Enemy);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Assert.AreEqual(0, processor.Contacts.Count);
				Assert.AreEqual(0, WorldSoundHub.LastPublishDeliveryCount);

				CombatEventHub.Subscribe(OnHeard);
				CombatEventHub.Publish(CombatEvent.Gunshot(
					shooter.GetComponent<UnitTeam>(),
					shooter.GetComponent<UnitTeam>(),
					null,
					shooter.transform.position));

				Assert.AreEqual(1, CombatEventHub.PublishCount);
				Assert.AreEqual(CombatEventType.Gunshot, CombatEventHub.LastPublished.Type);
				Assert.AreEqual(1, m_ListenerHits);
				Assert.AreEqual(CombatEventType.Gunshot, m_LastHeard.Type);
				Assert.AreEqual(0, processor.Contacts.Count, "event ≠ automatic knowledge");
				Assert.AreEqual(0, WorldSoundHub.LastPublishDeliveryCount);
				Assert.AreEqual(UnitAIState.Idle, observer.GetComponent<UnitAIController>().CurrentState);
			}
			finally
			{
				DestroyAll(observer, shooter);
			}
		}

		[Test]
		public void WorldSound_DoesNotPublishCombatEvent()
		{
			GameObject shooter = CreateActor("Shooter", UnitTeamId.Enemy);
			try
			{
				WorldSoundHub.PublishGunshot(shooter.transform, shooter.transform.position);
				Assert.AreEqual(0, CombatEventHub.PublishCount, "WorldSoundHub ≠ CombatEventHub");
			}
			finally
			{
				Object.DestroyImmediate(shooter);
			}
		}

		[Test]
		public void Gunshot_AimedHostile_SetsImmediateThreat_WithoutKnowledge()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				DetectionProcessor processor = victim.GetComponent<DetectionProcessor>();
				CombatEventHub.Publish(CombatEvent.Gunshot(
					attacker.GetComponent<UnitTeam>(),
					attacker.GetComponent<UnitTeam>(),
					victim.transform,
					victim.transform.position));

				Assert.IsTrue(victim.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.AreEqual(
					ImmediateThreatCause.IncomingFire,
					victim.GetComponent<ImmediateThreatSource>().LastCause);
				Assert.AreEqual(0, processor.Contacts.Count);
				Assert.AreEqual(UnitAIState.Idle, victim.GetComponent<UnitAIController>().CurrentState);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void Hit_Hostile_SetsConfirmedHit()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				CombatEventHub.Publish(CombatEvent.Hit(
					attacker.GetComponent<UnitTeam>(),
					attacker.GetComponent<UnitTeam>(),
					victim.GetComponent<UnitAIController>(),
					victim.transform.position));

				Assert.IsTrue(victim.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.AreEqual(
					ImmediateThreatCause.ConfirmedHit,
					victim.GetComponent<ImmediateThreatSource>().LastCause);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void FriendlyGunshot_Publishes_ButDoesNotSetThreat()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject ally = CreateActor("Ally", UnitTeamId.Player);
			try
			{
				CombatEventHub.Publish(CombatEvent.Gunshot(
					ally.GetComponent<UnitTeam>(),
					ally.GetComponent<UnitTeam>(),
					victim.transform,
					victim.transform.position));

				Assert.AreEqual(1, CombatEventHub.PublishCount);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, ally);
			}
		}

		[Test]
		public void Impact_DoesNotSetImmediateThreat()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				CombatEventHub.Publish(CombatEvent.Impact(
					attacker.GetComponent<UnitTeam>(),
					attacker.GetComponent<UnitTeam>(),
					null,
					Vector3.zero));

				Assert.AreEqual(CombatEventType.Impact, CombatEventHub.LastPublished.Type);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void Death_Publishes_AndDoesNotSetImmediateThreat()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			try
			{
				UnitHealth health = victim.AddComponent<UnitHealth>();
				health.EnterDead();
				Assert.AreEqual(1, CombatEventHub.PublishCount);
				Assert.AreEqual(CombatEventType.Death, CombatEventHub.LastPublished.Type);
				Assert.AreEqual(health, CombatEventHub.LastPublished.Target);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				Object.DestroyImmediate(victim);
			}
		}

		[Test]
		public void UnrelatedAim_DoesNotSetBystanderThreat()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject bystander = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				CombatEventHub.Publish(CombatEvent.Gunshot(
					attacker.GetComponent<UnitTeam>(),
					attacker.GetComponent<UnitTeam>(),
					victim.transform,
					victim.transform.position));

				Assert.IsTrue(victim.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.IsFalse(bystander.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, bystander, attacker);
			}
		}

		[Test]
		public void DamageableTargetDeath_PublishesDeathEvent()
		{
			var go = new GameObject("HpTarget");
			try
			{
				DamageableTarget target = go.AddComponent<DamageableTarget>();
				target.SetMaxHealth(1f, true);
				target.ApplyDamage(10f, Vector3.zero, Vector3.up, Vector3.forward, null);
				Assert.AreEqual(1, CombatEventHub.PublishCount);
				Assert.AreEqual(CombatEventType.Death, CombatEventHub.LastPublished.Type);
				Assert.AreEqual(target, CombatEventHub.LastPublished.Target);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void SignalApi_StillSetsThreat_WithoutRequiringHubPublish()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsTrue(victim.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.AreEqual(0, CombatEventHub.PublishCount, "Signal remains the #7 test API, not a world publisher");
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}
		#endregion

		#region Private Methods
		private void OnHeard(CombatEvent _evt)
		{
			m_ListenerHits++;
			m_LastHeard = _evt;
		}

		private static GameObject CreateVictim(UnitTeamId _team)
		{
			GameObject go = CreateObserver();
			go.name = "Victim";
			go.GetComponent<UnitTeam>().SetTeam(_team);
			return go;
		}

		private static GameObject CreateObserver()
		{
			var go = new GameObject("Observer");
			go.AddComponent<UnitTeam>().SetTeam(UnitTeamId.Player);
			go.AddComponent<DetectionProcessor>();
			go.AddComponent<UnitAIController>();
			return go;
		}

		private static GameObject CreateActor(string _name, UnitTeamId _team)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(_team);
			return go;
		}

		private static void DestroyAll(params GameObject[] _objects)
		{
			for (int i = 0; i < _objects.Length; i++)
			{
				if (_objects[i] != null)
					Object.DestroyImmediate(_objects[i]);
			}
		}
		#endregion
	}
}
