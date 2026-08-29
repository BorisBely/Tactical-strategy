#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #7–#11 Play regression in one Play session.
/// Writes FrozenLayersPlay_LAST.txt plus each layer LAST.txt.
/// </summary>
public static class FrozenLayersPlayRunner
{
	[MenuItem("Tools/Tests/Run Regression (Play)", false, 11)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunFrozenLayersPlay = true;

		if (EditorApplication.isPlaying)
		{
			FrozenLayersPlayCoordinator coordinator =
				Object.FindAnyObjectByType<FrozenLayersPlayCoordinator>();
			if (coordinator == null)
			{
				var go = new GameObject("FrozenLayersPlayCoordinator");
				coordinator = go.AddComponent<FrozenLayersPlayCoordinator>();
			}

			coordinator.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[FrozenLayersPlayRunner] Entering Play. Expect FrozenLayersPlay_LAST.txt");
	}
}
#endif
