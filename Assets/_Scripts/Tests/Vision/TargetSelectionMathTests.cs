using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class TargetSelectionMathTests
	{
		[Test]
		public void Observed_BeatsStaleRemembered()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var observed = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				LastKnownPosition = new Vector3(20f, 0f, 0f),
				Threat = ThreatLevel.None
			};
			var stale = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.2f,
				LastKnownPosition = new Vector3(2f, 0f, 0f),
				Threat = ThreatLevel.High
			};

			float a = TargetSelectionMath.Score(observed, Vector3.zero, policy);
			float b = TargetSelectionMath.Score(stale, Vector3.zero, policy);
			Assert.Greater(a, b);
		}

		[Test]
		public void HigherConfidence_BeatsLower_WhenBothLost()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var high = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.9f,
				LastKnownPosition = new Vector3(5f, 0f, 0f)
			};
			var low = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.3f,
				LastKnownPosition = new Vector3(5f, 0f, 0f)
			};
			Assert.Greater(
				TargetSelectionMath.Score(high, Vector3.zero, policy),
				TargetSelectionMath.Score(low, Vector3.zero, policy));
		}

		[Test]
		public void Hostile_ScoresAtLeastAsHighAsUnknown()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var unknown = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				Identity = PerceivedIdentity.Unknown,
				LastKnownPosition = new Vector3(4f, 0f, 0f)
			};
			var hostile = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				Identity = PerceivedIdentity.Hostile,
				Relationship = PerceivedRelationship.Hostile,
				LastKnownPosition = new Vector3(4f, 0f, 0f)
			};
			Assert.GreaterOrEqual(
				TargetSelectionMath.Score(hostile, Vector3.zero, policy),
				TargetSelectionMath.Score(unknown, Vector3.zero, policy));
		}

		[Test]
		public void Nearer_BeatsFarther_SameMemory()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var near = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.8f,
				LastKnownPosition = new Vector3(3f, 0f, 0f)
			};
			var far = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.8f,
				LastKnownPosition = new Vector3(30f, 0f, 0f)
			};
			Assert.Greater(
				TargetSelectionMath.Score(near, Vector3.zero, policy),
				TargetSelectionMath.Score(far, Vector3.zero, policy));
		}

		[Test]
		public void TryGetObservedAimPoint_FalseWhenLost()
		{
			var lost = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastKnownPosition = new Vector3(9f, 0f, 1f),
				LastObservation = new VisionObservation
				{
					HasAimPoint = true,
					IsVisible = true,
					AimPoint = new Vector3(9f, 1f, 1f)
				}
			};
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(lost, out Vector3 aim));
			Assert.AreEqual(Vector3.zero, aim);
			Assert.AreEqual(new Vector3(9f, 0f, 1f), TargetSelectionMath.ResolveBelievedPosition(lost));
		}

		[Test]
		public void SoundOnly_AimPointFalse_AndScoresFromSoundConfidence()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var silent = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				SoundConfidence = 0f,
				LastKnownPosition = new Vector3(4f, 0f, 0f)
			};
			var heard = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				SoundConfidence = 0.9f,
				SoundPosition = new Vector3(4f, 0f, 0f),
				LastKnownPosition = Vector3.zero
			};
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(heard, out Vector3 aim));
			Assert.AreEqual(Vector3.zero, aim);
			Assert.Greater(
				TargetSelectionMath.Score(heard, Vector3.zero, policy),
				TargetSelectionMath.Score(silent, Vector3.zero, policy));
		}

		[Test]
		public void ScoreWithModifiers_MatchesScore_WhenNoWeaponOrMission()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			policy.WeaponSuitabilityWeight = 0f;
			policy.MissionBonus = 0f;
			var contact = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				LastKnownPosition = new Vector3(12f, 0f, 0f),
				Threat = ThreatLevel.Low
			};
			float baseScore = TargetSelectionMath.Score(contact, Vector3.zero, policy);
			float modified = TargetSelectionMath.ScoreWithModifiers(
				contact,
				Vector3.zero,
				policy,
				WeaponClassType.Unknown,
				100f,
				null);
			Assert.AreEqual(baseScore, modified);
		}
	}
}
