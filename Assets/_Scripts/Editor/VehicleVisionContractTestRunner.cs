#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 13: passenger / turret VisionSource contract.
/// Writes Assets/_Docs/Logs/Tests/VehicleVisionContract_LAST.txt
/// </summary>
public static class VehicleVisionContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Vehicle Vision Contract (Play)", false, 165)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunVehicleVisionContract = true;

		if (EditorApplication.isPlaying)
		{
			VehicleVisionContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<VehicleVisionContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<VehicleVisionContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[VehicleVisionContractTestRunner] VehicleVisionContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[VehicleVisionContractTestRunner] Entering Play. Expect VehicleVisionContract_LAST.txt");
	}
}
#endif
