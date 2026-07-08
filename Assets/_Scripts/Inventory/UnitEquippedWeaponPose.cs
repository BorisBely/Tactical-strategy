using UnityEngine;

/// <summary>
/// Плавный переход локальной позы экипированного оружия между relaxed («не готов») и ready («готов»)
/// по <see cref="UnitWeaponReadyHandsLayer"/>. Единственная точка установки localPosition/localRotation на <see cref="UnitEquipment.MainWeaponRoot"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45)]
public sealed class UnitEquippedWeaponPose : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoading;
	[SerializeField] private UnitWeaponReloadController m_WeaponReload;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitRagdollController m_RagdollController;

	[Header("Переход Ready / Relaxed")]
	[SerializeField, Min(0f)] private float m_ReadyPoseBlendDuration = 0.28f;
	[Tooltip("Кривая веса ready-позы. Пустая — SmoothStep.")]
	[SerializeField] private AnimationCurve m_ReadyPoseBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	#endregion

	#region Private Fields
	private float m_ReadyBlend01;
	private float m_BlendStartReady01;
	private float m_TargetReadyBlend01;
	private bool m_IsReadyBlendAnimating;
	private float m_ReadyBlendElapsed;
	private int m_LastReadyBlendAdvanceFrame = -1;

	private Vector3 m_CurrentBaseWeaponLocalPosition;
	private Quaternion m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
	#endregion

	#region Public Properties
	/// <summary>Текущий вес ready-позы (0 = relaxed, 1 = ready).</summary>
	public float ReadyPoseBlend01 => m_ReadyBlend01;

	/// <summary>Локальная позиция оружия после бленда relaxed/ready (без aim-correction).</summary>
	public Vector3 CurrentBaseWeaponLocalPosition => m_CurrentBaseWeaponLocalPosition;

	/// <summary>Локальный поворот оружия после бленда relaxed/ready (без aim-correction).</summary>
	public Quaternion CurrentBaseWeaponLocalRotation => m_CurrentBaseWeaponLocalRotation;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
		SyncReadyTargetImmediate();
		m_ReadyBlend01 = m_TargetReadyBlend01;
		m_IsReadyBlendAnimating = false;
		ApplyWeaponLocalPose();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		StopReadyBlend();
	}

	private void Update()
	{
		if (IsBlockedByRagdoll())
			return;

		AdvanceReadyBlend();
		ApplyWeaponLocalPose();
	}
	#endregion

	#region Public Methods
	/// <summary>Вызывать при смене WeaponReady (E / ИИ).</summary>
	public void OnWeaponReadyStateChanged()
	{
		SyncReadyTargetImmediate();
		m_BlendStartReady01 = m_ReadyBlend01;

		if (m_ReadyPoseBlendDuration <= 0f)
		{
			m_ReadyBlend01 = m_TargetReadyBlend01;
			StopReadyBlend();
		}
		else
		{
			m_IsReadyBlendAnimating = true;
			m_ReadyBlendElapsed = 0f;
			m_LastReadyBlendAdvanceFrame = -1;
		}

		ApplyWeaponLocalPose();
	}

	/// <summary>Мгновенно выставить позу по текущему ready-состоянию (например после экипировки).</summary>
	public void ApplyImmediateFromEquipment()
	{
		SyncReadyTargetImmediate();
		m_ReadyBlend01 = m_TargetReadyBlend01;
		StopReadyBlend();
		ApplyWeaponLocalPose();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponentInParent<UnitWeaponReadyHandsLayer>();

		if (m_MagazineLoading == null)
			m_MagazineLoading = GetComponentInParent<UnitMagazineLoadingController>();
		if (m_WeaponReload == null)
			m_WeaponReload = GetComponentInParent<UnitWeaponReloadController>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponentInParent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponentInParent<UnitStabilizeOtherController>();
		if (m_BusyState == null)
			m_BusyState = GetComponentInParent<UnitBusyState>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponentInParent<UnitRagdollController>();
	}

	private bool IsBlockedByRagdoll()
	{
		return m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts;
	}

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void HandleEquipmentChanged()
	{
		ApplyImmediateFromEquipment();
	}

	private void SyncReadyTargetImmediate()
	{
		m_TargetReadyBlend01 = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady() ? 1f : 0f;
	}

	private void StopReadyBlend()
	{
		m_IsReadyBlendAnimating = false;
		m_ReadyBlendElapsed = 0f;
		m_LastReadyBlendAdvanceFrame = -1;
	}

	private void AdvanceReadyBlend()
	{
		if (!m_IsReadyBlendAnimating)
			return;

		if (m_LastReadyBlendAdvanceFrame != Time.frameCount)
		{
			m_LastReadyBlendAdvanceFrame = Time.frameCount;
			m_ReadyBlendElapsed += Time.deltaTime;
		}

		float duration = Mathf.Max(0.0001f, m_ReadyPoseBlendDuration);
		float normalizedTime = Mathf.Clamp01(m_ReadyBlendElapsed / duration);
		float curveT = m_ReadyPoseBlendCurve != null && m_ReadyPoseBlendCurve.length > 0
			? m_ReadyPoseBlendCurve.Evaluate(normalizedTime)
			: Mathf.SmoothStep(0f, 1f, normalizedTime);

		m_ReadyBlend01 = Mathf.Lerp(m_BlendStartReady01, m_TargetReadyBlend01, curveT);

		if (normalizedTime >= 1f)
		{
			m_ReadyBlend01 = m_TargetReadyBlend01;
			StopReadyBlend();
		}
	}

	private void ApplyWeaponLocalPose()
	{
		if (m_UnitEquipment == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
		{
			m_CurrentBaseWeaponLocalPosition = Vector3.zero;
			m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
			return;
		}

		Vector3 relaxedPosition = def.RightHandLocalPosition;
		Quaternion relaxedRotation = def.RightHandLocalRotation;
		Vector3 readyPosition = def.RightHandReadyLocalPosition;
		Quaternion readyRotation = def.RightHandReadyLocalRotation;
		if (readyPosition == Vector3.zero && def.RightHandReadyLocalEulerAngles == Vector3.zero)
		{
			readyPosition = relaxedPosition;
			readyRotation = relaxedRotation;
		}

		m_CurrentBaseWeaponLocalPosition = Vector3.Lerp(relaxedPosition, readyPosition, m_ReadyBlend01);
		m_CurrentBaseWeaponLocalRotation = Quaternion.Slerp(relaxedRotation, readyRotation, m_ReadyBlend01);

		weaponRoot.localPosition = m_CurrentBaseWeaponLocalPosition;
		weaponRoot.localRotation = m_CurrentBaseWeaponLocalRotation;
	}
	#endregion
}
