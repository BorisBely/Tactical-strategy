#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 12: projectile assignment vs physical flight.
/// Writes Assets/_Docs/Logs/Tests/ProjectileVisionContract_LAST.txt
/// </summary>
public static class ProjectileVisionContractTestRunner
{
	[MenuItem("Tools/Tests/Run Projectile Vision Contract (Play)", false, 24)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunProjectileVisionContract = true;

		if (EditorApplication.isPlaying)
		{
			ProjectileVisionContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<ProjectileVisionContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<ProjectileVisionContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[ProjectileVisionContractTestRunner] ProjectileVisionContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[ProjectileVisionContractTestRunner] Entering Play. Expect ProjectileVisionContract_LAST.txt");
	}
}
#endif
