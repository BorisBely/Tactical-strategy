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

	private LocomotionStance m_Stance = LocomotionStance.Standing;

	public LocomotionStance CurrentStance => m_Stance;

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
		if (Keyboard.current == null)
			return;

		bool zPressed = Keyboard.current.zKey.wasPressedThisFrame;
		bool cPressed = Keyboard.current.cKey.wasPressedThisFrame;

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
}
