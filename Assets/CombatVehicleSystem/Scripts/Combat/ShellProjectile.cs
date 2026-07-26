using UnityEngine;

namespace CombatVehicleSystem
{
	public class ShellProjectile : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private GameObject m_HitPrefab;
		[SerializeField] private float m_HitFxLifetime = 10f;
		[SerializeField] private float m_Lifetime = 25f;
		#endregion

		#region Unity Lifecycle
		private void Start()
		{
			if (m_Lifetime > 0f)
				Destroy(gameObject, m_Lifetime);
		}

		private void OnCollisionEnter(Collision _collision)
		{
			if (m_HitPrefab != null)
			{
				Quaternion rotation = Quaternion.FromToRotation(Vector3.up, transform.forward);
				GameObject hit = Instantiate(m_HitPrefab, transform.position, rotation);
				Destroy(hit, Mathf.Max(0.1f, m_HitFxLifetime));
			}

			Destroy(gameObject);
		}
		#endregion

		#region Public Methods
		public void Configure(GameObject _hitPrefab, float _hitFxLifetime, float _lifetime)
		{
			m_HitPrefab = _hitPrefab;
			m_HitFxLifetime = _hitFxLifetime;
			m_Lifetime = _lifetime;
		}
		#endregion
	}
}
