using UnityEngine;

/// <summary>
/// IK левой кисти к цели на экипированном оружии (<see cref="UnitEquipment.LeftHandIkTargetTransform"/>).
/// Компонент нужно повесить на тот же GameObject, где висит <see cref="Animator"/> (Humanoid).
/// В Animator Controller у слоя с движением должен быть включён <b>IK Pass</b> (сейчас: Aim_Point_U90-D90).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class AnimatorHandIk : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение на корне юнита (родитель или сам юнит с CharacterInventory).")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Пока идёт ручная зарядка магазина (T), IK левой руки отключается.")]
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoading;
	[Tooltip("Пока идёт перезарядка оружия (R), IK левой руки отключается.")]
	[SerializeField] private UnitWeaponReloadController m_WeaponReload;
	[Tooltip("Пока идёт самостабилизация IFAK, IK левой руки отключается.")]
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[Tooltip("Пока идёт стабилизация другого юнита, IK левой руки отключается.")]
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[Tooltip("Пока юнит тащит сражённого, IK левой руки отключается (рука уходит на drag-слой).")]
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandRotationWeight = 1f;
	[Header("Экипировка")]
	[Tooltip("Длительность плавного подтягивания левой руки к IK-точке после появления оружия. 0 — мгновенно.")]
	[SerializeField, Min(0f)] private float m_EquipBlendDuration = 0.35f;
	[Tooltip("Кривая веса IK при экипировке. Пустая — SmoothStep.")]
	[SerializeField] private AnimationCurve m_EquipBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	[Header("Локоть (подсказка IK)")]
	[SerializeField] private bool m_UseLeftElbowHint;
	[Tooltip("Пустой объект перед локтем слева (в пространстве персонажа), чтобы сгиб был естественнее.")]
	[SerializeField] private Transform m_LeftElbowHint;
	[SerializeField, Range(0f, 1f)] private float m_LeftElbowHintWeight = 1f;
	[Header("Отладка")]
	[SerializeField] private bool m_DrawIkTargetGizmo;
	#endregion

	#region Private Fields
	private Animator m_Animator;
	/// <summary>Запрошен сброс IK из кода вне <see cref="OnAnimatorIK"/> (например при «готов», пока идёт зарядка/перезарядка).</summary>
	private bool m_ClearLeftHandIkOnNextAnimatorIkPass;
	private bool m_IsEquipBlendActive;
	private float m_EquipBlendElapsed;
	private int m_LastEquipBlendAdvanceFrame = -1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Animator = GetComponent<Animator>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
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
	}

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		StopEquipBlend();
	}

	private void OnDrawGizmosSelected()
	{
		if (!m_DrawIkTargetGizmo || !Application.isPlaying)
			return;

		Transform ikTarget = ResolveLiveLeftHandIkTarget();
		if (ikTarget == null)
			return;

		Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
		Gizmos.DrawSphere(ikTarget.position, 0.015f);
		Gizmos.DrawLine(ikTarget.position, ikTarget.position + ikTarget.forward * 0.06f);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// При переходе в «готов»: если IK должен быть выключен (зарядка/перезарядка), запрашиваем сброс в ближайшем
	/// <see cref="OnAnimatorIK"/> — вызывать SetIK* из Update/LateUpdate нельзя (требование Unity).
	/// Если блокировок нет, ничего не делаем: тот же кадр или следующий <see cref="OnAnimatorIK"/> сам выставит IK.
	/// </summary>
	public void OnWeaponReadyStateApplied()
	{
		if (IsLeftHandIkBlocked())
		{
			StopEquipBlend();
			m_ClearLeftHandIkOnNextAnimatorIkPass = true;
		}
	}

	/// <summary>Сбросить IK в ближайшем <see cref="OnAnimatorIK"/> (вызов вне IK-pass).</summary>
	public void RequestClearLeftHandIk()
	{
		StopEquipBlend();
		m_ClearLeftHandIkOnNextAnimatorIkPass = true;
	}
	#endregion

	#region IK
	private void OnAnimatorIK(int _layerIndex)
	{
		if (m_Animator == null)
			return;

		if (m_ClearLeftHandIkOnNextAnimatorIkPass)
		{
			m_ClearLeftHandIkOnNextAnimatorIkPass = false;
			StopEquipBlend();
			ClearLeftHandIk();
		}

		if (IsLeftHandIkBlocked())
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return;
		}

		ApplyLeftHandIkInternal();
	}

	private bool IsLeftHandIkBlocked()
	{
		return IsLeftHandIkTemporarilyDisabled();
	}

	private bool IsLeftHandIkTemporarilyDisabled()
	{
		if (m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine)
			return true;
		if (m_WeaponReload != null && m_WeaponReload.IsReloadBusy)
			return true;
		if (m_SelfStabilization != null && m_SelfStabilization.IsHealPresentationActive)
			return true;
		if (m_StabilizeOther != null && m_StabilizeOther.IsHealPresentationActive)
			return true;
		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.DraggingFallen))
			return true;
		return false;
	}

	private void ClearLeftHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
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
		if (IsLeftHandIkBlocked() || ResolveLiveLeftHandIkTarget() == null)
		{
			StopEquipBlend();
			return;
		}

		StartEquipBlend();
	}

	private void StartEquipBlend()
	{
		if (m_EquipBlendDuration <= 0f)
		{
			StopEquipBlend();
			return;
		}

		m_IsEquipBlendActive = true;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private void StopEquipBlend()
	{
		m_IsEquipBlendActive = false;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private float GetEquipBlendMultiplier()
	{
		if (!m_IsEquipBlendActive)
			return 1f;

		if (m_LastEquipBlendAdvanceFrame != Time.frameCount)
		{
			m_LastEquipBlendAdvanceFrame = Time.frameCount;
			m_EquipBlendElapsed += Time.deltaTime;
		}

		if (m_EquipBlendElapsed >= m_EquipBlendDuration)
		{
			StopEquipBlend();
			return 1f;
		}

		float normalizedTime = m_EquipBlendDuration > 0f
			? Mathf.Clamp01(m_EquipBlendElapsed / m_EquipBlendDuration)
			: 1f;

		if (m_EquipBlendCurve != null && m_EquipBlendCurve.length > 0)
			return m_EquipBlendCurve.Evaluate(normalizedTime);

		return Mathf.SmoothStep(0f, 1f, normalizedTime);
	}

	private void ApplyLeftHandIkInternal()
	{
		Transform ikTarget = ResolveLiveLeftHandIkTarget();
		if (ikTarget == null)
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return;
		}

		float blend = GetEquipBlendMultiplier();
		float positionWeight = m_LeftHandPositionWeight * blend;
		float rotationWeight = m_LeftHandRotationWeight * blend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, ikTarget.position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, ikTarget.rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private Transform ResolveLiveLeftHandIkTarget()
	{
		if (m_UnitEquipment == null)
			return null;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
			return null;

		ItemDefinition equipped = m_UnitEquipment.EquippedDefinition;
		string ikName = equipped != null ? equipped.LeftHandIkTargetChildName : null;
		if (string.IsNullOrWhiteSpace(ikName))
			return null;

		EquippedWeapon equippedWeapon = m_UnitEquipment.EquippedWeapon;
		if (equippedWeapon != null)
			return equippedWeapon.ResolveLeftHandIkTargetTransform(ikName);

		return m_UnitEquipment.LeftHandIkTargetTransform;
	}
	#endregion
}
