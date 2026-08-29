#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #12 Target Calibration Play. Standalone TargetCalibrationArena smoke.
/// Writes Assets/_Docs/Logs/Tests/TargetCalibration_LAST.txt
/// </summary>
public static class TargetCalibrationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Target Calibration (Play)", false, 187)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunTargetCalibration = true;

		if (EditorApplication.isPlaying)
		{
			TargetCalibrationRuntimeSmoke smoke = Object.FindAnyObjectByType<TargetCalibrationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("TargetCalibrationRuntimeSmoke");
				smoke = go.AddComponent<TargetCalibrationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[TargetCalibrationTestRunner] Entering Play. Expect TargetCalibration_LAST.txt");
	}
}
#endif
