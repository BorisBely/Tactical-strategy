#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.7 Lean / Peek Play (archived; #13 FROZEN).
/// Writes Assets/_Docs/Logs/Tests/CoverPeek_LAST.txt
/// </summary>
public static class CoverPeekTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Peek (Play)", false, 194)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverPeek = true;

		if (EditorApplication.isPlaying)
		{
			CoverPeekRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverPeekRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverPeekRuntimeSmoke");
				smoke = go.AddComponent<CoverPeekRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverPeekTestRunner] Entering Play. Expect CoverPeek_LAST.txt");
	}
}
#endif
