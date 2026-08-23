#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// AI-1A Use of Force Play. ForcePermission + G6 handoff. No Weapon.
/// Tools/Tests/Run AI Use of Force (Play)
/// </summary>
public static class UseOfForcePolicyTestRunner
{
	[MenuItem("Tools/Tests/Archive/Tactics/Run AI Use of Force (Play)", false, 122)]
	public static void RunPlayFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunIdentityCalibration = false;
		DetectionHarnessPlayMode.RunAIPerceptionHandoff = false;
		DetectionHarnessPlayMode.RunAITacticalState = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunUseOfForcePolicy = true;
		DetectionHarnessPlayMode.RunCombatEngageExecution = false;
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;

		if (EditorApplication.isPlaying)
		{
			UseOfForcePolicyRuntimeSmoke smoke =
				UnityEngine.Object.FindAnyObjectByType<UseOfForcePolicyRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness =
					UnityEngine.Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<UseOfForcePolicyRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[UseOfForcePolicyTestRunner] UseOfForcePolicyRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log(
			"[UseOfForcePolicyTestRunner] AI-1A: entering Play (calibration and G1–G8 skipped). " +
			"Expect UseOfForcePolicy_LAST.txt. m_RunOnStart=false — ordinary Play does not run this suite.");
	}
}
#endif
