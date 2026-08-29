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
	private const string c_RunCombatEngageKey = "DetectionCalib.RunCombatEngage";
	private const string c_RunImmediateThreatKey = "DetectionCalib.RunImmediateThreat";
	private const string c_RunCombatEventWorldKey = "DetectionCalib.RunCombatEventWorld";
	private const string c_RunSoundInAiKey = "DetectionCalib.RunSoundInAi";
	private const string c_RunSearch20Key = "DetectionCalib.RunSearch20";
	private const string c_RunCommandPriorityKey = "DetectionCalib.RunCommandPriority";
	private const string c_RunTargetCalibrationKey = "DetectionCalib.RunTargetCalibration";
	private const string c_RunCoverGenerationKey = "DetectionCalib.RunCoverGeneration";
	private const string c_RunCoverClassificationKey = "DetectionCalib.RunCoverClassification";
	private const string c_RunCoverEvaluationKey = "DetectionCalib.RunCoverEvaluation";
	private const string c_RunCoverEmergencyKey = "DetectionCalib.RunCoverEmergency";
	private const string c_RunCoverTacticalKey = "DetectionCalib.RunCoverTactical";
	private const string c_RunCoverOccupancyKey = "DetectionCalib.RunCoverOccupancy";
	private const string c_RunCoverPeekKey = "DetectionCalib.RunCoverPeek";
	private const string c_RunCoverIntegrationKey = "DetectionCalib.RunCoverIntegration";
	private const string c_RunTacticalMovementKey = "DetectionCalib.RunTacticalMovement";
	private const string c_RunReadinessKey = "DetectionCalib.RunReadiness";
	private const string c_RunThreatDirectionKey = "DetectionCalib.RunThreatDirection";
	private const string c_RunThreatDirectionCoverKey = "DetectionCalib.RunThreatDirectionCover";
	private const string c_RunThreatDirectionQualityKey = "DetectionCalib.RunThreatDirectionQuality";
	private const string c_RunThreatDirectionPositionKey = "DetectionCalib.RunThreatDirectionPosition";
	private const string c_RunThreatDirectionReorientationKey = "DetectionCalib.RunThreatDirectionReorientation";
	private const string c_RunThreatDirectionRepositionKey = "DetectionCalib.RunThreatDirectionReposition";
	private const string c_RunFrozenLayersPlayKey = "DetectionCalib.RunFrozenLayersPlay";
	private const string c_RunSearchExecutionKey = "DetectionCalib.RunSearchExecution";
	private const string c_RunTacticalNavKey = "DetectionCalib.RunTacticalNav";
	private const string c_RunTacticalCommandKey = "DetectionCalib.RunTacticalCommand";
	private const string c_RunGameCommandKey = "DetectionCalib.RunGameCommand";
	private const string c_RunGameCommandInputKey = "DetectionCalib.RunGameCommandInput";
	private const string c_RunGameCommandLayerKey = "DetectionCalib.RunGameCommandLayer";
	private const string c_RunVisionEnvelopeKey = "DetectionCalib.RunVisionEnvelope";
	private const string c_RunVisionDetectCalKey = "DetectionCalib.RunVisionDetectCal";
	private const string c_RunVisionExposureFovKey = "DetectionCalib.RunExposureFov";
	private const string c_RunVisionDetectBalanceKey = "DetectionCalib.RunDetectBalance";
	private const string c_RunVisionContactLifecycleKey = "DetectionCalib.RunContactLifecycle";
	private const string c_RunVisionOpticRangeContractKey = "DetectionCalib.RunOpticRange";
	private const string c_RunWeaponRangeContractKey = "DetectionCalib.RunWeaponRange";
	private const string c_RunAccuracyAimCurveContractKey = "DetectionCalib.RunAccuracyAim";
	private const string c_RunFireDisciplineContractKey = "DetectionCalib.RunFireDiscipline";
	private const string c_RunProjectileVisionContractKey = "DetectionCalib.RunProjectileVision";
	private const string c_RunVehicleVisionContractKey = "DetectionCalib.RunVehicleVision";
	private const string c_RunCombatRetainContractKey = "DetectionCalib.RunCombatRetain";
	private const string c_RunAttentionFacingContractKey = "DetectionCalib.RunAttentionFacing";
	private const string c_RunSoundPerceptionContractKey = "DetectionCalib.RunSoundPerception";
	private const string c_RunAllyReportContractKey = "DetectionCalib.RunAllyReport";
	private const string c_RunFinalPerceptionContractKey = "DetectionCalib.RunFinalPerception";
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

	public static bool RunCombatEngageExecution
	{
		get => PlayerPrefs.GetInt(c_RunCombatEngageKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCombatEngageKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunImmediateThreatLive
	{
		get => PlayerPrefs.GetInt(c_RunImmediateThreatKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunImmediateThreatKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCombatEventWorld
	{
		get => PlayerPrefs.GetInt(c_RunCombatEventWorldKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCombatEventWorldKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunSoundInAi
	{
		get => PlayerPrefs.GetInt(c_RunSoundInAiKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunSoundInAiKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunSearch20
	{
		get => PlayerPrefs.GetInt(c_RunSearch20Key, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunSearch20Key, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCommandPriority
	{
		get => PlayerPrefs.GetInt(c_RunCommandPriorityKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCommandPriorityKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunTargetCalibration
	{
		get => PlayerPrefs.GetInt(c_RunTargetCalibrationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunTargetCalibrationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverGeneration
	{
		get => PlayerPrefs.GetInt(c_RunCoverGenerationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverGenerationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverClassification
	{
		get => PlayerPrefs.GetInt(c_RunCoverClassificationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverClassificationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverEvaluation
	{
		get => PlayerPrefs.GetInt(c_RunCoverEvaluationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverEvaluationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverEmergency
	{
		get => PlayerPrefs.GetInt(c_RunCoverEmergencyKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverEmergencyKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverTactical
	{
		get => PlayerPrefs.GetInt(c_RunCoverTacticalKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverTacticalKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverOccupancy
	{
		get => PlayerPrefs.GetInt(c_RunCoverOccupancyKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverOccupancyKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverPeek
	{
		get => PlayerPrefs.GetInt(c_RunCoverPeekKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverPeekKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCoverIntegration
	{
		get => PlayerPrefs.GetInt(c_RunCoverIntegrationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCoverIntegrationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunTacticalMovement
	{
		get => PlayerPrefs.GetInt(c_RunTacticalMovementKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunTacticalMovementKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunReadiness
	{
		get => PlayerPrefs.GetInt(c_RunReadinessKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunReadinessKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirection
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirectionCover
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionCoverKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionCoverKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirectionQuality
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionQualityKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionQualityKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirectionPosition
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionPositionKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionPositionKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirectionReorientation
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionReorientationKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionReorientationKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunThreatDirectionReposition
	{
		get => PlayerPrefs.GetInt(c_RunThreatDirectionRepositionKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunThreatDirectionRepositionKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunFrozenLayersPlay
	{
		get => PlayerPrefs.GetInt(c_RunFrozenLayersPlayKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunFrozenLayersPlayKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunSearchExecution
	{
		get => PlayerPrefs.GetInt(c_RunSearchExecutionKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunSearchExecutionKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunTacticalNavigationExecution
	{
		get => PlayerPrefs.GetInt(c_RunTacticalNavKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunTacticalNavKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunTacticalCommandContract
	{
		get => PlayerPrefs.GetInt(c_RunTacticalCommandKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunTacticalCommandKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunGameCommandSource
	{
		get => PlayerPrefs.GetInt(c_RunGameCommandKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunGameCommandKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunGameCommandInput
	{
		get => PlayerPrefs.GetInt(c_RunGameCommandInputKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunGameCommandInputKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionEnvelope
	{
		get => PlayerPrefs.GetInt(c_RunVisionEnvelopeKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionEnvelopeKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionDetectionCalibration
	{
		get => PlayerPrefs.GetInt(c_RunVisionDetectCalKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionDetectCalKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionExposureFovContract
	{
		get => PlayerPrefs.GetInt(c_RunVisionExposureFovKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionExposureFovKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionDetectionBalance
	{
		get => PlayerPrefs.GetInt(c_RunVisionDetectBalanceKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionDetectBalanceKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionContactLifecycle
	{
		get => PlayerPrefs.GetInt(c_RunVisionContactLifecycleKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionContactLifecycleKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVisionOpticRangeContract
	{
		get => PlayerPrefs.GetInt(c_RunVisionOpticRangeContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVisionOpticRangeContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunWeaponRangeContract
	{
		get => PlayerPrefs.GetInt(c_RunWeaponRangeContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunWeaponRangeContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunAccuracyAimCurveContract
	{
		get => PlayerPrefs.GetInt(c_RunAccuracyAimCurveContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunAccuracyAimCurveContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunFireDisciplineContract
	{
		get => PlayerPrefs.GetInt(c_RunFireDisciplineContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunFireDisciplineContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunProjectileVisionContract
	{
		get => PlayerPrefs.GetInt(c_RunProjectileVisionContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunProjectileVisionContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunVehicleVisionContract
	{
		get => PlayerPrefs.GetInt(c_RunVehicleVisionContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunVehicleVisionContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunCombatRetainContract
	{
		get => PlayerPrefs.GetInt(c_RunCombatRetainContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunCombatRetainContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunAttentionFacingContract
	{
		get => PlayerPrefs.GetInt(c_RunAttentionFacingContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunAttentionFacingContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunSoundPerceptionContract
	{
		get => PlayerPrefs.GetInt(c_RunSoundPerceptionContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunSoundPerceptionContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunAllyReportContract
	{
		get => PlayerPrefs.GetInt(c_RunAllyReportContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunAllyReportContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunFinalPerceptionContract
	{
		get => PlayerPrefs.GetInt(c_RunFinalPerceptionContractKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunFinalPerceptionContractKey, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	public static bool RunGameCommandLayer
	{
		get => PlayerPrefs.GetInt(c_RunGameCommandLayerKey, 0) == 1;
		set
		{
			PlayerPrefs.SetInt(c_RunGameCommandLayerKey, value ? 1 : 0);
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
		RunUseOfForcePolicy || RunCombatEngageExecution || RunSearchExecution ||
		RunTacticalNavigationExecution || RunTacticalCommandContract || RunGameCommandSource ||
		RunGameCommandInput || RunGameCommandLayer || RunVisionEnvelope ||
		RunVisionDetectionCalibration || RunVisionExposureFovContract || RunVisionDetectionBalance ||
		RunVisionContactLifecycle || RunVisionOpticRangeContract || RunWeaponRangeContract ||
		RunAccuracyAimCurveContract || RunFireDisciplineContract || RunProjectileVisionContract ||
		RunVehicleVisionContract || 		RunCombatRetainContract || RunAttentionFacingContract ||
		RunSoundPerceptionContract || RunAllyReportContract || RunFinalPerceptionContract;

	public static bool IsGRegressionPlay => !string.IsNullOrEmpty(RunGStage);
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void ActivateHarnessAfterSceneLoad()
	{
		if (!IsCalibrationPlay && !IsGRegressionPlay)
			return;

		DetectionTestController harness = EnsureHarnessActive();
		if (harness == null)
		{
			Debug.LogError(
				"[DetectionHarnessPlayMode] DetectionTestController not in the loaded scene. " +
				"Open SampleScene before Tools/Tests Play.");
		}
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// SampleScene keeps DetectionG1Harness inactive for normal Play.
	/// Dedicated Tools/Tests Play must wake it so the smoke can attach.
	/// </summary>
	public static DetectionTestController EnsureHarnessActive()
	{
		DetectionTestController harness =
			UnityEngine.Object.FindAnyObjectByType<DetectionTestController>(FindObjectsInactive.Include);
		if (harness == null)
			return null;

		if (!harness.gameObject.activeSelf)
		{
			harness.gameObject.SetActive(true);
			Debug.Log(
				$"[DetectionHarnessPlayMode] Activated '{harness.gameObject.name}' for Play test.",
				harness);
		}

		return harness;
	}

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
		RunCombatEngageExecution = false;
		RunImmediateThreatLive = false;
		RunCombatEventWorld = false;
		RunSoundInAi = false;
		RunSearch20 = false;
		RunCommandPriority = false;
		RunTargetCalibration = false;
		RunCoverGeneration = false;
		RunCoverClassification = false;
		RunCoverEvaluation = false;
		RunCoverEmergency = false;
		RunCoverTactical = false;
		RunCoverOccupancy = false;
		RunCoverPeek = false;
		RunCoverIntegration = false;
		RunTacticalMovement = false;
		RunReadiness = false;
		RunThreatDirection = false;
		RunThreatDirectionCover = false;
		RunThreatDirectionQuality = false;
		RunThreatDirectionPosition = false;
		RunThreatDirectionReorientation = false;
		RunThreatDirectionReposition = false;
		RunFrozenLayersPlay = false;
		RunSearchExecution = false;
		RunTacticalNavigationExecution = false;
		RunTacticalCommandContract = false;
		RunGameCommandSource = false;
		RunGameCommandInput = false;
		RunGameCommandLayer = false;
		RunVisionEnvelope = false;
		RunVisionDetectionCalibration = false;
		RunVisionExposureFovContract = false;
		RunVisionDetectionBalance = false;
		RunVisionContactLifecycle = false;
		RunVisionOpticRangeContract = false;
		RunWeaponRangeContract = false;
		RunAccuracyAimCurveContract = false;
		RunFireDisciplineContract = false;
		RunProjectileVisionContract = false;
		RunVehicleVisionContract = false;
		RunCombatRetainContract = false;
		RunAttentionFacingContract = false;
		RunSoundPerceptionContract = false;
		RunAllyReportContract = false;
		RunFinalPerceptionContract = false;
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
