#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C Threat Direction Play. Writes Assets/_Docs/Logs/Tests/ThreatDirection_LAST.txt
/// </summary>
public static class ThreatDirectionTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction (Play)", false, 7)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirection = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionRuntimeSmoke smoke = Object.FindAnyObjectByType<ThreatDirectionRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionTestRunner] Entering Play. Expect ThreatDirection_LAST.txt");
	}
}
#endif
