#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #8 Combat Event World Play. Standalone — does not require DetectionTestController.
/// Writes Assets/_Docs/Logs/Tests/CombatEvent_LAST.txt
/// </summary>
public static class CombatEventWorldTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Combat Event World (Play)", false, 181)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCombatEventWorld = true;

		if (EditorApplication.isPlaying)
		{
			CombatEventWorldRuntimeSmoke smoke =
				Object.FindAnyObjectByType<CombatEventWorldRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CombatEventWorldRuntimeSmoke");
				smoke = go.AddComponent<CombatEventWorldRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CombatEventWorldTestRunner] Entering Play. Expect CombatEvent_LAST.txt");
	}
}
#endif
