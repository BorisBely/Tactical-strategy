#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 14: reload/misfire retain vs ResolvedMaxRange.
/// Writes Assets/_Docs/Logs/Tests/CombatRetainContract_LAST.txt
/// </summary>
public static class CombatRetainContractTestRunner
{
	[MenuItem("Tools/Tests/Run Combat Retain Contract (Play)", false, 22)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCombatRetainContract = true;

		if (EditorApplication.isPlaying)
		{
			CombatRetainContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<CombatRetainContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<CombatRetainContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[CombatRetainContractTestRunner] CombatRetainContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CombatRetainContractTestRunner] Entering Play. Expect CombatRetainContract_LAST.txt");
	}
}
#endif
