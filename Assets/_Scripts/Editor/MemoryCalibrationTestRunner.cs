#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Block B memory calibration.
/// Math (no Play): Tools/Tests/Run Memory Calibration
/// Runtime M1–M10: Tools/Tests/Run Memory Calibration (Play)
/// Report: Assets/_Docs/Logs/Tests/MemoryCalibration_LAST.txt
/// </summary>
public static class MemoryCalibrationTestRunner
{
	[MenuItem("Tools/Tests/Run Memory Calibration", false, 136)]
	public static void RunMathFromMenu()
	{
		MemoryCalibrationScenarios.ReportResult result = MemoryCalibrationScenarios.BuildReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "MemoryCalibration_LAST.txt");
		File.WriteAllText(latest, result.Body, Encoding.UTF8);
		int resultAt = result.Body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? result.Body.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[MemoryCalibrationTestRunner] MATH (no Play). wrote {latest} {resultLine}\n{result.Body}");
	}

	[MenuItem("Tools/Tests/Run Memory Calibration (Play)", false, 137)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunMemoryCalibration = true;
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

		if (EditorApplication.isPlaying)
		{
			MemoryCalibrationRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<MemoryCalibrationRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<MemoryCalibrationRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[MemoryCalibrationTestRunner] MemoryCalibrationRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[MemoryCalibrationTestRunner] Block B runtime: entering Play (G1–G8 and Detection calibration skipped). " +
			"Expect MemoryCalibrationRuntime_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
