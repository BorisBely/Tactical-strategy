#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C.2 Confidence / uncertainty Play. Writes Assets/_Docs/Logs/Tests/ThreatDirectionQuality_LAST.txt
/// </summary>
public static class ThreatDirectionQualityTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction Quality (Play)", false, 11)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirectionQuality = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionQualityRuntimeSmoke smoke = Object.FindAnyObjectByType<ThreatDirectionQualityRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionQualityRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionQualityRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionQualityTestRunner] Entering Play. Expect ThreatDirectionQuality_LAST.txt");
	}
}
#endif
