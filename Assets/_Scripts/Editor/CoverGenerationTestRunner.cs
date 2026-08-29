#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.1 Candidate Generation Play. Archive after 13.2 opened.
/// Writes Assets/_Docs/Logs/Tests/CoverGeneration_LAST.txt
/// </summary>
public static class CoverGenerationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Generation (Play)", false, 188)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverGeneration = true;

		if (EditorApplication.isPlaying)
		{
			CoverGenerationRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverGenerationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverGenerationRuntimeSmoke");
				smoke = go.AddComponent<CoverGenerationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverGenerationTestRunner] Entering Play. Expect CoverGeneration_LAST.txt");
	}
}
#endif
