#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #7 ImmediateThreat live Play. Standalone — does not require DetectionTestController.
/// Writes Assets/_Docs/Logs/Tests/ImmediateThreatLive_LAST.txt
/// </summary>
public static class ImmediateThreatLiveTestRunner
{
	[MenuItem("Tools/Tests/Run Immediate Threat Live (Play)", false, 32)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunImmediateThreatLive = true;

		if (EditorApplication.isPlaying)
		{
			ImmediateThreatLiveRuntimeSmoke smoke =
				Object.FindAnyObjectByType<ImmediateThreatLiveRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ImmediateThreatLiveRuntimeSmoke");
				smoke = go.AddComponent<ImmediateThreatLiveRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ImmediateThreatLiveTestRunner] Entering Play. Expect ImmediateThreatLive_LAST.txt");
	}
}
#endif
