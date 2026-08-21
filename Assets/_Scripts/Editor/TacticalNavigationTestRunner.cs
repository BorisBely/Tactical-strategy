#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 4 FROZEN Attack / Retreat / Flee locomotion Play.
/// Tools/Tests/Run Tactical Navigation (Play)
/// </summary>
public static class TacticalNavigationTestRunner
{
	[MenuItem("Tools/Tests/Run Tactical Navigation (Play)", false, 145)]
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
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = true;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			TacticalNavigationRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<TacticalNavigationRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<TacticalNavigationRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[TacticalNavigationTestRunner] TacticalNavigationRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[TacticalNavigationTestRunner] Stage 4: entering Play (calibration and G1–G8 skipped). " +
			"Expect TacticalNavigation_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
