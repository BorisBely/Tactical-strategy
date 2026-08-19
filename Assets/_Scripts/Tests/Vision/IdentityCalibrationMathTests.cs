using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class IdentityCalibrationMathTests
	{
		[Test]
		public void CalibrationReport_Passes()
		{
			IdentityCalibrationScenarios.ReportResult result = IdentityCalibrationScenarios.BuildReport();
			Assert.AreEqual(0, result.FailCount, result.Body);
		}

		[Test]
		public void UnknownCue_DoesNotGrowConfidence()
		{
			float conf = IdentityKnowledgeMath.IntegrateConfidence(
				0f, 1f, 2f, true, ObservableAffiliation.Unknown);
			Assert.AreEqual(0f, conf, 0.0001f);
		}

		[Test]
		public void ValidCue_GrowsAndCommitsAtThreshold()
		{
			float below = IdentityKnowledgeMath.IntegrateConfidence(
				0f, 1f, 1.99f, true, ObservableAffiliation.Hostile);
			Assert.Less(below, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.AreEqual(
				PerceivedIdentity.Unknown,
				IdentityKnowledgeMath.ResolveIdentity(below, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown));

			float at = IdentityKnowledgeMath.IntegrateConfidence(
				0f, 1f, 2f, true, ObservableAffiliation.Hostile);
			Assert.GreaterOrEqual(at, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.AreEqual(
				PerceivedIdentity.Hostile,
				IdentityKnowledgeMath.ResolveIdentity(at, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown));
		}

		[Test]
		public void DetectionFull_WithZeroIdentity_IsAllowed()
		{
			var contact = new PerceivedContact
			{
				DetectionProgress = 1f,
				State = DetectionState.Detected,
				IdentityConfidence = 0f,
				Identity = PerceivedIdentity.Unknown,
				Relationship = PerceivedRelationship.Unknown,
				Threat = ThreatLevel.None
			};
			Assert.AreEqual(1f, contact.DetectionProgress);
			Assert.AreEqual(0f, contact.IdentityConfidence);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
		}

		[Test]
		public void CueConflict_DoesNotInstantlyRemapCommittedIdentity()
		{
			var contact = new PerceivedContact
			{
				Identity = PerceivedIdentity.Friendly,
				IdentityConfidence = 1f,
				Relationship = PerceivedRelationship.Friendly,
				CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f },
				LastObservation = new VisionObservation { DistanceSq = 100f, IsVisible = true }
			};

			IdentityKnowledgeMath.ApplyToContact(contact, true, ObservableAffiliation.Hostile, 0.05f);
			Assert.AreNotEqual(PerceivedIdentity.Hostile, contact.Identity);
			Assert.Less(contact.IdentityConfidence, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.AreEqual(PerceivedRelationship.Unknown, contact.Relationship);
		}

		[Test]
		public void CueConflict_ReaccumulatesToNewIdentity()
		{
			var contact = new PerceivedContact
			{
				Identity = PerceivedIdentity.Friendly,
				IdentityConfidence = 1f,
				Relationship = PerceivedRelationship.Friendly,
				CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f },
				LastObservation = new VisionObservation { DistanceSq = 100f, IsVisible = true }
			};

			IdentityKnowledgeMath.ApplyToContact(contact, true, ObservableAffiliation.Hostile, 0.05f);
			IdentityKnowledgeMath.ApplyToContact(contact, true, ObservableAffiliation.Hostile, 2f);
			Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);
			Assert.AreEqual(PerceivedRelationship.Hostile, contact.Relationship);
			Assert.GreaterOrEqual(contact.IdentityConfidence, IdentityKnowledgeMath.DefaultCommitThreshold);
		}

		[Test]
		public void Confidence_ClampedAndMonotone()
		{
			float prev = 0f;
			for (int i = 0; i <= 40; i++)
			{
				float conf = IdentityCalibrationScenarios.ConfidenceAt(i * 0.1f);
				Assert.GreaterOrEqual(conf, 0f);
				Assert.LessOrEqual(conf, 1f);
				Assert.GreaterOrEqual(conf + 0.0001f, prev);
				prev = conf;
			}
		}
	}
}
