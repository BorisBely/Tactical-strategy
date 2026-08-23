#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G7 sound/shared math smoke (no Play Mode). Full runtime suite writes DetectionG7_LAST.txt on Play.
/// </summary>
public static class DetectionG7TestRunner
{
	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG7 Sound Shared Smoke (no Play)", false, 135)]
	public static void RunSoundSharedSmokeFromMenu()
	{
		string report = BuildSoundSharedSmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG7_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		int resultAt = report.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? report.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionG7TestRunner] wrote {latest} {resultLine}\n{report}");
	}

	public static string BuildSoundSharedSmokeReport()
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

		sb.AppendLine($"DetectionG7 SoundSharedMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		Check("Math_SoundAtZeroEqualsInitial",
			Mathf.Abs(SoundKnowledgeMath.Evaluate(0f, 1f) - 1f) < 0.0001f,
			"Sound t=0");
		Check("Math_SoundHorizonZero",
			Mathf.Abs(SoundKnowledgeMath.Evaluate(SoundKnowledgeMath.DefaultHorizonSeconds, 1f)) < 0.0001f,
			"Sound horizon → 0");
		Check("Math_SharedHorizonZero",
			Mathf.Abs(SharedKnowledgeMath.Evaluate(SharedKnowledgeMath.DefaultHorizonSeconds, 1f)) < 0.0001f,
			"Shared horizon → 0");

		var dummy = new GameObject("G7MenuDummy");
		try
		{
			var soundOnly = new PerceivedContact
			{
				Target = dummy.transform,
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				SoundConfidence = 0.85f,
				LastKnownPosition = new Vector3(3f, 0f, 0f)
			};
			Check("Math_SoundOnlyEligible",
				ContactSelectionEligibility.Evaluate(soundOnly, true, policy, out _),
				"Sound-only selectable");
			Check("Math_SoundOnlyNoAim",
				!TargetSelectionMath.TryGetObservedAimPoint(soundOnly, out _),
				"Sound is not aim");

			var sharedOnly = new PerceivedContact
			{
				Target = dummy.transform,
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				SharedConfidence = 0.7f,
				LastKnownPosition = new Vector3(4f, 0f, 0f)
			};
			Check("Math_SharedOnlyEligible",
				ContactSelectionEligibility.Evaluate(sharedOnly, true, policy, out _),
				"Shared-only selectable");

			var forgotten = new PerceivedContact
			{
				Target = dummy.transform,
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0f
			};
			Check("Math_ForgottenRejected",
				!ContactSelectionEligibility.Evaluate(forgotten, true, policy, out _),
				"No channel → forgotten");

			var observed = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				SoundConfidence = 0.2f,
				LastKnownPosition = new Vector3(20f, 0f, 0f)
			};
			Check("Math_ObservedBeatsSoundOnly",
				TargetSelectionMath.Score(observed, Vector3.zero, policy) >
				TargetSelectionMath.Score(soundOnly, Vector3.zero, policy),
				"Observed bonus still vision-only");
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(dummy);
		}

		EngagementDecisionContext knowledge = new EngagementDecisionContext
		{
			HasSelectedTarget = true,
			HasContact = true,
			HasKnowledge = true,
			LastSeenConfidence = 0f,
			HasLosConfirmedAim = false,
			IsWorldEngageable = true,
			ObservationState = ObservationState.NotObserved,
			Identity = PerceivedIdentity.Unknown,
			Relationship = PerceivedRelationship.Unknown,
			WeaponCanFireEventually = true,
			AimReadyToFire = true
		};
		Check("Math_KnowledgeTrack",
			EngagementDecisionMath.Evaluate(knowledge) == EngagementDecision.Track,
			"Non-visual knowledge → Track");
		Check("Math_KnowledgeNotFire",
			EngagementDecisionMath.Evaluate(knowledge) != EngagementDecision.Fire,
			"Sound/shared must not Fire");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif
