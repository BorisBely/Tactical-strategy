#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 10: Accuracy / AimTime keys inside 150/300.
/// Writes Assets/_Docs/Logs/Tests/AccuracyAimCurveContract_LAST.txt
/// </summary>
public static class AccuracyAimCurveContractTestRunner
{
	[MenuItem("Tools/Tests/Run Accuracy Aim Curve Contract (Play)", false, 26)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunAccuracyAimCurveContract = true;

		if (EditorApplication.isPlaying)
		{
			AccuracyAimCurveContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<AccuracyAimCurveContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<AccuracyAimCurveContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[AccuracyAimCurveContractTestRunner] AccuracyAimCurveContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[AccuracyAimCurveContractTestRunner] Entering Play. Expect AccuracyAimCurveContract_LAST.txt");
	}
}
#endif
