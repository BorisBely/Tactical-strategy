using System.Collections;
using UnityEngine;

/// <summary>
/// Физический снаряд гранаты: летит по параболе, отскакивает при падении, катится, останавливается.
/// После отпускания из руки запускается фитиль; по истечении — VFX/звук взрыва и удаление снаряда.
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
	private ItemDefinition m_GrenadeDefinition;
	private bool m_HasPlayedImpactSound;
	private bool m_HasLanded;
	private bool m_HasExploded;
	private float m_LandingDrag;
	private float m_RollStopSpeed;
	private Coroutine m_FuseRoutine;
	private Coroutine m_LifetimeRoutine;
	private Coroutine m_SmokeBodyKeepAliveRoutine;
	private static PhysicsMaterial s_SoftLandingPhysicsMaterial;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();
	}

	private void OnDestroy()
	{
		if (m_FuseRoutine != null)
			StopCoroutine(m_FuseRoutine);
		if (m_LifetimeRoutine != null)
			StopCoroutine(m_LifetimeRoutine);
		if (m_SmokeBodyKeepAliveRoutine != null)
			StopCoroutine(m_SmokeBodyKeepAliveRoutine);
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

		Collider hitCollider = _collision.collider;
		bool isSoft = m_Data != null && m_Data.IsSoftSurface(hitCollider);

		if (!m_HasPlayedImpactSound)
		{
			m_HasPlayedImpactSound = true;
			TryPlayImpactSound(hitCollider);
		}

		if (isSoft)
			ApplySoftLandingResponse(_collision);

		if (!m_HasLanded)
		{
			m_HasLanded = true;
			if (isSoft && m_Data != null)
				m_LandingDrag = m_Data.SoftLandingDrag;

			StartCoroutine(WaitAndFreezeRoutine());
		}
	}
	#endregion

	#region Public Methods
	public void Initialize(
		Vector3 _targetPosition,
		GrenadeThrowData _data,
		GameObject _thrower = null,
		ItemDefinition _grenadeDefinition = null)
	{
		m_Data = _data;
		m_GrenadeDefinition = _grenadeDefinition;
		m_HasPlayedImpactSound = false;
		m_HasLanded = false;
		m_HasExploded = false;
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

		if (m_LifetimeRoutine != null)
			StopCoroutine(m_LifetimeRoutine);
		float lifetime = _data != null ? _data.ProjectileLifetime : 60f;
		m_LifetimeRoutine = StartCoroutine(LifetimeDestroyRoutine(lifetime));

		if (m_FuseRoutine != null)
			StopCoroutine(m_FuseRoutine);

		float fuseSeconds = _data != null ? _data.ExplosionFuseSeconds : 3.5f;
		if (fuseSeconds > 0f)
			m_FuseRoutine = StartCoroutine(FuseRoutine(fuseSeconds));
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
	private void TryPlayImpactSound(Collider _hitCollider)
	{
		if (m_Data == null)
			return;

		if (!m_Data.TryPickImpactSound(_hitCollider, out AudioClip clip, out _) || clip == null)
			return;

		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			transform.position,
			m_Data.ImpactVolume,
			m_Data.ImpactMaxDistance);
	}

	private void ApplySoftLandingResponse(Collision _collision)
	{
		if (m_Rigidbody == null)
			return;

		float bounceScale = m_Data != null ? m_Data.SoftLandingBounceScale : 0.08f;
		float horizontalScale = m_Data != null ? m_Data.SoftLandingHorizontalScale : 0.35f;

		Vector3 contactNormal = Vector3.up;
		if (_collision.contactCount > 0)
			contactNormal = _collision.GetContact(0).normal.normalized;

		Vector3 velocity = m_Rigidbody.linearVelocity;
		float intoSurface = Vector3.Dot(velocity, contactNormal);
		if (intoSurface < 0f)
		{
			Vector3 normalVelocity = contactNormal * intoSurface;
			Vector3 tangentVelocity = velocity - normalVelocity;
			velocity = tangentVelocity * horizontalScale + (-normalVelocity) * bounceScale;
		}
		else
		{
			velocity.x *= horizontalScale;
			velocity.z *= horizontalScale;
			velocity.y *= bounceScale;
		}

		m_Rigidbody.linearVelocity = velocity;
		m_Rigidbody.angularVelocity *= 0.35f;
		ApplySoftLandingPhysicsMaterial();
	}

	private void ApplySoftLandingPhysicsMaterial()
	{
		if (s_SoftLandingPhysicsMaterial == null)
		{
			s_SoftLandingPhysicsMaterial = new PhysicsMaterial("GrenadeSoftLanding")
			{
				bounciness = 0f,
				bounceCombine = PhysicsMaterialCombine.Minimum,
				dynamicFriction = 0.95f,
				staticFriction = 0.95f,
				frictionCombine = PhysicsMaterialCombine.Maximum
			};
		}

		Collider[] colliders = GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null && !colliders[i].isTrigger)
				colliders[i].material = s_SoftLandingPhysicsMaterial;
		}
	}

	private IEnumerator FuseRoutine(float _fuseSeconds)
	{
		yield return new WaitForSeconds(_fuseSeconds);
		Detonate();
	}

	private void Detonate()
	{
		if (m_HasExploded)
			return;

		m_HasExploded = true;
		m_FuseRoutine = null;

		Vector3 position = transform.position;
		GrenadeType type = m_GrenadeDefinition != null ? m_GrenadeDefinition.GrenadeType : GrenadeType.Fragmentation;

		PlayExplosionAudio(position);
		SpawnExplosionVfx(position, type);
		SpawnSmokeVfx(position, type);

		if (type == GrenadeType.Smoke)
		{
			CancelLifetimeDestroy();
			FreezeSpentShell();
			if (m_SmokeBodyKeepAliveRoutine != null)
				StopCoroutine(m_SmokeBodyKeepAliveRoutine);
			m_SmokeBodyKeepAliveRoutine = StartCoroutine(SmokeBodyKeepAliveRoutine());
			return;
		}

		Destroy(gameObject);
	}

	private IEnumerator LifetimeDestroyRoutine(float _seconds)
	{
		yield return new WaitForSeconds(Mathf.Max(0.1f, _seconds));
		m_LifetimeRoutine = null;
		if (!m_HasExploded)
			Destroy(gameObject);
	}

	private void CancelLifetimeDestroy()
	{
		if (m_LifetimeRoutine == null)
			return;

		StopCoroutine(m_LifetimeRoutine);
		m_LifetimeRoutine = null;
	}

	private void FreezeSpentShell()
	{
		if (m_Rigidbody == null)
			return;

		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
		m_Rigidbody.isKinematic = true;
	}

	private IEnumerator SmokeBodyKeepAliveRoutine()
	{
		float activeSeconds = m_Data != null ? m_Data.SmokeLifetimeSeconds : 32f;
		float fadeOutSeconds = m_Data != null ? m_Data.SmokeAudioFadeOutSeconds : 3f;
		float lingerSeconds = Mathf.Max(10f, fadeOutSeconds + 8f);

		yield return new WaitForSeconds(activeSeconds + lingerSeconds);
		m_SmokeBodyKeepAliveRoutine = null;
		Destroy(gameObject);
	}

	private void PlayExplosionAudio(Vector3 _position)
	{
		if (m_Data == null)
			return;

		if (!m_Data.TryPickExplosionSound(m_GrenadeDefinition, out AudioClip clip) || clip == null)
			return;

		float volume = m_Data.GetExplosionVolume(m_GrenadeDefinition);
		UnitNonFireAudioUtility.PlayAtPoint(clip, _position, volume, m_Data.ExplosionAudioMaxDistance);
	}

	private void SpawnExplosionVfx(Vector3 _position, GrenadeType _type)
	{
		if (m_Data == null || _type == GrenadeType.Smoke)
			return;

		GameObject prefab = m_Data.PickExplosionPrefab(m_GrenadeDefinition);
		if (prefab == null)
			return;

		float yaw = m_Data.GetExplosionVfxYawOffsetDegrees(m_GrenadeDefinition) + Random.Range(-8f, 8f);
		Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
		float scale = m_Data.GetDetonationVfxScale(m_GrenadeDefinition) * Random.Range(0.97f, 1.03f);

		CombatVfxBudgetService.TrySpawnExplosion(
			prefab,
			_position,
			rotation,
			Vector3.one * scale,
			m_Data.ExplosionMaxDistanceMeters,
			m_Data.GetDetonationVfxLifetimeSeconds(m_GrenadeDefinition));
	}

	private void SpawnSmokeVfx(Vector3 _position, GrenadeType _type)
	{
		if (m_Data == null || !m_Data.ShouldSpawnSmokeOnDetonation(_type))
			return;

		CombatVfxBudgetService.TrySpawnSmokeCloud(
			m_Data.SmokePrefab,
			_position,
			Quaternion.identity,
			Vector3.one,
			m_Data.SmokeMaxDistanceMeters,
			m_Data.GetDetonationVfxLifetimeSeconds(m_GrenadeDefinition),
			m_Data.SmokeLoopClip,
			m_Data.SmokeLoopVolume,
			m_Data.SmokeLoopMaxDistance,
			m_Data.SmokeAudioFadeInSeconds,
			m_Data.SmokeAudioFadeOutSeconds,
			m_Data.SmokeAudioCrossfadeSeconds,
			m_Data.SmokeDissipateSeconds);
	}

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
