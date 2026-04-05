using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Звуки шагов по пройденному расстоянию или Animation Event <see cref="Footstep"/>.
/// Какие клипы играть, задаёт локомоция (<see cref="UnitClickToMove"/>) через <see cref="SetActiveFootstepClipPool"/>; иначе — «Клипы по умолчанию».
/// Счёт шагов в <see cref="LateUpdate"/>, чтобы движение успело обновить пул в <c>Update</c>.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public sealed class UnitFootsteps : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Пусто — ищется на этом объекте, на Emit From, затем в дочерних (Reset / Awake).")]
	[SerializeField] private AudioSource m_AudioSource;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[Tooltip("Точка у ног для 3D-позиции, если AudioSource не дочерний к Emit From.")]
	[SerializeField] private Transform m_EmitFrom;

	[Header("RTS / простая экономия")]
	[Tooltip("2D-звук; игнорировать пул от локомоции — только клипы по умолчанию.")]
	[SerializeField] private bool m_RtsEconomyMode;
	[Tooltip("Не играть шаг по плоскости XZ дальше от AudioListener. 0 — не отсекать.")]
	[SerializeField, Min(0f)] private float m_RtsMaxPlanarDistanceFromListener;

	[Header("Клипы по умолчанию")]
	[Tooltip("Если локомоция не передала пул или вернулась к запасному варианту.")]
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
	[Tooltip("Если AudioSource не привязан к Emit From, перед каждым шагом переносить его в позицию ног.")]
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

	private AudioClip[] m_ActiveClipPoolOverride;

	private Transform m_ListenerTransform;
	#endregion

	#region Public Properties
	/// <summary>Режим RTS: локомоция не должна слать пул по поверхности.</summary>
	public bool RtsEconomyMode => m_RtsEconomyMode;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		AssignAudioSourceIfMissing();
	}

	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		AssignAudioSourceIfMissing();
		if (m_StanceSource == null)
			m_StanceSource = GetComponent<UnitAnimatorStance>();

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
		m_HasLastPosition = false;
		m_DistanceAccumulated = 0f;
		m_ActiveClipPoolOverride = null;
	}

	private void LateUpdate()
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
	/// Пул клипов на текущий кадр/движение от <see cref="UnitClickToMove"/> (луч по полу). Null или пустой — только «Клипы по умолчанию».
	/// В <see cref="m_RtsEconomyMode"/> вызов игнорируется.
	/// </summary>
	public void SetActiveFootstepClipPool(AudioClip[] _clips)
	{
		if (m_RtsEconomyMode)
			return;

		if (_clips == null || _clips.Length == 0)
			m_ActiveClipPoolOverride = null;
		else
			m_ActiveClipPoolOverride = _clips;
	}

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

		if (!m_RtsEconomyMode && m_ActiveClipPoolOverride != null && m_ActiveClipPoolOverride.Length > 0)
			return true;

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

	private AudioClip[] GetClipsToPlay()
	{
		if (m_RtsEconomyMode)
			return m_FootstepClips;

		if (m_ActiveClipPoolOverride != null && m_ActiveClipPoolOverride.Length > 0)
			return m_ActiveClipPoolOverride;

		return m_FootstepClips;
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

		AudioClip[] pool = GetClipsToPlay();
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
