using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Задаёт int-параметр <c>Stance</c> на Animator: стоя / присед / лёжа (только без оружия в текущей фазе).
/// Стоя: C — присесть; Z — лечь. Присед: Z — лечь, C — встать. Лёжа: Z — встать, C — в присед.
/// Переход в лёжа и выход из лёжа: сначала полная остановка NavMeshAgent, затем смена <c>Stance</c> (анимация).
/// Лёжа: клипы Prone_* в NavMeshLocomotion пока из RifleAnimsetPro_CrouchAndProne — плейсхолдер до отдельных безоружных.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorStance : MonoBehaviour
{
	public const string ParamStance = "Stance";

	private static readonly int s_Stance = Animator.StringToHash(ParamStance);

	[SerializeField] private Animator m_Animator;
	[SerializeField] private NavMeshAgent m_Agent;
	[Tooltip("Если true, дополнительно к C используется левый Ctrl (удобно при раскладке / когда current-клавиатура не та).")]
	[SerializeField] private bool m_LeftCtrlAlsoTogglesCrouch = true;
	[Tooltip("Планарная скорость агента ниже этого порога считается остановкой (согласуй с UnitClickToMove).")]
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;

	[Header("Анти-спам Z/C")]
	[Tooltip("Пока играется один из этих стейтов на слое 0 или идёт Transition, Z/C для смены стойки игнорируются (иначе Stance и граф расходятся).")]
	[SerializeField] private string[] m_BlockStanceInputUntilEndOfState =
	{
		"Unarmed_Idle2Prone",
		"Unarmed_Prone2Idle",
		"Unarmed_Idle2Crouch",
		"Unarmed_Crouch2Prone",
		"Unarmed_Prone2Crouch",
		"Rifle_Crouch2Prone",
		"Rifle_Prone2Crouch",
	};

	private LocomotionStance m_Stance = LocomotionStance.Standing;
	private bool m_PendingProne;
	private bool m_PendingStandFromProne;
	private bool m_PendingCrouchFromProne;

	public LocomotionStance CurrentStance => m_Stance;

	/// <summary>Встать из приседа/лёжа без ожидания C/Z (например, заказ бега/спринта).</summary>
	public void ForceStanding()
	{
		m_PendingProne = false;
		m_PendingStandFromProne = false;
		m_PendingCrouchFromProne = false;
		m_Stance = LocomotionStance.Standing;
		PushStance();
	}

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();
	}

	private void OnEnable()
	{
		PushStance();
	}

	private void Update()
	{
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();

		if (m_PendingProne || m_PendingStandFromProne || m_PendingCrouchFromProne)
			RequestFullStop();

		if (IsLocomotionFullyStopped())
		{
			if (m_PendingProne && m_Stance != LocomotionStance.Prone)
			{
				m_PendingProne = false;
				m_Stance = LocomotionStance.Prone;
				PushStance();
				return;
			}

			if (m_PendingStandFromProne && m_Stance == LocomotionStance.Prone)
			{
				m_PendingStandFromProne = false;
				m_Stance = LocomotionStance.Standing;
				PushStance();
				return;
			}

			if (m_PendingCrouchFromProne && m_Stance == LocomotionStance.Prone)
			{
				m_PendingCrouchFromProne = false;
				m_Stance = LocomotionStance.Crouch;
				PushStance();
				return;
			}
		}

		bool zPressed = WasZPressedThisFrame();
		bool cPressed = WasCrouchKeyPressedThisFrame();

		if (m_PendingProne || m_PendingStandFromProne || m_PendingCrouchFromProne)
		{
			PushStance();
			return;
		}

		if (ShouldBlockKeyboardStanceInput())
		{
			PushStance();
			return;
		}

		if (m_Stance == LocomotionStance.Prone)
		{
			if (zPressed)
			{
				if (!IsLocomotionFullyStopped())
				{
					m_PendingStandFromProne = true;
					RequestFullStop();
				}
				else
					m_Stance = LocomotionStance.Standing;
			}
			else if (cPressed)
			{
				if (!IsLocomotionFullyStopped())
				{
					m_PendingCrouchFromProne = true;
					RequestFullStop();
				}
				else
					m_Stance = LocomotionStance.Crouch;
			}
		}
		else if (m_Stance == LocomotionStance.Crouch)
		{
			if (zPressed)
			{
				if (!IsLocomotionFullyStopped())
				{
					m_PendingProne = true;
					RequestFullStop();
				}
				else
					m_Stance = LocomotionStance.Prone;
			}
			else if (cPressed)
				m_Stance = LocomotionStance.Standing;
		}
		else
		{
			if (zPressed)
			{
				if (!IsLocomotionFullyStopped())
				{
					m_PendingProne = true;
					RequestFullStop();
				}
				else
					m_Stance = LocomotionStance.Prone;
			}
			else if (cPressed)
				m_Stance = LocomotionStance.Crouch;
		}

		PushStance();
	}

	private void RequestFullStop()
	{
		if (m_Agent == null || !m_Agent.isOnNavMesh)
			return;

		m_Agent.isStopped = true;
		m_Agent.ResetPath();
	}

	private bool IsLocomotionFullyStopped()
	{
		if (m_Agent == null || !m_Agent.isOnNavMesh)
			return true;

		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		float eps = m_StopVelocityEpsilon;
		if (v.sqrMagnitude > eps * eps)
			return false;

		if (m_Agent.pathPending)
			return false;

		if (m_Agent.hasPath &&
		    !float.IsPositiveInfinity(m_Agent.remainingDistance) &&
		    m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f)
			return false;

		return true;
	}

	private bool ShouldBlockKeyboardStanceInput()
	{
		if (m_Animator == null)
			return false;

		if (m_Animator.IsInTransition(0))
			return true;

		AnimatorStateInfo info = m_Animator.GetCurrentAnimatorStateInfo(0);
		if (info.fullPathHash == 0)
			return false;

		if (m_BlockStanceInputUntilEndOfState == null || m_BlockStanceInputUntilEndOfState.Length == 0)
			return false;

		for (int i = 0; i < m_BlockStanceInputUntilEndOfState.Length; i++)
		{
			string stateName = m_BlockStanceInputUntilEndOfState[i];
			if (string.IsNullOrEmpty(stateName))
				continue;
			if (!info.IsName(stateName))
				continue;

			float nt = info.normalizedTime;
			if (info.loop)
				nt %= 1f;
			return nt < 0.99f;
		}

		return false;
	}

	private void PushStance()
	{
		if (m_Animator != null)
			m_Animator.SetInteger(s_Stance, (int)m_Stance);
	}

	private static bool WasZPressedThisFrame()
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;
			if (kb.zKey.wasPressedThisFrame)
				return true;
		}

		return false;
	}

	private bool WasCrouchKeyPressedThisFrame()
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;
			if (kb.cKey.wasPressedThisFrame)
				return true;
			if (m_LeftCtrlAlsoTogglesCrouch && kb.leftCtrlKey.wasPressedThisFrame)
				return true;
		}

		return false;
	}
}
