#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.2B.2 Opening bake Play. Writes Assets/_Docs/Logs/Tests/CoverOpening_LAST.txt
/// </summary>
public static class CoverOpeningTestRunner
{
	[MenuItem("Tools/Tests/Run Cover Opening Bake (Play)", false, 4)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverOpeningBake = true;

		if (EditorApplication.isPlaying)
		{
			CoverOpeningRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverOpeningRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverOpeningRuntimeSmoke");
				smoke = go.AddComponent<CoverOpeningRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverOpeningTestRunner] Entering Play. Expect CoverOpening_LAST.txt");
	}
}
#endif
