using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Задаёт int-параметр <c>Stance</c> на Animator: стоя / присед / лёжа (только без оружия в текущей фазе).
/// Стоя: C — присесть (одно нажатие); Z — лечь.
/// Из приседа или лёжа: короткое Z или C — встать в стойку.
/// Клипы prone сейчас из RifleAnimsetPro_CrouchAndProne (винтовка) — временный плейсхолдер до отдельных безоружных анимаций.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorStance : MonoBehaviour
{
	public const string ParamStance = "Stance";

	private static readonly int s_Stance = Animator.StringToHash(ParamStance);

	[SerializeField] private Animator m_Animator;
	[Tooltip("Если true, дополнительно к C используется левый Ctrl (удобно при раскладке / когда current-клавиатура не та).")]
	[SerializeField] private bool m_LeftCtrlAlsoTogglesCrouch = true;

	private LocomotionStance m_Stance = LocomotionStance.Standing;

	public LocomotionStance CurrentStance => m_Stance;

	/// <summary>Встать из приседа/лёжа без ожидания C/Z (например, заказ бега/спринта).</summary>
	public void ForceStanding()
	{
		m_Stance = LocomotionStance.Standing;
		PushStance();
	}

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
	}

	private void OnEnable()
	{
		PushStance();
	}

	private void Update()
	{
		bool zPressed = WasZPressedThisFrame();
		bool cPressed = WasCrouchKeyPressedThisFrame();

		if (m_Stance == LocomotionStance.Prone)
		{
			if (zPressed || cPressed)
				m_Stance = LocomotionStance.Standing;
		}
		else if (m_Stance == LocomotionStance.Crouch)
		{
			if (zPressed || cPressed)
				m_Stance = LocomotionStance.Standing;
		}
		else
		{
			if (zPressed)
				m_Stance = LocomotionStance.Prone;
			else if (cPressed)
				m_Stance = LocomotionStance.Crouch;
		}

		PushStance();
	}

	private void PushStance()
	{
		if (m_Animator != null)
			m_Animator.SetInteger(s_Stance, (int)m_Stance);
	}

	/// <summary>Обход всех подключённых клавиатур: <see cref="Keyboard.current"/> иногда null или не та раскладка/устройство.</summary>
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
