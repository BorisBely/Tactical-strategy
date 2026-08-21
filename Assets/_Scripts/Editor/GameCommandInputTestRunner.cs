#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 6.3 game command input Play.
/// Tools/Tests/Run Game Command Input (Play)
/// </summary>
public static class GameCommandInputTestRunner
{
	[MenuItem("Tools/Tests/Run Game Command Input (Play)", false, 148)]
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
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;
		DetectionHarnessPlayMode.RunGameCommandInput = true;

		if (EditorApplication.isPlaying)
		{
			GameCommandInputRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<GameCommandInputRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<GameCommandInputRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[GameCommandInputTestRunner] GameCommandInputRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[GameCommandInputTestRunner] Stage 6.3: entering Play (calibration and G1–G8 skipped). " +
			"Expect GameCommandInput_LAST.txt. Input is programmatic ConfirmPoint, not overlay TryIssue.");
	}
}
#endif
