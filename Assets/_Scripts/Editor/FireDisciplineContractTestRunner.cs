#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 11: Fire Discipline character of fire inside the live combat envelope.
/// Writes Assets/_Docs/Logs/Tests/FireDisciplineContract_LAST.txt
/// </summary>
public static class FireDisciplineContractTestRunner
{
	[MenuItem("Tools/Tests/Run Fire Discipline Contract (Play)", false, 25)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunFireDisciplineContract = true;

		if (EditorApplication.isPlaying)
		{
			FireDisciplineContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<FireDisciplineContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<FireDisciplineContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[FireDisciplineContractTestRunner] FireDisciplineContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[FireDisciplineContractTestRunner] Entering Play. Expect FireDisciplineContract_LAST.txt");
	}
}
#endif
