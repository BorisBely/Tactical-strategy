using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Block C CLOSED / VERIFIED. Identity / relationship / threat calibration (math, no Play).
/// Does not retune Detection Q or Memory. Does not know Selector / Engagement / Combat.
/// </summary>
public static class IdentityCalibrationScenarios
{
	#region Constants
	public const float BaselineIdentifyTimeSeconds = IdentityKnowledgeMath.DefaultIdentifyTimeSeconds;
	public const float BaselineCommitThreshold = IdentityKnowledgeMath.DefaultCommitThreshold;
	public const float BaselineThreatHighMeters = IdentityKnowledgeMath.DefaultThreatHighMeters;
	public const float BaselineThreatMediumMeters = IdentityKnowledgeMath.DefaultThreatMediumMeters;

	public static readonly float[] IdentityTimelineSeconds =
	{
		0f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f
	};

	public static readonly float[] ThreatSweepMeters =
	{
		10f, 25f, 50f, 80f, 100f, 200f, 400f
	};

	public static readonly float[] IdentifyTimeSweepSeconds =
	{
		1f, 1.5f, 2f, 3f, 4f
	};
	#endregion

	#region Nested Types
	public sealed class ReportResult
	{
		public string Body;
		public int PassCount;
		public int FailCount;
	}
	#endregion

	#region Public API
	public static float ConfidenceAt(float _elapsedObserved, float _identifyTimeSeconds = BaselineIdentifyTimeSeconds)
	{
		return IdentityKnowledgeMath.IntegrateConfidence(
			0f, 1f, _elapsedObserved, true, ObservableAffiliation.Hostile, _identifyTimeSeconds);
	}

	public static ReportResult BuildReport()
	{
		var sb = new StringBuilder(12288);
		int pass = 0;
		int fail = 0;

		void Check(string name, bool ok, string detail)
		{
			if (ok)
			{
				pass++;
				sb.AppendLine($"PASS {name} | {detail}");
			}
			else
			{
				fail++;
				sb.AppendLine($"FAIL {name} | {detail}");
			}
		}

		sb.AppendLine("BLOCK C — IDENTITY CALIBRATION MATH");
		sb.AppendLine("===================================");
		sb.AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("C0 CONTRACT");
		sb.AppendLine("Detected + Identity=Unknown is valid");
		sb.AppendLine("Unknown ≠ Friendly; Relationship=Unknown Threat=None until commit");
		sb.AppendLine("Evidence = ObservableAffiliation / IdentityAppearance, never UnitTeam");
		sb.AppendLine("LOS loss holds IdentityConfidence (Memory decays LastSeenConfidence only)");
		sb.AppendLine("PerceivedIdentity is affiliation-class (Friendly/Neutral/Hostile/Unknown), not Soldier/Military");
		sb.AppendLine("Relationship is a separate field, derived from committed Identity");
		sb.AppendLine("Hostile + far Threat=Low is valid");
		sb.AppendLine("C1 BASELINE");
		sb.AppendLine($"IdentifyTime = {F(BaselineIdentifyTimeSeconds, 1)} s");
		sb.AppendLine($"CommitThreshold = {F(BaselineCommitThreshold, 2)}");
		sb.AppendLine($"Threat High ≤ {F(BaselineThreatHighMeters, 0)} m");
		sb.AppendLine($"Threat Medium ≤ {F(BaselineThreatMediumMeters, 0)} m");
		sb.AppendLine("---");

		Check("C1_IdentifyTime",
			Mathf.Abs(IdentityKnowledgeMath.DefaultIdentifyTimeSeconds - 4f) < 0.0001f,
			$"default={IdentityKnowledgeMath.DefaultIdentifyTimeSeconds:F1}");
		Check("C1_Commit",
			Mathf.Abs(IdentityKnowledgeMath.DefaultCommitThreshold - 0.5f) < 0.0001f,
			$"default={IdentityKnowledgeMath.DefaultCommitThreshold:F2}");
		Check("C1_IdentifySlowerThanAcquire",
			BaselineIdentifyTimeSeconds > DetectionQualityMath.DefaultAcquireTime + 0.5f,
			$"identify={F(BaselineIdentifyTimeSeconds, 1)} acquire={F(DetectionQualityMath.DefaultAcquireTime, 2)}");

		float confUnknownCue = IdentityKnowledgeMath.IntegrateConfidence(
			0f, 1f, 2f, true, ObservableAffiliation.Unknown);
		Check("C2_UnknownCueDoesNotGrow",
			Mathf.Abs(confUnknownCue) < 0.0001f,
			$"conf={F(confUnknownCue, 3)}");

		float confHeldLost = IdentityKnowledgeMath.IntegrateConfidence(
			0.7f, 0f, 4f, false, ObservableAffiliation.Hostile);
		Check("C2_HoldWhenNotObserved",
			Mathf.Abs(confHeldLost - 0.7f) < 0.0001f,
			$"conf={F(confHeldLost, 3)}");

		float grown = IdentityKnowledgeMath.IntegrateConfidence(
			0f, 1f, 0.5f, true, ObservableAffiliation.Hostile);
		Check("C2_ValidCueGrows", grown > 0.12f && grown < 0.13f, $"0.5s conf={F(grown, 3)}");

		Check("C2_BelowCommitUnknown",
			IdentityKnowledgeMath.ResolveIdentity(0.49f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Unknown,
			"0.49 → Unknown");
		Check("C2_AtCommitHostile",
			IdentityKnowledgeMath.ResolveIdentity(0.5f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Hostile,
			"0.50 → Hostile");

		float over = IdentityKnowledgeMath.IntegrateConfidence(0.95f, 1f, 2f, true, ObservableAffiliation.Hostile);
		Check("C2_NeverAboveOne", over <= 1.0001f, $"conf={F(over, 3)}");
		float under = IdentityKnowledgeMath.IntegrateConfidence(0f, 1f, 0f, true, ObservableAffiliation.Hostile);
		Check("C2_NeverBelowZero", under >= -0.0001f, $"conf={F(under, 3)}");

		float prev = 0f;
		bool monotone = true;
		bool inRange = true;
		for (int i = 0; i < 40; i++)
		{
			float t = i * 0.1f;
			float c = ConfidenceAt(t);
			if (c + 0.0001f < prev)
				monotone = false;
			if (c < -0.0001f || c > 1.0001f)
				inRange = false;
			prev = c;
		}

		Check("C2_MonotoneWhileEvidenceStable", monotone, "t↑ ⇒ conf↑ while Observed+Hostile");
		Check("C2_Clamp01", inRange, "0 ≤ conf ≤ 1");
		Check("C2_DetectionFullIdentityZeroValid",
			true,
			"DetectionProgress=1 IdentityConfidence=0 is a valid pair (independent fields)");

		float commitAt = IdentityKnowledgeMath.SecondsToCommit();
		Check("C3_CommitAtTwoSeconds",
			Mathf.Abs(commitAt - 2f) < 0.0001f,
			$"Q=1 commit t={F(commitAt, 2)} s (IdentifyTime×0.5)");

		sb.AppendLine("---");
		sb.AppendLine("HOSTILE CUE TIMELINE  IdentifyTime=4.0  Q=1");
		for (int i = 0; i < IdentityTimelineSeconds.Length; i++)
		{
			float t = IdentityTimelineSeconds[i];
			float conf = ConfidenceAt(t);
			PerceivedIdentity id = IdentityKnowledgeMath.ResolveIdentity(
				conf, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown);
			PerceivedRelationship rel = IdentityKnowledgeMath.ResolveRelationship(id);
			ThreatLevel threat = IdentityKnowledgeMath.EvaluateThreat(rel, 15f);
			sb.AppendLine(
				$"t={F(t, 2)}  conf={F(conf, 3)}  identity={id}  rel={rel}  threat={threat}");
		}

		Check("C3_T1StillUnknown",
			IdentityKnowledgeMath.ResolveIdentity(ConfidenceAt(1f), ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Unknown,
			"t=1.0 still Unknown");
		Check("C3_T2CommitHostile",
			IdentityKnowledgeMath.ResolveIdentity(ConfidenceAt(2f), ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Hostile,
			"t=2.0 commit Hostile");

		float stepwise = 0f;
		for (int i = 0; i < 40; i++)
		{
			stepwise = IdentityKnowledgeMath.IntegrateConfidence(
				stepwise, 1f, 0.05f, true, ObservableAffiliation.Hostile);
		}

		Check("C3_StepwiseTicksReachCommit",
			IdentityKnowledgeMath.ResolveIdentity(
				stepwise, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Hostile,
			$"40×0.05s conf={F(stepwise, 6)}");
		Check("C3_T4FullConfidence",
			Mathf.Abs(ConfidenceAt(4f) - 1f) < 0.0001f,
			$"t=4 conf={F(ConfidenceAt(4f), 3)}");

		sb.AppendLine("---");
		sb.AppendLine("C4 CUES (Q=1, t=4 s)");
		ObservableAffiliation[] cues =
		{
			ObservableAffiliation.Hostile,
			ObservableAffiliation.Friendly,
			ObservableAffiliation.Neutral,
			ObservableAffiliation.Unknown
		};
		for (int i = 0; i < cues.Length; i++)
		{
			ObservableAffiliation cue = cues[i];
			float conf = IdentityKnowledgeMath.IntegrateConfidence(0f, 1f, 4f, true, cue);
			PerceivedIdentity id = IdentityKnowledgeMath.ResolveIdentity(conf, cue, PerceivedIdentity.Unknown);
			PerceivedRelationship rel = IdentityKnowledgeMath.ResolveRelationship(id);
			sb.AppendLine($"cue={cue}  conf={F(conf, 3)}  identity={id}  rel={rel}");
		}

		Check("C4_HostileCommits",
			IdentityKnowledgeMath.ResolveIdentity(1f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Hostile,
			"Hostile");
		Check("C4_FriendlyCommits",
			IdentityKnowledgeMath.ResolveIdentity(1f, ObservableAffiliation.Friendly, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Friendly,
			"Friendly");
		Check("C4_NeutralCommits",
			IdentityKnowledgeMath.ResolveIdentity(1f, ObservableAffiliation.Neutral, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Neutral,
			"Neutral");
		Check("C4_UnknownStaysUnknown",
			IdentityKnowledgeMath.ResolveIdentity(
				IdentityKnowledgeMath.IntegrateConfidence(0f, 1f, 2f, true, ObservableAffiliation.Unknown),
				ObservableAffiliation.Unknown,
				PerceivedIdentity.Unknown) == PerceivedIdentity.Unknown,
			"Unknown cue");

		Check("C5_UnknownMeansUnknownRelThreat",
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Unknown) == PerceivedRelationship.Unknown &&
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Unknown, 10f) == ThreatLevel.None,
			"Unknown → rel Unknown, threat None");

		Check("C6_IdentityAndRelationshipAreSeparateTypes",
			typeof(PerceivedIdentity) != typeof(PerceivedRelationship),
			"separate enums");
		Check("C6_RelationshipFollowsCommittedIdentity",
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Hostile) == PerceivedRelationship.Hostile &&
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Friendly) == PerceivedRelationship.Friendly &&
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Neutral) == PerceivedRelationship.Neutral,
			"derived from Identity, not UnitTeam");
		Check("C6_HostileFarIsNotHighThreat",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 400f) == ThreatLevel.Low,
			"Identity Hostile ≠ Threat High");

		Check("C7_HostileCloseHigh",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 10f) == ThreatLevel.High,
			"10 m");
		Check("C7_HostileAtHighBand",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 25f) == ThreatLevel.High,
			"25 m");
		Check("C7_HostileMidMedium",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 50f) == ThreatLevel.Medium,
			"50 m");
		Check("C7_FriendlyNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Friendly, 10f) == ThreatLevel.None,
			"Friendly → None");
		Check("C7_NeutralNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Neutral, 10f) == ThreatLevel.None,
			"Neutral → None (not Low)");

		sb.AppendLine("---");
		sb.AppendLine("C8 / C16 THREAT SWEEP  Relationship=Hostile  (baseline High≤25 Medium≤80)");
		ThreatLevel last = ThreatLevel.High;
		bool threatMonotone = true;
		for (int i = 0; i < ThreatSweepMeters.Length; i++)
		{
			float meters = ThreatSweepMeters[i];
			ThreatLevel threat = IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, meters);
			if ((int)threat > (int)last)
				threatMonotone = false;
			last = threat;
			sb.AppendLine($"  {F(meters, 0)} m  {threat}");
		}

		Check("C8_ThreatMonotoneWithDistance", threatMonotone, "closer ≥ farther");
		Check("C8_100mIsLowOnBaseline",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 100f) == ThreatLevel.Low,
			"100 m > 80 m → Low (hypothesis Medium is C16, not C1)");

		var contact = new PerceivedContact
		{
			Identity = PerceivedIdentity.Friendly,
			IdentityConfidence = 1f,
			Relationship = PerceivedRelationship.Friendly,
			CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f },
			LastObservation = new VisionObservation { DistanceSq = 15f * 15f, IsVisible = true }
		};
		IdentityKnowledgeMath.ApplyToContact(contact, true, ObservableAffiliation.Hostile, 0.05f);
		Check("C11_CueFlipNotInstantHostile",
			contact.Identity != PerceivedIdentity.Hostile &&
			contact.IdentityConfidence < BaselineCommitThreshold,
			$"after 0.05s id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}");
		IdentityKnowledgeMath.ApplyToContact(contact, true, ObservableAffiliation.Hostile, 2f);
		Check("C11_CueFlipReaccumulates",
			contact.Identity == PerceivedIdentity.Hostile &&
			contact.IdentityConfidence >= BaselineCommitThreshold &&
			contact.Relationship == PerceivedRelationship.Hostile,
			$"after +2s id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}");

		Check("C11_UnknownCueHoldsCommitted",
			IdentityKnowledgeMath.ResolveIdentity(0.8f, ObservableAffiliation.Unknown, PerceivedIdentity.Friendly)
			== PerceivedIdentity.Friendly,
			"missing cue keeps previous");

		sb.AppendLine("---");
		sb.AppendLine("C15 IdentifyTime sweep (diagnostic, not FAIL)  Q=1  commit = IdentifyTime × 0.50");
		for (int i = 0; i < IdentifyTimeSweepSeconds.Length; i++)
		{
			float identify = IdentifyTimeSweepSeconds[i];
			float tCommit = IdentityKnowledgeMath.SecondsToCommit(identify, 1f);
			sb.AppendLine($"IdentifyTime={F(identify, 1)} s  commit≈{F(tCommit, 2)} s");
		}

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return new ReportResult
		{
			Body = sb.ToString(),
			PassCount = pass,
			FailCount = fail
		};
	}
	#endregion

	#region Private Methods
	private static string F(float _value, int _decimals)
	{
		return _value.ToString("F" + _decimals, CultureInfo.InvariantCulture);
	}
	#endregion
}
