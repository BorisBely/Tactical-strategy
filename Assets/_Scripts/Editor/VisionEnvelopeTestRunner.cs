#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play smoke for the unified eye + optic vision envelope.
/// Writes Assets/_Docs/Logs/Tests/VisionEnvelope_LAST.txt
/// </summary>
public static class VisionEnvelopeTestRunner
{
	[MenuItem("Tools/Tests/Run Vision Envelope (Play)", false, 138)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCalibrationRuntime = false;
		DetectionHarnessPlayMode.RunCalibrationStrict = false;
		DetectionHarnessPlayMode.RunMemoryCalibration = false;
		DetectionHarnessPlayMode.RunIdentityCalibration = false;
		DetectionHarnessPlayMode.RunAIPerceptionHandoff = false;
		DetectionHarnessPlayMode.RunAITacticalState = false;
		DetectionHarnessPlayMode.RunUseOfForcePolicy = false;
		DetectionHarnessPlayMode.RunCombatEngageExecution = false;
		DetectionHarnessPlayMode.RunSearchExecution = false;
		DetectionHarnessPlayMode.RunTacticalNavigationExecution = false;
		DetectionHarnessPlayMode.RunTacticalCommandContract = false;
		DetectionHarnessPlayMode.RunGameCommandSource = false;
		DetectionHarnessPlayMode.RunGameCommandInput = false;
		DetectionHarnessPlayMode.RunGameCommandLayer = false;
		DetectionHarnessPlayMode.RunGStage = string.Empty;
		DetectionHarnessPlayMode.RunVisionEnvelope = true;

		if (EditorApplication.isPlaying)
		{
			VisionEnvelopeRuntimeSmoke smoke =
				Object.FindAnyObjectByType<VisionEnvelopeRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<VisionEnvelopeRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError("[VisionEnvelopeTestRunner] VisionEnvelopeRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[VisionEnvelopeTestRunner] Entering Play. Expect VisionEnvelope_LAST.txt");
	}
}
#endif
