#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #9 Sound / Reports in AI Play. Standalone — does not require DetectionTestController.
/// Writes Assets/_Docs/Logs/Tests/SoundInAi_LAST.txt
/// </summary>
public static class SoundInAiTestRunner
{
	[MenuItem("Tools/Tests/Run Sound In AI (Play)", false, 34)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunSoundInAi = true;

		if (EditorApplication.isPlaying)
		{
			SoundInAiRuntimeSmoke smoke =
				Object.FindAnyObjectByType<SoundInAiRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("SoundInAiRuntimeSmoke");
				smoke = go.AddComponent<SoundInAiRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[SoundInAiTestRunner] Entering Play. Expect SoundInAi_LAST.txt");
	}
}
#endif
