using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class ImmediateThreatSourceTests
	{
		[Test]
		public void A1_Default_IsFalse()
		{
			GameObject go = CreateVictim(UnitTeamId.Player);
			try
			{
				UnitAIController ai = go.GetComponent<UnitAIController>();
				Assert.IsFalse(ai.ImmediateThreat);
				ImmediateThreatSource source = ai.EnsureImmediateThreatSource();
				Assert.IsFalse(source.WindowActive);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void A2_HostileIncomingFire_SetsTrue()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				UnitAIController ai = victim.GetComponent<UnitAIController>();
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsTrue(ai.ImmediateThreat);
				ImmediateThreatSource source = victim.GetComponent<ImmediateThreatSource>();
				Assert.IsTrue(source.WindowActive);
				Assert.AreEqual(ImmediateThreatCause.IncomingFire, source.LastCause);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void A3_FriendlyFire_Ignored()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject ally = CreateActor("Ally", UnitTeamId.Player);
			try
			{
				ImmediateThreatSignal.NotifyIncomingFire(ally.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, ally);
			}
		}

		[Test]
		public void A4_NeutralFire_Ignored()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject neutral = CreateActor("Neutral", UnitTeamId.Neutral);
			try
			{
				ImmediateThreatSignal.NotifyIncomingFire(neutral.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, neutral);
			}
		}

		[Test]
		public void A5_UnrelatedAim_Ignored()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject bystander = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), bystander.transform);
				Assert.IsFalse(victim.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.IsTrue(bystander.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(victim, bystander, attacker);
			}
		}

		[Test]
		public void A6_ThreatLevelHigh_DoesNotSetFlag()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			try
			{
				UnitAIController ai = victim.GetComponent<UnitAIController>();
				ai.EnsureImmediateThreatSource();
				Assert.IsFalse(ai.ImmediateThreat, "ThreatLevel.High is not an ImmediateThreat source");
			}
			finally
			{
				Object.DestroyImmediate(victim);
			}
		}

		[Test]
		public void A7_TtlExpires()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				UnitAIController ai = victim.GetComponent<UnitAIController>();
				ImmediateThreatSource source = ai.EnsureImmediateThreatSource();
				source.DurationSeconds = 0.2f;
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsTrue(ai.ImmediateThreat);
				ai.Tick(0.25f);
				Assert.IsFalse(ai.ImmediateThreat);
				Assert.IsFalse(source.WindowActive);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void A8_RepeatAttack_RefreshesWindow()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				UnitAIController ai = victim.GetComponent<UnitAIController>();
				ImmediateThreatSource source = ai.EnsureImmediateThreatSource();
				source.DurationSeconds = 1f;
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), victim.transform);
				ai.Tick(0.4f);
				float remainingBefore = source.RemainingSeconds;
				Assert.Less(remainingBefore, 0.75f);
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), victim.transform);
				Assert.IsTrue(ai.ImmediateThreat);
				Assert.Greater(source.RemainingSeconds, remainingBefore);
			}
			finally
			{
				DestroyAll(victim, attacker);
			}
		}

		[Test]
		public void A9_Isolation_IndependentUnits()
		{
			GameObject a = CreateVictim(UnitTeamId.Player);
			GameObject b = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				ImmediateThreatSignal.NotifyIncomingFire(attacker.GetComponent<UnitTeam>(), a.transform);
				Assert.IsTrue(a.GetComponent<UnitAIController>().ImmediateThreat);
				Assert.IsFalse(b.GetComponent<UnitAIController>().ImmediateThreat);
			}
			finally
			{
				DestroyAll(a, b, attacker);
			}
		}

		[Test]
		public void ConfirmedHit_Hostile_SetsTrue()
		{
			GameObject victim = CreateVictim(UnitTeamId.Player);
			GameObject attacker = CreateActor("Attacker", UnitTeamId.Enemy);
			try
			{
				ImmediateThreatSignal.NotifyConfirmedHit(attacker.GetComponent<UnitTeam>(), victim.transform);
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

		private static GameObject CreateVictim(UnitTeamId _team)
		{
			GameObject go = CreateActor("Victim", _team);
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
	}
}
