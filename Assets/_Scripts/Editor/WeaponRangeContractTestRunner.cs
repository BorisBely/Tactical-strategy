#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision Stage 9: baked weapon/ammo EffectiveRange contract.
/// Writes Assets/_Docs/Logs/Tests/WeaponRangeContract_LAST.txt
/// </summary>
public static class WeaponRangeContractTestRunner
{
	[MenuItem("Tools/Tests/Archive/Vision/Run Weapon Range Contract (Play)", false, 169)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunWeaponRangeContract = true;

		if (EditorApplication.isPlaying)
		{
			WeaponRangeContractRuntimeSmoke smoke =
				Object.FindAnyObjectByType<WeaponRangeContractRuntimeSmoke>();
			if (smoke == null)
			{
				DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
				if (harness != null)
					smoke = harness.gameObject.AddComponent<WeaponRangeContractRuntimeSmoke>();
			}

			if (smoke != null)
				smoke.RunFromEditor();
			else
				Debug.LogError(
					"[WeaponRangeContractTestRunner] WeaponRangeContractRuntimeSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[WeaponRangeContractTestRunner] Entering Play. Expect WeaponRangeContract_LAST.txt");
	}
}
#endif
