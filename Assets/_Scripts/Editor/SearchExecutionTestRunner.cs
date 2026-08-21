#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 3 FROZEN Search locomotion Play. Walk to snapshotted LastKnown via infantry nav.
/// Tools/Tests/Run Search Execution (Play)
/// </summary>
public static class SearchExecutionTestRunner
{
	[MenuItem("Tools/Tests/Run Search Execution (Play)", false, 148)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunIdentityCalibration = false;
		DetectionHarnessPlayMode.RunAIPerceptionHandoff = false;
		DetectionHarnessPlayMode.RunAITacticalState = false;
		DetectionHarnessPlayMode.RunUseOfForcePolicy = false;
		DetectionHarnessPlayMode.RunCombatEngageExecution = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunSearchExecution = true;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			SearchExecutionRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<SearchExecutionRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<SearchExecutionRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[SearchExecutionTestRunner] SearchExecutionRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[SearchExecutionTestRunner] Stage 3: entering Play (calibration and G1–G8 skipped). " +
			"Expect SearchExecution_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
