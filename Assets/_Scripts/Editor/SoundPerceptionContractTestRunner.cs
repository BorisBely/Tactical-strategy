#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 16: Sound Perception contract.
/// Writes Assets/_Docs/Logs/Tests/SoundPerceptionContract_LAST.txt
/// </summary>
public static class SoundPerceptionContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Sound Perception Contract (Play)", false, 163)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunSoundPerceptionContract = true;

		if (EditorApplication.isPlaying)
		{
			SoundPerceptionContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<SoundPerceptionContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<SoundPerceptionContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[SoundPerceptionContractTestRunner] SoundPerceptionContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[SoundPerceptionContractTestRunner] Entering Play. Expect SoundPerceptionContract_LAST.txt");
	}
}
#endif
