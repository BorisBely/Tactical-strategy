using UnityEngine;

/// <summary>
/// Play-mode gate for detection harness. Uses PlayerPrefs so flags survive domain reload on Enter Play.
/// </summary>
public static class DetectionHarnessPlayMode
{
	#region Constants
	private const string c_RunRuntimeKey = "DetectionCalib.RunRuntime";
	private const string c_RunStrictKey = "DetectionCalib.RunStrict";
	private const string c_SkipGKey = "DetectionCalib.SkipGStages";
	private const string c_RunGKey = "DetectionCalib.RunGStage";
	private const string c_RunMemoryKey = "DetectionCalib.RunMemory";
	private const string c_RunIdentityKey = "DetectionCalib.RunIdentity";
	private const string c_RunAIPerceptionKey = "DetectionCalib.RunAIPerception";
	private const string c_RunAITacticalKey = "DetectionCalib.RunAITactical";
	private const string c_RunUseOfForceKey = "DetectionCalib.RunUseOfForce";
	public const string AllGStages = "All";
	#endregion

	#region Public Properties
	public static bool SkipClosedGStages
	{
		get => PlayerPrefs.GetInt(c_SkipGKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_SkipGKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCalibrationStrict
	{
		get => PlayerPrefs.GetInt(c_RunStrictKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunStrictKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCalibrationRuntime
	{
		get => PlayerPrefs.GetInt(c_RunRuntimeKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunRuntimeKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunMemoryCalibration
	{
		get => PlayerPrefs.GetInt(c_RunMemoryKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunMemoryKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunIdentityCalibration
	{
		get => PlayerPrefs.GetInt(c_RunIdentityKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunIdentityKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunAIPerceptionHandoff
	{
		get => PlayerPrefs.GetInt(c_RunAIPerceptionKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunAIPerceptionKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunAITacticalState
	{
		get => PlayerPrefs.GetInt(c_RunAITacticalKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunAITacticalKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunUseOfForcePolicy
	{
		get => PlayerPrefs.GetInt(c_RunUseOfForceKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunUseOfForceKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	/// <summary>G1, G2, … G8, G8Stress, or All. Empty = not a dedicated G Play.</summary>
	public static string RunGStage
	{
		get => PlayerPrefs.GetString(c_RunGKey, string.Empty) ?? string.Empty;
		set
		{
			PlayerPrefs.SetString(c_RunGKey, value ?? string.Empty);
			PlayerPrefs.Save();
		}
	}

	public static bool IsCalibrationPlay =>
		RunCalibrationStrict || RunCalibrationRuntime || RunMemoryCalibration ||
		RunIdentityCalibration || RunAIPerceptionHandoff || RunAITacticalState ||
		RunUseOfForcePolicy;

	public static bool IsGRegressionPlay => !string.IsNullOrEmpty(RunGStage);
	#endregion

	#region Public Methods
	public static bool ShouldRunGAutoSmoke(bool _runOnStart, string _stageId)
	{
		if (!string.IsNullOrEmpty(RunGStage))
			return RunGStage == _stageId;
		return _runOnStart && !SkipClosedGStages;
	}

	/// <summary>
	/// Stacked Play used stagger warmups so G4…G8 would not overlap.
	/// Dedicated V1.9.5 Play runs one stage only — skip that wait.
	/// </summary>
	public static float GWarmupSeconds(float _serializedWarmup)
	{
		return IsGRegressionPlay ? 0f : Mathf.Max(0f, _serializedWarmup);
	}

	public static void ResetFlags()
	{
		SkipClosedGStages = false;
		RunCalibrationRuntime = false;
		RunCalibrationStrict = false;
		RunMemoryCalibration = false;
		RunIdentityCalibration = false;
		RunAIPerceptionHandoff = false;
		RunAITacticalState = false;
		RunUseOfForcePolicy = false;
		RunGStage = string.Empty;
	}

	public static string FormatSelectorProbe(
		DetectionProcessor _processor,
		TargetSelector _selector,
		Transform _target)
	{
		string sel = _selector != null && _selector.SelectedTarget != null
			? _selector.SelectedTarget.name
			: string.Empty;
		bool aim = _selector != null && _selector.HasSelectedAimPoint;
		bool lof = _selector != null && _selector.IsLineOfFireSuppressed(_target);
		bool engage = TargetEngageability.IsEngageable(_target);

		if (_processor == null || _target == null || !_processor.TryGetContact(_target, out PerceivedContact contact) ||
		    contact == null)
		{
			return $"sel={sel} aim={aim} contact=null lof={lof} engage={engage}";
		}

		return
			$"sel={sel} aim={aim} obs={contact.ObservationState} " +
			$"conf={contact.LastSeenConfidence:F2} know={contact.HasKnowledge} " +
			$"lof={lof} engage={engage}";
	}
	#endregion
}
