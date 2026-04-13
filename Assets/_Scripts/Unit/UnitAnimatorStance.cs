using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Задаёт int-параметр <c>Stance</c> на Animator: стоя / присед / лёжа (только без оружия в текущей фазе).
/// Стоя: C — присесть; Z — лечь. Присед: Z — лечь, C — встать. Лёжа: Z — встать, C — в присед.
/// Z при экипированном оружии дополнительно включает «на готове» (<see cref="UnitWeaponReadyHandsLayer.EnableReadyFromStanceZInput"/>).
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
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private bool m_EnableKeyboardInput = true;
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
		"Rifle_Idle2Prone",
		"Rifle_Prone2Idle",
		"Rifle_Crouch2Prone",
		"Rifle_Prone2Crouch",
		"Pistol_Idle2Prone",
		"Pistol_Prone2Idle",
		"Pistol_Crouch2Prone",
		"Pistol_Prone2Crouch",
	};


	private LocomotionStance m_Stance = LocomotionStance.Standing;
	private bool m_PendingProne;
	private bool m_PendingStandFromProne;
	private bool m_PendingCrouchFromProne;

	#region Prone Ready Hack
	[Header("Костыль: Z из 'не готов' → сначала Ready → потом Prone")]
	[Tooltip("Если при Z юнит с оружием и в режиме 'не готов', сначала принудительно включаем Ready и только затем переводим Stance в Prone. На время костыля блокируются E/Z/C.")]
	[SerializeField] private bool m_EnableReadyBeforeProneHack = true;
	[Tooltip("Сколько секунд ждать после того, как Ready включён, прежде чем ставить Prone (чтобы успел начаться переход в 'готов').")]
	[SerializeField, Range(0f, 0.5f)] private float m_ReadyBeforeProneSettleSeconds = 0.12f;
	[Tooltip("Защита от зависания: максимум секунд на весь костыльный переход (Ready→Prone).")]
	[SerializeField, Range(0.1f, 2f)] private float m_ReadyBeforeProneTimeoutSeconds = 0.9f;

	private bool m_ReadyBeforeProneActive;
	private float m_ReadyBeforeProneStartTime;
	private float m_ReadyBeforeProneReadyTime;
	#endregion

	public LocomotionStance CurrentStance => m_Stance;

	/// <summary>Встать из приседа/лёжа без ожидания C/Z (например, заказ бега/спринта).</summary>
	public void ForceStanding()
	{
		StopReadyBeforeProneHack();
		m_PendingProne = false;
		m_PendingStandFromProne = false;
		m_PendingCrouchFromProne = false;
		m_Stance = LocomotionStance.Standing;
		PushStance();
	}

	public void SetKeyboardInputEnabled(bool _enabled)
	{
		m_EnableKeyboardInput = _enabled;
	}

	public void RequestStanding()
	{
		RequestStance(LocomotionStance.Standing);
	}

	public void RequestCrouch()
	{
		RequestStance(LocomotionStance.Crouch);
	}

	public void RequestProne()
	{
		RequestStance(LocomotionStance.Prone);
	}

	public void RequestStance(LocomotionStance _targetStance)
	{
		if (_targetStance == LocomotionStance.Standing)
		{
			StopReadyBeforeProneHack();
			m_PendingProne = false;
			m_PendingCrouchFromProne = false;

			if (m_Stance == LocomotionStance.Prone && !IsLocomotionFullyStopped())
			{
				m_PendingStandFromProne = true;
				RequestFullStop();
			}
			else
			{
				m_PendingStandFromProne = false;
				m_Stance = LocomotionStance.Standing;
			}
		}
		else if (_targetStance == LocomotionStance.Crouch)
		{
			StopReadyBeforeProneHack();
			m_PendingProne = false;
			m_PendingStandFromProne = false;

			if (m_Stance == LocomotionStance.Prone && !IsLocomotionFullyStopped())
			{
				m_PendingCrouchFromProne = true;
				RequestFullStop();
			}
			else
			{
				m_PendingCrouchFromProne = false;
				m_Stance = LocomotionStance.Crouch;
			}
		}
		else
		{
			m_PendingStandFromProne = false;
			m_PendingCrouchFromProne = false;

			if (m_EnableReadyBeforeProneHack && ShouldStartReadyBeforeProneHack())
			{
				StartReadyBeforeProneHack();
				PushStance();
				UpdateBusyFlag();
				return;
			}

			if (m_ReadyHands != null)
				m_ReadyHands.EnableReadyFromStanceZInput();

			if (!IsLocomotionFullyStopped())
			{
				m_PendingProne = true;
				RequestFullStop();
			}
			else
			{
				m_PendingProne = false;
				m_Stance = LocomotionStance.Prone;
			}
		}

		PushStance();
		UpdateBusyFlag();
	}

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
	}

	private void OnEnable()
	{
		PushStance();
		UpdateBusyFlag();
	}

	private void Update()
	{
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();

		if (m_ReadyBeforeProneActive || m_PendingProne || m_PendingStandFromProne || m_PendingCrouchFromProne)
			RequestFullStop();

		if (m_ReadyBeforeProneActive)
		{
			TickReadyBeforeProneHack();
			PushStance();
			UpdateBusyFlag();
			return;
		}

		// ВАЖНО: пока проигрывается переход стойки (стейт из списка) или идёт transition,
		// мы не должны учитывать новые Z/C, иначе Stance в аниматоре поменяется и по завершении клипа произойдёт "мгновенный" обратный переход.
		if (ShouldBlockKeyboardStanceInput())
		{
			PushStance();
			UpdateBusyFlag();
			return;
		}

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

		// Пока юнит "занят" (любой причиной) — не читаем Z/C.
		// Важно: обработка pending-логики выше всё равно выполнится, чтобы текущий переход смог завершиться.
		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			PushStance();
			UpdateBusyFlag();
			return;
		}

		bool allowKeyboardInput = CanUseDirectKeyboardInput();
		bool zPressed = allowKeyboardInput && WasZPressedThisFrame();
		bool cPressed = allowKeyboardInput && WasCrouchKeyPressedThisFrame();

		if (zPressed && m_ReadyHands != null)
		{
			// Костыль: если Z нажали из "не готов" с оружием — сначала включаем Ready, даём начаться переходу, потом ставим Prone.
			if (m_EnableReadyBeforeProneHack && ShouldStartReadyBeforeProneHack())
			{
				StartReadyBeforeProneHack();
				PushStance();
				return;
			}

			m_ReadyHands.EnableReadyFromStanceZInput();
		}

		if (m_PendingProne || m_PendingStandFromProne || m_PendingCrouchFromProne)
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
		UpdateBusyFlag();
	}

	#region Ready Before Prone Hack
	private bool ShouldStartReadyBeforeProneHack()
	{
		if (m_ReadyHands == null)
			return false;

		// Важно: костыль нужен только когда Z реально ведёт в prone (из Standing/Crouch).
		if (m_Stance == LocomotionStance.Prone)
			return false;

		// Если уже готов — обычная логика.
		if (m_ReadyHands.IsWeaponEquippedAndReady())
			return false;

		// В присяде «не готов» больше не переключает базовый граф на безоружный — проверяем намерение пользователя напрямую.
		return m_ReadyHands.IsEquippedWeaponUserNotReady();
	}

	private void StartReadyBeforeProneHack()
	{
		m_ReadyBeforeProneActive = true;
		m_ReadyBeforeProneStartTime = Time.time;
		m_ReadyBeforeProneReadyTime = -1f;

		if (m_ReadyHands != null)
		{
			m_ReadyHands.SetToggleInputBlocked(true);
			m_ReadyHands.EnableReadyFromStanceZInput();
		}

		// Мы всегда идём в prone (как обычный Z из standing/crouch), но только после ready.
		RequestFullStop();
	}

	private void StopReadyBeforeProneHack()
	{
		if (!m_ReadyBeforeProneActive)
			return;

		m_ReadyBeforeProneActive = false;
		m_ReadyBeforeProneStartTime = 0f;
		m_ReadyBeforeProneReadyTime = 0f;

		if (m_ReadyHands != null)
			m_ReadyHands.SetToggleInputBlocked(false);
	}

	private void TickReadyBeforeProneHack()
	{
		// Пока костыль активен — игнорируем любые новые стойки/ready input (Z/C/E) просто тем, что мы выходим ранним return.

		// Дожидаемся полной остановки, иначе prone может начать мешать navigation/блендам.
		if (!IsLocomotionFullyStopped())
		{
			RequestFullStop();
			return;
		}

		bool isReadyNow = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		if (isReadyNow && m_ReadyBeforeProneReadyTime < 0f)
			m_ReadyBeforeProneReadyTime = Time.time;

		bool settleOk = m_ReadyBeforeProneReadyTime >= 0f &&
		                (Time.time - m_ReadyBeforeProneReadyTime) >= m_ReadyBeforeProneSettleSeconds;

		bool timedOut = (Time.time - m_ReadyBeforeProneStartTime) >= m_ReadyBeforeProneTimeoutSeconds;

		if (!settleOk && !timedOut)
			return;

		// Теперь можно ставить prone.
		if (m_Stance != LocomotionStance.Prone)
			m_Stance = LocomotionStance.Prone;

		StopReadyBeforeProneHack();
	}
	#endregion

	private void RequestFullStop()
	{
		if (m_Agent == null || !m_Agent.isOnNavMesh)
			return;

		m_Agent.isStopped = true;
		m_Agent.ResetPath();
	}

	private bool CanUseDirectKeyboardInput()
	{
		if (!m_EnableKeyboardInput)
			return false;
		if (m_Team == null)
			return true;

		return m_Team.Team == UnitTeamId.Player;
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
			return ShouldBlockByTransitionClipNamesFallback(info);

		for (int i = 0; i < m_BlockStanceInputUntilEndOfState.Length; i++)
		{
			string stateName = m_BlockStanceInputUntilEndOfState[i];
			if (string.IsNullOrEmpty(stateName))
				continue;
			// info.IsName(...) часто требует полный путь (особенно если стейт вложен в sub-state machine).
			// Для наших переходных клипов достаточно совпадения по shortNameHash.
			int shortHash = Animator.StringToHash(stateName);
			if (info.shortNameHash != shortHash)
				continue;

			float nt = info.normalizedTime;
			if (info.loop)
				nt %= 1f;
			return nt < 0.99f;
		}

		// Фолбэк: если стейт переименован/не в списке — всё равно блокируем по имени клипа перехода.
		return ShouldBlockByTransitionClipNamesFallback(info);
	}

	private bool ShouldBlockByTransitionClipNamesFallback(AnimatorStateInfo _info)
	{
		if (m_Animator == null)
			return false;

		AnimatorClipInfo[] clips = m_Animator.GetCurrentAnimatorClipInfo(0);
		if (clips == null || clips.Length == 0)
			return false;

		float nt = _info.normalizedTime;
		if (_info.loop)
			nt %= 1f;
		if (nt >= 0.99f)
			return false;

		for (int i = 0; i < clips.Length; i++)
		{
			AnimationClip c = clips[i].clip;
			if (c == null)
				continue;

			string n = c.name;
			if (string.IsNullOrEmpty(n))
				continue;

			if (n.Contains("Idle2Prone") ||
			    n.Contains("Prone2Idle") ||
			    n.Contains("Crouch2Prone") ||
			    n.Contains("Prone2Crouch") ||
			    n.Contains("Idle2Crouch"))
				return true;
		}

		return false;
	}



	private void PushStance()
	{
		if (m_Animator != null)
			m_Animator.SetInteger(s_Stance, (int)m_Stance);
	}

	private void UpdateBusyFlag()
	{
		if (m_BusyState == null)
			return;

		// "Занят" на время смены стойки: pending-остановка, костыль ready→prone, transition и проигрывание переходного стейта.
		bool busy =
			m_ReadyBeforeProneActive ||
			m_PendingProne || m_PendingStandFromProne || m_PendingCrouchFromProne ||
			ShouldBlockKeyboardStanceInput();

		m_BusyState.SetReasonActive(UnitBusyState.BusyReason.StanceTransition, busy);
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
