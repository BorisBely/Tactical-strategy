using NUnit.Framework;

namespace Vision.Tests
{
	public sealed class EngagementDecisionMathTests
	{
		[Test]
		public void NoTarget_IsNone()
		{
			EngagementDecisionContext ctx = Base();
			ctx.HasSelectedTarget = false;
			Assert.AreEqual(EngagementDecision.None, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void UnknownIdentity_CanFire()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Identity = PerceivedIdentity.Unknown;
			Assert.AreEqual(EngagementDecision.Fire, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void FriendlyIdentity_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Identity = PerceivedIdentity.Friendly;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void FriendlyRelationship_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Identity = PerceivedIdentity.Unknown;
			ctx.Relationship = PerceivedRelationship.Friendly;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void NeutralIdentity_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Identity = PerceivedIdentity.Neutral;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void SelectedNoLos_IsTrack()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.Lost;
			Assert.AreEqual(EngagementDecision.Track, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void LosWithoutAimProgress_IsAim()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.AimReadyToFire = false;
			Assert.AreEqual(EngagementDecision.Aim, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void LosWeaponNotReady_IsAim()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.WeaponCanFireEventually = false;
			Assert.AreEqual(EngagementDecision.Aim, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void ValidAimAndFireGate_IsFire()
		{
			Assert.AreEqual(EngagementDecision.Fire, EngagementDecisionMath.Evaluate(FireReady()));
		}

		[Test]
		public void MemoryOnly_IsTrack()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.Lost;
			ctx.LastSeenConfidence = 0.6f;
			Assert.AreEqual(EngagementDecision.Track, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void Forgotten_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.LastSeenConfidence = 0f;
			ctx.HasKnowledge = false;
			ctx.ObservationState = ObservationState.Lost;
			ctx.HasLosConfirmedAim = false;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void KnowledgeWithoutVision_IsTrack()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.LastSeenConfidence = 0f;
			ctx.HasKnowledge = true;
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.NotObserved;
			Assert.AreEqual(EngagementDecision.Track, EngagementDecisionMath.Evaluate(ctx));
			Assert.AreNotEqual(EngagementDecision.Fire, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void NoContact_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.HasContact = false;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void NotEngageable_IsIgnore()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.IsWorldEngageable = false;
			Assert.AreEqual(EngagementDecision.Ignore, EngagementDecisionMath.Evaluate(ctx));
		}

		[Test]
		public void ThreatHighWithoutLos_IsNotFire()
		{
			EngagementDecisionContext ctx = FireReady();
			ctx.Threat = ThreatLevel.High;
			ctx.HasLosConfirmedAim = false;
			ctx.ObservationState = ObservationState.Lost;
			Assert.AreEqual(EngagementDecision.Track, EngagementDecisionMath.Evaluate(ctx));
		}

		private static EngagementDecisionContext Base()
		{
			return new EngagementDecisionContext
			{
				HasSelectedTarget = false,
				HasContact = false,
				Identity = PerceivedIdentity.Unknown,
				Relationship = PerceivedRelationship.Unknown,
				Threat = ThreatLevel.None,
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0f,
				IsWorldEngageable = false,
				HasLosConfirmedAim = false,
				WeaponCanFireEventually = false,
				AimReadyToFire = false
			};
		}

		private static EngagementDecisionContext FireReady()
		{
			return new EngagementDecisionContext
			{
				HasSelectedTarget = true,
				HasContact = true,
				Identity = PerceivedIdentity.Unknown,
				Relationship = PerceivedRelationship.Unknown,
				Threat = ThreatLevel.None,
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				IsWorldEngageable = true,
				HasLosConfirmedAim = true,
				WeaponCanFireEventually = true,
				AimReadyToFire = true
			};
		}
	}
}
