#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 18: Final Perception Contract.
/// Writes Assets/_Docs/Logs/Tests/FinalPerceptionContract_LAST.txt
/// </summary>
public static class FinalPerceptionContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Final Perception Contract (Play)", false, 160)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunFinalPerceptionContract = true;

		if (EditorApplication.isPlaying)
		{
			FinalPerceptionContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<FinalPerceptionContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<FinalPerceptionContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[FinalPerceptionContractTestRunner] FinalPerceptionContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[FinalPerceptionContractTestRunner] Entering Play. Expect FinalPerceptionContract_LAST.txt");
	}
}
#endif
