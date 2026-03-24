using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Click-to-move for NavMeshAgent + UnitController animator (RifleAnimsetPro / TPP).
/// Root motion off on Animator; agent moves the body; rotation is manual (updateRotation = false).
/// Input: single click = walk, Shift+click = run, double click = sprint (second click within a time window).
/// Hard stop key (default Space): cancel path immediately; plays the same stop / pivot logic as arriving at a point.
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
	[SerializeField] private Camera m_RayCamera;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField, Min(0.01f)] private float m_NavMeshSampleRadius = 2f;

	[Header("Agent speeds")]
	[SerializeField, Min(0.1f)] private float m_WalkSpeed = 1.6f;
	[SerializeField, Min(0.1f)] private float m_RunSpeed = 3.6f;
	[SerializeField, Min(0.1f)] private float m_SprintSpeed = 6.5f;

	[Header("Animator — matches UnitController thresholds (see transitions around 0.1 / 0.5)")]
	[Tooltip("Idle vs locomotion: keep < 0.5 to stay in Walking blend; typically 0.1–0.45.")]
	[SerializeField, Range(0.11f, 0.49f)] private float m_WalkInputMagnitude = 0.35f;
	[Tooltip("Run: above 0.5 switches to the faster locomotion branch.")]
	[SerializeField, Range(0.51f, 0.95f)] private float m_RunInputMagnitude = 0.7f;
	[Tooltip("Sprint: high input on the run/sprint side.")]
	[SerializeField, Range(0.51f, 1f)] private float m_SprintInputMagnitude = 1f;

	[Header("Blend tree shaping (Horizontal / Vertical)")]
	[Tooltip("Scales local move direction into the 2D blend (walk = smaller, sprint = full).")]
	[SerializeField, Range(0.1f, 1f)] private float m_WalkBlendScale = 0.35f;
	[SerializeField, Range(0.1f, 1f)] private float m_RunBlendScale = 0.78f;
	[SerializeField, Range(0.1f, 1f)] private float m_SprintBlendScale = 1f;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_RotationSpeed = 10f;

	[Header("Input")]
	[SerializeField, Min(0.05f)] private float m_DoubleClickWindow = 0.35f;
	[SerializeField] private bool m_BlockWhenPointerOverUi = true;

	[Header("Hard stop (cancel movement)")]
	[SerializeField] private bool m_EnableHardStopKey = true;
	[SerializeField] private Key m_HardStopKey = Key.Space;
	[Tooltip("When true, InputMagnitude / H/V snap down faster after a hard stop (still runs WalkStopAngle + pivot).")]
	[SerializeField] private bool m_FastAnimatorBlendOnHardStop = true;

	[Header("Stopping — IsStopRU / IsStopLU")]
	[SerializeField, Min(0.01f)] private float m_StopVelocityThreshold = 0.12f;
	[SerializeField, Min(0.01f)] private float m_StopAnglePulseDuration = 0.12f;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private float m_LastClickTime = -1f;
	private MoveMode m_CurrentMode = MoveMode.Walk;
	private float m_TargetInputMagnitude;
	private float m_BlendScale;
	private float m_SmoothedMagnitude;
	private float m_MagnitudeSmoothVel;
	private Vector2 m_SmoothHV;
	private Vector2 m_HvSmoothVel;
	private bool m_WasMoving;
	private Vector3 m_LastPlanarMoveDir;
	private Coroutine m_StopPulseRoutine;
	private bool m_HardStopBlend;
	#endregion

	private enum MoveMode
	{
		Walk,
		Run,
		Sprint
	}

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();

		m_Agent.updateRotation = false;

		if (m_Animator != null)
			m_Animator.applyRootMotion = false;

		m_TargetInputMagnitude = 0f;
		m_BlendScale = m_WalkBlendScale;
		m_SmoothedMagnitude = 0f;
	}

	private void Update()
	{
		if (m_RayCamera == null || m_Agent == null)
			return;

		HandleHardStopInput();
		HandleClickInput();
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

		if (!Mouse.current.leftButton.wasPressedThisFrame)
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

		bool shift = Keyboard.current != null &&
		             (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

		bool doubleClick = m_LastClickTime >= 0f &&
		                   (Time.time - m_LastClickTime) <= m_DoubleClickWindow;
		m_LastClickTime = Time.time;

		if (doubleClick)
			m_CurrentMode = MoveMode.Sprint;
		else if (shift)
			m_CurrentMode = MoveMode.Run;
		else
			m_CurrentMode = MoveMode.Walk;

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
				m_TargetInputMagnitude = m_WalkInputMagnitude;
				m_BlendScale = m_WalkBlendScale;
				break;
			case MoveMode.Run:
				m_Agent.speed = m_RunSpeed;
				m_TargetInputMagnitude = m_RunInputMagnitude;
				m_BlendScale = m_RunBlendScale;
				break;
			case MoveMode.Sprint:
				m_Agent.speed = m_SprintSpeed;
				m_TargetInputMagnitude = m_SprintInputMagnitude;
				m_BlendScale = m_SprintBlendScale;
				break;
		}
	}

	private void UpdateRotation()
	{
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

		Vector3 planarVel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		float speed = planarVel.magnitude;
		bool pathPending = m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
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
		Vector2 hvRaw = new Vector2(local.x, local.z);
		if (hvRaw.sqrMagnitude > 1e-6f)
			hvRaw = hvRaw.normalized * m_BlendScale;

		float inputAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

		float magTarget = moving ? m_TargetInputMagnitude : 0f;
		float magSmooth = m_HardStopBlend && !moving ? 0.02f : 0.08f;
		float hvSmooth = m_HardStopBlend && !moving ? 0.02f : 0.06f;

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

		m_Animator.SetFloat(s_HashHorizontal, m_SmoothHV.x);
		m_Animator.SetFloat(s_HashVertical, m_SmoothHV.y);
		m_Animator.SetFloat(s_HashInputMagnitude, m_SmoothedMagnitude);
		m_Animator.SetFloat(s_HashInputAngle, inputAngle);

		if (moving && !m_WasMoving)
			m_Animator.SetFloat(s_HashWalkStartAngle, inputAngle);

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
		m_WalkInputMagnitude = Mathf.Clamp(m_WalkInputMagnitude, 0.11f, 0.49f);
		if (m_RunInputMagnitude < 0.51f)
			m_RunInputMagnitude = 0.55f;
		if (m_SprintInputMagnitude < m_RunInputMagnitude)
			m_SprintInputMagnitude = Mathf.Max(m_RunInputMagnitude, 0.95f);
	}
#endif
}
