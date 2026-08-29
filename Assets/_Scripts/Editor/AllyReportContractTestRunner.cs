#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 17: Ally Report / Shared Perception contract.
/// Writes Assets/_Docs/Logs/Tests/AllyReportContract_LAST.txt
/// </summary>
public static class AllyReportContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Ally Report Contract (Play)", false, 162)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunAllyReportContract = true;

		if (EditorApplication.isPlaying)
		{
			AllyReportContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<AllyReportContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<AllyReportContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[AllyReportContractTestRunner] AllyReportContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[AllyReportContractTestRunner] Entering Play. Expect AllyReportContract_LAST.txt");
	}
}
#endif
