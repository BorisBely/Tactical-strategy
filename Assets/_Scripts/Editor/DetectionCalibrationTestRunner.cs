#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline detection gameplay calibration.
/// Math (no Play): Tools/Tests/Run Detection Calibration Math (no Play).
/// Runtime A–H: Tools/Tests/Run Detection Calibration Runtime (Play).
/// Strict V1.9.4: Tools/Tests/Run Detection Calibration Strict (Play).
/// </summary>
public static class DetectionCalibrationTestRunner
{
	[MenuItem("Tools/Tests/Run Detection Calibration Strict (Play)", false, 139)]
	public static void RunStrictFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = true;
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
		DetectionHarnessPlayMode.RunVisionEnvelope = false;
		if (EditorApplication.isPlaying)
		{
			DetectionCalibrationRuntimeStrictSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<DetectionCalibrationRuntimeStrictSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<DetectionCalibrationRuntimeStrictSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[DetectionCalibrationTestRunner] DetectionCalibrationRuntimeStrictSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[DetectionCalibrationTestRunner] V1.9.4 STRICT: entering Play (G1–G8 skipped). Expect DetectionCalibrationRuntimeStrict_LAST.txt");
	}

	[MenuItem("Tools/Tests/Run Detection Calibration Runtime (Play)", false, 140)]
	public static void RunRuntimeFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunCalibrationRuntime = true;
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
		DetectionHarnessPlayMode.RunVisionEnvelope = false;
		if (EditorApplication.isPlaying)
		{
			DetectionCalibrationRuntimeSmoke smoke = UnityEngine.Object.FindAnyObjectByType<DetectionCalibrationRuntimeSmoke>();
			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[DetectionCalibrationTestRunner] DetectionCalibrationRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[DetectionCalibrationTestRunner] V1.9.1 RUNTIME: entering Play (G1–G8 skipped). Expect [DetectionCalibrationRuntimeSmoke] and DetectionCalibrationRuntime_LAST.txt");
	}

	[MenuItem("Tools/Tests/Run Detection Calibration Math (no Play)", false, 141)]
	public static void RunFromMenu()
	{
		DetectionCalibrationScenarios.ReportResult result = DetectionCalibrationScenarios.BuildReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionCalibration_LAST.txt");
		File.WriteAllText(latest, result.Body, Encoding.UTF8);
		int resultAt = result.Body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? result.Body.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionCalibrationTestRunner] MATH only (not runtime). wrote {latest} {resultLine}\n{result.Body}");
	}

	[MenuItem("Tools/Tests/Run Detection Calibration", false, 142)]
	public static void RunFromMenuLegacyAlias()
	{
		Debug.LogWarning(
			"[DetectionCalibrationTestRunner] 'Run Detection Calibration' is MATH (no Play), already PASS 10/0. " +
			"For V1.9 use Tools/Tests/Run Detection Calibration Runtime (Play).");
		RunFromMenu();
	}
}
#endif
