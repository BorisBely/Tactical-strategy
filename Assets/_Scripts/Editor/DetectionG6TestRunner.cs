#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G6 engagement math smoke (no Play Mode). Full runtime suite writes DetectionG6_LAST.txt on Play.
/// </summary>
public static class DetectionG6TestRunner
{
	[MenuItem("Tools/Tests/Run DetectionG6 Engagement Smoke (no Play)")]
	public static void RunEngagementSmokeFromMenu()
	{
		string report = BuildEngagementSmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG6_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		int resultAt = report.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? report.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionG6TestRunner] wrote {latest} {resultLine}\n{report}");
	}

	public static string BuildEngagementSmokeReport()
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

		sb.AppendLine($"DetectionG6 EngagementMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		var none = new EngagementDecisionContext();
		Check("Math_NoTargetNone",
			EngagementDecisionMath.Evaluate(none) == EngagementDecision.None,
			"No target → None");

		EngagementDecisionContext unknown = FireReady();
		Check("Math_UnknownCanFire",
			EngagementDecisionMath.Evaluate(unknown) == EngagementDecision.Fire,
			"Unknown may Fire");

		EngagementDecisionContext friendly = FireReady();
		friendly.Identity = PerceivedIdentity.Friendly;
		friendly.Relationship = PerceivedRelationship.Friendly;
		Check("Math_FriendlyIgnore",
			EngagementDecisionMath.Evaluate(friendly) == EngagementDecision.Ignore,
			"Friendly → Ignore");

		EngagementDecisionContext forgotten = FireReady();
		forgotten.LastSeenConfidence = 0f;
		forgotten.HasLosConfirmedAim = false;
		Check("Math_ForgottenIgnore",
			EngagementDecisionMath.Evaluate(forgotten) == EngagementDecision.Ignore,
			"Forgotten → Ignore");

		EngagementDecisionContext memory = FireReady();
		memory.HasLosConfirmedAim = false;
		memory.ObservationState = ObservationState.Lost;
		memory.LastSeenConfidence = 0.4f;
		Check("Math_MemoryTrack",
			EngagementDecisionMath.Evaluate(memory) == EngagementDecision.Track,
			"Memory → Track");
		Check("Math_MemoryNotFire",
			EngagementDecisionMath.Evaluate(memory) != EngagementDecision.Fire,
			"LastKnown is not Fire");

		EngagementDecisionContext aim = FireReady();
		aim.AimReadyToFire = false;
		Check("Math_AimWhenProgressLow",
			EngagementDecisionMath.Evaluate(aim) == EngagementDecision.Aim,
			"LOS without aim progress → Aim");

		Check("Math_FireWhenGatesPass",
			EngagementDecisionMath.Evaluate(FireReady()) == EngagementDecision.Fire,
			"Gates pass → Fire");

		IEngagementPolicy policy = new DefaultCombatEngagementPolicy();
		Check("Policy_MatchesMath",
			policy.Evaluate(FireReady()) == EngagementDecision.Fire,
			"DefaultCombatPolicy Fire");
		Check("Policy_NoReserved",
			policy.Evaluate(memory) != EngagementDecision.Observe &&
			policy.Evaluate(memory) != EngagementDecision.Suppress &&
			policy.Evaluate(memory) != EngagementDecision.Report,
			"Reserved unused");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
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
#endif
