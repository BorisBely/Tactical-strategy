#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 6.4 game command layer Play.
/// Tools/Tests/Run Game Command Layer (Play)
/// </summary>
public static class GameCommandLayerTestRunner
{
	[MenuItem("Tools/Tests/Run Game Command Layer (Play)", false, 148)]
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
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunGameCommandLayer = true;

		if (EditorApplication.isPlaying)
		{
			DetectionTestController harness = DetectionHarnessPlayMode.EnsureHarnessActive();
			GameCommandLayerRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<GameCommandLayerRuntimeSmoke>(FindObjectsInactive.Include);
			if (smoke == null && harness != null)
				smoke = harness.gameObject.AddComponent<GameCommandLayerRuntimeSmoke>();

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[GameCommandLayerTestRunner] GameCommandLayerRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[GameCommandLayerTestRunner] Stage 6.4: entering Play (calibration and G1–G8 skipped). " +
			"Expect GameCommandLayer_LAST.txt. Source is GameCommandInput.ConfirmPoint. SHOT is not a criterion.");
	}
}
#endif
