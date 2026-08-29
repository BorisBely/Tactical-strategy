#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #14 Tactical Movement Play (CLOSED / FROZEN 14.0–14.10). Overlay does not Move. Not #15.
/// Writes Assets/_Docs/Logs/Tests/TacticalMovement_LAST.txt
/// </summary>
public static class TacticalMovementTestRunner
{
	[MenuItem("Tools/Tests/Run Tactical Movement (Play)", false, 2)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunTacticalMovement = true;

		if (EditorApplication.isPlaying)
		{
			TacticalMovementRuntimeSmoke smoke =
				Object.FindAnyObjectByType<TacticalMovementRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("TacticalMovementRuntimeSmoke");
				smoke = go.AddComponent<TacticalMovementRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[TacticalMovementTestRunner] Entering Play. Expect TacticalMovement_LAST.txt");
	}
}
#endif
