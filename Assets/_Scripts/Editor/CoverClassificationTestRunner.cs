#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.2 Cover Classification Play (archive after 13.3).
/// Writes Assets/_Docs/Logs/Tests/CoverClassification_LAST.txt
/// </summary>
public static class CoverClassificationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Classification (Play)", false, 189)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverClassification = true;

		if (EditorApplication.isPlaying)
		{
			CoverClassificationRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverClassificationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverClassificationRuntimeSmoke");
				smoke = go.AddComponent<CoverClassificationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverClassificationTestRunner] Entering Play. Expect CoverClassification_LAST.txt");
	}
}
#endif
