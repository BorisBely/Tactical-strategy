#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C.3 Tactical positioning Play. Writes Assets/_Docs/Logs/Tests/ThreatDirectionPosition_LAST.txt
/// </summary>
public static class ThreatDirectionPositionTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction Position (Play)", false, 13)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirectionPosition = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionPositionRuntimeSmoke smoke = Object.FindAnyObjectByType<ThreatDirectionPositionRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionPositionRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionPositionRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionPositionTestRunner] Entering Play. Expect ThreatDirectionPosition_LAST.txt");
	}
}
#endif
