using UnityEngine;

/// <summary>
/// IK левой кисти к цели на экипированном оружии (<see cref="UnitEquipment.LeftHandIkTargetTransform"/>).
/// Компонент нужно повесить на тот же GameObject, где висит <see cref="Animator"/> (Humanoid).
/// В Animator Controller у слоя с движением должен быть включён <b>IK Pass</b>.
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
	[SerializeField, Range(0f, 1f)] private float m_LeftHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandRotationWeight = 1f;
	[Header("Локоть (подсказка IK)")]
	[SerializeField] private bool m_UseLeftElbowHint;
	[Tooltip("Пустой объект перед локтем слева (в пространстве персонажа), чтобы сгиб был естественнее.")]
	[SerializeField] private Transform m_LeftElbowHint;
	[SerializeField, Range(0f, 1f)] private float m_LeftElbowHintWeight = 1f;
	#endregion

	#region Private Fields
	private Animator m_Animator;
	/// <summary>Запрошен сброс IK из кода вне <see cref="OnAnimatorIK"/> (например при «готов», пока идёт зарядка/перезарядка).</summary>
	private bool m_ClearLeftHandIkOnNextAnimatorIkPass;
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
		if (IsLeftHandIkTemporarilyDisabled())
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
			ClearLeftHandIk();
		}

		if (IsLeftHandIkTemporarilyDisabled())
		{
			ClearLeftHandIk();
			return;
		}

		ApplyLeftHandIkInternal();
	}

	private bool IsLeftHandIkTemporarilyDisabled()
	{
		if (m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine)
			return true;
		if (m_WeaponReload != null && m_WeaponReload.IsReloadBusy)
			return true;
		return false;
	}

	private void ClearLeftHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private void ApplyLeftHandIkInternal()
	{
		Transform ikTarget = m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetTransform : null;
		if (ikTarget == null)
		{
			ClearLeftHandIk();
			return;
		}

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, m_LeftHandPositionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, m_LeftHandRotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, ikTarget.position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, ikTarget.rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}
	#endregion
}
