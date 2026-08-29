#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 15: Attention / Facing rate contract.
/// Writes Assets/_Docs/Logs/Tests/AttentionFacingContract_LAST.txt
/// </summary>
public static class AttentionFacingContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Attention Facing Contract (Play)", false, 161)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunAttentionFacingContract = true;

		if (EditorApplication.isPlaying)
		{
			AttentionFacingContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<AttentionFacingContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<AttentionFacingContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[AttentionFacingContractTestRunner] AttentionFacingContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[AttentionFacingContractTestRunner] Entering Play. Expect AttentionFacingContract_LAST.txt");
	}
}
#endif
