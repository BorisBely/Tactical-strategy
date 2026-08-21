#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// AI-0 perception handoff Play. Does not retune Vision.
/// Tools/Tests/Run AI Perception Handoff (Play)
/// </summary>
public static class AIPerceptionHandoffTestRunner
{
	[MenuItem("Tools/Tests/Run AI Perception Handoff (Play)", false, 143)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunIdentityCalibration = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunAIPerceptionHandoff = true;
		DetectionHarnessPlayMode.RunAITacticalState = false;
		DetectionHarnessPlayMode.RunUseOfForcePolicy = false;
		DetectionHarnessPlayMode.RunCombatEngageExecution = false;
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			AIPerceptionHandoffSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<AIPerceptionHandoffSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<AIPerceptionHandoffSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[AIPerceptionHandoffTestRunner] AIPerceptionHandoffSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[AIPerceptionHandoffTestRunner] AI-0: entering Play (G1–G8 and calibration skipped). " +
			"Expect AIPerceptionHandoff_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
