using System.Collections;
using UnityEngine;

/// <summary>
/// Физический снаряд гранаты: летит по параболе, отскакивает при падении, катится, останавливается.
/// После приземления применяет активное демпирование для быстрого замедления в пределах радиуса.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class GrenadeProjectile : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Rigidbody m_Rigidbody;
	[SerializeField] private LayerMask m_GroundLayers = ~0;
	[SerializeField, Min(0.1f)] private float m_StopSpeedThreshold = 0.15f;
	[SerializeField, Min(0.05f)] private float m_StopCheckInterval = 0.1f;
	[SerializeField, Min(0f)] private float m_FreezeDelay = 0.2f;
	#endregion

	#region Private Fields
	private GrenadeThrowData m_Data;
	private bool m_HasPlayedImpactSound;
	private bool m_HasLanded;
	private float m_LandingDrag;
	private float m_RollStopSpeed;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();
	}

	private void FixedUpdate()
	{
		if (!m_HasLanded || m_Rigidbody == null)
			return;

		if (m_LandingDrag <= 0f)
			return;

		Vector3 vel = m_Rigidbody.linearVelocity;
		if (vel.sqrMagnitude < 0.0001f)
			return;

		float factor = 1f / (1f + m_LandingDrag * Time.fixedDeltaTime);
		vel.x *= factor;
		vel.z *= factor;
		m_Rigidbody.linearVelocity = vel;
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (((1 << _collision.gameObject.layer) & m_GroundLayers) == 0)
			return;

		if (!m_HasPlayedImpactSound)
		{
			m_HasPlayedImpactSound = true;

			if (m_Data != null && m_Data.TryPickImpactSound(out AudioClip clip))
				UnitNonFireAudioUtility.PlayAtPoint(clip, transform.position, m_Data.ImpactVolume);
		}

		if (!m_HasLanded)
		{
			m_HasLanded = true;
			StartCoroutine(WaitAndFreezeRoutine());
		}
	}
	#endregion

	#region Public Methods
	public void Initialize(Vector3 _targetPosition, GrenadeThrowData _data, GameObject _thrower = null)
	{
		m_Data = _data;
		m_HasPlayedImpactSound = false;
		m_HasLanded = false;
		m_LandingDrag = _data != null ? _data.LandingDrag : 4f;
		m_RollStopSpeed = _data != null ? _data.RollStopSpeed : 0.1f;

		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();

		IgnoreCollisionsWithThrower(_thrower);

		m_Rigidbody.mass = 0.35f;
		m_Rigidbody.isKinematic = false;
		m_Rigidbody.useGravity = true;
		m_Rigidbody.linearDamping = 0.1f;
		m_Rigidbody.angularDamping = 0.3f;
		m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

		float arcHeight = _data != null ? _data.ArcHeight : 3f;
		Vector3 velocity = CalculateLaunchVelocity(transform.position, _targetPosition, arcHeight);
		m_Rigidbody.linearVelocity = velocity;

		float lifetime = _data != null ? _data.ProjectileLifetime : 60f;
		Destroy(gameObject, lifetime);
	}

	public static Vector3 CalculateLaunchVelocity(Vector3 _origin, Vector3 _target, float _arcHeight)
	{
		float g = Mathf.Abs(Physics.gravity.y);
		if (g < 0.01f)
			g = 9.81f;

		Vector3 toTarget = _target - _origin;
		Vector3 toTargetXZ = new Vector3(toTarget.x, 0f, toTarget.z);
		float distXZ = toTargetXZ.magnitude;

		if (distXZ < 0.01f)
			return Vector3.up * Mathf.Sqrt(2f * g * _arcHeight);

		float apexY = Mathf.Max(_origin.y, _target.y) + _arcHeight;
		float riseHeight = apexY - _origin.y;
		float riseTime = Mathf.Sqrt(2f * riseHeight / g);

		float fallHeight = apexY - _target.y;
		float fallTime = Mathf.Sqrt(2f * fallHeight / g);

		float totalTime = riseTime + fallTime;
		if (totalTime < 0.01f)
			totalTime = 0.01f;

		Vector3 horizontalVelocity = toTargetXZ / totalTime;
		float verticalVelocity = g * riseTime;

		return new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
	}
	#endregion

	#region Private Methods
	private void IgnoreCollisionsWithThrower(GameObject _thrower)
	{
		if (_thrower == null)
			return;

		Collider[] myColliders = GetComponentsInChildren<Collider>(true);
		Collider[] throwerColliders = _thrower.GetComponentsInChildren<Collider>(true);

		for (int i = 0; i < myColliders.Length; i++)
		{
			for (int j = 0; j < throwerColliders.Length; j++)
			{
				if (myColliders[i] != null && throwerColliders[j] != null)
					Physics.IgnoreCollision(myColliders[i], throwerColliders[j], true);
			}
		}
	}

	private IEnumerator WaitAndFreezeRoutine()
	{
		float threshold = m_RollStopSpeed > 0f ? m_RollStopSpeed : m_StopSpeedThreshold;

		while (m_Rigidbody != null && m_Rigidbody.linearVelocity.magnitude > threshold)
			yield return new WaitForSeconds(m_StopCheckInterval);

		yield return new WaitForSeconds(m_FreezeDelay);

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
			m_Rigidbody.isKinematic = true;
		}
	}
	#endregion
}
