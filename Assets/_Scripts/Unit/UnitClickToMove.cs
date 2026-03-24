using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Click-to-move для NavMeshAgent + UnitController (RifleAnimsetPro / TPP).
/// Цель движения — <b>точка на NavMesh под кликом по земле</b> (не клик по врагу). Враги для поворота — отдельно <c>UnitEnemyFacing</c>.
/// Root motion выключен; поворот тела вручную (updateRotation = false).
/// ПКМ — ходьба, Shift+ПКМ — бег, двойной ПКМ — спринт; Space — жёсткая остановка.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class UnitClickToMove : MonoBehaviour
{
	#region Animator Hashes
	private static readonly int s_HashInputAngle = Animator.StringToHash("InputAngle");
	private static readonly int s_HashWalkStartAngle = Animator.StringToHash("WalkStartAngle");
	private static readonly int s_HashWalkStopAngle = Animator.StringToHash("WalkStopAngle");
	private static readonly int s_HashHorizontal = Animator.StringToHash("Horizontal");
	private static readonly int s_HashVertical = Animator.StringToHash("Vertical");
	private static readonly int s_HashInputMagnitude = Animator.StringToHash("InputMagnitude");
	private static readonly int s_HashIsStopRU = Animator.StringToHash("IsStopRU");
	private static readonly int s_HashIsStopLU = Animator.StringToHash("IsStopLU");
	// IsRU is driven by animation curves in UnitController — do not SetFloat from script (Unity warns).
	#endregion

	#region Serialized Fields
	[Tooltip("Камера для луча ПКМ → земля. Если пусто, в Awake подставится Camera.main.")]
	[SerializeField] private Camera m_RayCamera;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField, Min(0.01f)] private float m_NavMeshSampleRadius = 2f;

	[Header("NavMeshAgent")]
	[Tooltip("Applied in Awake. Small value = точнее остановка у точки; совпадает с логикой «дошёл до цели».")]
	[SerializeField, Min(0f)] private float m_AgentStoppingDistance = 0.15f;
	[Tooltip("If the agent spawns outside the baked surface, warp to the nearest valid position (avoids broken paths).")]
	[SerializeField] private bool m_AutoWarpToNavMeshOnStart = true;
	[SerializeField, Min(0.5f)] private float m_WarpSearchRadius = 12f;

	[Header("Agent speeds")]
	[SerializeField, Min(0.1f)] private float m_WalkSpeed = 1.5f;
	[SerializeField, Min(0.1f)] private float m_RunSpeed = 3.5f;
	[SerializeField, Min(0.1f)] private float m_SprintSpeed = 7.25f;

	[Header("Animator — InputMagnitude vs UnitController")]
	[Tooltip("Locomotion «ввод» для переходов idle→move. Держите < 0.5 вместе с ходьбой/бегом по 2D blend (см. ниже).")]
	[SerializeField, Range(0.11f, 0.49f)] private float m_WalkInputMagnitude = 0.35f;
	[Tooltip("Бег в том же Walking Blend Tree: ОБЯЗАТЕЛЬНО < 0.5 — иначе граф уходит в WalkStarts (старт/спринт), а не в устойчивый бег по ±0.9.")]
	[SerializeField, Range(0.11f, 0.49f)] private float m_RunInputMagnitude = 0.45f;
	[Tooltip("Спринт / WalkStarts: ≥ 0.5 по контроллеру — переход к веткам старта и отдельному Sprint sub-state.")]
	[SerializeField, Range(0.51f, 1f)] private float m_SprintInputMagnitude = 1f;

	[Header("Walking Blend Tree — 2D Cartesian (как в UnitController)")]
	[Tooltip("Ходьба: точки клипов WalkFwd/WalkBwd/стрейф — порядка ±0.2.")]
	[SerializeField, Range(0.05f, 0.35f)] private float m_WalkBlendExtent = 0.2f;
	[Tooltip("Бег в том же дереве: RunFwd/RunBwd и т.д. — порядка ±0.9 (не смешивать с 0.2 при скорости ходьбы — будет «плыть»).")]
	[SerializeField, Range(0.4f, 1f)] private float m_RunBlendExtent = 0.9f;

	[Header("Sprint — отдельное под-состояние в UnitController (не Walking Blend Tree)")]
	[Tooltip("Путь состояния для CrossFade (Base Layer). По умолчанию: под-граф Sprint → Rifle_SprintStart.")]
	[SerializeField] private string m_SprintCrossFadeState = "Sprint.Rifle_SprintStart";
	[SerializeField, Min(0.02f)] private float m_SprintCrossFadeDuration = 0.12f;
	[Tooltip("Возврат к ходьбе/бегу по тому же 2D дереву Walking.")]
	[SerializeField] private string m_WalkingBlendCrossFadeState = "Walking.Walking Blend Tree";
	[SerializeField, Min(0.02f)] private float m_WalkingBlendCrossFadeDuration = 0.15f;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_RotationSpeed = 12f;

	[Header("Input")]
	[SerializeField, Min(0.05f)] private float m_DoubleClickWindow = 0.25f;
	[SerializeField] private bool m_BlockWhenPointerOverUi = true;

	[Header("Hard stop (cancel movement)")]
	[SerializeField] private bool m_EnableHardStopKey = true;
	[SerializeField] private Key m_HardStopKey = Key.Space;
	[Tooltip("When true, InputMagnitude / H/V snap down faster after a hard stop (still runs WalkStopAngle + pivot).")]
	[SerializeField] private bool m_FastAnimatorBlendOnHardStop = true;

	[Header("Stopping — IsStopRU / IsStopLU")]
	[SerializeField, Min(0.01f)] private float m_StopVelocityThreshold = 0.1f;
	[SerializeField, Min(0.01f)] private float m_StopAnglePulseDuration = 0.14f;

	[Header("Foot slide / blend")]
	[Tooltip("Уменьшает «плавание»: масштабирует InputMagnitude к отношению фактической скорости к agent.speed (угол, разгон, торможение NavMesh).")]
	[SerializeField] private bool m_MatchInputMagnitudeToAgentSpeed = true;
	[Tooltip("Нижняя граница масштаба при движении — чтобы не проваливаться в почти-idle на микропаузах.")]
	[SerializeField, Range(0.05f, 0.6f)] private float m_InputMagSpeedScaleFloor = 0.28f;
	[Tooltip("Доля desiredVelocity: пока агент разгоняется или упирается в угол, реальная velocity мала — иначе анимация «бежит», тело стоит.")]
	[SerializeField, Range(0f, 1f)] private float m_DesiredVelocityForSpeedMatch = 0.45f;
	[Tooltip("Мёртвая зона по боковой оси (локальный X): меньше дёрганья между поворотами в Idle/Turn blend при почти прямом беге.")]
	[SerializeField, Range(0f, 0.2f)] private float m_StrafeDeadZone = 0.06f;
	[Tooltip("Чуть резче следование H/V к цели при движении (меньше — меньше скольжение при смене направления).")]
	[SerializeField, Min(0.01f)] private float m_HvSmoothTimeMoving = 0.045f;
	[Tooltip("Сглаживание InputMagnitude при движении.")]
	[SerializeField, Min(0.01f)] private float m_MagnitudeSmoothTimeMoving = 0.055f;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private float m_LastClickTime = -1f;
	private MoveMode m_CurrentMode = MoveMode.Walk;
	private float m_TargetInputMagnitude;
	private float m_SmoothedMagnitude;
	private float m_MagnitudeSmoothVel;
	private Vector2 m_SmoothHV;
	private Vector2 m_HvSmoothVel;
	private bool m_WasMoving;
	private Vector3 m_LastPlanarMoveDir;
	private Coroutine m_StopPulseRoutine;
	private bool m_HardStopBlend;
	private bool m_NeedSprintCrossfade;
	private bool m_NeedWalkingBlendCrossfade;
	private int m_SuppressLocomotionFrames;
	#endregion

	private enum MoveMode
	{
		Walk,
		Run,
		Sprint
	}

	/// <summary>Внешние системы (например поворот к врагу): true, если заказан режим спринта (двойной ПКМ).</summary>
	public bool IsSprintMoveMode => m_CurrentMode == MoveMode.Sprint;

	/// <summary>Ходьба или бег (не спринт) — для логики «смотреть на врага только в walk/run».</summary>
	public bool IsWalkOrRunMoveMode =>
		m_CurrentMode == MoveMode.Walk || m_CurrentMode == MoveMode.Run;

	/// <summary>Задать точку на NavMesh из кода (текущий режим Walk/Run/Sprint не меняется).</summary>
	public bool TrySetDestination(Vector3 _worldPosition)
	{
		if (m_Agent == null)
			return false;

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit navHit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return false;

		m_Agent.isStopped = false;
		ApplyModeToAgent();
		m_Agent.ResetPath();
		m_Agent.SetDestination(navHit.position);
		return true;
	}

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();

		if (m_RayCamera == null)
			m_RayCamera = Camera.main;

		m_Agent.updateRotation = false;
		m_Agent.stoppingDistance = m_AgentStoppingDistance;

		if (m_Animator != null)
			m_Animator.applyRootMotion = false;

		m_TargetInputMagnitude = 0f;
		m_SmoothedMagnitude = 0f;
		m_Agent.speed = m_WalkSpeed;
	}

	private void Start()
	{
		if (m_RayCamera == null)
		{
#if UNITY_2023_1_OR_NEWER
			m_RayCamera = Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Exclude);
#else
			m_RayCamera = Object.FindObjectOfType<Camera>();
#endif
			if (m_RayCamera == null)
				Debug.LogError(
					"UnitClickToMove: не назначена Ray Camera и в сцене нет камеры — клик по земле не работает. " +
					"Повесь камеру на слот или добавьте объект с тегом MainCamera.",
					this);
		}

		if (m_Agent == null || !m_AutoWarpToNavMeshOnStart || m_Agent.isOnNavMesh)
			return;

		if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, m_WarpSearchRadius, NavMesh.AllAreas))
		{
			m_Agent.Warp(hit.position);
			Debug.LogWarning("UnitClickToMove: NavMeshAgent was not on a NavMesh — moved to nearest baked surface. Adjust spawn or rebake NavMesh.", this);
		}
		else
			Debug.LogError("UnitClickToMove: not on NavMesh and no valid position within Warp Search Radius. Bake AI Navigation or move the unit.", this);
	}

	private void Update()
	{
		if (m_Agent == null)
			return;

		HandleHardStopInput();
		if (m_RayCamera != null)
			HandleClickInput();

		ProcessAnimatorCrossfades();
		UpdateRotation();
		UpdateAnimatorLocomotion();
	}
	#endregion

	#region Input
	private void HandleHardStopInput()
	{
		if (!m_EnableHardStopKey || Keyboard.current == null)
			return;

		if (!Keyboard.current[m_HardStopKey].wasPressedThisFrame)
			return;

		if (!IsAgentActuallyMoving())
			return;

		PerformHardStop();
	}

	private bool IsAgentActuallyMoving()
	{
		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > m_StopVelocityThreshold * m_StopVelocityThreshold)
			return true;

		if (!m_Agent.hasPath)
			return false;

		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	/// <summary>
	/// Cancels NavMesh movement immediately. isStopped is cleared again on the next valid click-to-move.
	/// </summary>
	private void PerformHardStop()
	{
		m_Agent.isStopped = true;
		m_Agent.ResetPath();

		if (m_FastAnimatorBlendOnHardStop)
			m_HardStopBlend = true;
	}

	private void HandleClickInput()
	{
		if (Mouse.current == null)
			return;

		if (!Mouse.current.rightButton.wasPressedThisFrame)
			return;

		if (m_BlockWhenPointerOverUi && EventSystem.current != null &&
		    EventSystem.current.IsPointerOverGameObject())
			return;

		Ray ray = m_RayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 500f, m_GroundMask, QueryTriggerInteraction.Ignore))
			return;

		if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return;

		m_Agent.isStopped = false;

		MoveMode modeBefore = m_CurrentMode;

		bool shift = Keyboard.current != null &&
		             (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

		bool doubleClick = m_LastClickTime >= 0f &&
		                   (Time.time - m_LastClickTime) <= m_DoubleClickWindow;

		if (doubleClick)
			m_CurrentMode = MoveMode.Sprint;
		else if (shift)
			m_CurrentMode = MoveMode.Run;
		else
			m_CurrentMode = MoveMode.Walk;

		m_LastClickTime = Time.time;

		if (m_CurrentMode == MoveMode.Sprint && modeBefore != MoveMode.Sprint)
			m_NeedSprintCrossfade = true;
		if (m_CurrentMode != MoveMode.Sprint && modeBefore == MoveMode.Sprint)
			m_NeedWalkingBlendCrossfade = true;

		ApplyModeToAgent();
		m_Agent.ResetPath();
		m_Agent.SetDestination(navHit.position);
	}
	#endregion

	#region Movement / Rotation
	private void ApplyModeToAgent()
	{
		switch (m_CurrentMode)
		{
			case MoveMode.Walk:
				m_Agent.speed = m_WalkSpeed;
				m_TargetInputMagnitude = Mathf.Min(m_WalkInputMagnitude, c_MaxInputMagForWalkingBlendTree);
				break;
			case MoveMode.Run:
				m_Agent.speed = m_RunSpeed;
				m_TargetInputMagnitude = Mathf.Min(m_RunInputMagnitude, c_MaxInputMagForWalkingBlendTree);
				break;
			case MoveMode.Sprint:
				m_Agent.speed = m_SprintSpeed;
				m_TargetInputMagnitude = Mathf.Max(m_SprintInputMagnitude, c_MinInputMagForSprintBranch);
				break;
		}
	}

	/// <summary>Ниже этого порога остаёмся в «Walking Blend Tree» (устойчивый бег по ±0.9 без ухода в WalkStarts).</summary>
	private const float c_MaxInputMagForWalkingBlendTree = 0.49f;

	/// <summary>Выше 0.5 в UnitController — переходы к WalkStarts / спринту.</summary>
	private const float c_MinInputMagForSprintBranch = 0.51f;

	private float GetBlendExtentForCurrentMode()
	{
		switch (m_CurrentMode)
		{
			case MoveMode.Walk:
				return m_WalkBlendExtent;
			case MoveMode.Run:
				return m_RunBlendExtent;
			case MoveMode.Sprint:
				// Пока не в под-графе Sprint, не смешиваем «бег ±0.9» как устойчивый бег — CrossFade уведёт в Sprint.
				return m_RunBlendExtent;
			default:
				return m_WalkBlendExtent;
		}
	}

	private void ProcessAnimatorCrossfades()
	{
		if (m_Animator == null)
			return;

		if (m_NeedSprintCrossfade)
		{
			m_Animator.CrossFade(m_SprintCrossFadeState, m_SprintCrossFadeDuration, 0, 0f);
			m_NeedSprintCrossfade = false;
			m_SuppressLocomotionFrames = 2;
		}

		if (m_NeedWalkingBlendCrossfade)
		{
			m_Animator.CrossFade(m_WalkingBlendCrossFadeState, m_WalkingBlendCrossFadeDuration, 0, 0f);
			m_NeedWalkingBlendCrossfade = false;
			m_SuppressLocomotionFrames = 2;
		}
	}

	private static bool IsInSprintLocomotionState(Animator _animator)
	{
		AnimatorStateInfo s = _animator.GetCurrentAnimatorStateInfo(0);
		return s.IsName("Rifle_SprintStart") || s.IsName("Rifle_SprintLoop") ||
		       s.IsName("Rifle_SprintStop_LU") || s.IsName("Rifle_SprintStop_RU");
	}

	private void UpdateRotation()
	{
		// UnitEnemyFacing крутит тело в LateUpdate; без этого следующий кадр снова тянет yaw на desiredVelocity.
		if (TryGetComponent<UnitEnemyFacing>(out UnitEnemyFacing enemyFacing) &&
		    enemyFacing.ShouldSuppressMovementRotationTowardVelocity())
			return;

		Vector3 planar = new Vector3(m_Agent.desiredVelocity.x, 0f, m_Agent.desiredVelocity.z);
		if (planar.sqrMagnitude < 0.0001f)
			return;

		Quaternion target = Quaternion.LookRotation(planar.normalized, Vector3.up);
		transform.rotation = Quaternion.Slerp(transform.rotation, target, m_RotationSpeed * Time.deltaTime);
	}
	#endregion

	#region Animator
	private void UpdateAnimatorLocomotion()
	{
		if (m_Animator == null)
			return;

		bool suppressBlendWrites = m_SuppressLocomotionFrames > 0;
		if (m_SuppressLocomotionFrames > 0)
			m_SuppressLocomotionFrames--;

		Vector3 planarVel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		float speed = planarVel.magnitude;
		bool pathPending = m_Agent.hasPath &&
		                   m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
		bool moving = speed > m_StopVelocityThreshold || pathPending;

		Vector3 worldDir;
		if (speed > m_StopVelocityThreshold)
		{
			worldDir = planarVel.normalized;
			m_LastPlanarMoveDir = worldDir;
		}
		else if (pathPending)
		{
			Vector3 toSteer = m_Agent.steeringTarget - transform.position;
			toSteer.y = 0f;
			worldDir = toSteer.sqrMagnitude > 0.0001f ? toSteer.normalized : transform.forward;
		}
		else
			worldDir = transform.forward;

		Vector3 local = transform.InverseTransformDirection(worldDir);
		if (m_StrafeDeadZone > 0f && Mathf.Abs(local.x) < m_StrafeDeadZone &&
		    Mathf.Abs(local.z) > m_StrafeDeadZone)
			local = new Vector3(0f, local.y, local.z).normalized;

		float blendExtent = GetBlendExtentForCurrentMode();
		Vector2 hvRaw = new Vector2(local.x, local.z);
		if (hvRaw.sqrMagnitude > 1e-6f)
			hvRaw = hvRaw.normalized * blendExtent;

		float inputAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

		bool sprintClipActive = m_CurrentMode == MoveMode.Sprint && IsInSprintLocomotionState(m_Animator);
		if (sprintClipActive)
		{
			if (!suppressBlendWrites)
				m_Animator.SetFloat(s_HashInputAngle, inputAngle);

			if (!moving && m_WasMoving)
			{
				float stopAngle = Vector3.SignedAngle(transform.forward, m_LastPlanarMoveDir, Vector3.up);
				m_Animator.SetFloat(s_HashWalkStopAngle, stopAngle);
				TriggerStopPivot(stopAngle);
			}

			m_WasMoving = moving;
			return;
		}

		float magTarget = moving ? m_TargetInputMagnitude : 0f;
		// Пока CrossFade ведёт в под-граф Sprint, не поднимаем InputMagnitude до ветки WalkStarts (≥0.5).
		if (moving && m_CurrentMode == MoveMode.Sprint && !IsInSprintLocomotionState(m_Animator))
			magTarget = Mathf.Min(m_RunInputMagnitude, c_MaxInputMagForWalkingBlendTree);

		if (moving && m_MatchInputMagnitudeToAgentSpeed)
		{
			float maxSpeed = Mathf.Max(m_Agent.speed, 0.01f);
			Vector3 desiredPlanar = new Vector3(m_Agent.desiredVelocity.x, 0f, m_Agent.desiredVelocity.z);
			float drive = Mathf.Max(speed, desiredPlanar.magnitude * m_DesiredVelocityForSpeedMatch);
			float speedRatio = Mathf.Clamp01(drive / maxSpeed);
			float scale = Mathf.Lerp(m_InputMagSpeedScaleFloor, 1f, speedRatio);
			magTarget *= scale;
		}

		float magSmooth = m_HardStopBlend && !moving ? 0.02f : (moving ? m_MagnitudeSmoothTimeMoving : 0.08f);
		float hvSmooth = m_HardStopBlend && !moving ? 0.02f : (moving ? m_HvSmoothTimeMoving : 0.06f);

		m_SmoothedMagnitude = Mathf.SmoothDamp(
			m_SmoothedMagnitude,
			magTarget,
			ref m_MagnitudeSmoothVel,
			magSmooth,
			Mathf.Infinity,
			Time.deltaTime);

		m_SmoothHV = Vector2.SmoothDamp(
			m_SmoothHV,
			moving ? hvRaw : Vector2.zero,
			ref m_HvSmoothVel,
			hvSmooth,
			Mathf.Infinity,
			Time.deltaTime);

		if (m_HardStopBlend && !moving && m_SmoothedMagnitude < 0.02f && m_SmoothHV.sqrMagnitude < 0.0001f)
			m_HardStopBlend = false;

		if (!suppressBlendWrites)
		{
			m_Animator.SetFloat(s_HashHorizontal, m_SmoothHV.x);
			m_Animator.SetFloat(s_HashVertical, m_SmoothHV.y);
			m_Animator.SetFloat(s_HashInputMagnitude, m_SmoothedMagnitude);
			m_Animator.SetFloat(s_HashInputAngle, inputAngle);

			if (moving && !m_WasMoving)
				m_Animator.SetFloat(s_HashWalkStartAngle, inputAngle);
		}

		if (!moving && m_WasMoving)
		{
			float stopAngle = Vector3.SignedAngle(transform.forward, m_LastPlanarMoveDir, Vector3.up);
			m_Animator.SetFloat(s_HashWalkStopAngle, stopAngle);
			TriggerStopPivot(stopAngle);
		}

		m_WasMoving = moving;
	}

	private void TriggerStopPivot(float _signedAngleFromForwardToVelocity)
	{
		if (m_StopPulseRoutine != null)
			StopCoroutine(m_StopPulseRoutine);

		m_StopPulseRoutine = StartCoroutine(StopPivotPulseRoutine(_signedAngleFromForwardToVelocity));
	}

	private IEnumerator StopPivotPulseRoutine(float _signedAngle)
	{
		// Right turn while stopping → RU, left → LU (tune if your animator expects the opposite).
		bool ru = _signedAngle > 0f;
		m_Animator.SetBool(s_HashIsStopRU, ru);
		m_Animator.SetBool(s_HashIsStopLU, !ru);

		yield return new WaitForSeconds(m_StopAnglePulseDuration);

		m_Animator.SetBool(s_HashIsStopRU, false);
		m_Animator.SetBool(s_HashIsStopLU, false);
		m_StopPulseRoutine = null;
	}
	#endregion

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_WalkInputMagnitude = Mathf.Clamp(m_WalkInputMagnitude, 0.11f, c_MaxInputMagForWalkingBlendTree);
		m_RunInputMagnitude = Mathf.Clamp(m_RunInputMagnitude, 0.11f, c_MaxInputMagForWalkingBlendTree);
		if (m_SprintInputMagnitude < c_MinInputMagForSprintBranch)
			m_SprintInputMagnitude = c_MinInputMagForSprintBranch;
	}
#endif
}
