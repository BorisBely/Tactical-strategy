using NUnit.Framework;

namespace Vision.Tests
{
	public sealed class CombatIntentMathTests
	{
		[Test]
		public void FromEngageAction_MapsOnlyEngage()
		{
			Assert.AreEqual(CombatIntent.Engage, CombatIntentMath.FromEngageAction(true));
			Assert.AreEqual(CombatIntent.Hold, CombatIntentMath.FromEngageAction(false));
		}

		[Test]
		public void Hold_VetoesFireAndAim_KeepsTrack()
		{
			Assert.AreEqual(
				EngagementDecision.Ignore,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Fire, CombatIntent.Hold));
			Assert.AreEqual(
				EngagementDecision.Ignore,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Aim, CombatIntent.Hold));
			Assert.AreEqual(
				EngagementDecision.Track,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Track, CombatIntent.Hold));
			Assert.AreEqual(
				EngagementDecision.None,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.None, CombatIntent.Hold));
			Assert.AreEqual(
				EngagementDecision.Ignore,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Ignore, CombatIntent.Hold));
		}

		[Test]
		public void Engage_DoesNotChangeG6()
		{
			Assert.AreEqual(
				EngagementDecision.Fire,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Fire, CombatIntent.Engage));
			Assert.AreEqual(
				EngagementDecision.Aim,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Aim, CombatIntent.Engage));
			Assert.AreEqual(
				EngagementDecision.Track,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Track, CombatIntent.Engage));
		}

		[Test]
		public void Hold_DoesNotInventFire()
		{
			Assert.AreNotEqual(
				EngagementDecision.Fire,
				CombatIntentMath.ApplyHoldVeto(EngagementDecision.Track, CombatIntent.Hold));
		}
	}
}
