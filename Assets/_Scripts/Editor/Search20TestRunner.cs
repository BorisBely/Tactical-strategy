#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #10 Search 2.0 Play. Standalone SearchTestArena smoke.
/// Writes Assets/_Docs/Logs/Tests/Search20_LAST.txt
/// </summary>
public static class Search20TestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Search 2.0 (Play)", false, 183)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunSearch20 = true;

		if (EditorApplication.isPlaying)
		{
			Search20RuntimeSmoke smoke = Object.FindAnyObjectByType<Search20RuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("Search20RuntimeSmoke");
				smoke = go.AddComponent<Search20RuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[Search20TestRunner] Entering Play. Expect Search20_LAST.txt");
	}
}
#endif
