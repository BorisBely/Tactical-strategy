#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.8 Final Dynamic Cover Integration Play (FROZEN). No new mechanics. Not Move. Not Fire. Not #14.
/// Writes Assets/_Docs/Logs/Tests/CoverIntegration_LAST.txt
/// </summary>
public static class CoverIntegrationTestRunner
{
	[MenuItem("Tools/Tests/Run Dynamic Cover (Play)", false, 3)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverIntegration = true;

		if (EditorApplication.isPlaying)
		{
			CoverIntegrationRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverIntegrationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverIntegrationRuntimeSmoke");
				smoke = go.AddComponent<CoverIntegrationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverIntegrationTestRunner] Entering Play. Expect CoverIntegration_LAST.txt");
	}
}
#endif
