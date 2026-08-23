#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G3 identity math smoke (no Play Mode). Full runtime suite writes DetectionG3_LAST.txt on Play.
/// </summary>
public static class DetectionG3TestRunner
{
	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG3 Identity Smoke (no Play)", false, 131)]
	public static void RunIdentitySmokeFromMenu()
	{
		string report = BuildIdentitySmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG3_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		Debug.Log($"[DetectionG3TestRunner] wrote {latest}\n{report}");
	}

	public static string BuildIdentitySmokeReport()
	{
		var sb = new StringBuilder(2048);
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

		sb.AppendLine($"DetectionG3 IdentityMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		float idStep = IdentityKnowledgeMath.IntegrateConfidence(
			0f, 1f, 0.35f, true, ObservableAffiliation.Hostile);
		float detStep = DetectionQualityMath.IntegrateProgress(0f, 1f, 0.35f);
		Check("Math_IdentitySlowerThanDetection",
			idStep < detStep && idStep < IdentityKnowledgeMath.DefaultCommitThreshold,
			$"id={idStep:F3} det={detStep:F3}");

		float held = IdentityKnowledgeMath.IntegrateConfidence(
			0.55f, 0f, 1f, false, ObservableAffiliation.Hostile);
		Check("Math_HoldWhenLost", Mathf.Abs(held - 0.55f) < 0.0001f, $"held={held:F3}");

		float noCue = IdentityKnowledgeMath.IntegrateConfidence(
			0.2f, 1f, 1f, true, ObservableAffiliation.Unknown);
		Check("Math_NoCueDoesNotGrow", Mathf.Abs(noCue - 0.2f) < 0.0001f, $"c={noCue:F3}");

		Check("Math_UnknownBelowCommit",
			IdentityKnowledgeMath.ResolveIdentity(0.49f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Unknown,
			"0.49 must stay Unknown");
		Check("Math_CommitHostile",
			IdentityKnowledgeMath.ResolveIdentity(0.5f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Hostile,
			"0.5 Hostile cue");
		Check("Math_HoldCommittedWithoutCue",
			IdentityKnowledgeMath.ResolveIdentity(0.8f, ObservableAffiliation.Unknown, PerceivedIdentity.Friendly)
			== PerceivedIdentity.Friendly,
			"missing cue holds previous");

		Check("Math_RelationshipFollowsIdentity",
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Hostile) == PerceivedRelationship.Hostile,
			"Hostile→Hostile");
		Check("Math_UnknownRelationship",
			IdentityKnowledgeMath.ResolveRelationship(PerceivedIdentity.Unknown) == PerceivedRelationship.Unknown,
			"Unknown→Unknown");

		Check("Math_HostileFarLow",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 400f) == ThreatLevel.Low,
			"400m Hostile");
		Check("Math_HostileNearHigh",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 10f) == ThreatLevel.High,
			"10m Hostile");
		Check("Math_FriendlyNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Friendly, 10f) == ThreatLevel.None,
			"Friendly near");
		Check("Math_NeutralNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Neutral, 10f) == ThreatLevel.None,
			"Neutral near");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif
