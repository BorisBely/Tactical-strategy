#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 7: contact lifecycle + test optic envelopes.
/// Writes Assets/_Docs/Logs/Tests/VisionContactLifecycle_LAST.txt
/// </summary>
public static class VisionContactLifecycleTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Vision Contact Lifecycle (Play)", false, 171)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunVisionContactLifecycle = true;

		if (EditorApplication.isPlaying)
		{
			VisionContactLifecycleRuntimeSmoke smoke =
				Object.FindAnyObjectByType<VisionContactLifecycleRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<VisionContactLifecycleRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[VisionContactLifecycleTestRunner] VisionContactLifecycleRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[VisionContactLifecycleTestRunner] Entering Play. Expect VisionContactLifecycle_LAST.txt");
	}
}
#endif
