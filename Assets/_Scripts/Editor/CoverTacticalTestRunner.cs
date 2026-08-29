#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.5 Tactical Cover Play (archive after 13.6).
/// Writes Assets/_Docs/Logs/Tests/CoverTactical_LAST.txt
/// </summary>
public static class CoverTacticalTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Tactical (Play)", false, 192)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverTactical = true;

		if (EditorApplication.isPlaying)
		{
			CoverTacticalRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverTacticalRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverTacticalRuntimeSmoke");
				smoke = go.AddComponent<CoverTacticalRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverTacticalTestRunner] Entering Play. Expect CoverTactical_LAST.txt");
	}
}
#endif
