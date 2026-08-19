#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G5 selection math smoke (no Play Mode). Full runtime suite writes DetectionG5_LAST.txt on Play.
/// </summary>
public static class DetectionG5TestRunner
{
	[MenuItem("Tools/Tests/Run DetectionG5 Selection Smoke (no Play)")]
	public static void RunSelectionSmokeFromMenu()
	{
		string report = BuildSelectionSmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG5_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		int resultAt = report.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? report.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionG5TestRunner] wrote {latest} {resultLine}\n{report}");
	}

	public static string BuildSelectionSmokeReport()
	{
		var sb = new StringBuilder(2048);
		int pass = 0;
		int fail = 0;
		ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();

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

		sb.AppendLine($"DetectionG5 SelectionMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		var unknown = new PerceivedContact
		{
			Target = null,
			Identity = PerceivedIdentity.Unknown,
			LastSeenConfidence = 1f,
			ObservationState = ObservationState.Observed
		};
		unknown.Target = new GameObject("G5MenuUnknown").transform;
		Check("Math_UnknownEligible",
			ContactSelectionEligibility.Evaluate(unknown, true, policy, out _),
			"Unknown selectable");

		var friendly = new PerceivedContact
		{
			Target = unknown.Target,
			Identity = PerceivedIdentity.Friendly,
			Relationship = PerceivedRelationship.Friendly,
			LastSeenConfidence = 1f,
			ObservationState = ObservationState.Observed
		};
		Check("Math_FriendlyRejected",
			!ContactSelectionEligibility.Evaluate(friendly, true, policy, out _),
			"Friendly out");

		var forgotten = new PerceivedContact
		{
			Target = unknown.Target,
			LastSeenConfidence = 0f,
			ObservationState = ObservationState.Lost
		};
		Check("Math_ForgottenRejected",
			!ContactSelectionEligibility.Evaluate(forgotten, true, policy, out _),
			"Forgotten out");

		var observed = new PerceivedContact
		{
			ObservationState = ObservationState.Observed,
			LastSeenConfidence = 1f,
			LastKnownPosition = new Vector3(20f, 0f, 0f)
		};
		var stale = new PerceivedContact
		{
			ObservationState = ObservationState.Lost,
			LastSeenConfidence = 0.2f,
			LastKnownPosition = new Vector3(2f, 0f, 0f),
			Threat = ThreatLevel.High
		};
		Check("Math_ObservedBeatsStale",
			TargetSelectionMath.Score(observed, Vector3.zero, policy) >
			TargetSelectionMath.Score(stale, Vector3.zero, policy),
			"Observed > stale");
		Check("Math_AimFalseWhenLost",
			!TargetSelectionMath.TryGetObservedAimPoint(stale, out _),
			"LastKnown is not aim");

		if (unknown.Target != null)
			UnityEngine.Object.DestroyImmediate(unknown.Target.gameObject);

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif
