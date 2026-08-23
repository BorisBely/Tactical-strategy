#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 8: real optic ScopeVisionRange contract.
/// Writes Assets/_Docs/Logs/Tests/OpticRangeContract_LAST.txt
/// </summary>
public static class VisionOpticRangeContractTestRunner
{
	[MenuItem("Tools/Tests/Run Vision Optic Range Contract (Play)", false, 28)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunVisionOpticRangeContract = true;

		if (EditorApplication.isPlaying)
		{
			VisionOpticRangeContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<VisionOpticRangeContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<VisionOpticRangeContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[VisionOpticRangeContractTestRunner] VisionOpticRangeContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[VisionOpticRangeContractTestRunner] Entering Play. Expect OpticRangeContract_LAST.txt");
	}
}
#endif
