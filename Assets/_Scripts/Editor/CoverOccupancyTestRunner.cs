#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.6 Occupancy / Reservation Play (archive after 13.7).
/// Writes Assets/_Docs/Logs/Tests/CoverOccupancy_LAST.txt
/// </summary>
public static class CoverOccupancyTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Occupancy (Play)", false, 193)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverOccupancy = true;

		if (EditorApplication.isPlaying)
		{
			CoverOccupancyRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverOccupancyRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverOccupancyRuntimeSmoke");
				smoke = go.AddComponent<CoverOccupancyRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverOccupancyTestRunner] Entering Play. Expect CoverOccupancy_LAST.txt");
	}
}
#endif
