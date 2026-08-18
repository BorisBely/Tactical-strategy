using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Диагностика трёх уровней spine lean по клавише K.
/// Aiming, цель закреплена (без расхода патронов). Idle + Walk, стоя и в присяде.
/// Фильтр консоли: SpineLeanDiag. Повторное K отменяет.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitSpineLeanCalibrationTest : MonoBehaviour
{
	#region Types
	private struct BoneSample
	{
		public bool Valid;
		public Vector3 WorldPos;
		public float LocalX;
		public float PitchDegrees;
		public float YawDegrees;
		public float RollDegrees;
		public Quaternion BodyLocal;
	}

	private struct PoseSnapshot
	{
		public bool Valid;
		public Vector3 RootPos;
		public float RootYawDegrees;
		public int LeanLevel;
		public int LeanSide;
		public float TargetDegrees;
		public float LeanDegrees;
		public bool Blocked;
		public bool Settled;
		public WeaponPoseState Pose;
		public bool HasTarget;
		public BoneSample Spine01;
		public BoneSample Spine02;
		public bool HasBarrel;
		public Vector3 BarrelWorldPos;
		public Vector3 BarrelForward;
		public float BarrelLocalX;
		public float BarrelYawDegrees;
		public float BarrelPitchDegrees;
		public float AimErrorDegrees;
	}

	private struct SideResult
	{
		public string CellName;
		public bool Pass;
		public string Verdict;
	}

	private struct LevelActual
	{
		public LocomotionStance Stance;
		public bool Walking;
		public int Side;
		public int Level;
		public float ActualAbs;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitSpineLean m_Lean;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private RtsUnitMember m_RtsMember;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDiscipline;
	[SerializeField] private UnitWeaponAutoFireWhenAimed m_AutoFire;
	[SerializeField] private UnitWeaponFireController m_FireController;

	[Header("Input")]
	[SerializeField] private bool m_EnableKeyboardStart = false;
	[SerializeField] private Key m_StartTestKey = Key.K;
	[SerializeField] private bool m_RequireSelected = true;

	[Header("Timing")]
	[SerializeField, Min(0.05f)] private float m_PoseSettleTimeoutSeconds = 4f;
	[SerializeField, Min(0.05f)] private float m_LeanSettleTimeoutSeconds = 4f;
	[SerializeField, Min(0f)] private float m_HoldAfterSettleSeconds = 0.3f;
	[SerializeField, Min(0.05f)] private float m_SampleWindowSeconds = 0.25f;
	[SerializeField, Min(0.1f)] private float m_StanceWaitTimeoutSeconds = 5f;

	[Header("Pass thresholds")]
	[SerializeField, Min(0.1f)] private float m_MaxAngleErrorDegrees = 1f;
	[SerializeField, Min(0.01f)] private float m_MaxRootShiftMeters = 0.02f;
	[SerializeField, Min(0.5f)] private float m_MaxBarrelYawDeltaDegrees = 3f;
	[SerializeField, Range(0.2f, 1f)] private float m_MinBarrelForwardDot = 0.55f;
	[SerializeField, Min(0.1f)] private float m_MaxSymmetryErrorDegrees = 1f;

	[Header("Walk pass")]
	[SerializeField] private bool m_IncludeWalkPass = true;
	[SerializeField, Min(1f)] private float m_WalkDistanceMeters = 22f;
	[SerializeField, Min(0.5f)] private float m_WalkStartTimeoutSeconds = 3f;
	[SerializeField, Min(0.05f)] private float m_WalkSpeedSettleSeconds = 0.4f;
	[SerializeField, Min(2f)] private float m_WalkRefreshRemainingMeters = 4f;
	[SerializeField, Min(0.1f)] private float m_MaxWalkBoneAngleErrorDegrees = 5f;
	[SerializeField, Min(0.5f)] private float m_MaxWalkBarrelYawDeltaDegrees = 8f;
	[SerializeField, Range(0.2f, 1f)] private float m_MinWalkBarrelForwardDot = 0.45f;

	[Header("Target / ammo")]
	[SerializeField] private bool m_PinAimTarget = true;
	[SerializeField, Min(6f)] private float m_DummyTargetDistanceMeters = 28f;
	[SerializeField, Min(8f)] private float m_DummyKeepAheadMeters = 14f;
	[SerializeField, Min(0.05f)] private float m_AimSettleSeconds = 0.45f;
	[SerializeField, Min(0.5f)] private float m_MaxIdleAimErrorDegrees = 8f;
	[SerializeField, Min(0.5f)] private float m_MaxWalkAimErrorDegrees = 18f;
	#endregion

	#region Private Fields
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);

	private Coroutine m_TestRoutine;
	private bool m_CancelRequested;
	private bool m_CurrentWalk;
	private bool m_HasCapturedRestore;
	private bool m_HadReadyKeyboard;
	private bool m_HadStanceKeyboard;
	private bool m_HadTargetSelectorEnabled;
	private bool m_HadDisciplineEnabled;
	private bool m_HadAutoFireEnabled;
	private bool m_HadTryReloadWhenOutOfAmmo;
	private bool m_HadFireControllerEnabled;
	private bool m_HadLeanDiagnostics;
	private bool m_SpawnedDummyTarget;
	private GameObject m_DummyTarget;
	private bool m_RestorePeaceful;
	private WeaponPoseMode m_RestoreMode;
	private WeaponPoseState m_RestorePeacefulPose;
	private LocomotionStance m_RestoreStance;
	private readonly List<SideResult> m_Results = new List<SideResult>(128);
	private readonly List<LevelActual> m_LevelActuals = new List<LevelActual>(48);
	private PoseSnapshot m_LastSnapshot;
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
			Debug.Log("[SpineLeanDiag] CANCEL requested.", this);
			return;
		}

		StartDiagnostics();
	}
	#endregion

	#region Public Methods
	public void StartDiagnostics()
	{
		if (m_TestRoutine != null)
			return;

		ResolveReferences();
		m_CancelRequested = false;
		m_TestRoutine = StartCoroutine(CoRunDiagnostics());
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Lean == null)
			m_Lean = GetComponent<UnitSpineLean>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
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
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_FireDiscipline == null)
			m_FireDiscipline = GetComponent<UnitWeaponFireDisciplineController>();
		if (m_AutoFire == null)
			m_AutoFire = GetComponent<UnitWeaponAutoFireWhenAimed>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
	}

	private IEnumerator CoRunDiagnostics()
	{
		m_Results.Clear();
		m_LevelActuals.Clear();
		CaptureRestoreState();
		LockGameplayInput(true);
		MuteLiveLeanLogs(true);
		m_ClickToMove?.HardStop();
		m_Lean?.SetLeanLevel(0, 0);
		PinAimTarget();

		string targetName = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null
			? m_TargetSelector.SelectedTarget.name
			: "none";
		Debug.Log(
			$"[SpineLeanDiag] START unit={name} pose=Aiming target={targetName} fire=off " +
			$"levels=0..3 locomotion=idle{(m_IncludeWalkPass ? "+walk" : "")} filter=SpineLeanDiag",
			this);

		if (m_Lean == null)
		{
			Debug.LogError("[SpineLeanDiag] ABORT: UnitSpineLean missing.", this);
			FinishRun();
			yield break;
		}

		if (m_ReadyHands == null || !m_ReadyHands.IsWeaponEquipped())
		{
			Debug.LogError("[SpineLeanDiag] ABORT: equipped weapon required.", this);
			FinishRun();
			yield break;
		}

		if (!TryGetBarrel(out _))
		{
			Debug.LogError("[SpineLeanDiag] ABORT: barrel transform not found.", this);
			FinishRun();
			yield break;
		}

		yield return CoRunStanceMatrix(LocomotionStance.Standing, _includeSkipJumps: true, _walking: false);
		if (!m_CancelRequested)
			yield return CoRunStanceMatrix(LocomotionStance.Crouch, _includeSkipJumps: false, _walking: false);

		if (m_IncludeWalkPass && !m_CancelRequested)
		{
			yield return CoRunStanceMatrix(LocomotionStance.Standing, _includeSkipJumps: false, _walking: true);
			if (!m_CancelRequested)
				yield return CoRunStanceMatrix(LocomotionStance.Crouch, _includeSkipJumps: false, _walking: true);
			yield return CoEnsureIdle();
		}

		if (!m_CancelRequested)
			EvaluateSymmetry();

		FinishRun();
	}

	private void FinishRun()
	{
		RestoreUnitState();
		LogSummary();
		Debug.Log(
			m_CancelRequested
				? $"[SpineLeanDiag] CANCELLED unit={name}"
				: $"[SpineLeanDiag] DONE unit={name}",
			this);
		m_TestRoutine = null;
		m_CancelRequested = false;
		m_CurrentWalk = false;
	}

	private IEnumerator CoRunStanceMatrix(LocomotionStance _stance, bool _includeSkipJumps, bool _walking)
	{
		m_CurrentWalk = _walking;
		string stanceName = FormatStanceName(_stance, _walking);
		Debug.Log($"[SpineLeanDiag] CELL {stanceName}/Aiming BEGIN", this);

		m_Lean.SetLeanLevel(0, 0);
		m_ClickToMove?.HardStop();
		KeepDummyAhead();

		if (m_Stance != null)
		{
			if (_stance == LocomotionStance.Crouch)
				m_Stance.RequestCrouch();
			else
				m_Stance.RequestStanding();
		}

		yield return CoWaitStance(_stance);
		yield return CoWaitStanceTransitionClear();
		if (m_CancelRequested)
			yield break;

		m_ReadyHands.SetPoseModeWanted(WeaponPoseMode.Aiming, true);
		yield return CoWaitPose(WeaponPoseState.Aiming);
		if (m_CancelRequested)
			yield break;

		if (m_AimSettleSeconds > 0f)
			yield return new WaitForSeconds(m_AimSettleSeconds);
		if (m_CancelRequested)
			yield break;

		if (_walking)
		{
			yield return CoEnsureWalking();
			if (m_CancelRequested)
				yield break;
			if (!IsWalkingNow())
			{
				string fail = "нет шага (IssueNavOrder/walk start)";
				Record($"{stanceName}/Neutral", false, fail);
				Debug.LogWarning($"[SpineLeanDiag] VERDICT {stanceName}/Neutral FAIL {fail}", this);
				yield break;
			}
		}

		m_Lean.SetLeanLevel(0, 0);
		yield return CoWaitLeanSettled();
		if (m_HoldAfterSettleSeconds > 0f)
			yield return new WaitForSeconds(m_HoldAfterSettleSeconds);
		if (m_CancelRequested)
			yield break;

		if (_walking)
			RefreshWalkIfNeeded();

		yield return CoSampleSnapshot();
		PoseSnapshot neutral = m_LastSnapshot;
		if (!IsAimingStraightSetupOk(neutral, out string setupFail))
		{
			Record($"{stanceName}/Neutral", false, setupFail);
			Debug.LogWarning($"[SpineLeanDiag] VERDICT {stanceName}/Neutral FAIL {setupFail}", this);
			yield break;
		}

		yield return CoEvaluateState($"{stanceName}/N", 0, 0, neutral, _stance, _walking);

		int[] sides = { -1, 1 };
		for (int s = 0; s < sides.Length && !m_CancelRequested; s++)
		{
			for (int level = 1; level <= UnitSpineLean.c_MaxLeanLevel && !m_CancelRequested; level++)
			{
				string cell = FormatCell(stanceName, sides[s], level);
				yield return CoEvaluateState(cell, level, sides[s], neutral, _stance, _walking);
			}

			m_Lean.SetLeanLevel(0, 0);
			yield return CoWaitLeanSettled();
			if (_walking && !m_CancelRequested)
			{
				RefreshWalkIfNeeded();
				yield return CoSampleSnapshot();
				if (m_LastSnapshot.Valid)
					neutral = m_LastSnapshot;
			}
		}

		if (m_CancelRequested)
			yield break;

		for (int s = 0; s < sides.Length && !m_CancelRequested; s++)
		{
			if (_walking)
			{
				m_Lean.SetLeanLevel(0, 0);
				yield return CoWaitLeanSettled();
				RefreshWalkIfNeeded();
				yield return CoSampleSnapshot();
				if (m_LastSnapshot.Valid)
					neutral = m_LastSnapshot;
			}

			int[] chain = { 0, 1, 2, 3, 2, 1, 0 };
			yield return CoRunLevelSequence($"{stanceName} chain", chain, sides[s], neutral, _stance, _walking);
		}

		if (_includeSkipJumps && !m_CancelRequested)
		{
			int[] jumpsFrom = { 0, 0, 1, 3, 3 };
			int[] jumpsTo = { 2, 3, 3, 1, 0 };
			for (int s = 0; s < sides.Length && !m_CancelRequested; s++)
			{
				for (int i = 0; i < jumpsFrom.Length && !m_CancelRequested; i++)
				{
					m_Lean.SetLeanLevel(jumpsFrom[i], jumpsFrom[i] == 0 ? 0 : sides[s]);
					yield return CoWaitLeanSettled();
					if (m_HoldAfterSettleSeconds > 0f)
						yield return new WaitForSeconds(m_HoldAfterSettleSeconds);
					if (m_CancelRequested)
						yield break;

					string cell = $"{stanceName} jump {FormatSide(sides[s])}{jumpsFrom[i]}→{jumpsTo[i]}";
					yield return CoEvaluateState(
						cell,
						jumpsTo[i],
						jumpsTo[i] == 0 ? 0 : sides[s],
						neutral,
						_stance,
						_walking);
				}
			}
		}

		m_Lean.SetLeanLevel(0, 0);
		yield return CoWaitLeanSettled();
		Debug.Log($"[SpineLeanDiag] CELL {stanceName}/Aiming END", this);
	}

	private IEnumerator CoRunLevelSequence(
		string _label,
		int[] _levels,
		int _side,
		PoseSnapshot _neutral,
		LocomotionStance _stance,
		bool _walking)
	{
		for (int i = 0; i < _levels.Length && !m_CancelRequested; i++)
		{
			int level = _levels[i];
			int side = level == 0 ? 0 : _side;
			string cell = i == 0
				? $"{_label} {FormatSide(_side)} start L{level}"
				: $"{_label} {FormatSide(_side)} {_levels[i - 1]}→{level}";
			yield return CoEvaluateState(cell, level, side, _neutral, _stance, _walking);
		}

		m_Lean.SetLeanLevel(0, 0);
		yield return CoWaitLeanSettled();
	}

	private IEnumerator CoEvaluateState(
		string _cell,
		int _level,
		int _side,
		PoseSnapshot _neutral,
		LocomotionStance _stance,
		bool _walking)
	{
		if (_walking)
			RefreshWalkIfNeeded();

		m_Lean.SetLeanLevel(_level, _side);
		yield return CoWaitLeanSettled();
		if (m_HoldAfterSettleSeconds > 0f)
			yield return new WaitForSeconds(m_HoldAfterSettleSeconds);
		if (m_CancelRequested)
			yield break;

		if (_walking)
			RefreshWalkIfNeeded();

		yield return CoSampleSnapshot();
		PoseSnapshot snap = m_LastSnapshot;
		bool crouch = _stance == LocomotionStance.Crouch;
		float targetAbs = m_Lean.GetLeanAngle(_level, crouch);
		float targetSigned = targetAbs * (_level == 0 ? 0 : (_side < 0 ? -1 : 1));
		float actualAbs = Quaternion.Angle(_neutral.Spine02.BodyLocal, snap.Spine02.BodyLocal);
		if (_walking && actualAbs > 90f)
			actualAbs = Mathf.Abs(snap.LeanDegrees);
		float s1Abs = Quaternion.Angle(_neutral.Spine01.BodyLocal, snap.Spine01.BodyLocal);
		float w1 = m_Lean.Spine01Weight;

		LogState(_cell, snap, _neutral, targetSigned, actualAbs, s1Abs, w1, _walking);
		bool pass = EvaluateState(snap, _neutral, targetAbs, actualAbs, _walking, out string verdict);
		Record(_cell, pass, verdict);
		Debug.Log($"[SpineLeanDiag] VERDICT {_cell} {(pass ? "PASS" : "FAIL")} {verdict}", this);

		if (_level > 0)
		{
			m_LevelActuals.Add(new LevelActual
			{
				Stance = _stance,
				Walking = _walking,
				Side = _side,
				Level = _level,
				ActualAbs = _walking ? Mathf.Abs(snap.LeanDegrees) : actualAbs
			});
		}
	}

	private bool EvaluateState(
		PoseSnapshot _snap,
		PoseSnapshot _neutral,
		float _targetAbs,
		float _actualAbs,
		bool _walking,
		out string _verdict)
	{
		var issues = new List<string>(8);
		var notes = new List<string>(8);

		if (!_snap.Valid)
			issues.Add("нет снимка");
		if (m_PinAimTarget && !_snap.HasTarget)
			issues.Add("нет цели");
		if (_snap.Pose != WeaponPoseState.Aiming)
			issues.Add($"поза {_snap.Pose}");
		if (_snap.Blocked)
			issues.Add("BLOCKED");
		if (!_snap.Settled)
			issues.Add("не settled");
		if (_walking && !IsWalkingNow())
			issues.Add("нет шага");

		float smoothedAbs = Mathf.Abs(_snap.LeanDegrees);
		float smoothedErr = Mathf.Abs(smoothedAbs - _targetAbs);
		if (smoothedErr <= m_MaxAngleErrorDegrees)
			notes.Add($"smoothed={smoothedAbs:F1}° target={_targetAbs:F1}°");
		else
			issues.Add($"smoothed={smoothedAbs:F1}° target={_targetAbs:F1}° err={smoothedErr:F1}°");

		float boneTol = _walking ? m_MaxWalkBoneAngleErrorDegrees : m_MaxAngleErrorDegrees;
		float angleErr = Mathf.Abs(_actualAbs - _targetAbs);
		if (angleErr <= boneTol)
			notes.Add($"actual={_actualAbs:F1}° target={_targetAbs:F1}°");
		else
			issues.Add($"угол actual={_actualAbs:F1}° target={_targetAbs:F1}° err={angleErr:F1}°");

		float rootShift = HorizontalDistance(_neutral.RootPos, _snap.RootPos);
		if (_walking)
			notes.Add($"rootTravel={rootShift:F3}m");
		else if (rootShift <= m_MaxRootShiftMeters)
			notes.Add($"root={rootShift:F3}m");
		else
			issues.Add($"корень шагнул {rootShift:F3}m > {m_MaxRootShiftMeters:F2}m");

		if (!_snap.HasBarrel)
		{
			issues.Add("нет ствола");
		}
		else if (m_PinAimTarget)
		{
			float aimLimit = _walking ? m_MaxWalkAimErrorDegrees : m_MaxIdleAimErrorDegrees;
			if (_snap.AimErrorDegrees <= aimLimit)
				notes.Add($"aimErr={_snap.AimErrorDegrees:F1}°");
			else
				issues.Add($"aimErr={_snap.AimErrorDegrees:F1}° > {aimLimit:F0}°");

			float yawDelta = Mathf.Abs(Mathf.DeltaAngle(_neutral.BarrelYawDegrees, _snap.BarrelYawDegrees));
			notes.Add($"barrelΔyaw={yawDelta:F1}°");
		}
		else
		{
			float yawLimit = _walking ? m_MaxWalkBarrelYawDeltaDegrees : m_MaxBarrelYawDeltaDegrees;
			float yawDelta = Mathf.Abs(Mathf.DeltaAngle(_neutral.BarrelYawDegrees, _snap.BarrelYawDegrees));
			if (yawDelta <= yawLimit)
				notes.Add($"barrelΔyaw={yawDelta:F1}°");
			else
				issues.Add($"barrelΔyaw={yawDelta:F1}° > {yawLimit:F0}°");

			float minDot = _walking ? m_MinWalkBarrelForwardDot : m_MinBarrelForwardDot;
			Vector3 bodyFwd = Flatten(transform.forward);
			Vector3 barrelXZ = Flatten(_snap.BarrelForward);
			float forwardDot = bodyFwd.sqrMagnitude > 0f && barrelXZ.sqrMagnitude > 0f
				? Vector3.Dot(bodyFwd, barrelXZ)
				: 0f;
			if (forwardDot < minDot)
				issues.Add($"ствол не вперёд dot={forwardDot:F2}");
			else
				notes.Add($"lookStraight dot={forwardDot:F2}");
		}

		bool pass = issues.Count == 0;
		var sb = new StringBuilder(160);
		if (pass)
			sb.Append("OK ");
		sb.Append(string.Join("; ", notes));
		if (!pass)
		{
			if (sb.Length > 0)
				sb.Append(" | ");
			sb.Append("FAIL: ");
			sb.Append(string.Join("; ", issues));
		}

		_verdict = sb.ToString();
		return pass;
	}

	private void EvaluateSymmetry()
	{
		LocomotionStance[] stances = { LocomotionStance.Standing, LocomotionStance.Crouch };
		bool[] walks = { false, true };
		for (int w = 0; w < walks.Length; w++)
		{
			if (walks[w] && !m_IncludeWalkPass)
				continue;

			for (int s = 0; s < stances.Length; s++)
			{
				for (int level = 1; level <= UnitSpineLean.c_MaxLeanLevel; level++)
				{
					if (!TryGetLevelActual(stances[s], walks[w], -1, level, out float left) ||
					    !TryGetLevelActual(stances[s], walks[w], 1, level, out float right))
						continue;

					float err = Mathf.Abs(left - right);
					string cell = $"{FormatStanceName(stances[s], walks[w])} symmetry L{level}";
					bool pass = err <= m_MaxSymmetryErrorDegrees;
					string verdict = pass
						? $"OK |L|={left:F1}° |R|={right:F1}°"
						: $"FAIL |L|={left:F1}° |R|={right:F1}° err={err:F1}°";
					Record(cell, pass, verdict);
					Debug.Log($"[SpineLeanDiag] VERDICT {cell} {(pass ? "PASS" : "FAIL")} {verdict}", this);
				}
			}
		}
	}

	private bool TryGetLevelActual(
		LocomotionStance _stance,
		bool _walking,
		int _side,
		int _level,
		out float _actual)
	{
		_actual = 0f;
		for (int i = 0; i < m_LevelActuals.Count; i++)
		{
			LevelActual a = m_LevelActuals[i];
			if (a.Stance != _stance || a.Walking != _walking || a.Side != _side || a.Level != _level)
				continue;
			_actual = a.ActualAbs;
			return true;
		}

		return false;
	}

	private void LogState(
		string _cell,
		PoseSnapshot _s,
		PoseSnapshot _neutral,
		float _targetSigned,
		float _actualAbs,
		float _s1Abs,
		float _w1,
		bool _walking)
	{
		if (!_s.Valid)
		{
			Debug.LogWarning($"[SpineLeanDiag] STATE {_cell} sample=failed", this);
			return;
		}

		float s1Share = Mathf.Abs(_targetSigned) > 0.01f ? _s1Abs / Mathf.Abs(_targetSigned) : 0f;
		float s2Share = Mathf.Abs(_targetSigned) > 0.01f ? _actualAbs / Mathf.Abs(_targetSigned) : 0f;
		float s1RollDelta = _s.Spine01.RollDegrees - _neutral.Spine01.RollDegrees;
		float s2RollDelta = _s.Spine02.RollDegrees - _neutral.Spine02.RollDegrees;
		float rootShift = HorizontalDistance(_neutral.RootPos, _s.RootPos);
		float yawDelta = Mathf.DeltaAngle(_neutral.BarrelYawDegrees, _s.BarrelYawDegrees);

		Debug.Log(
			$"[SpineLeanDiag] STATE {_cell} pose={_s.Pose} target={(_s.HasTarget ? "YES" : "none")} " +
			$"walk={_walking} nav={GetAnimatorNavSpeed():F2} " +
			$"level={_s.LeanLevel} side={_s.LeanSide} " +
			$"targetDeg={_targetSigned:F1} actualDeg={_actualAbs * Mathf.Sign(_targetSigned == 0f ? 1f : _targetSigned):F1} " +
			$"smoothed={_s.LeanDegrees:F1} blocked={_s.Blocked} settled={_s.Settled} " +
			$"s1RollΔ={s1RollDelta:F1}° s2RollΔ={s2RollDelta:F1}° " +
			$"s1Share={s1Share:F2} s2Share={s2Share:F2} w1={_w1:F2} " +
			$"rootShift={rootShift:F3}m " +
			$"s1 pos=({_s.Spine01.WorldPos.x:F3},{_s.Spine01.WorldPos.y:F3},{_s.Spine01.WorldPos.z:F3}) " +
			$"s2 pos=({_s.Spine02.WorldPos.x:F3},{_s.Spine02.WorldPos.y:F3},{_s.Spine02.WorldPos.z:F3}) " +
			$"barrel localX={_s.BarrelLocalX:F3}m yaw={_s.BarrelYawDegrees:F1}° pitch={_s.BarrelPitchDegrees:F1}° " +
			$"ΔbarrelYaw={yawDelta:F1}° aimErr={_s.AimErrorDegrees:F1}°",
			this);
	}

	private bool IsAimingStraightSetupOk(PoseSnapshot _before, out string _fail)
	{
		if (!_before.Valid)
		{
			_fail = "нет снимка Neutral";
			return false;
		}

		if (m_PinAimTarget)
		{
			if (!_before.HasTarget)
			{
				_fail = "цель не закреплена";
				return false;
			}
		}
		else if (_before.HasTarget)
		{
			_fail = "цель не снята";
			return false;
		}

		if (_before.Pose != WeaponPoseState.Aiming)
		{
			_fail = $"поза {_before.Pose}, ждали Aiming";
			return false;
		}

		if (_before.Blocked)
		{
			_fail = "lean blocked ещё до наклона";
			return false;
		}

		if (!_before.Spine01.Valid || !_before.Spine02.Valid)
		{
			_fail = "кости Spine_01/Spine_02 не найдены";
			return false;
		}

		if (!_before.HasBarrel)
		{
			_fail = "нет ствола";
			return false;
		}

		if (m_PinAimTarget)
		{
			if (_before.AimErrorDegrees > m_MaxIdleAimErrorDegrees && !m_CurrentWalk)
			{
				_fail = $"нейтраль не на цель aimErr={_before.AimErrorDegrees:F1}°";
				return false;
			}

			_fail = string.Empty;
			return true;
		}

		Vector3 bodyFwd = Flatten(transform.forward);
		Vector3 barrelXZ = Flatten(_before.BarrelForward);
		float forwardDot = bodyFwd.sqrMagnitude > 0f && barrelXZ.sqrMagnitude > 0f
			? Vector3.Dot(bodyFwd, barrelXZ)
			: 0f;
		if (forwardDot < m_MinBarrelForwardDot)
		{
			_fail = $"ствол не смотрит прямо (dot={forwardDot:F2} yaw={_before.BarrelYawDegrees:F1}°)";
			return false;
		}

		_fail = string.Empty;
		return true;
	}

	private IEnumerator CoSampleSnapshot()
	{
		m_LastSnapshot = default;
		float duration = Mathf.Max(0.05f, m_SampleWindowSeconds);
		int count = 0;
		var acc = new SnapshotAccum();
		float t = 0f;

		while (t < duration)
		{
			yield return new WaitForEndOfFrame();
			if (m_CancelRequested)
				yield break;
			if (m_CurrentWalk)
				RefreshWalkIfNeeded();
			if (!TryCaptureInstant(out PoseSnapshot sample))
			{
				t += Time.deltaTime;
				continue;
			}

			acc.Add(sample);
			count++;
			t += Time.deltaTime;
		}

		if (count <= 0)
			yield break;

		m_LastSnapshot = acc.Average(count);
	}

	private bool TryCaptureInstant(out PoseSnapshot _s)
	{
		_s = default;
		Transform s1 = m_Lean != null ? m_Lean.Spine01Bone : null;
		Transform s2 = m_Lean != null ? m_Lean.Spine02Bone : null;
		if (s1 == null && m_Animator != null)
			s1 = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
		if (s2 == null && m_Animator != null)
			s2 = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
		if (s1 == null || s2 == null)
			return false;

		Quaternion rootYawOnly = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
		Quaternion inv = Quaternion.Inverse(rootYawOnly);
		Vector3 rightXZ = Flatten(transform.right);

		_s.Valid = true;
		_s.RootPos = transform.position;
		_s.RootYawDegrees = transform.eulerAngles.y;
		_s.LeanLevel = m_Lean != null ? m_Lean.CurrentLeanLevel : 0;
		_s.LeanSide = m_Lean != null ? m_Lean.CurrentLeanSide : 0;
		_s.TargetDegrees = m_Lean != null ? m_Lean.TargetLeanDegrees : 0f;
		_s.LeanDegrees = m_Lean != null ? m_Lean.CurrentLeanDegrees : 0f;
		_s.Blocked = m_Lean != null && m_Lean.IsLeanBlockedNow;
		_s.Settled = m_Lean != null && m_Lean.IsLeanSettled;
		_s.Pose = m_ReadyHands != null ? m_ReadyHands.EffectivePoseState : WeaponPoseState.NotReady;
		_s.HasTarget = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null;
		_s.Spine01 = SampleBone(s1, inv, rightXZ);
		_s.Spine02 = SampleBone(s2, inv, rightXZ);

		if (TryGetBarrel(out Transform barrel))
		{
			_s.HasBarrel = true;
			_s.BarrelWorldPos = barrel.position;
			_s.BarrelForward = barrel.forward;
			_s.BarrelLocalX = rightXZ.sqrMagnitude > 0f
				? Vector3.Dot(barrel.position - transform.position, rightXZ)
				: 0f;
			Vector3 bodyFwd = Flatten(transform.forward);
			Vector3 barrelXZ = Flatten(barrel.forward);
			_s.BarrelYawDegrees = bodyFwd.sqrMagnitude > 0f && barrelXZ.sqrMagnitude > 0f
				? Vector3.SignedAngle(bodyFwd, barrelXZ, Vector3.up)
				: 0f;
			_s.BarrelPitchDegrees = Mathf.Asin(Mathf.Clamp(barrel.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
			_s.AimErrorDegrees = ComputeAimErrorDegrees(barrel);
		}

		return true;
	}

	private BoneSample SampleBone(Transform _bone, Quaternion _invRootYaw, Vector3 _rightXZ)
	{
		Quaternion bodyLocal = _invRootYaw * _bone.rotation;
		DecomposeBoneAngles(bodyLocal, out float pitch, out float yaw, out float roll);
		return new BoneSample
		{
			Valid = true,
			WorldPos = _bone.position,
			LocalX = _rightXZ.sqrMagnitude > 0f
				? Vector3.Dot(_bone.position - transform.position, _rightXZ)
				: 0f,
			PitchDegrees = pitch,
			YawDegrees = yaw,
			RollDegrees = roll,
			BodyLocal = bodyLocal
		};
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

		Debug.LogWarning(
			$"[SpineLeanDiag] pose settle timeout want={_expected} have={GetHeldPose()} " +
			$"blend={IsPoseBlending()}",
			this);
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

		Debug.LogWarning(
			$"[SpineLeanDiag] stance wait timeout want={_stance} have={m_Stance.CurrentStance}",
			this);
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

	private IEnumerator CoWaitLeanSettled()
	{
		float t = 0f;
		while (t < m_LeanSettleTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;
			if (m_CurrentWalk)
				RefreshWalkIfNeeded();
			if (m_Lean != null && m_Lean.IsLeanSettled)
				yield break;
			t += Time.deltaTime;
			yield return null;
		}

		Debug.LogWarning(
			$"[SpineLeanDiag] lean settle timeout level={m_Lean.CurrentLeanLevel} " +
			$"targetDeg={m_Lean.TargetLeanDegrees:F1} smoothed={m_Lean.CurrentLeanDegrees:F1} " +
			$"blocked={m_Lean.IsLeanBlockedNow}",
			this);
	}

	private void CaptureRestoreState()
	{
		m_HasCapturedRestore = true;
		m_HadReadyKeyboard = m_ReadyHands != null && m_ReadyHands.IsKeyboardInputEnabled;
		m_HadStanceKeyboard = m_Stance != null && m_Stance.IsKeyboardInputEnabled;
		m_HadTargetSelectorEnabled = m_TargetSelector != null && m_TargetSelector.enabled;
		m_HadDisciplineEnabled = m_FireDiscipline != null && m_FireDiscipline.enabled;
		m_HadAutoFireEnabled = m_AutoFire != null && m_AutoFire.enabled;
		m_HadTryReloadWhenOutOfAmmo = m_FireController != null && m_FireController.TryReloadWhenOutOfAmmo;
		m_HadFireControllerEnabled = m_FireController != null && m_FireController.enabled;
		m_HadLeanDiagnostics = m_Lean != null && m_Lean.DiagnosticsLoggingEnabled;
		m_RestorePeaceful = m_ReadyHands != null && m_ReadyHands.IsPeacefulNotReady;
		m_RestoreMode = m_ReadyHands != null ? m_ReadyHands.WantedMode : WeaponPoseMode.LowReady;
		m_RestorePeacefulPose = m_ReadyHands != null
			? m_ReadyHands.PeacefulCarryPose
			: WeaponPoseState.NotReady;
		m_RestoreStance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
	}

	private void LockGameplayInput(bool _lock)
	{
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(!_lock && m_HadReadyKeyboard);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(!_lock && m_HadStanceKeyboard);

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
				m_FireController.enabled = false;
			}

			m_RtsMember?.StopFiring();
			return;
		}

		if (m_FireController != null)
		{
			m_FireController.TryReloadWhenOutOfAmmo = m_HadTryReloadWhenOutOfAmmo;
			m_FireController.enabled = m_HadFireControllerEnabled;
		}
		if (m_FireDiscipline != null)
			m_FireDiscipline.enabled = m_HadDisciplineEnabled;
		if (m_AutoFire != null)
			m_AutoFire.enabled = m_HadAutoFireEnabled;
	}

	private void MuteLiveLeanLogs(bool _mute)
	{
		if (m_Lean == null)
			return;
		m_Lean.DiagnosticsLoggingEnabled = _mute ? false : m_HadLeanDiagnostics;
	}

	private void PinAimTarget()
	{
		if (!m_PinAimTarget)
		{
			ClearEngageTarget();
			return;
		}

		if (m_TargetSelector == null)
		{
			Debug.LogWarning("[SpineLeanDiag] TargetSelector missing — цель не закрепить.", this);
			return;
		}

		m_TargetSelector.enabled = false;

		Transform existing = m_TargetSelector.SelectedTarget;
		if (existing != null && TargetEngageability.IsEngageable(existing))
		{
			Vector3 aim = m_TargetSelector.HasSelectedAimPoint
				? m_TargetSelector.SelectedAimPointWorld
				: existing.position + Vector3.up * 1.2f;
			m_TargetSelector.SetSelectedTargetForDiagnostics(existing, aim);
			return;
		}

		EnsureDummyTarget();
		Vector3 dummyAim = m_DummyTarget.transform.position;
		m_TargetSelector.SetSelectedTargetForDiagnostics(m_DummyTarget.transform, dummyAim);
	}

	private void EnsureDummyTarget()
	{
		if (m_DummyTarget == null)
		{
			m_DummyTarget = new GameObject("SpineLeanDiag_DummyTarget");
			m_SpawnedDummyTarget = true;
		}

		PlaceDummyAhead();
	}

	private void KeepDummyAhead()
	{
		if (!m_PinAimTarget || m_DummyTarget == null)
			return;

		float dist = HorizontalDistance(transform.position, m_DummyTarget.transform.position);
		if (dist >= m_DummyKeepAheadMeters)
			return;

		PlaceDummyAhead();
	}

	private void PlaceDummyAhead()
	{
		if (m_DummyTarget == null)
			return;

		Vector3 fwd = Flatten(transform.forward);
		if (fwd.sqrMagnitude < 1e-6f)
			fwd = Vector3.forward;

		m_DummyTarget.transform.position = transform.position + fwd * m_DummyTargetDistanceMeters + Vector3.up * 1.2f;
		if (m_TargetSelector != null && m_TargetSelector.SelectedTarget == m_DummyTarget.transform)
			m_TargetSelector.SetSelectedTargetForDiagnostics(m_DummyTarget.transform, m_DummyTarget.transform.position);
	}

	private void DestroyDummyTarget()
	{
		if (!m_SpawnedDummyTarget || m_DummyTarget == null)
		{
			m_DummyTarget = null;
			m_SpawnedDummyTarget = false;
			return;
		}

		Destroy(m_DummyTarget);
		m_DummyTarget = null;
		m_SpawnedDummyTarget = false;
	}

	private Vector3 GetPinnedAimPoint()
	{
		if (m_TargetSelector != null && m_TargetSelector.SelectedTarget != null)
		{
			Vector3 aim = m_TargetSelector.GetEngageableAimPointWorld();
			if (aim.sqrMagnitude > 0.01f)
				return aim;
			return m_TargetSelector.SelectedTarget.position + Vector3.up * 1.2f;
		}

		if (m_DummyTarget != null)
			return m_DummyTarget.transform.position;

		return Vector3.zero;
	}

	private float ComputeAimErrorDegrees(Transform _barrel)
	{
		if (_barrel == null)
			return 0f;

		Vector3 aim = GetPinnedAimPoint();
		Vector3 toTarget = aim - _barrel.position;
		if (toTarget.sqrMagnitude < 1e-6f || _barrel.forward.sqrMagnitude < 1e-6f)
			return 0f;

		return Vector3.Angle(_barrel.forward, toTarget.normalized);
	}

	private Vector3 GetWalkDestination()
	{
		if (m_DummyTarget != null)
		{
			Vector3 toDummy = Flatten(m_DummyTarget.transform.position - transform.position);
			if (toDummy.sqrMagnitude > 1f)
				return m_DummyTarget.transform.position;
		}

		return transform.position + Flatten(transform.forward) * m_WalkDistanceMeters;
	}

	private void ClearEngageTarget()
	{
		if (m_TargetSelector == null)
			return;
		m_TargetSelector.ClearSelectionAndNotifyIfHadTarget();
		m_TargetSelector.enabled = false;
	}

	private void RestoreUnitState()
	{
		if (!m_HasCapturedRestore)
			return;

		m_ClickToMove?.HardStop();
		m_Lean?.SetLeanLevel(0, 0);
		MuteLiveLeanLogs(false);
		DestroyDummyTarget();

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

		if (m_TargetSelector != null)
			m_TargetSelector.enabled = m_HadTargetSelectorEnabled;

		LockGameplayInput(false);
		m_HasCapturedRestore = false;
	}

	private void Record(string _cell, bool _pass, string _verdict)
	{
		m_Results.Add(new SideResult
		{
			CellName = _cell,
			Pass = _pass,
			Verdict = _verdict
		});
	}

	private void LogSummary()
	{
		int pass = 0;
		int fail = 0;
		var sb = new StringBuilder(512);
		sb.AppendLine($"[SpineLeanDiag] SUMMARY unit={name} cells={m_Results.Count}");
		for (int i = 0; i < m_Results.Count; i++)
		{
			SideResult r = m_Results[i];
			if (r.Pass)
				pass++;
			else
				fail++;
			sb.AppendLine($"  {(r.Pass ? "PASS" : "FAIL")} {r.CellName} {r.Verdict}");
		}

		sb.Append($"[SpineLeanDiag] SUMMARY totals PASS={pass} FAIL={fail}");
		Debug.Log(sb.ToString(), this);
	}

	private bool IsPoseBlending() =>
		m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;

	private WeaponPoseState GetHeldPose() =>
		m_ReadyHands != null ? m_ReadyHands.EffectivePoseState : WeaponPoseState.NotReady;

	private bool IsPoseHeld(WeaponPoseState _expected) =>
		!IsPoseBlending() && GetHeldPose() == _expected;

	private bool TryGetBarrel(out Transform _barrel)
	{
		_barrel = null;
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_Equipment == null)
			return false;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null)
			return false;

		_barrel = weapon.BarrelTransform != null ? weapon.BarrelTransform : weapon.FireOriginTransform;
		if (_barrel == null)
			_barrel = m_Equipment.MainWeaponRoot;
		return _barrel != null;
	}

	private static string FormatCell(string _stance, int _side, int _level)
	{
		return $"{_stance}/{FormatSide(_side)}{_level}";
	}

	private static string FormatStanceName(LocomotionStance _stance, bool _walking)
	{
		if (_stance == LocomotionStance.Crouch)
			return _walking ? "CrouchWalk" : "Crouch";
		return _walking ? "StandingWalk" : "Standing";
	}

	private IEnumerator CoEnsureWalking()
	{
		if (m_ClickToMove == null)
		{
			Debug.LogWarning("[SpineLeanDiag] SKIP walk: no UnitClickToMove.", this);
			yield break;
		}

		m_ClickToMove.ForceWalkMoveMode();
		if (!ShouldRefreshWalkDestination())
			yield break;

		KeepDummyAhead();
		Vector3 dest = GetWalkDestination();
		if (!m_ClickToMove.IssueNavOrder(dest, UnitClickToMove.MoveTier.Walk))
		{
			Debug.LogWarning("[SpineLeanDiag] IssueNavOrder failed.", this);
			yield break;
		}

		yield return CoWaitWalkStarted();
		if (m_WalkSpeedSettleSeconds > 0f)
			yield return new WaitForSeconds(m_WalkSpeedSettleSeconds);
	}

	private IEnumerator CoEnsureIdle()
	{
		m_CurrentWalk = false;
		if (m_ClickToMove != null)
			m_ClickToMove.HardStop();

		float t = 0f;
		while (t < m_WalkStartTimeoutSeconds)
		{
			if (m_CancelRequested)
				yield break;

			bool intent = m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
			bool moving = IsAnimatorMoving();
			if (!intent && !moving)
				yield break;

			t += Time.deltaTime;
			yield return null;
		}

		Debug.LogWarning("[SpineLeanDiag] idle settle timeout.", this);
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

		Debug.LogWarning("[SpineLeanDiag] walk start timeout.", this);
	}

	private void RefreshWalkIfNeeded()
	{
		if (!m_CurrentWalk || m_ClickToMove == null)
			return;
		if (!ShouldRefreshWalkDestination())
			return;

		m_ClickToMove.ForceWalkMoveMode();
		KeepDummyAhead();
		Vector3 dest = GetWalkDestination();
		m_ClickToMove.IssueNavOrder(dest, UnitClickToMove.MoveTier.Walk);
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

	private bool IsWalkingNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.HasMoveIntent)
			return true;
		return IsAnimatorMoving();
	}

	private bool IsAnimatorMoving()
	{
		return GetAnimatorNavSpeed() >= 0.055f;
	}

	private float GetAnimatorNavSpeed()
	{
		return m_Animator != null ? m_Animator.GetFloat(s_NavSpeed) : 0f;
	}

	private static string FormatSide(int _side)
	{
		if (_side < 0)
			return "L";
		if (_side > 0)
			return "R";
		return "N";
	}

	private static void DecomposeBoneAngles(
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

	private static Vector3 Flatten(Vector3 _v)
	{
		_v.y = 0f;
		if (_v.sqrMagnitude < 1e-6f)
			return Vector3.zero;
		return _v.normalized;
	}

	private static float HorizontalDistance(Vector3 _a, Vector3 _b)
	{
		_a.y = 0f;
		_b.y = 0f;
		return Vector3.Distance(_a, _b);
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

	#region Nested Types
	private struct SnapshotAccum
	{
		public Vector3 RootPos;
		public float SinYaw;
		public float CosYaw;
		public int LeanLevel;
		public int LeanSide;
		public float TargetDegrees;
		public float LeanDegrees;
		public int BlockedCount;
		public int SettledCount;
		public int HasTargetCount;
		public WeaponPoseState Pose;
		public BoneAccum Spine01;
		public BoneAccum Spine02;
		public int BarrelCount;
		public Vector3 BarrelPos;
		public Vector3 BarrelFwd;
		public float BarrelLocalX;
		public float BarrelYaw;
		public float BarrelPitch;
		public float AimError;

		public void Add(PoseSnapshot _s)
		{
			RootPos += _s.RootPos;
			float rad = _s.RootYawDegrees * Mathf.Deg2Rad;
			SinYaw += Mathf.Sin(rad);
			CosYaw += Mathf.Cos(rad);
			LeanLevel = _s.LeanLevel;
			LeanSide = _s.LeanSide;
			TargetDegrees += _s.TargetDegrees;
			LeanDegrees += _s.LeanDegrees;
			if (_s.Blocked)
				BlockedCount++;
			if (_s.Settled)
				SettledCount++;
			if (_s.HasTarget)
				HasTargetCount++;
			Pose = _s.Pose;
			Spine01.Add(_s.Spine01);
			Spine02.Add(_s.Spine02);
			if (!_s.HasBarrel)
				return;
			BarrelCount++;
			BarrelPos += _s.BarrelWorldPos;
			BarrelFwd += _s.BarrelForward;
			BarrelLocalX += _s.BarrelLocalX;
			BarrelYaw += _s.BarrelYawDegrees;
			BarrelPitch += _s.BarrelPitchDegrees;
			AimError += _s.AimErrorDegrees;
		}

		public PoseSnapshot Average(int _count)
		{
			float inv = 1f / Mathf.Max(1, _count);
			var s = new PoseSnapshot
			{
				Valid = true,
				RootPos = RootPos * inv,
				RootYawDegrees = Mathf.Atan2(SinYaw, CosYaw) * Mathf.Rad2Deg,
				LeanLevel = LeanLevel,
				LeanSide = LeanSide,
				TargetDegrees = TargetDegrees * inv,
				LeanDegrees = LeanDegrees * inv,
				Blocked = BlockedCount * 2 >= _count,
				Settled = SettledCount * 2 >= _count,
				Pose = Pose,
				HasTarget = HasTargetCount * 2 >= _count,
				Spine01 = Spine01.Average(_count),
				Spine02 = Spine02.Average(_count)
			};

			if (BarrelCount <= 0)
				return s;

			float bInv = 1f / BarrelCount;
			s.HasBarrel = true;
			s.BarrelWorldPos = BarrelPos * bInv;
			s.BarrelForward = BarrelFwd.normalized;
			s.BarrelLocalX = BarrelLocalX * bInv;
			s.BarrelYawDegrees = BarrelYaw * bInv;
			s.BarrelPitchDegrees = BarrelPitch * bInv;
			s.AimErrorDegrees = AimError * bInv;
			return s;
		}
	}

	private struct BoneAccum
	{
		public Vector3 WorldPos;
		public float LocalX;
		public Quaternion BodyLocal;
		public int Count;

		public void Add(BoneSample _s)
		{
			if (!_s.Valid)
				return;
			float w = 1f / (Count + 1);
			WorldPos += _s.WorldPos;
			LocalX += _s.LocalX;
			BodyLocal = Count == 0 ? _s.BodyLocal : Quaternion.Slerp(BodyLocal, _s.BodyLocal, w);
			Count++;
		}

		public BoneSample Average(int _fallbackCount)
		{
			int n = Count > 0 ? Count : _fallbackCount;
			if (n <= 0)
				return default;

			float inv = 1f / n;
			DecomposeBoneAngles(BodyLocal, out float pitch, out float yaw, out float roll);
			return new BoneSample
			{
				Valid = Count > 0,
				WorldPos = WorldPos * inv,
				LocalX = LocalX * inv,
				PitchDegrees = pitch,
				YawDegrees = yaw,
				RollDegrees = roll,
				BodyLocal = BodyLocal
			};
		}
	}
	#endregion
}
