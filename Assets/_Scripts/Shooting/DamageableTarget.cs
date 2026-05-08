using System;
using UnityEngine;

/// <summary>
/// Простая цель для hitscan: здоровье и события попадания. Повесь на корень объекта или на объект с коллайдером (ищется через GetComponentInParent).
/// Текущее HP сериализуется, чтобы в Play Mode оно отображалось и обновлялось в инспекторе.
/// </summary>
[DisallowMultipleComponent]
public sealed class DamageableTarget : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0.01f)] private float m_MaxHealth = 100f;
	[SerializeField, Tooltip("Текущее здоровье. В начале сцены в Awake выставляется в Max Health.")]
	private float m_CurrentHealth = 100f;
	[SerializeField] private bool m_DestroyOnDeath;
	[SerializeField] private GameObject m_RootToDestroy;
	#endregion

	#region Public Properties
	public float MaxHealth => m_MaxHealth;
	public float CurrentHealth => m_CurrentHealth;
	public bool IsAlive => m_CurrentHealth > 0f;
	#endregion

	#region Events
	public event Action<DamageHitInfo> Damaged;
	public event Action<DamageHitInfo> Died;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_CurrentHealth = m_MaxHealth;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_MaxHealth = Mathf.Max(0.01f, m_MaxHealth);
		m_CurrentHealth = Mathf.Clamp(m_CurrentHealth, 0f, m_MaxHealth);
	}
#endif
	#endregion

	#region Public Methods
	/// <summary>Нанести урон от выстрела. Точка и направление — в мировых координатах (для VFX).</summary>
	public void ApplyDamage(float _damage, Vector3 _hitPointWorld, Vector3 _hitNormalWorld, Vector3 _incomingDirection, AmmoDefinition _ammo)
	{
		if (!IsAlive || _damage <= 0f)
			return;

		float applied = Mathf.Min(_damage, m_CurrentHealth);
		m_CurrentHealth -= applied;

		var info = new DamageHitInfo
		{
			Damage = applied,
			HitPointWorld = _hitPointWorld,
			HitNormalWorld = _hitNormalWorld,
			IncomingDirection = _incomingDirection,
			Ammo = _ammo,
			RemainingHealth = Mathf.Max(0f, m_CurrentHealth)
		};

		Damaged?.Invoke(info);

		if (m_CurrentHealth <= 0f)
		{
			Died?.Invoke(info);
			if (m_DestroyOnDeath)
			{
				GameObject root = m_RootToDestroy != null ? m_RootToDestroy : gameObject;
				Destroy(root);
			}
		}
	}
	#endregion
}

/// <summary>Данные одного попадания по <see cref="DamageableTarget"/>.</summary>
public struct DamageHitInfo
{
	public float Damage;
	public Vector3 HitPointWorld;
	public Vector3 HitNormalWorld;
	public Vector3 IncomingDirection;
	public AmmoDefinition Ammo;
	public float RemainingHealth;
}
