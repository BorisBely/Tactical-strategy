using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Звуки шагов: расстояние или Animation Event <see cref="Footstep"/>.
/// Поверхность — луч вниз от ног (с кэшем: не на каждый шаг, чтобы масштабировать десятки/сотню юнитов).
/// 3D: звук позиционируется у ног; громкость и панорама считаются относительно <see cref="AudioListener"/> (обычно на главной камере).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public sealed class UnitFootsteps : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private AudioSource m_AudioSource;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[Tooltip("Точка у ступней для луча и для 3D-позиции, если AudioSource не является её дочерним объектом.")]
	[SerializeField] private Transform m_EmitFrom;

	[Header("Поверхность")]
	[Tooltip("Слои коллайдеров пола для Raycast.")]
	[SerializeField] private LayerMask m_GroundLayers = ~0;
	[SerializeField, Min(0.01f)] private float m_GroundRayUpOffset = 0.35f;
	[SerializeField, Min(0.05f)] private float m_GroundRayLength = 2f;
	[Tooltip("Сверху вниз: первое подходящее правило. Запасной набор — «Клипы по умолчанию».")]
	[SerializeField] private FootstepSurfaceRule[] m_SurfaceRules;

	[Header("Производительность (много юнитов)")]
	[Tooltip("Повторный Raycast к полу не чаще этого интервала, если ноги почти не сдвинулись. 0 — не ограничивать по времени (только по сдвигу и кадру).")]
	[SerializeField, Min(0f)] private float m_SurfaceRayMinIntervalSeconds = 0.15f;
	[Tooltip("Если ноги сместились по горизонтали больше этого значения — снова луч (границы материалов).")]
	[SerializeField, Min(0.01f)] private float m_SurfaceRayReusePlanarMeters = 0.3f;
	[Tooltip("Сильное изменение высоты точки эмита — снова луч (ступеньки, склоны).")]
	[SerializeField, Min(0.05f)] private float m_SurfaceRayReuseVerticalMeters = 0.35f;

	[Header("Клипы по умолчанию")]
	[Tooltip("Если луч не попал или ни одно правило не подошло.")]
	[SerializeField] private AudioClip[] m_FootstepClips;

	[Header("Режим")]
	[Tooltip("Если включено — расстояние не считается; шаг только из Animation Event (функция Footstep).")]
	[SerializeField] private bool m_AnimationEventsOnly;

	[Header("Порог движения (согласуйте с UnitClickToMove / UnitAnimatorStance)")]
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;

	[Header("Интервал шагов: метры между звуками (планарно)")]
	[SerializeField, Min(0.05f)] private float m_StepDistanceStanding = 0.42f;
	[SerializeField, Min(0.05f)] private float m_StepDistanceCrouch = 0.38f;
	[SerializeField, Min(0.05f)] private float m_StepDistanceProne = 0.28f;

	[Header("3D звук (направление и дистанция относительно AudioListener)")]
	[Tooltip("При старте выставить Spatial Blend = 1, Min/Max Distance и Rolloff на AudioSource.")]
	[SerializeField] private bool m_ApplySpatialPreset = true;
	[SerializeField, Min(0.01f)] private float m_SpatialMinDistance = 0.6f;
	[SerializeField, Min(0.1f)] private float m_SpatialMaxDistance = 22f;
	[SerializeField] private AudioRolloffMode m_VolumeRolloff = AudioRolloffMode.Logarithmic;
	[SerializeField, Range(0f, 5f)] private float m_DopplerLevel;
	[Tooltip("Если AudioSource не привязан к Emit From, перед каждым шагом переносить его в позицию ног (для панорамы и затухания по расстоянию).")]
	[SerializeField] private bool m_SyncAudioSourceWorldPositionToEmitPoint = true;

	[Header("Вариация")]
	[SerializeField, Range(0f, 0.3f)] private float m_VolumeJitter = 0.06f;
	[SerializeField, Range(0f, 0.15f)] private float m_PitchJitter = 0.04f;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private Vector3 m_LastPlanarPosition;
	private float m_DistanceAccumulated;
	private bool m_HasLastPosition;

	private AudioClip[] m_CachedGroundClipPool;
	private bool m_HasGroundClipCache;
	private float m_GroundClipCacheTime;
	private Vector2 m_GroundClipCachePlanarXZ;
	private float m_GroundClipCacheEmitY;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_AudioSource == null)
			m_AudioSource = GetComponent<AudioSource>();
		if (m_StanceSource == null)
			m_StanceSource = GetComponent<UnitAnimatorStance>();

		ApplySpatialPresetIfNeeded();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_SpatialMaxDistance < m_SpatialMinDistance + 0.01f)
			m_SpatialMaxDistance = m_SpatialMinDistance + 0.01f;

		if (!Application.isPlaying && m_AudioSource != null)
			ApplySpatialPresetIfNeeded();
	}
#endif

	private void OnEnable()
	{
		m_HasLastPosition = false;
		m_DistanceAccumulated = 0f;
		InvalidateGroundClipCache();
	}

	private void Update()
	{
		if (m_AnimationEventsOnly || !HasAnyClipsConfigured())
			return;

		if (!IsAgentMoving())
		{
			m_HasLastPosition = false;
			m_DistanceAccumulated = 0f;
			return;
		}

		Vector3 p = transform.position;
		Vector3 planarNow = new Vector3(p.x, 0f, p.z);

		if (!m_HasLastPosition)
		{
			m_LastPlanarPosition = planarNow;
			m_HasLastPosition = true;
			return;
		}

		float delta = (planarNow - m_LastPlanarPosition).magnitude;
		m_LastPlanarPosition = planarNow;
		m_DistanceAccumulated += delta;

		float stepDistance = GetStepDistanceForStance();
		while (m_DistanceAccumulated >= stepDistance)
		{
			m_DistanceAccumulated -= stepDistance;
			PlayFootstepInternal();
		}
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Вызов из Animation Event (имя функции в клипе: Footstep).
	/// </summary>
	public void Footstep()
	{
		if (m_AnimationEventsOnly && !IsAgentMoving())
			return;

		if (!HasAnyClipsConfigured())
			return;

		PlayFootstepInternal();
	}
	#endregion

	#region Private Methods
	private bool HasAnyClipsConfigured()
	{
		if (m_FootstepClips != null && m_FootstepClips.Length > 0)
			return true;

		if (m_SurfaceRules == null)
			return false;

		for (int i = 0; i < m_SurfaceRules.Length; i++)
		{
			AudioClip[] c = m_SurfaceRules[i].Clips;
			if (c != null && c.Length > 0)
				return true;
		}

		return false;
	}

	private void ApplySpatialPresetIfNeeded()
	{
		if (m_AudioSource == null || !m_ApplySpatialPreset)
			return;

		m_AudioSource.spatialBlend = 1f;
		m_AudioSource.minDistance = m_SpatialMinDistance;
		m_AudioSource.maxDistance = m_SpatialMaxDistance;
		m_AudioSource.rolloffMode = m_VolumeRolloff;
		m_AudioSource.dopplerLevel = m_DopplerLevel;
	}

	private bool IsAgentMoving()
	{
		if (m_Agent == null)
			return false;

		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
			return true;

		return m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	private float GetStepDistanceForStance()
	{
		if (m_StanceSource == null)
			return m_StepDistanceStanding;

		return m_StanceSource.CurrentStance switch
		{
			LocomotionStance.Crouch => m_StepDistanceCrouch,
			LocomotionStance.Prone => m_StepDistanceProne,
			_ => m_StepDistanceStanding
		};
	}

	private Vector3 GetEmitWorldPosition()
	{
		return m_EmitFrom != null ? m_EmitFrom.position : transform.position;
	}

	private void InvalidateGroundClipCache()
	{
		m_HasGroundClipCache = false;
		m_CachedGroundClipPool = null;
	}

	private bool ShouldRefreshGroundSurfaceProbe(Vector3 _emitWorld)
	{
		if (!m_HasGroundClipCache)
			return true;

		float dt = Time.time - m_GroundClipCacheTime;
		if (m_SurfaceRayMinIntervalSeconds > 0f && dt >= m_SurfaceRayMinIntervalSeconds)
			return true;

		float dx = _emitWorld.x - m_GroundClipCachePlanarXZ.x;
		float dz = _emitWorld.z - m_GroundClipCachePlanarXZ.y;
		if (dx * dx + dz * dz >= m_SurfaceRayReusePlanarMeters * m_SurfaceRayReusePlanarMeters)
			return true;

		if (Mathf.Abs(_emitWorld.y - m_GroundClipCacheEmitY) >= m_SurfaceRayReuseVerticalMeters)
			return true;

		return false;
	}

	private AudioClip[] ProbeGroundSurfaceForClips()
	{
		Vector3 origin = GetEmitWorldPosition() + Vector3.up * m_GroundRayUpOffset;
		if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, m_GroundRayLength, m_GroundLayers, QueryTriggerInteraction.Ignore))
			return m_FootstepClips;

		for (int i = 0; i < m_SurfaceRules.Length; i++)
		{
			FootstepSurfaceRule rule = m_SurfaceRules[i];
			if (rule.Clips == null || rule.Clips.Length == 0)
				continue;

			bool hasLayerFilter = rule.Layers.value != 0;
			bool hasMatFilter = rule.PhysicsMaterial != null;
			if (!hasLayerFilter && !hasMatFilter)
				continue;

			bool layerOk = !hasLayerFilter || (((1 << hit.collider.gameObject.layer) & rule.Layers) != 0);
			bool matOk = !hasMatFilter || hit.collider.sharedMaterial == rule.PhysicsMaterial;
			if (layerOk && matOk)
				return rule.Clips;
		}

		return m_FootstepClips;
	}

	private AudioClip[] ResolveClipsForGround()
	{
		if (m_SurfaceRules == null || m_SurfaceRules.Length == 0)
			return m_FootstepClips;

		Vector3 emit = GetEmitWorldPosition();
		if (!ShouldRefreshGroundSurfaceProbe(emit) && m_CachedGroundClipPool != null)
			return m_CachedGroundClipPool;

		m_CachedGroundClipPool = ProbeGroundSurfaceForClips();
		m_HasGroundClipCache = true;
		m_GroundClipCacheTime = Time.time;
		m_GroundClipCachePlanarXZ = new Vector2(emit.x, emit.z);
		m_GroundClipCacheEmitY = emit.y;
		return m_CachedGroundClipPool;
	}

	private void PrepareAudioSourceWorldPositionFor3D()
	{
		if (!m_SyncAudioSourceWorldPositionToEmitPoint || m_AudioSource == null || m_EmitFrom == null)
			return;

		Transform a = m_AudioSource.transform;
		Transform e = m_EmitFrom;
		if (a == e || a.IsChildOf(e))
			return;

		a.position = e.position;
	}

	private void PlayFootstepInternal()
	{
		AudioClip[] pool = ResolveClipsForGround();
		if (pool == null || pool.Length == 0)
			return;

		AudioClip clip = pool[Random.Range(0, pool.Length)];
		if (clip == null)
			return;

		float vol = Mathf.Clamp01(1f + Random.Range(-m_VolumeJitter, m_VolumeJitter));
		float pitch = Mathf.Clamp(1f + Random.Range(-m_PitchJitter, m_PitchJitter), 0.5f, 1.5f);

		Vector3 pos = GetEmitWorldPosition();

		if (m_AudioSource != null)
		{
			PrepareAudioSourceWorldPositionFor3D();
			float savedPitch = m_AudioSource.pitch;
			m_AudioSource.pitch = pitch;
			m_AudioSource.PlayOneShot(clip, vol);
			m_AudioSource.pitch = savedPitch;
			return;
		}

		AudioSource.PlayClipAtPoint(clip, pos, vol);
	}
	#endregion
}
