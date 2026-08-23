using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.AI;
#pragma warning disable CS0414

/// <summary>
/// Звук шага: поверхность (<see cref="FootstepSurfaceRule"/>), воспроизведение.
/// Момент шага — Animation Event <see cref="Footstep"/>; если Animator на дочернем объекте — <see cref="UnitFootstepsAnimatorEvents"/> на нём.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitFootsteps : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Пусто — ищется на этом объекте, на Emit From, затем в дочерних.")]
	[SerializeField] private AudioSource m_AudioSource;
	[Tooltip("Точка луча вниз и 3D-источника (если AudioSource не дочерний к Emit From).")]
	[SerializeField] private Transform m_EmitFrom;

	[Header("Поверхность")]
	[SerializeField] private LayerMask m_GroundLayers = ~0;
	[SerializeField, Min(0.01f)] private float m_GroundRayUpOffset = 0.35f;
	[SerializeField, Min(0.05f)] private float m_GroundRayLength = 2f;
	[Tooltip("Сверху вниз: первое подходящее правило. Пусто — только клипы по умолчанию.")]
	[SerializeField] private FootstepSurfaceRule[] m_SurfaceRules;

	[Header("Кэш луча")]
	[SerializeField, Min(0f)] private float m_SurfaceRayMinIntervalSeconds = 0.15f;
	[SerializeField, Min(0.01f)] private float m_SurfaceRayReusePlanarMeters = 0.3f;
	[SerializeField, Min(0.05f)] private float m_SurfaceRayReuseVerticalMeters = 0.35f;

	[Header("Клипы по умолчанию")]
	[SerializeField] private AudioClip[] m_FootstepClips;
	[Tooltip("Базовая громкость шага до общего множителя UnitNonFireAudioUtility (как ReloadSoundsVolume).")]
	[SerializeField, Range(0f, 1f)] private float m_FootstepBaseVolume = 0.6f;

	[Header("RTS / экономия")]
	[Tooltip("Без Raycast по поверхности — только клипы по умолчанию.")]
	[SerializeField] private bool m_RtsEconomyMode;
	[SerializeField, Min(0f)] private float m_RtsMaxPlanarDistanceFromListener;

	[Header("3D звук")]
	[SerializeField] private bool m_ApplySpatialPreset = true;
	[SerializeField, Min(0.01f)] private float m_SpatialMinDistance = 2.5f;
	[SerializeField, Min(0.1f)] private float m_SpatialMaxDistance = 18f;
	[SerializeField] private AudioRolloffMode m_VolumeRolloff = AudioRolloffMode.Logarithmic;
	[SerializeField, Range(0f, 5f)] private float m_DopplerLevel;
	[SerializeField] private bool m_SyncAudioSourceWorldPositionToEmitPoint = true;

	[Header("Вариация")]
	[SerializeField, Range(0f, 0.3f)] private float m_VolumeJitter = 0.06f;
	[SerializeField, Range(0f, 0.15f)] private float m_PitchJitter = 0.04f;

	[Header("Animation Event")]
	[Tooltip("Не играть шаг, если NavMeshAgent на юните считает, что он стоит (согласуй epsilon с UnitClickToMove).")]
	[SerializeField] private bool m_RequireNavAgentMoving = true;
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;

	[Header("Anti-double (blend/diagonal)")]
	[Tooltip("Защита от дублей при бленде/диагонали: не более одного шага за кадр.")]
	[SerializeField] private bool m_LimitOneFootstepPerFrame = true;
	[Tooltip("Минимальный интервал между звуками шагов (сек). 0 — отключить. Полезно, когда два Animation Event прилетают почти одновременно.")]
	[SerializeField, Min(0f)] private float m_FootstepMinIntervalSeconds = 0.14f;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;

	private AudioClip[] m_CachedGroundClipPool;
	private bool m_HasGroundClipCache;
	private float m_GroundClipCacheTime;
	private Vector2 m_GroundClipCachePlanarXZ;
	private float m_GroundClipCacheEmitY;

	private Transform m_ListenerTransform;

	private int m_LastFootstepFrame = -1;
	private float m_LastFootstepTime = -999f;
	#endregion

	#region Public Properties
	public bool RtsEconomyMode => m_RtsEconomyMode;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		AssignAudioSourceIfMissing();
	}

	private void Awake()
	{
		m_Agent = GetComponentInParent<NavMeshAgent>();
		AssignAudioSourceIfMissing();
		CacheAudioListenerTransform();
		ApplySpatialPresetIfNeeded();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_SpatialMaxDistance < m_SpatialMinDistance + 0.01f)
			m_SpatialMaxDistance = m_SpatialMinDistance + 0.01f;

		if (m_AudioSource == null)
			AssignAudioSourceIfMissing();

		if (!Application.isPlaying && m_AudioSource != null)
			ApplySpatialPresetIfNeeded();
	}
#endif

	private void OnEnable()
	{
		InvalidateGroundClipCache();
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Animation Event (имя функции: <c>Footstep</c>) на объекте с Animator или через <see cref="UnitFootstepsAnimatorEvents"/>.
	/// </summary>
	public void Footstep()
	{
		if (m_RequireNavAgentMoving)
		{
			if (m_Agent == null || !IsNavAgentMoving())
				return;
		}

		if (!HasAnyClipsConfigured())
			return;

		if (m_LimitOneFootstepPerFrame && m_LastFootstepFrame == Time.frameCount)
			return;

		if (m_FootstepMinIntervalSeconds > 0f && (Time.time - m_LastFootstepTime) < m_FootstepMinIntervalSeconds)
			return;

		m_LastFootstepFrame = Time.frameCount;
		m_LastFootstepTime = Time.time;

		WorldSoundHub.PublishFootstep(transform, GetEmitWorldPosition());
		PlayFootstepInternal();
	}
	#endregion

	#region Private Methods
	private bool IsNavAgentMoving()
	{
		if (m_Agent == null)
			return false;

		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
			return true;

		return m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	private void AssignAudioSourceIfMissing()
	{
		if (m_AudioSource != null)
			return;

		if (TryGetComponent(out AudioSource onSelf))
		{
			m_AudioSource = onSelf;
			return;
		}

		if (m_EmitFrom != null && m_EmitFrom.TryGetComponent(out AudioSource onEmit))
		{
			m_AudioSource = onEmit;
			return;
		}

		m_AudioSource = GetComponentInChildren<AudioSource>(true);
	}

	private bool HasAnyClipsConfigured()
	{
		if (m_FootstepClips != null && m_FootstepClips.Length > 0)
			return true;

		if (!m_RtsEconomyMode && m_SurfaceRules != null)
		{
			for (int i = 0; i < m_SurfaceRules.Length; i++)
			{
				AudioClip[] c = m_SurfaceRules[i].Clips;
				if (c != null && c.Length > 0)
					return true;
			}
		}

		return false;
	}

	private void ApplySpatialPresetIfNeeded()
	{
		if (m_AudioSource == null)
			return;

		if (m_RtsEconomyMode)
		{
			m_AudioSource.spatialBlend = 0f;
			m_AudioSource.spatialize = false;
			m_AudioSource.dopplerLevel = 0f;
			return;
		}

		if (!m_ApplySpatialPreset)
			return;

		UnitNonFireAudioUtility.ConfigureSpatial(m_AudioSource, m_SpatialMaxDistance);
		m_AudioSource.dopplerLevel = m_DopplerLevel;
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

		if (m_SurfaceRules == null)
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
		if (m_RtsEconomyMode)
			return m_FootstepClips;

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

	private void CacheAudioListenerTransform()
	{
		if (m_RtsMaxPlanarDistanceFromListener <= 0f)
			return;

#if UNITY_2023_1_OR_NEWER
		AudioListener listener = Object.FindAnyObjectByType<AudioListener>(FindObjectsInactive.Exclude);
#else
		AudioListener listener = Object.FindObjectOfType<AudioListener>();
#endif
		m_ListenerTransform = listener != null ? listener.transform : null;
	}

	private bool IsBeyondRtsPlanarHearingRange()
	{
		if (m_RtsMaxPlanarDistanceFromListener <= 0f)
			return false;

		if (m_ListenerTransform == null)
			return false;

		Vector3 e = GetEmitWorldPosition();
		Vector3 l = m_ListenerTransform.position;
		float dx = e.x - l.x;
		float dz = e.z - l.z;
		float r = m_RtsMaxPlanarDistanceFromListener;
		return dx * dx + dz * dz > r * r;
	}

	private void PrepareAudioSourceWorldPositionFor3D()
	{
		if (m_RtsEconomyMode)
			return;

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
		if (IsBeyondRtsPlanarHearingRange())
			return;

		AudioClip[] pool = ResolveClipsForGround();
		if (pool == null || pool.Length == 0)
			return;

		AudioClip clip = pool[Random.Range(0, pool.Length)];
		if (clip == null)
			return;

		float jitteredVolume = m_FootstepBaseVolume * (1f + Random.Range(-m_VolumeJitter, m_VolumeJitter));
		float vol = UnitNonFireAudioUtility.ScaleVolume(Mathf.Clamp01(jitteredVolume));
		float pitch = Mathf.Clamp(1f + Random.Range(-m_PitchJitter, m_PitchJitter), 0.5f, 1.5f);

		if (m_AudioSource != null)
		{
			PrepareAudioSourceWorldPositionFor3D();
			float savedPitch = m_AudioSource.pitch;
			m_AudioSource.pitch = pitch;
			m_AudioSource.PlayOneShot(clip, vol);
			m_AudioSource.pitch = savedPitch;
			return;
		}

		UnitNonFireAudioUtility.PlayAtPoint(clip, GetEmitWorldPosition(), Mathf.Clamp01(jitteredVolume), m_SpatialMaxDistance);
	}
	#endregion
}

/// <summary>
/// Unity вызывает Animation Event только на <see cref="GameObject"/> с <see cref="Animator"/>.
/// Если модель с Animator — дочерний объект, повесьте этот компонент туда и в клипе укажите функцию <c>Footstep</c>.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitFootstepsAnimatorEvents : MonoBehaviour
{
	[SerializeField] private UnitFootsteps m_Footsteps;

	private void Awake()
	{
		if (m_Footsteps == null)
			m_Footsteps = GetComponentInParent<UnitFootsteps>();
	}

	/// <summary>Имя в Animation Event: <c>Footstep</c> (без параметров).</summary>
	public void Footstep()
	{
		if (m_Footsteps != null)
			m_Footsteps.Footstep();
	}
}
