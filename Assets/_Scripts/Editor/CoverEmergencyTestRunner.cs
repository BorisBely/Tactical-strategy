#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.4 Emergency Cover Play (archive after 13.5).
/// Writes Assets/_Docs/Logs/Tests/CoverEmergency_LAST.txt
/// </summary>
public static class CoverEmergencyTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Emergency (Play)", false, 191)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverEmergency = true;

		if (EditorApplication.isPlaying)
		{
			CoverEmergencyRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverEmergencyRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverEmergencyRuntimeSmoke");
				smoke = go.AddComponent<CoverEmergencyRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverEmergencyTestRunner] Entering Play. Expect CoverEmergency_LAST.txt");
	}
}
#endif
