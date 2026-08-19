using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class IdentityKnowledgeMathTests
	{
		[Test]
		public void Confidence_GrowsOnlyWhenObservedWithCue()
		{
			float grown = IdentityKnowledgeMath.IntegrateConfidence(
				0f, 1f, 0.2f, true, ObservableAffiliation.Hostile);
			Assert.Greater(grown, 0f);

			float heldNoCue = IdentityKnowledgeMath.IntegrateConfidence(
				0.3f, 1f, 0.2f, true, ObservableAffiliation.Unknown);
			Assert.AreEqual(0.3f, heldNoCue, 0.0001f);

			float heldLost = IdentityKnowledgeMath.IntegrateConfidence(
				0.3f, 0f, 0.2f, false, ObservableAffiliation.Hostile);
			Assert.AreEqual(0.3f, heldLost, 0.0001f);
		}

		[Test]
		public void Identity_StaysUnknownBelowCommitThreshold()
		{
			PerceivedIdentity id = IdentityKnowledgeMath.ResolveIdentity(
				0.49f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown);
			Assert.AreEqual(PerceivedIdentity.Unknown, id);
		}

		[Test]
		public void Identity_CommitsFromCueAboveThreshold()
		{
			PerceivedIdentity id = IdentityKnowledgeMath.ResolveIdentity(
				0.5f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown);
			Assert.AreEqual(PerceivedIdentity.Hostile, id);
		}

		[Test]
		public void Identity_StepwiseTicksReachCommitAtTwoSeconds()
		{
			float conf = 0f;
			for (int i = 0; i < 40; i++)
			{
				conf = IdentityKnowledgeMath.IntegrateConfidence(
					conf, 1f, 0.05f, true, ObservableAffiliation.Hostile);
			}

			Assert.IsTrue(IdentityKnowledgeMath.HasReachedCommitThreshold(conf), $"conf={conf:R}");
			Assert.AreEqual(
				PerceivedIdentity.Hostile,
				IdentityKnowledgeMath.ResolveIdentity(conf, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown));
		}

		[Test]
		public void Identity_HoldsPreviousWhenCueMissingAfterCommit()
		{
			PerceivedIdentity id = IdentityKnowledgeMath.ResolveIdentity(
				0.8f, ObservableAffiliation.Unknown, PerceivedIdentity.Hostile);
			Assert.AreEqual(PerceivedIdentity.Hostile, id);
		}

		[Test]
		public void Threat_HostileFarIsLow_HostileNearIsHigh()
		{
			Assert.AreEqual(
				ThreatLevel.Low,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 400f));
			Assert.AreEqual(
				ThreatLevel.High,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 10f));
			Assert.AreEqual(
				ThreatLevel.None,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Friendly, 10f));
			Assert.AreEqual(
				ThreatLevel.None,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Unknown, 10f));
		}

		[Test]
		public void Relationship_FollowsCommittedIdentity()
		{
			Assert.AreEqual(
				PerceivedRelationship.Hostile,
				IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Hostile));
			Assert.AreEqual(
				PerceivedRelationship.Unknown,
				IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Unknown));
		}

		[Test]
		public void IdentifyRate_IsSlowerThanDetectionAcquire()
		{
			float identityDt = IdentityKnowledgeMath.IntegrateConfidence(
				0f, 1f, 0.35f, true, ObservableAffiliation.Hostile);
			float detectionDt = DetectionQualityMath.IntegrateProgress(0f, 1f, 0.35f);
			Assert.Less(identityDt, detectionDt);
			Assert.Less(identityDt, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.GreaterOrEqual(detectionDt, 0.99f);
		}
	}
}
