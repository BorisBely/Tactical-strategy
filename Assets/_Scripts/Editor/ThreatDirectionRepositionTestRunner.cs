#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C.5 Reposition Decision Play. Writes Assets/_Docs/Logs/Tests/ThreatDirectionReposition_LAST.txt
/// </summary>
public static class ThreatDirectionRepositionTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction Reposition (Play)", false, 17)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirectionReposition = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionRepositionRuntimeSmoke smoke =
				Object.FindAnyObjectByType<ThreatDirectionRepositionRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionRepositionRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionRepositionRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionRepositionTestRunner] Entering Play. Expect ThreatDirectionReposition_LAST.txt");
	}
}
#endif
