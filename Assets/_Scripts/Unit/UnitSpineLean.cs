using UnityEngine;

/// <summary>
/// Наклон корпуса влево/вправо (peek) через Spine_01 / Spine_02.
/// Три уровня одной системы: Neutral / Small / Combat / Full.
/// Сглаживание в градусах. Симметрия Left/Right. Без walk/AI/cover.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class UnitSpineLean : MonoBehaviour
{
	#region Constants
	public const int c_MaxLeanLevel = 3;
	private const float c_SettleVelocityDegrees = 2f;
	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrowController;
	[SerializeField] private UnitFallenDragController m_FallenDragController;
	[SerializeField] private UnitFiremanCarryController m_FiremanCarryController;
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private TargetSelector m_TargetSelector;

	[Header("Bones")]
	[SerializeField] private Transform m_Spine01;
	[SerializeField] private Transform m_Spine02;

	[Header("Levels")]
	[Tooltip("Standing: Neutral / Small / Combat / Full.")]
	[SerializeField] private float[] m_StandingDegrees = { 0f, 11f, 20f, 26f };
	[Tooltip("Crouch: Neutral / Small / Combat / Full.")]
	[SerializeField] private float[] m_CrouchDegrees = { 0f, 9f, 17f, 22f };

	[Header("Spine split")]
	[Tooltip("Доля TargetAngle на Spine_01 (остальное — Spine_02).")]
	[SerializeField, Range(0f, 1f)] private float m_Spine01Weight = 0.40f;

	[Header("Smooth")]
	[SerializeField, Min(0.01f)] private float m_SmoothTime = 0.13f;
	[Tooltip("|smoothed − target| в градусах считается settled.")]
	[SerializeField, Min(0.1f)] private float m_SettleDegrees = 1f;
	[SerializeField] private bool m_BlockDuringStanceTransition = true;

	[Header("Debug")]
	[SerializeField] private int m_DebugLeanLevel;
	[SerializeField] private int m_DebugLeanSide;
	[SerializeField] private float m_DebugTargetLeanDegrees;
	[SerializeField] private float m_DebugSmoothedLeanDegrees;
	[SerializeField] private bool m_DebugBlocked;

	[Header("Debug Log")]
	[Tooltip("Живые логи REQUEST/SETTLED/TICK. Для прогона по K выключены — смотри [SpineLeanDiag].")]
	[SerializeField] private bool m_LogLeanDiagnostics = false;
	[SerializeField, Min(0.1f)] private float m_LogIntervalSeconds = 0.45f;
	#endregion

	#region Private Fields
	private int m_LeanLevel;
	private int m_LeanSide;
	private float m_TargetLeanDegrees;
	private float m_SmoothedLeanDegrees;
	private float m_LeanDegreesVelocity;
	private bool m_BonesResolved;
	private bool m_LoggedSettledForTarget;
	private float m_LastDiagnosticLogTime = -999f;
	#endregion

	#region Public Properties
	/// <summary>Запрошенный уровень 0..3.</summary>
	public int CurrentLeanLevel => m_LeanLevel;

	/// <summary>−1 влево, 0 нейтраль, +1 вправо.</summary>
	public int CurrentLeanSide => m_LeanSide;

	/// <summary>Целевой угол (градусы), + вправо. При блоке для сглаживания = 0.</summary>
	public float TargetLeanDegrees => IsLeanBlocked() ? 0f : m_TargetLeanDegrees;

	/// <summary>Сглаженный угол (градусы), + вправо.</summary>
	public float CurrentLeanDegrees => m_SmoothedLeanDegrees;

	/// <summary>−1…+1: TargetAngle / FullAngle текущей стойки. Для aiming skip.</summary>
	public float CurrentLean01
	{
		get
		{
			float max = Mathf.Max(0.01f, GetLeanAngle(c_MaxLeanLevel));
			return Mathf.Clamp(m_TargetLeanDegrees / max, -1f, 1f);
		}
	}

	public bool IsLeanBlockedNow => IsLeanBlocked();

	public bool IsLeanSettled
	{
		get
		{
			if (IsLeanBlocked() && Mathf.Abs(m_TargetLeanDegrees) > 0.01f)
				return false;

			float target = IsLeanBlocked() ? 0f : m_TargetLeanDegrees;
			return Mathf.Abs(m_SmoothedLeanDegrees - target) <= m_SettleDegrees &&
			       Mathf.Abs(m_LeanDegreesVelocity) < c_SettleVelocityDegrees;
		}
	}

	public Transform Spine01Bone => m_Spine01;
	public Transform Spine02Bone => m_Spine02;
	public float Spine01Weight => Mathf.Clamp01(m_Spine01Weight);

	/// <summary>Живые REQUEST/SETTLED/TICK. Прогон по K пишет свои логи [SpineLeanDiag].</summary>
	public bool DiagnosticsLoggingEnabled
	{
		get => m_LogLeanDiagnostics;
		set => m_LogLeanDiagnostics = value;
	}
	#endregion

	#region Public Methods
	/// <summary>Задать уровень наклона. level 0..3, side −1/0/+1. При level 0 сторона сбрасывается.</summary>
	public void SetLeanLevel(int _level, int _side)
	{
		int level = Mathf.Clamp(_level, 0, c_MaxLeanLevel);
		int side = level <= 0 ? 0 : (_side < 0 ? -1 : 1);
		float target = GetLeanAngle(level) * side;
		bool changed = level != m_LeanLevel || side != m_LeanSide ||
		               Mathf.Abs(target - m_TargetLeanDegrees) > 0.01f;

		m_LeanLevel = level;
		m_LeanSide = side;
		m_TargetLeanDegrees = target;
		m_DebugLeanLevel = m_LeanLevel;
		m_DebugLeanSide = m_LeanSide;
		m_DebugTargetLeanDegrees = m_TargetLeanDegrees;
		if (!changed)
			return;

		m_LoggedSettledForTarget = false;
		if (m_LogLeanDiagnostics)
			LogLeanSnapshot("REQUEST", _force: true);
	}

	/// <summary>Цикл debug-кнопки: 0 → 1 → 2 → 3 → 0 на своей стороне. Чужая сторона сбрасывает в уровень 1.</summary>
	public void CycleLeanSide(int _side)
	{
		int side = _side < 0 ? -1 : 1;
		if (m_LeanLevel <= 0 || m_LeanSide != side)
			SetLeanLevel(1, side);
		else if (m_LeanLevel >= c_MaxLeanLevel)
			SetLeanLevel(0, 0);
		else
			SetLeanLevel(m_LeanLevel + 1, side);
	}

	/// <summary>Совместимость: квантует −1…+1 к ближайшему уровню текущей стойки.</summary>
	public void SetLeanTarget(float _lean01)
	{
		float clamped = Mathf.Clamp(_lean01, -1f, 1f);
		if (Mathf.Abs(clamped) < 0.05f)
		{
			SetLeanLevel(0, 0);
			return;
		}

		int side = clamped < 0f ? -1 : 1;
		float wantedAbs = Mathf.Abs(clamped) * GetLeanAngle(c_MaxLeanLevel);
		int bestLevel = 1;
		float bestErr = float.MaxValue;
		for (int level = 1; level <= c_MaxLeanLevel; level++)
		{
			float err = Mathf.Abs(wantedAbs - GetLeanAngle(level));
			if (err >= bestErr)
				continue;
			bestErr = err;
			bestLevel = level;
		}

		SetLeanLevel(bestLevel, side);
	}

	/// <summary>Угол уровня для текущей стойки (всегда ≥ 0).</summary>
	public float GetLeanAngle(int _level)
	{
		return GetLeanAngle(_level, IsCrouchStance());
	}

	/// <summary>Угол уровня для указанной стойки (всегда ≥ 0).</summary>
	public float GetLeanAngle(int _level, bool _crouch)
	{
		int level = Mathf.Clamp(_level, 0, c_MaxLeanLevel);
		float[] table = ResolveDegreeTable(_crouch);
		if (table == null || table.Length <= level)
			return 0f;
		return Mathf.Max(0f, table[level]);
	}

	public void TickDiagnosticsAfterAim()
	{
		TickLeanDiagnostics();
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		ResolveBones();
		EnsureDegreeTables();
	}

	private void OnDisable()
	{
		m_LeanLevel = 0;
		m_LeanSide = 0;
		m_TargetLeanDegrees = 0f;
		m_SmoothedLeanDegrees = 0f;
		m_LeanDegreesVelocity = 0f;
		m_DebugLeanLevel = 0;
		m_DebugLeanSide = 0;
		m_DebugTargetLeanDegrees = 0f;
		m_DebugSmoothedLeanDegrees = 0f;
		m_DebugBlocked = false;
		m_LoggedSettledForTarget = false;
	}

	private void Update()
	{
		EvaluateLean();
	}

	private void LateUpdate()
	{
		if (!m_BonesResolved)
			ResolveBones();
		if (!m_BonesResolved)
			return;

		ApplySpineLean(m_SmoothedLeanDegrees);
		if (!ShouldDeferDiagnosticsToAiming())
			TickLeanDiagnostics();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_GrenadeThrowController == null)
			m_GrenadeThrowController = GetComponent<UnitGrenadeThrowController>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponent<VehiclePassengerState>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
	}

	private void ResolveBones()
	{
		m_BonesResolved = false;
		if (m_Animator == null)
			return;

		if (m_Spine01 == null)
		{
			m_Spine01 = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
			if (m_Spine01 == null)
				m_Spine01 = FindChildRecursive(transform, "Spine_01");
		}

		if (m_Spine02 == null)
		{
			m_Spine02 = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
			if (m_Spine02 == null)
				m_Spine02 = FindChildRecursive(transform, "Spine_02");
		}

		m_BonesResolved = m_Spine01 != null && m_Spine02 != null;
	}

	private void EnsureDegreeTables()
	{
		m_StandingDegrees = EnsureTable(m_StandingDegrees, new[] { 0f, 11f, 20f, 26f });
		m_CrouchDegrees = EnsureTable(m_CrouchDegrees, new[] { 0f, 9f, 17f, 22f });
	}

	private static float[] EnsureTable(float[] _table, float[] _fallback)
	{
		if (_table != null && _table.Length > c_MaxLeanLevel)
			return _table;
		return (float[])_fallback.Clone();
	}

	private void EvaluateLean()
	{
		EnsureDegreeTables();
		bool blocked = IsLeanBlocked();
		m_DebugBlocked = blocked;

		m_TargetLeanDegrees = GetLeanAngle(m_LeanLevel) * m_LeanSide;
		float target = blocked ? 0f : m_TargetLeanDegrees;
		float smooth = Mathf.Max(0.0001f, m_SmoothTime);
		m_SmoothedLeanDegrees = Mathf.SmoothDamp(
			m_SmoothedLeanDegrees,
			target,
			ref m_LeanDegreesVelocity,
			smooth,
			Mathf.Infinity,
			Time.deltaTime);

		m_DebugLeanLevel = m_LeanLevel;
		m_DebugLeanSide = m_LeanSide;
		m_DebugTargetLeanDegrees = m_TargetLeanDegrees;
		m_DebugSmoothedLeanDegrees = m_SmoothedLeanDegrees;
	}

	private bool IsCrouchStance()
	{
		return m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Crouch;
	}

	private float[] ResolveDegreeTable(bool _crouch)
	{
		return _crouch ? m_CrouchDegrees : m_StandingDegrees;
	}

	private bool IsLeanBlocked()
	{
		if (!m_BonesResolved && m_Animator != null)
			ResolveBones();
		if (!m_BonesResolved)
			return true;

		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return true;
		if (m_VehiclePassengerState != null && m_VehiclePassengerState.IsVehicleReady)
			return true;
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return true;
		if (m_FiremanCarryController != null && m_FiremanCarryController.IsCarryingFallen)
			return true;
		if (m_GrenadeThrowController != null &&
		    (m_GrenadeThrowController.IsAiming || m_GrenadeThrowController.IsThrowAnimPlaying))
			return true;
		if (m_BlockDuringStanceTransition &&
		    m_BusyState != null &&
		    m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return true;
		if (m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Prone)
			return true;
		if (IsRunOrSprintActive())
			return true;

		return false;
	}

	private void ApplySpineLean(float _leanDegrees)
	{
		if (Mathf.Abs(_leanDegrees) < 0.0001f)
			return;

		Vector3 forwardXZ = transform.forward;
		forwardXZ.y = 0f;
		if (forwardXZ.sqrMagnitude < 1e-6f)
			return;
		forwardXZ.Normalize();

		float w1 = Spine01Weight;
		// +lean = вправо: negative AngleAxis around forward tipит торс к +right.
		float roll1 = -_leanDegrees * w1;
		float roll2 = -_leanDegrees * (1f - w1);

		if (m_Spine01 != null && Mathf.Abs(roll1) > 0.0001f)
			m_Spine01.rotation = Quaternion.AngleAxis(roll1, forwardXZ) * m_Spine01.rotation;
		if (m_Spine02 != null && Mathf.Abs(roll2) > 0.0001f)
			m_Spine02.rotation = Quaternion.AngleAxis(roll2, forwardXZ) * m_Spine02.rotation;
	}

	private bool IsRunOrSprintActive()
	{
		if (m_ClickToMove != null && m_ClickToMove.isActiveAndEnabled)
			return m_ClickToMove.IsRunMoveMode || m_ClickToMove.IsSprintMoveMode;
		if (m_LocomotionDriver != null && m_LocomotionDriver.isActiveAndEnabled)
			return m_LocomotionDriver.IsRunMoveMode || m_LocomotionDriver.IsSprintMoveMode;
		return false;
	}

	private bool ShouldDeferDiagnosticsToAiming()
	{
		return m_TargetSelector != null && m_TargetSelector.SelectedTarget != null;
	}

	private void TickLeanDiagnostics()
	{
		if (!m_LogLeanDiagnostics)
			return;

		bool active = Mathf.Abs(m_TargetLeanDegrees) > 0.01f || Mathf.Abs(m_SmoothedLeanDegrees) > 0.01f;
		if (!active)
			return;

		if (!m_LoggedSettledForTarget && IsLeanSettled)
		{
			m_LoggedSettledForTarget = true;
			LogLeanSnapshot("SETTLED", _force: true);
			return;
		}

		LogLeanSnapshot("TICK", _force: false);
	}

	private void LogLeanSnapshot(string _tag, bool _force)
	{
		if (!_force && Time.unscaledTime - m_LastDiagnosticLogTime < m_LogIntervalSeconds)
			return;

		m_LastDiagnosticLogTime = Time.unscaledTime;
		string sideName = m_LeanSide < 0 ? "Left" : (m_LeanSide > 0 ? "Right" : "Off");
		Debug.Log(
			$"[SpineLean] {_tag} unit={name} side={sideName} level={m_LeanLevel} " +
			$"targetDeg={m_TargetLeanDegrees:F1} smoothedDeg={m_SmoothedLeanDegrees:F1} " +
			$"blocked={IsLeanBlocked()} settled={IsLeanSettled} " +
			$"w1={Spine01Weight:F2} crouch={IsCrouchStance()}",
			this);
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}
