using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Play Mode прогон поз выбранного юнита: клавиша L.
/// Матрица: Standing/Crouch × Idle/Walk × LowReady/HighReady/PreAim/HipFire/PointAim/Aiming × 1/3/10 выстрелов.
/// Логи [ArmRecoil] и [WeaponVisDiag] на каждый выстрел. Старые [RecoilSweep]/[HeadSweep] выключены.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitWeaponPoseSweepTest : MonoBehaviour
{
	#region Types
	private struct CellResult
	{
		public string CellName;
		public WeaponPoseMode WantedMode;
		public WeaponPoseState ExpectedPose;
		public WeaponPoseState GotPose;
		public bool Walk;
		public LocomotionStance Stance;
		public int WantedShots;
		public int FiredShots;
		public float PeakPunch;
		public float PeakClimb;
		public float PeakVisErr;
		public float RecoverSeconds;
		public float OverlayDelta;
		public float PlateauDelta;
		public float AimResidual;
		public float VisualError;
		public float WalkComp;
		public bool PoseOk;
		public bool Pass;
		public string Verdict;
		public bool HasHeadSample;
		public float HeadPitchDegrees;
		public float HeadYawDegrees;
		public float HeadRollDegrees;
	}

	private struct BarrelSample
	{
		public float AimResidual;
		public float AimYaw;
		public float VisualBarrelPitch;
		public float VisualError;
		public float VisualYawErr;
		public float Punch;
		public float Climb;
		public float Penalty;
		public float WalkComp;
		public float AimQuality;
		public bool KickActive;
	}

	/// <summary>
	/// Усреднённый за окно замер положения головы/шеи в системе тела юнита
	/// (относительно yaw-оси корня, горизонт мира).
	/// Знаки: pitch + = нос вверх, yaw + = вправо от направления тела, roll + = наклон вправо.
	/// </summary>
	private struct HeadSample
	{
		public bool Valid;
		public float HeadPitchDegrees;
		public float HeadYawDegrees;
		public float HeadRollDegrees;
		public float NeckPitchDegrees;
		public float NeckYawDegrees;
		public float NeckRollDegrees;
		public float NavSpeed;
		public float RootYawDegrees;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitWeaponAiming m_Aiming;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private RtsUnitMember m_RtsMember;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDiscipline;
	[SerializeField] private UnitWeaponAutoFireWhenAimed m_AutoFire;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponRecoil m_WeaponRecoil;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;

	[Header("Input")]
	[Tooltip("По умолчанию выключено. Компонент не на юните; K/L не стартуют прогон, пока Enable Keyboard Start не включён вручную.")]
	[SerializeField] private bool m_EnableKeyboardStart = false;
	[SerializeField] private Key m_StartTestKey = Key.L;
	[SerializeField] private bool m_RequireSelected = true;

	[Header("Matrix")]
	[SerializeField] private bool m_IncludeCrouch = true;
	[SerializeField] private bool m_IncludeHipFire = true;
	[SerializeField] private bool m_IncludeReadySkip = true;
	[SerializeField] private int[] m_BurstShotCounts = { 1, 3, 10 };

	[Header("Timing")]
	[SerializeField, Min(0.05f)] private float m_PoseSettleTimeoutSeconds = 4f;
	[SerializeField, Min(0f)] private float m_HoldAfterSettleSeconds = 0.25f;
	[SerializeField, Min(0.1f)] private float m_StanceWaitTimeoutSeconds = 5f;
	[SerializeField, Min(1f)] private float m_WalkDistanceMeters = 22f;
	[SerializeField, Min(0.5f)] private float m_WalkStartTimeoutSeconds = 3f;
	[SerializeField, Min(0.05f)] private float m_WalkSpeedSettleSeconds = 0.4f;
	[SerializeField, Min(0.5f)] private float m_ReloadWaitTimeoutSeconds = 8f;
	[SerializeField, Min(1f)] private float m_AimGeometryTimeoutSeconds = 8f;
	[SerializeField, Min(0.05f)] private float m_AimGeometryStableSeconds = 0.2f;
	[SerializeField, Min(2f)] private float m_WalkRefreshRemainingMeters = 4f;
	[SerializeField, Min(1f)] private float m_FireTimeoutSeconds = 6f;
	[SerializeField, Min(0.5f)] private float m_RecoverTimeoutSeconds = 8f;
	[SerializeField, Min(0.02f)] private float m_RecoverStableSeconds = 0.1f;
	[SerializeField, Min(0.05f)] private float m_RecoverLogIntervalSeconds = 0.25f;

	[Header("Pass thresholds")]
	[Tooltip("PointAim / Aiming: |visualError − baseline| после возврата.")]
	[SerializeField, Min(0.1f)] private float m_MaxResidualPitchDegrees = 2f;
	[Tooltip("HipFire: |visualError − baseline| после возврата (клип гуляет сильнее).")]
	[SerializeField, Min(0.1f)] private float m_MaxHipFirePlateauDegrees = 4.5f;
	[Tooltip("PointAim / Aiming: кадры с |residualYaw| больше порога не стартуют огонь.")]
	[SerializeField, Min(5f)] private float m_MaxSampleYawDegrees = 35f;
	[Tooltip("|visualError − aimResidual| после punch/climb=0. Ненулевое — overlay застрял на кости.")]
	[SerializeField, Min(0.05f)] private float m_MaxOverlayLeftoverDegrees = 0.75f;
	[Tooltip("Минимальный peak punch, иначе камера/LOD съели kick.")]
	[SerializeField, Min(0.01f)] private float m_MinPeakPunchDegrees = 0.2f;
	[Tooltip("Punch/climb ниже этого считаются вернувшимися.")]
	[SerializeField, Min(0.001f)] private float m_KickSettledDegrees = 0.05f;
	[SerializeField] private bool m_SuppressCombatFire = true;
	[SerializeField] private bool m_MuteWeaponSpinDuringSweep = true;
	[SerializeField] private bool m_PreferFullAuto = true;

	[Header("Logging")]
	[Tooltip("Старые логи [RecoilSweep] (калибровка отдачи). Выключены по умолчанию; включать только для повторной диагностики отдачи.")]
	[SerializeField] private bool m_LogRecoilSweep = false;
	[Tooltip("Логи [HeadSweep]. Выключены: калибровка головы снята, наклон смотри [SpineLeanDiag] по K.")]
	[SerializeField] private bool m_LogHeadSweep = false;
	[Tooltip("Окно усреднения замеров головы в клетке (сек).")]
	[SerializeField, Min(0.05f)] private float m_HeadSampleWindowSeconds = 0.2f;

	[Header("Weapon shot pose logging")]
	[Tooltip("Подробный лог изменения позы оружия на каждый выстрел прогона (Hand_R, корень оружия, дуло, затвор). Фильтр консоли: WeaponVisDiag.")]
	[SerializeField] private bool m_LogWeaponShotPose = true;
	[Tooltip("Лог визуальной отдачи правой руки на каждый выстрел прогона. Фильтр консоли: ArmRecoil.")]
	[SerializeField] private bool m_LogArmRecoil = true;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private WeaponVisualRecoilApplicator m_RecoilApplicator;
	[SerializeField] private UnitWeaponBoltCycleVisual m_BoltCycleVisual;
	[SerializeField] private UnitWeaponArmRecoil m_ArmRecoil;
	#endregion

	#region Private Fields
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);

	private static readonly WeaponPoseMode[] s_ReadySkipModes =
	{
		WeaponPoseMode.LowReady,
		WeaponPoseMode.HighReady,
		WeaponPoseMode.PreAim,
	};

	private static readonly WeaponPoseMode[] s_FireModes =
	{
		WeaponPoseMode.HipFire,
		WeaponPoseMode.PointAim,
		WeaponPoseMode.Aiming,
	};

	private static readonly int[] s_DefaultBursts = { 1, 3, 10 };

	private Coroutine m_TestRoutine;
	private readonly List<CellResult> m_Results = new List<CellResult>(48);
	private readonly WaitForEndOfFrame m_EndOfFrame = new WaitForEndOfFrame();
	private bool m_CancelRequested;
	private bool m_HadReadyKeyboard;
	private bool m_HadStanceKeyboard;
	private bool m_HadDisciplineEnabled;
	private bool m_HadAutoFireEnabled;
	private bool m_HadTryReloadWhenOutOfAmmo;
	private bool m_HadResetRecoilOnStopFiring;
	private bool m_HadIgnoreCameraCull;
	private bool m_HadForceArmRecoilFullQuality;
	private bool m_HadLogWeaponSpin;
	private bool m_HasCapturedRestore;
	private WeaponPoseMode m_RestoreMode;
	private bool m_RestorePeaceful;
	private WeaponPoseState m_RestorePeacefulPose;
	private LocomotionStance m_RestoreStance;
	private WeaponFireMode m_RestoreFireMode;
	private bool m_HasRestoreFireMode;
	private AmmoDefinition m_CapturedAmmo;
	private bool m_CurrentWalk;
	private int m_BurstShotsFired;
	private HeadSample m_LastHeadSample;

	private const string c_WeaponVisDiagTag = "[WeaponVisDiag]";
	private const string c_ArmRecoilTag = "[ArmRecoil]";
	private string m_CurrentCellName;
	private float m_LastFramePenalty;
	private bool m_HasPendingShotCapture;
	private float m_ShotLogTime;
	private string m_ShotLogAmmoName;
	private WeaponFireMode m_ShotLogFireMode;
	private int m_ShotLogBurstIndex;
	private float m_ShotLogRecoilAdded;
	private float m_ShotLogPenaltyBefore;
	private float m_ShotLogKickScale;
	private Vector3 m_ShotLogHandBaseLocalPos;
	private Quaternion m_ShotLogHandBaseLocalRot;
	private Vector3 m_ShotLogWeaponWorldPos;
	private Quaternion m_ShotLogWeaponWorldRot;
	private Vector3 m_ShotLogMuzzleWorldPos;
	private Quaternion m_ShotLogMuzzleWorldRot;
	private Vector3 m_ShotLogBoltLocalPos;
	#endregion

	#region Public Properties
	public bool IsRunning => m_TestRoutine != null;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnDisable()
	{
		if (m_TestRoutine != null)
		{
			StopCoroutine(m_TestRoutine);
			m_TestRoutine = null;
		}

		RestoreUnitState();
	}

	private void Update()
	{
		if (!m_EnableKeyboardStart)
			return;
		if (!WasKeyPressedThisFrame(m_StartTestKey))
			return;
		if (m_RequireSelected && (m_RtsMember == null || !m_RtsMember.IsSelected))
			return;

		if (m_TestRoutine != null)
		{
			m_CancelRequested = true;
			LogRecoilSweep("[RecoilSweep] CANCEL requested.");
			return;
		}

		StartSweep();
	}
	#endregion

	#region Public Methods
	public void StartSweep()
	{
		if (m_TestRoutine != null)
			return;

		ResolveReferences();
		m_CancelRequested = false;
		m_TestRoutine = StartCoroutine(CoRunSweep());
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_Aiming == null)
			m_Aiming = GetComponent<UnitWeaponAiming>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponentInChildren<TargetSelector>();
		if (m_FireDiscipline == null)
			m_FireDiscipline = GetComponent<UnitWeaponFireDisciplineController>();
		if (m_AutoFire == null)
			m_AutoFire = GetComponent<UnitWeaponAutoFireWhenAimed>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_WeaponRecoil == null)
			m_WeaponRecoil = GetComponent<UnitWeaponRecoil>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);
		if (m_RecoilApplicator == null)
			m_RecoilApplicator = GetComponent<WeaponVisualRecoilApplicator>();
		if (m_BoltCycleVisual == null)
			m_BoltCycleVisual = GetComponent<UnitWeaponBoltCycleVisual>();
		if (m_ArmRecoil == null)
			m_ArmRecoil = GetComponent<UnitWeaponArmRecoil>();
	}

	private IEnumerator CoRunSweep()
	{
		m_Results.Clear();
		CaptureRestoreState();
		LockGameplayInput(true);
		ApplySweepRecoilHooks(true);

		string targetName = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null
			? m_TargetSelector.SelectedTarget.name
			: "none";
		WeaponFireMode selected = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		WeaponFireMode effective = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: selected;
		bool cameraNear = m_WeaponRecoil == null || m_WeaponRecoil.IsCameraNearForVisualKick();
		LogRecoilSweep(
			$"[RecoilSweep] START unit={name} target={targetName} selectedFire={selected} " +
			$"effectiveFire={effective} cameraNear={cameraNear} " +
			$"resetOnStop={m_FireController != null && m_FireController.ResetRecoilOnStopFiring} " +
			$"filter=RecoilSweep");

		if (m_LogArmRecoil)
			Debug.Log($"{c_ArmRecoilTag} START unit={name} target={targetName} filter=ArmRecoil", this);

		LogHeadSweep(
			$"[HeadSweep] START unit={name} target={targetName} " +
			$"filter=HeadSweep");

		m_FireController?.StopFiring();
		yield return CoWaitReloadClear();
		if (m_CancelRequested)
		{
			FinishSweep();
			yield break;
		}

		if (m_ReadyHands == null || !m_ReadyHands.IsWeaponEquipped())
		{
			Debug.LogError("[RecoilSweep] ABORT: equipped weapon required.", this);
			FinishSweep();
			yield break;
		}

		if (m_FireController == null || m_WeaponRecoil == null || m_WeaponRuntime == null)
		{
			Debug.LogError("[RecoilSweep] ABORT: fire/recoil/runtime components missing.", this);
			FinishSweep();
			yield break;
		}

		if (m_TargetSelector == null || m_TargetSelector.SelectedTarget == null)
		{
			Debug.LogError(
				"[RecoilSweep] ABORT: SelectedTarget required. Выбери цель, затем L.",
				this);
			FinishSweep();
			yield break;
		}

		CaptureAmmoDefinition();
		if (m_CapturedAmmo == null)
		{
			Debug.LogError("[RecoilSweep] ABORT: no chambered/magazine ammo to refill.", this);
			FinishSweep();
			yield break;
		}

		if (!cameraNear)
			LogRecoilSweepWarning(
				"[RecoilSweep] camera is outside VFX near radius; IgnoreCameraDistanceCull=1 for this run.");

		LocomotionStance[] stances = m_IncludeCrouch
			? new[] { LocomotionStance.Standing, LocomotionStance.Crouch }
			: new[] { LocomotionStance.Standing };

		for (int s = 0; s < stances.Length && !m_CancelRequested; s++)
		{
			LocomotionStance stance = stances[s];
			yield return CoParkToLowReady();
			yield return CoEnsureIdle();
			if (m_Stance != null)
			{
				if (stance == LocomotionStance.Crouch)
					m_Stance.RequestCrouch();
				else
					m_Stance.RequestStanding();
			}

			yield return CoWaitStance(stance);
			yield return CoWaitStanceTransitionClear();

			for (int w = 0; w < 2 && !m_CancelRequested; w++)
			{
				bool walk = w == 1;
				m_CurrentWalk = walk;
				if (walk)
				{
					yield return CoParkToLowReady();
					yield return CoEnsureWalking();
				}
				else
					yield return CoEnsureIdle();

				if (m_IncludeReadySkip)
				{
					for (int i = 0; i < s_ReadySkipModes.Length && !m_CancelRequested; i++)
						yield return CoRunSkipCell(stance, walk, s_ReadySkipModes[i]);
				}

				WeaponPoseMode[] fireModes = BuildFireModes();
				for (int i = 0; i < fireModes.Length && !m_CancelRequested; i++)
				{
					if (walk)
						yield return CoEnsureWalking();
					yield return CoRunFirePose(stance, walk, fireModes[i]);
				}

				if (walk && m_ClickToMove != null)
					m_ClickToMove.HardStop();
			}
		}

		FinishSweep();
	}

	private void FinishSweep()
	{
		RestoreUnitState();
		LogSummary();
		LogHeadSummary();
		string doneMessage =
			m_CancelRequested
				? $"[RecoilSweep] CANCELLED unit={name}"
				: $"[RecoilSweep] DONE unit={name}";
		LogRecoilSweep(doneMessage);
		LogHeadSweep(
			m_CancelRequested
				? $"[HeadSweep] CANCELLED unit={name}"
				: $"[HeadSweep] DONE unit={name}");
		m_TestRoutine = null;
	}

	private WeaponPoseMode[] BuildFireModes()
	{
		if (m_IncludeHipFire)
			return s_FireModes;

		return new[] { WeaponPoseMode.PointAim, WeaponPoseMode.Aiming };
	}

	private int[] ResolveBurstCounts()
	{
		if (m_BurstShotCounts == null || m_BurstShotCounts.Length == 0)
			return s_DefaultBursts;
		return m_BurstShotCounts;
	}

	private IEnumerator CoRunSkipCell(LocomotionStance _stance, bool _walk, WeaponPoseMode _mode)
	{
		WeaponPoseState expected = ResolveExpectedPose(_mode, _stance, _walk);
		string cell = FormatCell(_stance, _walk, _mode, expected, 0);
		m_ReadyHands.SetPoseModeWanted(_mode, true);
		yield return CoWaitPose(expected);
		if (m_CancelRequested)
			yield break;

		WeaponPoseState got = GetHeldPose();
		bool poseOk = got == expected;
		HeadSample head = default;
		if (m_LogHeadSweep)
		{
			yield return CoSampleHeadPose(m_HeadSampleWindowSeconds);
			head = m_LastHeadSample;
			LogHeadLine(cell, "SKIP");
		}

		RecordResult(new CellResult
		{
			CellName = cell,
			WantedMode = _mode,
			ExpectedPose = expected,
			GotPose = got,
			Walk = _walk,
			Stance = _stance,
			WantedShots = 0,
			FiredShots = 0,
			PoseOk = poseOk,
			Pass = poseOk,
			Verdict = poseOk ? "SKIP-FIRE" : "FAIL-POSE",
			HasHeadSample = head.Valid,
			HeadPitchDegrees = head.HeadPitchDegrees,
			HeadYawDegrees = head.HeadYawDegrees,
			HeadRollDegrees = head.HeadRollDegrees
		});
		LogRecoilSweep(
			$"[RecoilSweep] CELL {cell} got={got} result={(poseOk ? "SKIP-FIRE" : "FAIL-POSE")} " +
			$"(CanShoot=0)");
	}

	private IEnumerator CoRunFirePose(LocomotionStance _stance, bool _walk, WeaponPoseMode _mode)
	{
		WeaponPoseState expected = ResolveExpectedPose(_mode, _stance, _walk);
		m_ReadyHands.SetPoseModeWanted(_mode, true);
		yield return CoWaitPose(expected);
		if (m_CancelRequested)
			yield break;

		if (ShouldWaitAimGeometry(_mode))
			yield return CoWaitScoredAimReady(expected);
		if (m_CancelRequested)
			yield break;

		if (m_HoldAfterSettleSeconds > 0f)
			yield return new WaitForSeconds(m_HoldAfterSettleSeconds);
		if (m_CancelRequested)
			yield break;

		if (_walk)
			yield return CoEnsureWalking();

		if (ShouldWaitAimGeometry(_mode))
			yield return CoWaitScoredAimReady(expected);
		if (m_CancelRequested)
			yield break;

		yield return CoWaitReloadClear();
		if (m_CancelRequested)
			yield break;

		int[] bursts = ResolveBurstCounts();
		for (int i = 0; i < bursts.Length && !m_CancelRequested; i++)
		{
			if (_walk)
				RefreshWalkIfNeeded();
			yield return CoRunFireCell(_stance, _walk, _mode, expected, Mathf.Max(1, bursts[i]));
		}
	}

	private IEnumerator CoRunFireCell(
		LocomotionStance _stance,
		bool _walk,
		WeaponPoseMode _mode,
		WeaponPoseState _expected,
		int _wantedShots)
	{
		string cell = FormatCell(_stance, _walk, _mode, _expected, _wantedShots);
		m_CurrentCellName = cell;
		LogRecoilSweep($"[RecoilSweep] CELL begin {cell}");

		RefillMagazine();
		yield return CoWaitKickSettled(0.4f, snapIfTimeout: true);
		if (m_CancelRequested)
			yield break;

		if (_walk)
			yield return CoEnsureWalking();
		if (ShouldWaitAimGeometry(_mode))
			yield return CoWaitScoredAimReady(_expected);
		if (m_CancelRequested)
			yield break;

		yield return m_EndOfFrame;
		BarrelSample baseline = CaptureSample();
		LogRecoilSweep(
			$"[RecoilSweep] BASE {cell} pose={GetHeldPose()} {FormatSample(baseline)}");

		HeadSample headBase = default;
		if (m_LogHeadSweep)
		{
			yield return CoSampleHeadPose(m_HeadSampleWindowSeconds);
			headBase = m_LastHeadSample;
			LogHeadLine(cell, "BASE");
		}

		WeaponFireMode fireMode = m_FireController.ResolveEffectiveFireMode();
		float fireTimeout = m_FireTimeoutSeconds + Mathf.Max(0, _wantedShots - 1) * 0.12f;
		float fireStarted = Time.time;
		float peakPunch = 0f;
		float peakClimb = 0f;
		float peakVisErr = 0f;
		yield return CoFireWantedShots(
			_wantedShots,
			fireTimeout,
			(_sample) =>
			{
				peakPunch = Mathf.Max(peakPunch, Mathf.Abs(_sample.Punch));
				peakClimb = Mathf.Max(peakClimb, Mathf.Abs(_sample.Climb));
				peakVisErr = Mathf.Max(peakVisErr, Mathf.Abs(_sample.VisualError));
			});
		if (m_CancelRequested)
			yield break;
		float fireElapsed = Time.time - fireStarted;
		int fired = m_BurstShotsFired;

		BarrelSample release = CaptureSample();
		float recoveryPerSec = m_RecoilController != null
			? m_RecoilController.GetCurrentRecoveryPerSecond()
			: 0f;
		LogRecoilSweep(
			$"[RecoilSweep] FIRE end {cell} n={fired}/{_wantedShots} mode={fireMode} " +
			$"elapsed={fireElapsed:F2}s last={m_FireController.LastShotAttemptResult} " +
			$"gate={m_FireController.DebugLastAimGateFail} " +
			$"barrelErr={m_FireController.DebugLastBarrelAimErrorDegrees:F1}° " +
			$"peakPunch={peakPunch:F2} peakClimb={peakClimb:F2} peakVisErr={peakVisErr:F2} " +
			$"recovery/s={recoveryPerSec:F2} {FormatSample(release)}");

		float recoverTimeout = m_RecoverTimeoutSeconds;
		if (_wantedShots <= 1)
			recoverTimeout = Mathf.Min(recoverTimeout, 2.5f);
		else if (_wantedShots <= 3)
			recoverTimeout = Mathf.Min(recoverTimeout, 4.5f);

		float recoverStarted = Time.time;
		bool returned = false;
		float lastRecoverLog = -999f;
		float stable = 0f;
		while (Time.time - recoverStarted < recoverTimeout)
		{
			if (m_CancelRequested)
				yield break;

			if (_walk)
				RefreshWalkIfNeeded();

			yield return m_EndOfFrame;
			BarrelSample rec = CaptureSample();
			bool settled = IsKickSettled(rec);
			if (settled)
			{
				stable += Time.deltaTime;
				if (stable >= m_RecoverStableSeconds)
				{
					returned = true;
					LogRecoilSweep(
						$"[RecoilSweep] RETURN {cell} t={Time.time - recoverStarted:F2}s " +
						$"{FormatSample(rec)} overlayΔ={OverlayDelta(rec):F2}° " +
						$"plateauΔ={PlateauDelta(rec, baseline):F2}°");
					break;
				}
			}
			else
			{
				stable = 0f;
				if (Time.time - lastRecoverLog >= m_RecoverLogIntervalSeconds)
				{
					lastRecoverLog = Time.time;
					LogRecoilSweep(
						$"[RecoilSweep] RECOVER {cell} t={Time.time - recoverStarted:F2}s " +
						$"{FormatSample(rec)} overlayΔ={OverlayDelta(rec):F2}°");
				}
			}
		}

		yield return m_EndOfFrame;
		BarrelSample finalSample = CaptureSample();
		float recoverSeconds = Time.time - recoverStarted;
		if (!returned)
		{
			LogRecoilSweepWarning(
				$"[RecoilSweep] RETURN-TIMEOUT {cell} t={recoverSeconds:F2}s {FormatSample(finalSample)}");
			SnapRecoilForNextCell();
		}

		if (m_LogHeadSweep)
		{
			yield return CoSampleHeadPose(m_HeadSampleWindowSeconds);
			LogHeadLine(cell, "END");
		}

		WeaponPoseState got = GetHeldPose();
		bool poseOk = got == _expected && !IsPoseBlending();
		float overlayDelta = OverlayDelta(finalSample);
		float plateauDelta = PlateauDelta(finalSample, baseline);
		bool fireOk = fired >= _wantedShots;
		bool kickOk = peakPunch >= m_MinPeakPunchDegrees;
		bool overlayOk = overlayDelta <= m_MaxOverlayLeftoverDegrees;
		float plateauLimit = IsHipFireHold(_expected)
			? m_MaxHipFirePlateauDegrees
			: m_MaxResidualPitchDegrees;
		bool plateauOk = plateauDelta <= plateauLimit;

		string verdict;
		if (!poseOk)
			verdict = "FAIL-POSE";
		else if (!fireOk)
			verdict = "FAIL-FIRE";
		else if (!kickOk)
			verdict = "FAIL-KICK";
		else if (!returned)
			verdict = "FAIL-RETURN";
		else if (!overlayOk)
			verdict = "FAIL-STICK";
		else if (!plateauOk)
			verdict = "FAIL-PLATEAU";
		else
			verdict = "PASS";

		bool pass = verdict == "PASS";
		RecordResult(new CellResult
		{
			CellName = cell,
			WantedMode = _mode,
			ExpectedPose = _expected,
			GotPose = got,
			Walk = _walk,
			Stance = _stance,
			WantedShots = _wantedShots,
			FiredShots = fired,
			PeakPunch = peakPunch,
			PeakClimb = peakClimb,
			PeakVisErr = peakVisErr,
			RecoverSeconds = recoverSeconds,
			OverlayDelta = overlayDelta,
			PlateauDelta = plateauDelta,
			AimResidual = finalSample.AimResidual,
			VisualError = finalSample.VisualError,
			WalkComp = finalSample.WalkComp,
			PoseOk = poseOk,
			Pass = pass,
			Verdict = verdict,
			HasHeadSample = headBase.Valid,
			HeadPitchDegrees = headBase.HeadPitchDegrees,
			HeadYawDegrees = headBase.HeadYawDegrees,
			HeadRollDegrees = headBase.HeadRollDegrees
		});

		LogRecoilSweep(
			$"[RecoilSweep] CELL end {cell} got={got} n={fired}/{_wantedShots} " +
			$"peakPunch={peakPunch:F2} peakClimb={peakClimb:F2} recover={recoverSeconds:F2}s " +
			$"aimRes={finalSample.AimResidual:F2}° visErr={finalSample.VisualError:F2}° " +
			$"overlayΔ={overlayDelta:F2}° plateauΔ={plateauDelta:F2}° " +
			$"walkComp={finalSample.WalkComp:F1}° result={verdict}");
	}

	private IEnumerator CoFireWantedShots(
		int _wantedShots,
		float _timeoutSeconds,
		System.Action<BarrelSample> _onSample)
	{
		m_BurstShotsFired = 0;
		m_HasPendingShotCapture = false;
		m_LastFramePenalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
		m_FireController.ShotFired += HandleSweepShotFired;
		try
		{
			RefillMagazine();
			m_FireController.ResetSemiTriggerState();
			m_FireController.StartFiring();
			float aimProgress = m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
				? m_WeaponRuntime.TransientState.AimProgress01
				: 0f;
			LogRecoilSweep(
				$"[RecoilSweep] FIRE start n={_wantedShots} mode={m_FireController.ResolveEffectiveFireMode()} " +
				$"aim={aimProgress:F2}");

			float t = 0f;
			float stallT = 0f;
			int lastShots = 0;
			bool loggedStall = false;
			while (m_BurstShotsFired < _wantedShots && t < _timeoutSeconds)
			{
				if (m_CancelRequested)
					yield break;

				RefreshWalkIfNeeded();
				yield return m_EndOfFrame;
				BarrelSample sample = CaptureSample();
				_onSample?.Invoke(sample);

				if (m_HasPendingShotCapture)
					LogWeaponShotFrame();
				m_LastFramePenalty = sample.Penalty;

				if (m_BurstShotsFired >= _wantedShots)
					break;

				WeaponFireMode mode = m_FireController.ResolveEffectiveFireMode();
				bool automatic = WeaponFireModeUtility.IsAutomaticEffectiveMode(mode);
				if (!automatic)
				{
					m_FireController.ResetSemiTriggerState();
					m_FireController.StartFiring();
				}
				else if (!m_FireController.IsFiringCommandActive)
				{
					m_FireController.StartFiring();
				}

				if (m_BurstShotsFired == lastShots)
				{
					stallT += Time.deltaTime;
					if (stallT >= 0.4f && !loggedStall)
					{
						loggedStall = true;
						LogRecoilSweep(
							$"[RecoilSweep] STALL n={m_BurstShotsFired}/{_wantedShots} " +
							$"last={m_FireController.LastShotAttemptResult} " +
							$"gate={m_FireController.DebugLastAimGateFail} " +
							$"barrelErr={m_FireController.DebugLastBarrelAimErrorDegrees:F1}° " +
							$"{FormatSample(sample)}");
					}
				}
				else
				{
					lastShots = m_BurstShotsFired;
					stallT = 0f;
					loggedStall = false;
				}

				t += Time.deltaTime;
			}
		}
		finally
		{
			m_FireController.ShotFired -= HandleSweepShotFired;
			m_FireController.StopFiring();
		}
	}

	private void HandleSweepShotFired(AmmoDefinition _ammo)
	{
		m_BurstShotsFired++;

		if (!m_LogWeaponShotPose && !m_LogArmRecoil)
			return;

		m_HasPendingShotCapture = true;
		m_ShotLogTime = Time.time;
		m_ShotLogAmmoName = _ammo != null ? _ammo.name : "?";
		m_ShotLogFireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;
		m_ShotLogBurstIndex = m_BurstShotsFired;
		m_ShotLogRecoilAdded = m_RecoilController != null
			? m_RecoilController.ComputeRecoilAddedPerShot(_ammo)
			: 0f;
		m_ShotLogPenaltyBefore = m_LastFramePenalty;
		m_ShotLogKickScale = ResolveVisualKickScale();

		Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
		if (hand != null)
		{
			m_ShotLogHandBaseLocalPos = hand.localPosition;
			m_ShotLogHandBaseLocalRot = hand.localRotation;
		}
		else
		{
			m_ShotLogHandBaseLocalPos = Vector3.zero;
			m_ShotLogHandBaseLocalRot = Quaternion.identity;
		}

		Transform weaponRoot = m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
		m_ShotLogWeaponWorldPos = weaponRoot != null ? weaponRoot.position : Vector3.zero;
		m_ShotLogWeaponWorldRot = weaponRoot != null ? weaponRoot.rotation : Quaternion.identity;

		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform muzzle = equippedWeapon != null ? equippedWeapon.FireOriginTransform : null;
		m_ShotLogMuzzleWorldPos = muzzle != null ? muzzle.position : Vector3.zero;
		m_ShotLogMuzzleWorldRot = muzzle != null ? muzzle.rotation : Quaternion.identity;

		Transform bolt = equippedWeapon != null ? equippedWeapon.BoltCarrierTransform : null;
		m_ShotLogBoltLocalPos = bolt != null ? bolt.localPosition : Vector3.zero;
	}

	private void LogWeaponShotFrame()
	{
		m_HasPendingShotCapture = false;

		Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
		if (hand == null)
		{
			if (m_LogWeaponShotPose)
			{
				LogWeaponShot(
					$"{c_WeaponVisDiagTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | Hand_R не найден");
			}

			if (m_LogArmRecoil)
				LogArmRecoilShot();
			return;
		}

		Vector3 finalLocalPos = hand.localPosition;
		Quaternion finalLocalRot = hand.localRotation;
		Vector3 posDelta = finalLocalPos - m_ShotLogHandBaseLocalPos;
		float rotDelta = Quaternion.Angle(m_ShotLogHandBaseLocalRot, finalLocalRot);

		WeaponVisualRecoilState kick = m_WeaponRecoil != null ? m_WeaponRecoil.CurrentState : default;
		bool overlayApplied = m_RecoilApplicator != null && m_RecoilApplicator.AppliedThisFrame;
		bool canApply = m_WeaponRecoil != null && m_WeaponRecoil.ShouldApplyOverlayThisFrame();
		float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
		float maxPenalty = m_RecoilController != null ? m_RecoilController.MaxRecoilPenalty : 0f;
		float tau = ResolveVisualDecayTau();
		float impulse = m_WeaponRecoil != null ? m_WeaponRecoil.ShotImpulse : 0f;

		Transform weaponRoot = m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
		Vector3 weaponPosDelta = weaponRoot != null
			? weaponRoot.position - m_ShotLogWeaponWorldPos
			: Vector3.zero;
		float weaponRotDelta = weaponRoot != null
			? Quaternion.Angle(m_ShotLogWeaponWorldRot, weaponRoot.rotation)
			: 0f;

		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform muzzle = equippedWeapon != null ? equippedWeapon.FireOriginTransform : null;
		Vector3 muzzlePosDelta = muzzle != null ? muzzle.position - m_ShotLogMuzzleWorldPos : Vector3.zero;
		float muzzleRotDelta = muzzle != null
			? Quaternion.Angle(m_ShotLogMuzzleWorldRot, muzzle.rotation)
			: 0f;
		Vector3 muzzlePreForward = m_ShotLogMuzzleWorldRot * Vector3.forward;
		float muzzleBackProjection = Vector3.Dot(muzzlePosDelta, -muzzlePreForward);
		float muzzleUpProjection = Vector3.Dot(muzzlePosDelta, Vector3.up);

		Transform bolt = equippedWeapon != null ? equippedWeapon.BoltCarrierTransform : null;
		Vector3 boltPosDelta = bolt != null ? bolt.localPosition - m_ShotLogBoltLocalPos : Vector3.zero;
		bool boltOwnsShell = m_BoltCycleVisual != null && m_BoltCycleVisual.WillHandlePhysicalShellEjection;

		Vector3 samplePos = weaponRoot != null ? weaponRoot.position : transform.position;
		float cameraDistance = WeaponVfxUtility.TryGetEffectViewerDistance(samplePos, out float dist)
			? dist
			: -1f;
		bool nearCamera = m_WeaponRecoil != null && m_WeaponRecoil.IsCameraNearForVisualKick();

		var sb = new StringBuilder(640);
		sb.AppendLine(
			$"{c_WeaponVisDiagTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | " +
			$"t={m_ShotLogTime:F3} | ammo={m_ShotLogAmmoName} | mode={m_ShotLogFireMode}");
		sb.AppendLine(
			$"  recoil: added={m_ShotLogRecoilAdded:F3} | penalty {m_ShotLogPenaltyBefore:F2}→{penalty:F2}/{maxPenalty:F1} | kickScale={m_ShotLogKickScale:F2}");
		sb.AppendLine(
			$"  visualState: impulse={impulse:F3} | climbPitch={kick.climbPitch:F3}° punchPitch={kick.punchPitch:F3}° punchYaw={kick.punchYaw:F3}° | " +
			$"back={kick.backOffset:F4}м up={kick.upOffset:F4}м active={kick.isActive} | tau={tau:F3}с");
		sb.AppendLine(
			$"  Hand_R local: base rot={FormatEuler(m_ShotLogHandBaseLocalRot)} pos={FormatVector(m_ShotLogHandBaseLocalPos)}");
		sb.AppendLine(
			$"    → final rot={FormatEuler(finalLocalRot)} pos={FormatVector(finalLocalPos)} | Δrot={rotDelta:F3}° " +
			$"Δpos={FormatVector(posDelta)} |Δpos|={posDelta.magnitude:F4}м | applied={overlayApplied} canApply={canApply}");
		sb.AppendLine(
			$"  WeaponRoot world: pre pos={FormatVector(m_ShotLogWeaponWorldPos)} rot={FormatEuler(m_ShotLogWeaponWorldRot)}");
		sb.AppendLine(
			$"    → post pos={(weaponRoot != null ? FormatVector(weaponRoot.position) : "null")} " +
			$"rot={(weaponRoot != null ? FormatEuler(weaponRoot.rotation) : "null")} | " +
			$"Δpos={FormatVector(weaponPosDelta)} |Δpos|={weaponPosDelta.magnitude:F4}м Δrot={weaponRotDelta:F3}°");
		sb.AppendLine(
			$"  Muzzle world: pre pos={FormatVector(m_ShotLogMuzzleWorldPos)} rot={FormatEuler(m_ShotLogMuzzleWorldRot)}");
		sb.AppendLine(
			$"    → post pos={(muzzle != null ? FormatVector(muzzle.position) : "null")} " +
			$"rot={(muzzle != null ? FormatEuler(muzzle.rotation) : "null")} | " +
			$"Δpos={FormatVector(muzzlePosDelta)} |Δpos|={muzzlePosDelta.magnitude:F4}м Δrot={muzzleRotDelta:F3}° | " +
			$"backProj={muzzleBackProjection:F4}м upProj={muzzleUpProjection:F4}м");
		sb.AppendLine(
			$"  Bolt local: pre pos={FormatVector(m_ShotLogBoltLocalPos)} → " +
			$"post pos={(bolt != null ? FormatVector(bolt.localPosition) : "null")} | Δpos={FormatVector(boltPosDelta)}");
		sb.Append(
			$"  cameraDist={cameraDistance:F1}м nearDetail={nearCamera} | firing={m_FireController != null && m_FireController.IsFiringCommandActive} | boltOwnsShell={boltOwnsShell}");
		if (m_LogWeaponShotPose)
			LogWeaponShot(sb.ToString());

		if (m_LogArmRecoil)
			LogArmRecoilShot();
	}

	private void LogArmRecoilShot()
	{
		if (m_ArmRecoil == null)
		{
			Debug.Log(
				$"{c_ArmRecoilTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | UnitWeaponArmRecoil нет",
				this);
			return;
		}

		WeaponArmRecoilState state = m_ArmRecoil.CurrentState;
		Debug.Log(
			$"{c_ArmRecoilTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | " +
			$"t={m_ShotLogTime:F3} | applied={m_ArmRecoil.AppliedThisFrame} quality={m_ArmRecoil.Quality} | " +
			$"impulse={m_ArmRecoil.CurrentImpulse:F3}/{m_ArmRecoil.TargetImpulse:F3} cap=2.5 " +
			$"shoulderImp={m_ArmRecoil.ShoulderImpulse:F3} | " +
			$"handKick={m_ArmRecoil.LastCarryMeters:F4}м elbowMove={m_ArmRecoil.LastElbowMoveMeters:F4}м " +
			$"shoulderMove={m_ArmRecoil.LastShoulderMoveMeters:F4}м pole={m_ArmRecoil.LastPoleMode} | " +
			$"onPlane={m_ArmRecoil.LastRecoilOnPlane:F2} armBarrel={m_ArmRecoil.LastArmBarrelAngleDegrees:F1}° | " +
			$"elbowBack={m_ArmRecoil.LastElbowBackMeters:F4}м elbowSide={m_ArmRecoil.LastElbowSideMeters:F4}м elbowUp={m_ArmRecoil.LastElbowUpMeters:F4}м | " +
			$"solveErr={m_ArmRecoil.LastSolveHandErrorMeters:F4}м restoreErr={m_ArmRecoil.LastRestoreHandErrorMeters:F4}м " +
			$"rotErr={m_ArmRecoil.LastHandRotationErrorDegrees:F2}° | " +
			$"sh01={state.shoulderAmount:F2} el01={state.elbowAmount:F2} active={state.isActive}",
			this);
	}

	private float ResolveVisualKickScale()
	{
		WeaponDefinition definition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		return definition != null ? definition.VisualRecoilKickScale : 1f;
	}

	private float ResolveVisualDecayTau()
	{
		if (m_WeaponRecoil == null)
			return 0f;

		float tau = Mathf.Max(0.01f, m_WeaponRecoil.ShotSmoothTime);
		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			tau *= Mathf.Max(1f, m_WeaponRecoil.DecayWhileFiringMultiplier);
		return tau;
	}

	private void LogWeaponShot(string _message)
	{
		if (m_LogWeaponShotPose)
			Debug.Log(_message, this);
	}

	private static string FormatVector(Vector3 _value)
	{
		return $"({_value.x:F4}, {_value.y:F4}, {_value.z:F4})";
	}

	private static string FormatEuler(Quaternion _rotation)
	{
		Vector3 euler = _rotation.eulerAngles;
		return $"({euler.x:F2}, {euler.y:F2}, {euler.z:F2})";
	}

	private IEnumerator CoWaitKickSettled(float _timeoutSeconds, bool snapIfTimeout)
	{
		float t = 0f;
		float stable = 0f;
		while (t < _timeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;

			yield return m_EndOfFrame;
			if (IsKickSettled(CaptureSample()))
			{
				stable += Time.deltaTime;
				if (stable >= m_RecoverStableSeconds)
					yield break;
			}
			else
				stable = 0f;

			t += Time.deltaTime;
		}

		if (snapIfTimeout && !IsKickSettled(CaptureSample()))
			SnapRecoilForNextCell();
	}

	private void SnapRecoilForNextCell()
	{
		m_RecoilController?.ResetRecoilPenalty();
		m_WeaponRecoil?.ResetVisualKick();
	}

	private BarrelSample CaptureSample()
	{
		WeaponVisualRecoilState kick = m_WeaponRecoil != null
			? m_WeaponRecoil.CurrentState
			: default;
		float visPitch = 0f;
		float visYaw = 0f;
		float visErr = 0f;
		if (m_Aiming != null)
			m_Aiming.MeasureVisualBarrel(out visPitch, out visYaw, out visErr);

		return new BarrelSample
		{
			AimResidual = m_Aiming != null ? m_Aiming.ResidualPitchDegrees : 0f,
			AimYaw = m_Aiming != null ? m_Aiming.ResidualYawDegrees : 0f,
			VisualBarrelPitch = visPitch,
			VisualError = visErr,
			VisualYawErr = visYaw,
			Punch = kick.punchPitch,
			Climb = kick.climbPitch,
			Penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f,
			WalkComp = m_Aiming != null ? m_Aiming.WalkPitchCompensationDegrees : 0f,
			AimQuality = m_Aiming != null ? m_Aiming.AimQuality01 : 0f,
			KickActive = kick.isActive
		};
	}

	private static string FormatSample(BarrelSample _s)
	{
		return $"punch={_s.Punch:F2} climb={_s.Climb:F2} penalty={_s.Penalty:F2} " +
		       $"aimRes={_s.AimResidual:F2}° visErr={_s.VisualError:F2}° " +
		       $"visPitch={_s.VisualBarrelPitch:F1}° yaw={_s.AimYaw:F1}° kick={(_s.KickActive ? 1 : 0)}";
	}

	private static float OverlayDelta(BarrelSample _s) =>
		Mathf.Abs(_s.VisualError - _s.AimResidual);

	private static float PlateauDelta(BarrelSample _final, BarrelSample _baseline) =>
		Mathf.Abs(_final.VisualError - _baseline.VisualError);

	private bool IsKickSettled(BarrelSample _s)
	{
		return Mathf.Abs(_s.Punch) <= m_KickSettledDegrees
		       && Mathf.Abs(_s.Climb) <= m_KickSettledDegrees
		       && _s.Penalty <= m_KickSettledDegrees
		       && !_s.KickActive;
	}

	private static bool IsHipFireHold(WeaponPoseState _pose) => _pose.IsHipFireHold();

	private static bool ShouldWaitAimGeometry(WeaponPoseMode _mode) =>
		_mode == WeaponPoseMode.PointAim || _mode == WeaponPoseMode.Aiming;

	private void CaptureAmmoDefinition()
	{
		WeaponRuntimeState state = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (state == null)
			return;

		if (state.ChamberedAmmoDefinition != null)
			m_CapturedAmmo = state.ChamberedAmmoDefinition;
		else if (m_WeaponRuntime.CurrentMagazine != null)
			m_CapturedAmmo = m_WeaponRuntime.CurrentMagazine.LoadedAmmoDefinition;
	}

	private void RefillMagazine()
	{
		WeaponRuntimeState state = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (state == null)
			return;

		if (m_CapturedAmmo == null)
			CaptureAmmoDefinition();

		MagazineRuntimeState mag = m_WeaponRuntime.CurrentMagazine;
		if (mag != null && mag.Definition != null && m_CapturedAmmo != null)
			mag.Configure(mag.Definition, m_CapturedAmmo, mag.Definition.Capacity);

		if (!state.HasRoundInChamber)
			state.TryChamberRoundFromMagazine();
	}

	private void RecordResult(CellResult _result)
	{
		m_Results.Add(_result);
	}

	private static WeaponPoseState ResolveExpectedPose(
		WeaponPoseMode _mode,
		LocomotionStance _stance,
		bool _walk)
	{
		if (_mode == WeaponPoseMode.HipFire)
		{
			if (!_walk)
				return WeaponPoseState.HipFire;
			return _stance == LocomotionStance.Crouch
				? WeaponPoseState.HipFireCrouchWalk
				: WeaponPoseState.HipFireWalk;
		}

		switch (_mode)
		{
			case WeaponPoseMode.HighReady:
				return WeaponPoseState.HighReady;
			case WeaponPoseMode.PreAim:
				return WeaponPoseState.PreAim;
			case WeaponPoseMode.PointAim:
				return WeaponPoseState.PointAim;
			case WeaponPoseMode.Aiming:
				return WeaponPoseState.Aiming;
			default:
				return WeaponPoseState.LowReady;
		}
	}

	private static string FormatCell(
		LocomotionStance _stance,
		bool _walk,
		WeaponPoseMode _mode,
		WeaponPoseState _expected,
		int _shots)
	{
		string loc = _walk ? "Walk" : "Idle";
		if (_shots <= 0)
			return $"{_stance}/{loc}/{_mode}({_expected})";
		return $"{_stance}/{loc}/{_mode}({_expected}) x{_shots}";
	}

	private IEnumerator CoEnsureWalking()
	{
		if (m_ClickToMove == null)
		{
			LogRecoilSweepWarning("[RecoilSweep] SKIP walk: no UnitClickToMove.");
			yield break;
		}

		m_ClickToMove.ForceWalkMoveMode();
		if (!ShouldRefreshWalkDestination())
			yield break;

		Vector3 dest = transform.position + Flatten(transform.forward) * m_WalkDistanceMeters;
		if (!m_ClickToMove.IssueNavOrder(dest, UnitClickToMove.MoveTier.Walk))
		{
			LogRecoilSweepWarning("[RecoilSweep] IssueNavOrder failed.");
			yield break;
		}

		yield return CoWaitWalkStarted();
		if (m_WalkSpeedSettleSeconds > 0f)
			yield return new WaitForSeconds(m_WalkSpeedSettleSeconds);
	}

	private void RefreshWalkIfNeeded()
	{
		if (!m_CurrentWalk || m_ClickToMove == null)
			return;
		if (!ShouldRefreshWalkDestination())
			return;

		m_ClickToMove.ForceWalkMoveMode();
		Vector3 dest = transform.position + Flatten(transform.forward) * m_WalkDistanceMeters;
		m_ClickToMove.IssueNavOrder(dest, UnitClickToMove.MoveTier.Walk);
	}

	private IEnumerator CoEnsureIdle()
	{
		if (m_ClickToMove != null)
			m_ClickToMove.HardStop();

		float t = 0f;
		while (t < m_WalkStartTimeoutSeconds)
		{
			bool intent = m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
			bool moving = IsAnimatorMoving();
			if (!intent && !moving)
				yield break;
			t += Time.deltaTime;
			yield return null;
		}

		LogRecoilSweepWarning("[RecoilSweep] idle settle timeout.");
	}

	private IEnumerator CoWaitPose(WeaponPoseState _expected)
	{
		float t = 0f;
		while (t < m_PoseSettleTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;

			if (IsPoseHeld(_expected))
				yield break;

			t += Time.deltaTime;
			yield return null;
		}

		WeaponPoseState have = GetHeldPose();
		LogRecoilSweepWarning(
			$"[RecoilSweep] pose settle timeout want={_expected} have={have} " +
			$"blend={IsPoseBlending()}");
	}

	private IEnumerator CoWaitScoredAimReady(WeaponPoseState _expected)
	{
		float t = 0f;
		float stable = 0f;
		bool loggedWait = false;
		while (t < m_AimGeometryTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;

			if (IsScoredAimGeometryReady(_expected))
			{
				stable += Time.deltaTime;
				if (stable >= m_AimGeometryStableSeconds)
				{
					if (loggedWait)
					{
						LogRecoilSweep(
							$"[RecoilSweep] aim-ready recovered pose={_expected} " +
							$"yaw={GetAbsResidualYaw():F1}°");
					}

					yield break;
				}
			}
			else
			{
				stable = 0f;
				if (!loggedWait)
				{
					loggedWait = true;
					LogRecoilSweep(
						$"[RecoilSweep] wait aim-ready want={_expected} have={GetHeldPose()} " +
						$"yaw={GetSignedResidualYaw():F1}° blend={IsPoseBlending()} " +
						$"turnRestore={m_ReadyHands != null && m_ReadyHands.HasPendingReadyRestore}");
				}
			}

			t += Time.deltaTime;
			yield return null;
		}

		LogRecoilSweepWarning(
			$"[RecoilSweep] aim-ready timeout want={_expected} have={GetHeldPose()} " +
			$"yaw={GetSignedResidualYaw():F1}°");
	}

	private IEnumerator CoWaitStance(LocomotionStance _stance)
	{
		if (m_Stance == null)
			yield break;

		float t = 0f;
		while (t < m_StanceWaitTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;
			if (m_Stance.CurrentStance == _stance)
				yield break;
			t += Time.deltaTime;
			yield return null;
		}

		LogRecoilSweepWarning(
			$"[RecoilSweep] stance wait timeout want={_stance} have={m_Stance.CurrentStance}");
	}

	private IEnumerator CoWaitStanceTransitionClear()
	{
		float t = 0f;
		while (t < m_StanceWaitTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;
			bool busy = m_BusyState != null &&
			            m_BusyState.IsBusy &&
			            (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;
			if (!busy)
				yield break;
			t += Time.deltaTime;
			yield return null;
		}
	}

	private IEnumerator CoParkToLowReady()
	{
		if (m_ReadyHands == null)
			yield break;

		m_FireController?.StopFiring();
		m_ReadyHands.SetPoseModeWanted(WeaponPoseMode.LowReady, false);
		yield return CoWaitPose(WeaponPoseState.LowReady);
	}

	private IEnumerator CoWaitReloadClear()
	{
		m_FireController?.StopFiring();
		float t = 0f;
		while (t < m_ReloadWaitTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;
			if (!IsCombatBusyNow())
				yield break;
			t += Time.deltaTime;
			yield return null;
		}

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
		{
			LogRecoilSweepWarning("[RecoilSweep] reload still busy, StopReload.");
			m_ReloadController.StopReload();
		}
	}

	private bool IsCombatBusyNow()
	{
		return m_ReloadController != null && m_ReloadController.IsReloadBusy;
	}

	private bool ShouldRefreshWalkDestination()
	{
		if (m_ClickToMove == null || !m_ClickToMove.HasMoveIntent)
			return true;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh)
			return !IsAnimatorMoving();
		if (agent.pathPending)
			return false;
		if (!agent.hasPath)
			return true;
		if (float.IsPositiveInfinity(agent.remainingDistance))
			return true;
		return agent.remainingDistance < m_WalkRefreshRemainingMeters;
	}

	private bool IsPoseBlending() =>
		m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;

	private WeaponPoseState GetHeldPose() =>
		m_ReadyHands != null ? m_ReadyHands.EffectivePoseState : WeaponPoseState.NotReady;

	private bool IsPoseHeld(WeaponPoseState _expected) =>
		!IsPoseBlending() && GetHeldPose() == _expected;

	private float GetSignedResidualYaw() =>
		m_Aiming != null ? m_Aiming.ResidualYawDegrees : 0f;

	private float GetAbsResidualYaw() => Mathf.Abs(GetSignedResidualYaw());

	private bool IsScoredAimGeometryReady(WeaponPoseState _expected)
	{
		if (!IsPoseHeld(_expected))
			return false;
		if (m_ReadyHands != null && m_ReadyHands.HasPendingReadyRestore)
			return false;
		if (GetAbsResidualYaw() > m_MaxSampleYawDegrees)
			return false;
		return true;
	}

	private IEnumerator CoWaitWalkStarted()
	{
		float t = 0f;
		while (t < m_WalkStartTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;
			if (m_ClickToMove != null && m_ClickToMove.HasMoveIntent)
				yield break;
			t += Time.deltaTime;
			yield return null;
		}

		LogRecoilSweepWarning("[RecoilSweep] walk start timeout.");
	}

	private bool IsAnimatorMoving()
	{
		return m_Animator != null && m_Animator.GetFloat(s_NavSpeed) >= 0.055f;
	}

	private void CaptureRestoreState()
	{
		m_RestoreMode = m_ReadyHands != null ? m_ReadyHands.WantedMode : WeaponPoseMode.LowReady;
		m_RestorePeaceful = m_ReadyHands != null && m_ReadyHands.IsPeacefulNotReady;
		m_RestorePeacefulPose = m_ReadyHands != null
			? m_ReadyHands.PeacefulCarryPose
			: WeaponPoseState.NotReady;
		m_RestoreStance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
		m_HadReadyKeyboard = m_ReadyHands == null || m_ReadyHands.IsKeyboardInputEnabled;
		m_HadStanceKeyboard = true;
		m_HadDisciplineEnabled = m_FireDiscipline != null && m_FireDiscipline.enabled;
		m_HadAutoFireEnabled = m_AutoFire != null && m_AutoFire.enabled;
		m_HadTryReloadWhenOutOfAmmo = m_FireController != null && m_FireController.TryReloadWhenOutOfAmmo;
		m_HadResetRecoilOnStopFiring = m_FireController == null || m_FireController.ResetRecoilOnStopFiring;
		m_HadIgnoreCameraCull = m_WeaponRecoil != null && m_WeaponRecoil.IgnoreCameraDistanceCull;
		m_HadForceArmRecoilFullQuality = m_ArmRecoil != null && m_ArmRecoil.ForceFullQuality;
		m_HadLogWeaponSpin = m_Aiming != null && m_Aiming.LogWeaponSpin;
		m_HasRestoreFireMode = false;
		if (m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null)
		{
			m_RestoreFireMode = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			m_HasRestoreFireMode = true;
		}

		m_HasCapturedRestore = true;
	}

	private void ApplySweepRecoilHooks(bool _enable)
	{
		if (m_FireController != null)
			m_FireController.ResetRecoilOnStopFiring = !_enable && m_HadResetRecoilOnStopFiring;

		if (m_WeaponRecoil != null)
			m_WeaponRecoil.IgnoreCameraDistanceCull = _enable || m_HadIgnoreCameraCull;

		if (m_ArmRecoil != null)
			m_ArmRecoil.ForceFullQuality = _enable || m_HadForceArmRecoilFullQuality;

		if (m_MuteWeaponSpinDuringSweep && m_Aiming != null)
			m_Aiming.LogWeaponSpin = !_enable && m_HadLogWeaponSpin;

		if (!_enable)
			return;

		if (m_FireController != null)
			m_FireController.ResetRecoilOnStopFiring = false;

		if (m_PreferFullAuto)
			TrySetFullAuto();
	}

	private void TrySetFullAuto()
	{
		if (m_WeaponRuntime == null)
			return;

		WeaponDefinition definition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (definition == null)
			return;

		WeaponFireMode[] modes = definition.AvailableFireModes;
		if (modes == null)
			return;

		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] != WeaponFireMode.FullAuto)
				continue;

			m_WeaponRuntime.SetSelectedFireMode(WeaponFireMode.FullAuto);
			return;
		}
	}

	private void LockGameplayInput(bool _lock)
	{
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(!_lock && m_HadReadyKeyboard);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(!_lock && m_HadStanceKeyboard);

		if (!m_SuppressCombatFire)
			return;

		if (_lock)
		{
			if (m_FireDiscipline != null)
				m_FireDiscipline.enabled = false;
			if (m_AutoFire != null)
				m_AutoFire.enabled = false;
			if (m_FireController != null)
			{
				m_FireController.TryReloadWhenOutOfAmmo = false;
				m_FireController.StopFiring();
			}

			m_RtsMember?.StopFiring();
			return;
		}

		if (m_FireController != null)
			m_FireController.TryReloadWhenOutOfAmmo = m_HadTryReloadWhenOutOfAmmo;
		if (m_FireDiscipline != null)
			m_FireDiscipline.enabled = m_HadDisciplineEnabled;
		if (m_AutoFire != null)
			m_AutoFire.enabled = m_HadAutoFireEnabled;
	}

	private void RestoreUnitState()
	{
		if (!m_HasCapturedRestore)
			return;

		if (m_ClickToMove != null)
			m_ClickToMove.HardStop();

		if (m_ReadyHands != null)
		{
			if (m_RestorePeaceful)
				m_ReadyHands.SetPeacefulCarryPose(m_RestorePeacefulPose);
			else
				m_ReadyHands.SetPoseModeWanted(m_RestoreMode, false);
		}

		if (m_Stance != null)
		{
			if (m_RestoreStance == LocomotionStance.Crouch)
				m_Stance.RequestCrouch();
			else
				m_Stance.RequestStanding();
		}

		if (m_HasRestoreFireMode && m_WeaponRuntime != null)
			m_WeaponRuntime.SetSelectedFireMode(m_RestoreFireMode);

		ApplySweepRecoilHooks(false);
		if (m_FireController != null)
		{
			m_FireController.ResetRecoilOnStopFiring = m_HadResetRecoilOnStopFiring;
			m_FireController.StopFiring();
		}

		SnapRecoilForNextCell();
		LockGameplayInput(false);
		m_HasCapturedRestore = false;
	}

	private void LogSummary()
	{
		var sb = new StringBuilder(2048);
		sb.AppendLine($"[RecoilSweep] SUMMARY unit={name} cancelled={m_CancelRequested}");
		int pass = 0;
		int fail = 0;
		int skip = 0;
		for (int i = 0; i < m_Results.Count; i++)
		{
			CellResult r = m_Results[i];
			if (r.Verdict == "SKIP-FIRE")
				skip++;
			else if (r.Pass)
				pass++;
			else
				fail++;

			if (r.WantedShots <= 0)
			{
				sb.AppendLine($"  {r.CellName}: got={r.GotPose} {r.Verdict}");
				continue;
			}

			sb.AppendLine(
				$"  {r.CellName}: n={r.FiredShots}/{r.WantedShots} peakPunch={r.PeakPunch:F2} " +
				$"peakClimb={r.PeakClimb:F2} recover={r.RecoverSeconds:F2}s " +
				$"aimRes={r.AimResidual:F2}° visErr={r.VisualError:F2}° " +
				$"overlayΔ={r.OverlayDelta:F2}° plateauΔ={r.PlateauDelta:F2}° " +
				$"walkComp={r.WalkComp:F1}° {r.Verdict}");
		}

		sb.Append($"  totals: PASS={pass} FAIL={fail} SKIP-FIRE={skip} cells={m_Results.Count}");
		LogRecoilSweep(sb.ToString());
	}

	private void LogHeadSummary()
	{
		if (!m_LogHeadSweep)
			return;

		var sb = new StringBuilder(2048);
		sb.AppendLine($"[HeadSweep] SUMMARY unit={name} cancelled={m_CancelRequested}");
		int withHead = 0;
		for (int i = 0; i < m_Results.Count; i++)
		{
			CellResult r = m_Results[i];
			if (!r.HasHeadSample)
			{
				sb.AppendLine($"  {r.CellName}: no head sample");
				continue;
			}

			withHead++;
			sb.AppendLine(
				$"  {r.CellName}: headPitch={r.HeadPitchDegrees:F1}° " +
				$"headYaw={r.HeadYawDegrees:F1}° headRoll={r.HeadRollDegrees:F1}°");
		}

		sb.Append($"  totals: cells={m_Results.Count} headSamples={withHead}");
		LogHeadSweep(sb.ToString());
	}

	private void LogRecoilSweep(string _message)
	{
		if (m_LogRecoilSweep)
			Debug.Log(_message, this);
	}

	private void LogRecoilSweepWarning(string _message)
	{
		if (m_LogRecoilSweep)
			Debug.LogWarning(_message, this);
	}

	private void LogHeadSweep(string _message)
	{
		if (m_LogHeadSweep)
			Debug.Log(_message, this);
	}

	private void LogHeadLine(string _cell, string _phase)
	{
		HeadSample s = m_LastHeadSample;
		if (!s.Valid)
		{
			LogHeadSweep($"[HeadSweep] HEAD {_phase} {_cell} sample=failed");
			return;
		}

		LogHeadSweep(
			$"[HeadSweep] HEAD {_phase} {_cell} " +
			$"nav={s.NavSpeed:F2} rootYaw={s.RootYawDegrees:F1}° " +
			$"headPitch={s.HeadPitchDegrees:F1}° headYaw={s.HeadYawDegrees:F1}° headRoll={s.HeadRollDegrees:F1}° " +
			$"neckPitch={s.NeckPitchDegrees:F1}° neckYaw={s.NeckYawDegrees:F1}° neckRoll={s.NeckRollDegrees:F1}°");
	}

	/// <summary>
	/// Усредняет за окно реальное положение головы/шеи относительно yaw-оси корня юнита.
	/// Позволяет увидеть, что анимация позы делает с головой без влияния поворота тела.
	/// </summary>
	private IEnumerator CoSampleHeadPose(float _seconds)
	{
		m_LastHeadSample = default;

		Transform head = ResolveHeadBone();
		if (head == null)
		{
			LogHeadSweep("[HeadSweep] HEAD sample failed: head bone missing.");
			yield break;
		}

		Transform neck = ResolveNeckBone();
		float duration = Mathf.Max(0.05f, _seconds);
		Quaternion avgHeadLocal = Quaternion.identity;
		Quaternion avgNeckLocal = Quaternion.identity;
		bool hasNeck = neck != null;
		float sinSum = 0f;
		float cosSum = 0f;
		int count = 0;
		float t = 0f;

		while (t < duration)
		{
			Quaternion rootYawOnly = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
			Quaternion inv = Quaternion.Inverse(rootYawOnly);
			float w = 1f / (count + 1);
			avgHeadLocal = Quaternion.Slerp(avgHeadLocal, inv * head.rotation, w);
			if (hasNeck)
				avgNeckLocal = Quaternion.Slerp(avgNeckLocal, inv * neck.rotation, w);

			float rad = transform.eulerAngles.y * Mathf.Deg2Rad;
			sinSum += Mathf.Sin(rad);
			cosSum += Mathf.Cos(rad);
			count++;
			t += Time.deltaTime;
			yield return null;
		}

		if (count <= 0)
			yield break;

		float rootYawAvgDegrees = Mathf.Atan2(sinSum, cosSum) * Mathf.Rad2Deg;
		DecomposeHeadAngles(avgHeadLocal, out float headPitch, out float headYaw, out float headRoll);
		DecomposeHeadAngles(
			hasNeck ? avgNeckLocal : Quaternion.identity,
			out float neckPitch,
			out float neckYaw,
			out float neckRoll);

		m_LastHeadSample = new HeadSample
		{
			Valid = true,
			// Знаки: pitch + = нос вверх, yaw + = вправо от направления тела, roll + = наклон вправо.
			HeadPitchDegrees = headPitch,
			HeadYawDegrees = headYaw,
			HeadRollDegrees = headRoll,
			NeckPitchDegrees = neckPitch,
			NeckYawDegrees = neckYaw,
			NeckRollDegrees = neckRoll,
			NavSpeed = m_Animator != null ? m_Animator.GetFloat(s_NavSpeed) : 0f,
			RootYawDegrees = rootYawAvgDegrees
		};
	}

	/// <summary>
	/// Раскладка поворота кости в системе тела юнита без euler-особенностей.
	/// Pitch из forward.y (над горизонтом), yaw из горизонтальной проекции forward
	/// (atan2 покрывает все квадранты и не «прыгает» через ±180), roll из up-вектора.
	/// </summary>
	private static void DecomposeHeadAngles(
		Quaternion _bodyLocal,
		out float _pitchDegrees,
		out float _yawDegrees,
		out float _rollDegrees)
	{
		Vector3 fwd = _bodyLocal * Vector3.forward;
		_pitchDegrees = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;

		Vector3 flatFwd = fwd;
		flatFwd.y = 0f;
		_yawDegrees = 0f;
		if (flatFwd.sqrMagnitude > 1e-8f)
		{
			flatFwd.Normalize();
			_yawDegrees = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
		}

		_rollDegrees = 0f;
		if (fwd.sqrMagnitude < 1e-8f)
			return;

		Vector3 fwdN = fwd.normalized;
		Vector3 curUp = Vector3.ProjectOnPlane(_bodyLocal * Vector3.up, fwdN);
		Vector3 refUp = Vector3.ProjectOnPlane(
			Quaternion.AngleAxis(-_pitchDegrees, Vector3.right) * Vector3.up,
			fwdN);
		if (curUp.sqrMagnitude > 1e-8f && refUp.sqrMagnitude > 1e-8f)
			_rollDegrees = Vector3.SignedAngle(curUp.normalized, refUp.normalized, fwdN);
	}

	private Transform ResolveHeadBone()
	{
		return m_Animator != null ? m_Animator.GetBoneTransform(HumanBodyBones.Head) : null;
	}

	private Transform ResolveNeckBone()
	{
		return m_Animator != null ? m_Animator.GetBoneTransform(HumanBodyBones.Neck) : null;
	}

	private static Vector3 Flatten(Vector3 _v)
	{
		_v.y = 0f;
		if (_v.sqrMagnitude < 1e-6f)
			return Vector3.zero;
		return _v.normalized;
	}

	private static bool WasKeyPressedThisFrame(Key _key)
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;

			KeyControl key = kb[_key];
			if (key != null && key.wasPressedThisFrame)
				return true;
		}

		return false;
	}
	#endregion
}
