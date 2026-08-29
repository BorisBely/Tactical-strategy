#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// #13.3 Individual Evaluation Play (archive after 13.4).
/// Writes Assets/_Docs/Logs/Tests/CoverEvaluation_LAST.txt
/// </summary>
public static class CoverEvaluationTestRunner
{
	[MenuItem("Tools/Tests/Archive/Regression/Run Cover Evaluation (Play)", false, 190)]
	public static void RunFromMenu()
	{
		DetectionHarnessPlayMode.ResetFlags();
		DetectionHarnessPlayMode.SkipClosedGStages = true;
		DetectionHarnessPlayMode.RunCoverEvaluation = true;

		if (EditorApplication.isPlaying)
		{
			CoverEvaluationRuntimeSmoke smoke = Object.FindAnyObjectByType<CoverEvaluationRuntimeSmoke>();
			if (smoke == null)
			{
				var go = new GameObject("CoverEvaluationRuntimeSmoke");
				smoke = go.AddComponent<CoverEvaluationRuntimeSmoke>();
			}

			smoke.RunFromEditor();
			return;
		}

		EditorApplication.isPlaying = true;
		Debug.Log("[CoverEvaluationTestRunner] Entering Play. Expect CoverEvaluation_LAST.txt");
	}
}
#endif
