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
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Animator = GetComponent<Animator>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
	}
	#endregion

	#region IK
	private void OnAnimatorIK(int _layerIndex)
	{
		if (m_Animator == null)
			return;

		Transform ikTarget = m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetTransform : null;
		if (ikTarget == null)
		{
			m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
			m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
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
