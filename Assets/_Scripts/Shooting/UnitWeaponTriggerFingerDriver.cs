using UnityEngine;

/// <summary>
/// Нажатие на спусковой крючок при успешном выстреле (<see cref="UnitWeaponFireController.ShotFired"/>).
/// Значение не «перезапускается с нуля» на каждый выстрел: при выстреле поднимается к 1 (если уже ниже),
/// затем плавно <b>падает</b> к 0 через <see cref="Mathf.SmoothDamp"/>. Очередь удерживает палец вверху без рывка вниз между патронами.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponTriggerFingerDriver : MonoBehaviour
{
	#region Constants
	public const string ParamTriggerPress = "TriggerPress";
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private Animator m_Animator;
	[Tooltip("Писать ли float в Animator. Выключи, если параметр ещё не добавлен в Controller.")]
	[SerializeField] private bool m_DriveAnimatorParameter = true;
	[Tooltip("Имя float в Animator (1D blend / вес в Blend Tree).")]
	[SerializeField] private string m_AnimatorParameterName = ParamTriggerPress;
	[Header("Падение к покою")]
	[Tooltip("Примерное время сглаженного спада TriggerPress к 0 после выстрела (SmoothDamp). Меньше — быстрее отпускает курок.")]
	[SerializeField, Min(0.02f)] private float m_FallSmoothTime = 0.3f;
	#endregion

	#region Private Fields
	private int m_ParameterHash;
	private float m_Trigger01;
	private float m_FallVelocity;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();

		ResolveParameterHash();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;

		m_Trigger01 = 0f;
		m_FallVelocity = 0f;
		SetAnimatorTrigger01(0f);
	}

	private void Update()
	{
		if (!m_DriveAnimatorParameter || m_Animator == null)
			return;

		float smooth = Mathf.Max(0.02f, m_FallSmoothTime);
		m_Trigger01 = Mathf.SmoothDamp(m_Trigger01, 0f, ref m_FallVelocity, smooth, Mathf.Infinity, Time.deltaTime);
		if (m_Trigger01 < 0.0005f)
		{
			m_Trigger01 = 0f;
			m_FallVelocity = 0f;
		}

		SetAnimatorTrigger01(m_Trigger01);
	}
	#endregion

	#region Private Methods
	private void ResolveParameterHash()
	{
		if (string.IsNullOrEmpty(m_AnimatorParameterName))
			m_AnimatorParameterName = ParamTriggerPress;
		m_ParameterHash = Animator.StringToHash(m_AnimatorParameterName);
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (!isActiveAndEnabled)
			return;

		m_Trigger01 = Mathf.Max(m_Trigger01, 1f);
		m_FallVelocity = 0f;
	}

	private void SetAnimatorTrigger01(float _value)
	{
		if (!m_DriveAnimatorParameter || m_Animator == null)
			return;

		if (!m_Animator.isInitialized)
			return;

		m_Animator.SetFloat(m_ParameterHash, _value);
	}
	#endregion

#if UNITY_EDITOR
	private void OnValidate()
	{
		ResolveParameterHash();
	}
#endif
}
