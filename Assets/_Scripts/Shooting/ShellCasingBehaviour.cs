using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Гильза из пула: физика до первого удара по маске слоёв патрона, звук через общий AudioSource, возврат в пул.
/// Rigidbody + Collider на том же объекте, что и этот компонент (объект может быть дочерним к корню префаба).
/// При активации включается Continuous + Interpolate, чтобы реже пролетать сквозь статический пол.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ShellCasingBehaviour : MonoBehaviour
{
	#region Private Fields
	private ObjectPool<GameObject> m_Pool;
	private GameObject m_PooledRoot;
	private AudioSource m_SharedImpactAudio;
	private AmmoDefinition m_Ammo;
	private bool m_HasPlayedImpact;
	private float m_ReleaseAtUnscaledTime = -1f;
	private float m_AirborneExpireAtUnscaledTime = -1f;
	private Rigidbody m_Rigidbody;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
	}

	private void Update()
	{
		if (m_ReleaseAtUnscaledTime >= 0f && Time.unscaledTime >= m_ReleaseAtUnscaledTime)
		{
			ReleaseToPool();
			return;
		}

		if (!m_HasPlayedImpact && m_AirborneExpireAtUnscaledTime >= 0f && Time.unscaledTime >= m_AirborneExpireAtUnscaledTime)
			ReleaseToPool();
	}
	#endregion

	#region Public Methods
	/// <param name="_pooledRoot">Тот же <see cref="GameObject"/>, что вернул <c>ObjectPool.Get()</c> (корень инстанса).</param>
	public void ActivateFromPool(
		ObjectPool<GameObject> _pool,
		GameObject _pooledRoot,
		AudioSource _sharedImpactAudio,
		AmmoDefinition _ammo,
		Vector3 _worldPosition,
		Quaternion _worldRotation,
		Vector3 _worldLinearVelocity,
		Vector3 _worldAngularVelocity)
	{
		m_Pool = _pool;
		m_PooledRoot = _pooledRoot != null ? _pooledRoot : gameObject;
		m_SharedImpactAudio = _sharedImpactAudio;
		m_Ammo = _ammo;
		m_HasPlayedImpact = false;
		m_ReleaseAtUnscaledTime = -1f;
		m_AirborneExpireAtUnscaledTime = Time.unscaledTime + Mathf.Max(0.5f, _ammo.ShellMaxAirborneSeconds);

		transform.SetPositionAndRotation(_worldPosition, _worldRotation);

		m_Rigidbody.WakeUp();
		// Discrete даёт пролёты сквозь тонкий статический пол у быстрых мелких тел; Continuous — sweep против статики.
		m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
		m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
		m_Rigidbody.linearVelocity = _worldLinearVelocity;
		m_Rigidbody.angularVelocity = _worldAngularVelocity;
	}
	#endregion

	#region Private Methods
	private void OnCollisionEnter(Collision _collision)
	{
		if (m_HasPlayedImpact || m_Ammo == null)
			return;

		if (_collision.contactCount <= 0)
			return;

		if (m_Ammo.ShellImpactMinSpeedSqr > 0f &&
			_collision.relativeVelocity.sqrMagnitude < m_Ammo.ShellImpactMinSpeedSqr)
			return;

		int maskBits = m_Ammo.ShellImpactMaskBits;
		if (maskBits != 0 && (maskBits & (1 << _collision.collider.gameObject.layer)) == 0)
			return;

		m_HasPlayedImpact = true;

		ContactPoint contact = _collision.GetContact(0);
		Vector3 playPos = contact.point;

		if (m_SharedImpactAudio != null && m_Ammo.TryPickShellImpactSound(out AudioClip clip, out float volume))
		{
			m_SharedImpactAudio.transform.position = playPos;
			m_SharedImpactAudio.PlayOneShot(clip, volume);
		}

		float lifetime = Mathf.Max(0.05f, m_Ammo.ShellLifetimeAfterImpactSeconds);
		m_ReleaseAtUnscaledTime = Time.unscaledTime + lifetime;
	}

	private void ReleaseToPool()
	{
		m_ReleaseAtUnscaledTime = -1f;
		m_AirborneExpireAtUnscaledTime = -1f;
		m_Ammo = null;
		m_SharedImpactAudio = null;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
			m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
			m_Rigidbody.interpolation = RigidbodyInterpolation.None;
		}

		GameObject toRelease = m_PooledRoot != null ? m_PooledRoot : gameObject;
		m_PooledRoot = null;

		if (m_Pool != null)
			m_Pool.Release(toRelease);
		else
			toRelease.SetActive(false);
	}
	#endregion
}
