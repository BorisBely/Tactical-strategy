#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Block C CLOSED / VERIFIED. Identity World Evidence FROZEN (Play 49/0, C13).
/// Math (no Play): Tools/Tests/Run Identity Calibration
/// Runtime C3–C14: Tools/Tests/Run Identity Calibration (Play)
/// Report: Assets/_Docs/Logs/Tests/IdentityCalibration_LAST.txt
/// </summary>
public static class IdentityCalibrationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Calibration/Run Identity Calibration", false, 109)]
	public static void RunMathFromMenu()
	{
		IdentityCalibrationScenarios.ReportResult result = IdentityCalibrationScenarios.BuildReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "IdentityCalibration_LAST.txt");
		File.WriteAllText(latest, result.Body, Encoding.UTF8);
		int resultAt = result.Body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? result.Body.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[IdentityCalibrationTestRunner] MATH (no Play). wrote {latest} {resultLine}\n{result.Body}");
	}

	[MenuItem("Tools/Tests/Archive/Calibration/Run Identity Calibration (Play)", false, 110)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunIdentityCalibration = true;
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
		DetectionHarnessPlayMode.RunVisionEnvelope = false;
		DetectionHarnessPlayMode.RunVisionDetectionCalibration = false;
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
			IdentityCalibrationRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<IdentityCalibrationRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<IdentityCalibrationRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[IdentityCalibrationTestRunner] IdentityCalibrationRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[IdentityCalibrationTestRunner] Block C runtime: entering Play (G1–G8 and Detection/Memory calibration skipped). " +
			"Expect IdentityCalibrationRuntime_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
