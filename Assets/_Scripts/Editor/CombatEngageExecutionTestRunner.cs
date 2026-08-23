#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 2 FROZEN CombatIntent Play. AI Engage/Hold → existing combat contour.
/// Tools/Tests/Run Combat Engage Execution (Play)
/// </summary>
public static class CombatEngageExecutionTestRunner
{
	[MenuItem("Tools/Tests/Archive/Tactics/Run Combat Engage Execution (Play)", false, 123)]
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
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunCombatEngageExecution = true;
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			CombatEngageExecutionRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<CombatEngageExecutionRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<CombatEngageExecutionRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[CombatEngageExecutionTestRunner] CombatEngageExecutionRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[CombatEngageExecutionTestRunner] Stage 2: entering Play (calibration and G1–G8 skipped). " +
			"Expect CombatEngageExecution_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
