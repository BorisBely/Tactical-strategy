#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14B.1–14B.7 Readiness Play. Writes Assets/_Docs/Logs/Tests/Readiness_LAST.txt
/// </summary>
public static class ReadinessTestRunner
{
	[MenuItem("Tools/Tests/Run Readiness (Play)", false, 5)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunReadiness = true;

		if (EditorApplication.isPlaying)
		{
			ReadinessRuntimeSmoke smoke = Object.FindAnyObjectByType<ReadinessRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ReadinessRuntimeSmoke");
				smoke = go.AddComponent<ReadinessRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ReadinessTestRunner] Entering Play. Expect Readiness_LAST.txt");
	}
}
#endif
