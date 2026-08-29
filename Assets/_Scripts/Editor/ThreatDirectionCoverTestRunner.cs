#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C.1 Cover orientation & facing Play. Writes Assets/_Docs/Logs/Tests/ThreatDirectionCover_LAST.txt
/// Does not replace Tools/Tests/Run Threat Direction (Play).
/// </summary>
public static class ThreatDirectionCoverTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction Cover (Play)", false, 9)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirectionCover = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionCoverRuntimeSmoke smoke = Object.FindAnyObjectByType<ThreatDirectionCoverRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionCoverRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionCoverRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionCoverTestRunner] Entering Play. Expect ThreatDirectionCover_LAST.txt");
	}
}
#endif
