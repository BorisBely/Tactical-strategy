#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 6.2 game command source Play.
/// Tools/Tests/Run Game Command Source (Play)
/// </summary>
public static class GameCommandSourceTestRunner
{
	[MenuItem("Tools/Tests/Run Game Command Source (Play)", false, 148)]
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
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunGameCommandSource = true;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			GameCommandSourceRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<GameCommandSourceRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<GameCommandSourceRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[GameCommandSourceTestRunner] GameCommandSourceRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[GameCommandSourceTestRunner] Stage 6.2: entering Play (calibration and G1–G8 skipped). " +
			"Expect GameCommandSource_LAST.txt. Path is DebugGameCommandSource, not RTS.");
	}
}
#endif
