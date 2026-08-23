#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 4: measure detection time vs distance/FOV/exposure/movement/sweep. Does not retune Q.
/// Writes Assets/_Docs/Logs/Tests/VisionDetectionCalibration_LAST.txt
/// </summary>
public static class VisionDetectionCalibrationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Calibration/Run Vision Detection Calibration (Play)", false, 103)]
	public static void RunFromMenu()
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
		DetectionHarnessPlayMode.RunGameCommandLayer = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunVisionEnvelope = false;
		DetectionHarnessPlayMode.RunVisionDetectionCalibration = true;
		DetectionHarnessPlayMode.RunVisionExposureFovContract = false;
		DetectionHarnessPlayMode.RunVisionDetectionBalance = false;
		DetectionHarnessPlayMode.RunVisionContactLifecycle = false;
		DetectionHarnessPlayMode.RunVisionOpticRangeContract = false;
		DetectionHarnessPlayMode.RunWeaponRangeContract = false;
		DetectionHarnessPlayMode.RunAccuracyAimCurveContract = false;
		DetectionHarnessPlayMode.RunFireDisciplineContract = false;
		DetectionHarnessPlayMode.RunProjectileVisionContract = false;
		DetectionHarnessPlayMode.RunVehicleVisionContract = false;
		DetectionHarnessPlayMode.RunCombatRetainContract = false;
		DetectionHarnessPlayMode.RunAttentionFacingContract = false;
		DetectionHarnessPlayMode.RunSoundPerceptionContract = false;
		DetectionHarnessPlayMode.RunAllyReportContract = false;
		DetectionHarnessPlayMode.RunFinalPerceptionContract = false;

		if (EditorApplication.isPlaying)
		{
			VisionDetectionCalibrationRuntimeSmoke smoke =
				Object.FindAnyObjectByType<VisionDetectionCalibrationRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<VisionDetectionCalibrationRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[VisionDetectionCalibrationTestRunner] VisionDetectionCalibrationRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[VisionDetectionCalibrationTestRunner] Entering Play. Expect VisionDetectionCalibration_LAST.txt");
	}
}
#endif
