using NUnit.Framework;

namespace Vision.Tests
{
	public sealed class DefaultCombatEngagementPolicyTests
	{
		private readonly DefaultCombatEngagementPolicy m_Policy = new DefaultCombatEngagementPolicy();

		[Test]
		public void MatchesMath()
		{
			EngagementDecisionContext fire = FireReady();
			Assert.AreEqual(EngagementDecisionMath.Evaluate(fire), m_Policy.Evaluate(fire));

			EngagementDecisionContext memory = FireReady();
			memory.HasLosConfirmedAim = false;
			memory.ObservationState = ObservationState.Lost;
			Assert.AreEqual(EngagementDecisionMath.Evaluate(memory), m_Policy.Evaluate(memory));
		}

		[Test]
		public void NeverReturnsReservedStates()
		{
			EngagementDecisionContext[] snapshots =
			{
				default,
				FireReady(),
				Friendly(),
				Memory(),
				Dead()
			};

			for (int i = 0; i < snapshots.Length; i++)
			{
				EngagementDecision decision = m_Policy.Evaluate(snapshots[i]);
				Assert.AreNotEqual(EngagementDecision.Observe, decision, i.ToString());
				Assert.AreNotEqual(EngagementDecision.Suppress, decision, i.ToString());
				Assert.AreNotEqual(EngagementDecision.Report, decision, i.ToString());
			}
		}

		[Test]
		public void ReproduceLegacyFireIntent()
		{
			Assert.AreEqual(EngagementDecision.Fire, m_Policy.Evaluate(FireReady()));
		}

		[Test]
		public void ThreatDoesNotBypassLos()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Threat = ThreatLevel.High;
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.Lost;
			Assert.AreEqual(EngagementDecision.Track, m_Policy.Evaluate(ctx));
		}

		private static EngagementDecisionContext FireReady()
		{
			return new EngagementDecisionContext
			{
				HasSelectedTarget = true,
				HasContact = true,
				Identity = PerceivedIdentity.Unknown,
				Relationship = PerceivedRelationship.Unknown,
				Threat = ThreatLevel.Low,
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				IsWorldEngageable = true,
				HasLosConfirmedAim = true,
				WeaponCanFireEventually = true,
				AimReadyToFire = true
			};
		}

		private static EngagementDecisionContext Friendly()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Identity = PerceivedIdentity.Friendly;
			ctx.Relationship = PerceivedRelationship.Friendly;
			return ctx;
		}

		private static EngagementDecisionContext Memory()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.Lost;
			ctx.LastSeenConfidence = 0.5f;
			return ctx;
		}

		private static EngagementDecisionContext Dead()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.IsWorldEngageable = false;
			return ctx;
		}
	}
}
