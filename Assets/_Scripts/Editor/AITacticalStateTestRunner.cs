#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// AI-1 FROZEN. State from orders; Search reads LastKnown; Engage is not a state.
/// Tools/Tests/Run AI Tactical State (Play)
/// </summary>
public static class AITacticalStateTestRunner
{
	[MenuItem("Tools/Tests/Run AI Tactical State (Play)", false, 144)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunIdentityCalibration = false;
		DetectionHarnessPlayMode.RunAIPerceptionHandoff = false;
		DetectionHarnessPlayMode.RunUseOfForcePolicy = false;
		DetectionHarnessPlayMode.RunCombatEngageExecution = false;
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunAITacticalState = true;

		if (EditorApplication.isPlaying)
		{
			AITacticalStateRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<AITacticalStateRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<AITacticalStateRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[AITacticalStateTestRunner] AITacticalStateRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[AITacticalStateTestRunner] AI-1: entering Play (calibration and G1–G8 skipped). " +
			"Expect AITacticalState_LAST.txt (skeleton + AI-1.9). m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
