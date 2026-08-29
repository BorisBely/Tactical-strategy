#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// V1.9.5 Block A G regression Play menus. One stage per Play.
/// Does not retune Q / defaults. Writes DetectionG*_LAST.txt from AutoSmoke.
/// </summary>
public static class DetectionGPlayTestRunner
{
	[MenuItem("Tools/Tests/Archive/G Stages/Run Detection G1–G8 (Play)", false, 110)]
	public static void RunAllG() => EnterPlay(DetectionHarnessPlayMode.AllGStages);

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG1 (Play)", false, 111)]
	public static void RunG1() => EnterPlay("G1");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG2 (Play)", false, 112)]
	public static void RunG2() => EnterPlay("G2");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG3 (Play)", false, 113)]
	public static void RunG3() => EnterPlay("G3");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG4 (Play)", false, 114)]
	public static void RunG4() => EnterPlay("G4");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG5 (Play)", false, 115)]
	public static void RunG5() => EnterPlay("G5");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG6 (Play)", false, 116)]
	public static void RunG6() => EnterPlay("G6");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG7 (Play)", false, 117)]
	public static void RunG7() => EnterPlay("G7");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG8 (Play)", false, 118)]
	public static void RunG8() => EnterPlay("G8");

	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG8 Stress (Play)", false, 119)]
	public static void RunG8Stress() => EnterPlay("G8Stress");

	private static void EnterPlay(string _stage)
	{
		DetectionHarnessPlayMode.SkipClosedGStages = false;
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
		DetectionHarnessPlayMode.RunVisionEnvelope = false;
		DetectionHarnessPlayMode.RunVisionDetectionCalibration = false;
		DetectionHarnessPlayMode.RunVisionExposureFovContract = false;
		DetectionHarnessPlayMode.RunVisionDetectionBalance = false;
		DetectionHarnessPlayMode.RunVisionContactLifecycle = false;
		DetectionHarnessPlayMode.RunVisionOpticRangeContract = false;
		DetectionHarnessPlayMode.RunWeaponRangeContract = false;
		DetectionHarnessPlayMode.RunAccuracyAimCurveContract = false;
		DetectionHarnessPlayMode.RunFireDisciplineContract = false;
		DetectionHarnessPlayMode.RunProjectileVisionContract = false;
		DetectionHarnessPlayMode.RunVehicleVisionContract = false;
		DetectionHarnessPlayMode.RunCombatRetainContract = false;
		DetectionHarnessPlayMode.RunAttentionFacingContract = false;
		DetectionHarnessPlayMode.RunSoundPerceptionContract = false;
		DetectionHarnessPlayMode.RunAllyReportContract = false;
		DetectionHarnessPlayMode.RunFinalPerceptionContract = false;
		DetectionHarnessPlayMode.RunGStage = _stage;

		if (EditorApplication.isPlaying)
		{
			if (!TryRunInCurrentPlay(_stage))
				Debug.LogError($"[DetectionGPlayTestRunner] {_stage} AutoSmoke not in loaded scene.");
			return;
		}

		EditorApplication.isPlaying = true;
		string expect = _stage == DetectionHarnessPlayMode.AllGStages
			? "DetectionG_Regression_LAST.txt (and each DetectionG*_LAST.txt)"
			: $"Detection{_stage.Replace("G8Stress", "G8_Stress")}_LAST.txt";
		Debug.Log($"[DetectionGPlayTestRunner] V1.9.5 {_stage}: entering Play. Expect {expect}");
	}

	private static bool TryRunInCurrentPlay(string _stage)
	{
		switch (_stage)
		{
			case "G1":
				return Run(Object.FindAnyObjectByType<DetectionG1AutoSmoke>(), s => s.RunFromEditor());
			case "G2":
				return Run(Object.FindAnyObjectByType<DetectionG2AutoSmoke>(), s => s.RunFromEditor());
			case "G3":
				return Run(Object.FindAnyObjectByType<DetectionG3AutoSmoke>(), s => s.RunFromEditor());
			case "G4":
				return Run(Object.FindAnyObjectByType<DetectionG4AutoSmoke>(), s => s.RunFromEditor());
			case "G5":
				return Run(Object.FindAnyObjectByType<DetectionG5AutoSmoke>(), s => s.RunFromEditor());
			case "G6":
				return Run(Object.FindAnyObjectByType<DetectionG6AutoSmoke>(), s => s.RunFromEditor());
			case "G7":
				return Run(Object.FindAnyObjectByType<DetectionG7AutoSmoke>(), s => s.RunFromEditor());
			case "G8":
				return Run(Object.FindAnyObjectByType<DetectionG8AutoSmoke>(), s => s.RunFromEditor());
			case "G8Stress":
				return Run(Object.FindAnyObjectByType<DetectionG8StressSmoke>(), s => s.RunFromEditor());
			case DetectionHarnessPlayMode.AllGStages:
				return TryRunAllInCurrentPlay();
			default:
				return false;
		}
	}

	private static bool TryRunAllInCurrentPlay()
	{
		DetectionGRegressionPlaySmoke smoke = Object.FindAnyObjectByType<DetectionGRegressionPlaySmoke>();
		if (smoke == null)
		{
			DetectionTestController harness = Object.FindAnyObjectByType<DetectionTestController>();
			if (harness != null)
				smoke = harness.gameObject.AddComponent<DetectionGRegressionPlaySmoke>();
		}

		if (smoke == null)
			return false;
		smoke.RunFromEditor();
		return true;
	}

	private static bool Run<T>(T _smoke, System.Action<T> _run) where T : Behaviour
	{
		if (_smoke == null)
			return false;
		_run(_smoke);
		return true;
	}
}
#endif
