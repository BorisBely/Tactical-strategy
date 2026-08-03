using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Звук «пролёта» пули рядом с камерой.
/// Зона слышимости задаётся триггер-<see cref="SphereCollider"/> на дочернем объекте камеры;
/// hitscan остаётся hitscan — проверка пересечения траектории с зоной выполняется геометрически,
/// без физических снарядов и без участия в <see cref="UnitWeaponHitscanShooting"/> raycast.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioListener))]
[DefaultExecutionOrder(120)]
public sealed class CameraBulletWhizListener : MonoBehaviour
{
	#region Constants
	private const string c_ZoneObjectName = "BulletWhizZone";
	private const float c_DefaultWhizRadiusMeters = 5f;
	private const float c_DefaultAmmoVelocityMetersPerSecond = 400f;
	private const float c_DefaultMuzzleSkipDistanceMeters = 15f;
	private const float c_PendingWhizExpireSeconds = 0.35f;
	private const float c_MinAudibleVolume = 0.001f;
	private const float c_ImpactAudioWhizVolumeScale = 0.7f;
	#endregion

	#region Serialized Fields
	[Header("Zone")]
	[Tooltip("Максимальная дистанция (м) от камеры до траектории/точки попадания, на которой ещё слышен whiz.")]
	[SerializeField, Min(0.1f)] private float m_WhizRadius = c_DefaultWhizRadiusMeters;
	[SerializeField] private SphereCollider m_WhizZoneCollider;
	[Tooltip("Whiz не играет, если стрелок ближе этого расстояния (м) к слушателю — выстрел и так слышен с источника.")]
	[SerializeField, Min(0f)] private float m_MuzzleSkipDistance = c_DefaultMuzzleSkipDistanceMeters;

	[Header("Audio")]
	[SerializeField] private WeaponRandomAudioClipSet m_WhizClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_BaseVolume = 1f;
	[Tooltip("Громкость на краю радиуса (Whiz Radius). В упор — Base Volume.")]
	[SerializeField, Range(0.02f, 0.6f)] private float m_EdgeVolumeMultiplier = 0.14f;
	[Tooltip("Кривая затухания: >1 — быстрее тише при отдалении от траектории, <1 — мягче.")]
	[SerializeField, Range(0.5f, 3f)] private float m_DistanceFalloffPower = 1.45f;
	[Tooltip("Множитель громкости whiz, если у выстрела есть звук попадания по поверхности (−30% = 0.7).")]
	[SerializeField, Range(0f, 1f)] private float m_ImpactAudioWhizVolumeScale = c_ImpactAudioWhizVolumeScale;
	[SerializeField, Range(0f, 0.25f)] private float m_PitchVariance = 0.06f;
	#endregion

	#region Private Fields
	private Transform m_ListenerTransform;
	private readonly List<PendingWhiz> m_PendingWhizzes = new List<PendingWhiz>(16);
	#endregion

	#region Nested Types
	private struct PendingWhiz
	{
		public float PlayAtTime;
		public float Volume;
		public float Pitch;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_ListenerTransform = transform;
		EnsureWhizZoneCollider();
		WeaponShotTraceBroadcast.TracePublished += HandleTracePublished;
	}

	private void OnDestroy()
	{
		WeaponShotTraceBroadcast.TracePublished -= HandleTracePublished;
	}

	private void Update()
	{
		if (m_PendingWhizzes.Count == 0)
			return;

		if (PauseMenuController.IsPaused)
			return;

		float now = Time.time;
		for (int i = m_PendingWhizzes.Count - 1; i >= 0; i--)
		{
			PendingWhiz pending = m_PendingWhizzes[i];
			if (now < pending.PlayAtTime)
				continue;

			if (PlayWhiz(pending.Volume, pending.Pitch))
			{
				m_PendingWhizzes.RemoveAt(i);
				continue;
			}

			if (now - pending.PlayAtTime > c_PendingWhizExpireSeconds)
				m_PendingWhizzes.RemoveAt(i);
		}
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_WhizRadius = Mathf.Max(0.1f, m_WhizRadius);
		if (m_WhizZoneCollider != null)
			m_WhizZoneCollider.radius = m_WhizRadius;
	}

	private void OnDrawGizmosSelected()
	{
		Vector3 center = GetZoneCenter();
		float radius = m_WhizZoneCollider != null ? m_WhizZoneCollider.radius : m_WhizRadius;
		Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.35f);
		Gizmos.DrawWireSphere(center, radius);
	}
#endif
	#endregion

	#region Private Methods
	private Vector3 GetZoneCenter()
	{
		if (m_ListenerTransform != null)
			return m_ListenerTransform.position;

		return transform.position;
	}

	private void EnsureWhizZoneCollider()
	{
		if (m_WhizZoneCollider == null)
		{
			Transform existing = transform.Find(c_ZoneObjectName);
			if (existing != null)
				existing.TryGetComponent(out m_WhizZoneCollider);
		}

		if (m_WhizZoneCollider == null)
		{
			GameObject zoneGo = new GameObject(c_ZoneObjectName);
			zoneGo.transform.SetParent(transform, false);
			m_WhizZoneCollider = zoneGo.AddComponent<SphereCollider>();
		}

		m_WhizZoneCollider.isTrigger = true;
		m_WhizZoneCollider.radius = m_WhizRadius;
		m_WhizZoneCollider.center = Vector3.zero;
		m_WhizZoneCollider.enabled = true;
		m_WhizZoneCollider.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
	}

	private bool PlayWhiz(float _volume, float _pitch)
	{
		if (!m_WhizClips.TryPickClip(out AudioClip clip) || clip == null || _volume <= c_MinAudibleVolume)
			return false;

		return CombatAudioManager.TryPlayBulletWhiz(clip, _volume, _pitch);
	}

	private void HandleTracePublished(WeaponShotTraceInfo _trace)
	{
		if (_trace.HitSelf || !m_WhizClips.HasAnyClips)
			return;

		Vector3 zoneCenter = GetZoneCenter();
		float radius = m_WhizZoneCollider != null ? m_WhizZoneCollider.radius : m_WhizRadius;

		if ((zoneCenter - _trace.Origin).sqrMagnitude <= m_MuzzleSkipDistance * m_MuzzleSkipDistance)
			return;

		if (!TryEvaluateWhiz(_trace, zoneCenter, radius, out float playDelay, out float volume))
			return;

		if (_trace.HasImpactAudio)
			volume *= m_ImpactAudioWhizVolumeScale;

		float pitch = m_PitchVariance > 0f
			? Random.Range(1f - m_PitchVariance, 1f + m_PitchVariance)
			: 1f;

		if (volume <= c_MinAudibleVolume)
			return;

		m_PendingWhizzes.Add(new PendingWhiz
		{
			PlayAtTime = Time.time + playDelay,
			Volume = volume,
			Pitch = pitch
		});
	}

	private bool TryEvaluateWhiz(
		WeaponShotTraceInfo _trace,
		Vector3 _zoneCenter,
		float _radius,
		out float _playDelay,
		out float _volume)
	{
		_playDelay = 0f;
		_volume = 0f;

		Vector3 segment = _trace.EndPoint - _trace.Origin;
		float segmentLengthSqr = segment.sqrMagnitude;
		if (segmentLengthSqr <= 1e-6f)
			return false;

		float segmentLength = Mathf.Sqrt(segmentLengthSqr);
		float ammoVelocity = _trace.Ammo != null ? _trace.Ammo.Velocity : c_DefaultAmmoVelocityMetersPerSecond;
		float flightSeconds = segmentLength / Mathf.Max(0.1f, ammoVelocity);

		bool pathPass = TryEvaluatePathPassWhiz(
			_trace.Origin,
			segment,
			segmentLengthSqr,
			_zoneCenter,
			_radius,
			flightSeconds,
			out float pathDelay,
			out float pathVolume);

		bool hitProximity = false;
		float hitDelay = flightSeconds;
		float hitVolume = 0f;
		if (_trace.HasHit)
		{
			float hitDistance = Vector3.Distance(_zoneCenter, _trace.EndPoint);
			if (hitDistance <= _radius)
			{
				hitProximity = true;
				hitVolume = ComputeVolumeFromMissDistance(hitDistance, _radius);
				hitDelay = flightSeconds * 0.98f;
			}
		}

		if (!pathPass && !hitProximity)
			return false;

		if (pathPass && hitProximity)
		{
			if (pathVolume >= hitVolume)
			{
				_playDelay = pathDelay;
				_volume = pathVolume;
			}
			else
			{
				_playDelay = hitDelay;
				_volume = hitVolume;
			}

			return _volume > c_MinAudibleVolume;
		}

		if (pathPass)
		{
			_playDelay = pathDelay;
			_volume = pathVolume;
			return _volume > c_MinAudibleVolume;
		}

		_playDelay = hitDelay;
		_volume = hitVolume;
		return _volume > c_MinAudibleVolume;
	}

	private bool TryEvaluatePathPassWhiz(
		Vector3 _origin,
		Vector3 _segment,
		float _segmentLengthSqr,
		Vector3 _zoneCenter,
		float _radius,
		float _flightSeconds,
		out float _playDelay,
		out float _volume)
	{
		_playDelay = 0f;
		_volume = 0f;

		float radiusSqr = _radius * _radius;
		Vector3 fromOriginToCenter = _origin - _zoneCenter;
		float b = 2f * Vector3.Dot(fromOriginToCenter, _segment);
		float c = Vector3.Dot(fromOriginToCenter, fromOriginToCenter) - radiusSqr;
		float discriminant = b * b - 4f * _segmentLengthSqr * c;

		float closestT = Mathf.Clamp01(-Vector3.Dot(fromOriginToCenter, _segment) / _segmentLengthSqr);
		Vector3 closestPoint = _origin + _segment * closestT;
		float missDistance = Vector3.Distance(_zoneCenter, closestPoint);

		if (discriminant < 0f && missDistance > _radius)
			return false;

		float entryT = closestT;
		if (discriminant >= 0f)
		{
			float sqrtDisc = Mathf.Sqrt(discriminant);
			float invDenominator = 1f / (2f * _segmentLengthSqr);
			float tEnter = (-b - sqrtDisc) * invDenominator;
			float tExit = (-b + sqrtDisc) * invDenominator;

			if (tEnter >= 0f && tEnter <= 1f)
				entryT = tEnter;
			else if (tExit >= 0f && tExit <= 1f)
				entryT = tExit;
			else if (tEnter < 0f && tExit > 1f)
				entryT = 0f;
		}

		_volume = ComputeVolumeFromMissDistance(missDistance, _radius);
		_playDelay = Mathf.Clamp01(entryT) * _flightSeconds;
		return _volume > c_MinAudibleVolume;
	}

	private float ComputeVolumeFromMissDistance(float _missDistanceMeters, float _radiusMeters)
	{
		float normalizedProximity = 1f - Mathf.Clamp01(_missDistanceMeters / Mathf.Max(0.01f, _radiusMeters));
		float shapedProximity = Mathf.Pow(normalizedProximity, m_DistanceFalloffPower);
		return m_BaseVolume * Mathf.Lerp(m_EdgeVolumeMultiplier, 1f, shapedProximity);
	}
	#endregion
}
