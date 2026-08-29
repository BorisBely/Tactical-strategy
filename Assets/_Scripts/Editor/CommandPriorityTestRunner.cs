#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #11 Command Priority Play. Standalone smoke.
/// Writes Assets/_Docs/Logs/Tests/CommandPriority_LAST.txt
/// </summary>
public static class CommandPriorityTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Command Priority (Play)", false, 185)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCommandPriority = true;

		if (EditorApplication.isPlaying)
		{
			CommandPriorityRuntimeSmoke smoke = Object.FindAnyObjectByType<CommandPriorityRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CommandPriorityRuntimeSmoke");
				smoke = go.AddComponent<CommandPriorityRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CommandPriorityTestRunner] Entering Play. Expect CommandPriority_LAST.txt");
	}
}
#endif
