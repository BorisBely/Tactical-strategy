#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14C.4 Reorientation Play. Writes Assets/_Docs/Logs/Tests/ThreatDirectionReorientation_LAST.txt
/// </summary>
public static class ThreatDirectionReorientationTestRunner
{
	[MenuItem("Tools/Tests/Run Threat Direction Reorientation (Play)", false, 15)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunThreatDirectionReorientation = true;

		if (EditorApplication.isPlaying)
		{
			ThreatDirectionReorientationRuntimeSmoke smoke =
				Object.FindAnyObjectByType<ThreatDirectionReorientationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("ThreatDirectionReorientationRuntimeSmoke");
				smoke = go.AddComponent<ThreatDirectionReorientationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ThreatDirectionReorientationTestRunner] Entering Play. Expect ThreatDirectionReorientation_LAST.txt");
	}
}
#endif
