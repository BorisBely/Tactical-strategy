using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Шум снаряжения при движении стоя: случайные one-shot клипы с интервалами (чаще/громче на беге, реже/тише на шаге).
/// В приседе цикл не играет. При смене стойки — отдельные one-shot переходы.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitGearRattleAudio : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private AudioSource m_AudioSource;
	[SerializeField] private Transform m_EmitFrom;
	[SerializeField] private NavMeshAgent m_Agent;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;

	[Header("Loop clips")]
	[SerializeField] private AudioClip[] m_LoopClips;

	[Header("Stance transition clips")]
	[SerializeField] private AudioClip m_StandToCrouchClip;
	[SerializeField] private AudioClip m_CrouchToStandClip;

	[Header("Run / sprint loop")]
	[SerializeField, Min(0.05f)] private float m_RunIntervalMinSeconds = 0.22f;
	[SerializeField, Min(0.05f)] private float m_RunIntervalMaxSeconds = 0.48f;
	[SerializeField, Range(0f, 1f)] private float m_RunBaseVolume = 0.42f;

	[Header("Walk loop")]
	[SerializeField, Min(0.05f)] private float m_WalkIntervalMinSeconds = 0.55f;
	[SerializeField, Min(0.05f)] private float m_WalkIntervalMaxSeconds = 0.95f;
	[SerializeField, Range(0f, 1f)] private float m_WalkBaseVolume = 0.22f;

	[Header("Stance transition")]
	[SerializeField, Range(0f, 1f)] private float m_StanceTransitionVolume = 0.5f;

	[Header("Movement")]
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;
	[SerializeField] private bool m_RequireNavAgentMoving = true;

	[Header("3D sound")]
	[SerializeField] private bool m_ApplySpatialPreset = true;
	[SerializeField, Min(0.1f)] private float m_SpatialMaxDistance = 14f;
	[SerializeField] private bool m_SyncAudioSourceWorldPositionToEmitPoint = true;

	[Header("Variation")]
	[SerializeField, Range(0f, 0.2f)] private float m_VolumeJitter = 0.05f;
	[SerializeField, Range(0f, 0.12f)] private float m_PitchJitter = 0.035f;
	#endregion

	#region Private Fields
	private LocomotionStance m_LastStance = LocomotionStance.Standing;
	private float m_NextLoopTime = -1f;
	private bool m_WasLooping;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		AssignReferencesIfMissing();
		EnsureAudioSource();
	}

	private void Awake()
	{
		AssignReferencesIfMissing();
		EnsureAudioSource();
		ApplySpatialPresetIfNeeded();

		if (m_StanceSource != null)
			m_LastStance = m_StanceSource.CurrentStance;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_RunIntervalMaxSeconds < m_RunIntervalMinSeconds)
			m_RunIntervalMaxSeconds = m_RunIntervalMinSeconds;
		if (m_WalkIntervalMaxSeconds < m_WalkIntervalMinSeconds)
			m_WalkIntervalMaxSeconds = m_WalkIntervalMinSeconds;

		if (m_AudioSource == null)
			EnsureAudioSource();

		if (!Application.isPlaying && m_AudioSource != null)
			ApplySpatialPresetIfNeeded();
	}
#endif

	private void Update()
	{
		TickStanceTransitions();
		TickLocomotionLoop();
	}
	#endregion

	#region Private Methods
	private void AssignReferencesIfMissing()
	{
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();
		if (m_StanceSource == null)
			m_StanceSource = GetComponent<UnitAnimatorStance>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_EmitFrom == null)
			m_EmitFrom = transform;
	}

	private void EnsureAudioSource()
	{
		if (m_AudioSource != null)
			return;

		if (TryGetComponent(out AudioSource onSelf))
		{
			m_AudioSource = onSelf;
			return;
		}

		var sourceGo = new GameObject("GearRattleAudioSource");
		sourceGo.transform.SetParent(transform, false);
		m_AudioSource = sourceGo.AddComponent<AudioSource>();
		m_AudioSource.playOnAwake = false;
		m_AudioSource.loop = false;
	}

	private void ApplySpatialPresetIfNeeded()
	{
		if (m_AudioSource == null || !m_ApplySpatialPreset)
			return;

		UnitNonFireAudioUtility.ConfigureSpatial(m_AudioSource, m_SpatialMaxDistance);
	}

	private void TickStanceTransitions()
	{
		if (m_StanceSource == null)
			return;

		LocomotionStance current = m_StanceSource.CurrentStance;
		if (current == m_LastStance)
			return;

		if (m_LastStance == LocomotionStance.Standing && current == LocomotionStance.Crouch)
			PlayTransitionClip(m_StandToCrouchClip);
		else if (m_LastStance == LocomotionStance.Crouch && current == LocomotionStance.Standing)
			PlayTransitionClip(m_CrouchToStandClip);

		m_LastStance = current;
		m_NextLoopTime = -1f;
		m_WasLooping = false;
	}

	private void TickLocomotionLoop()
	{
		if (!TryResolveLoopProfile(out float intervalMin, out float intervalMax, out float baseVolume))
		{
			m_WasLooping = false;
			m_NextLoopTime = -1f;
			return;
		}

		if (!m_WasLooping || m_NextLoopTime < 0f)
			ScheduleNextLoop(intervalMin, intervalMax);

		m_WasLooping = true;

		if (Time.time < m_NextLoopTime)
			return;

		PlayLoopClip(baseVolume);
		ScheduleNextLoop(intervalMin, intervalMax);
	}

	private bool TryResolveLoopProfile(out float _intervalMin, out float _intervalMax, out float _baseVolume)
	{
		_intervalMin = 0f;
		_intervalMax = 0f;
		_baseVolume = 0f;

		if (m_LoopClips == null || m_LoopClips.Length == 0)
			return false;

		if (m_StanceSource == null || m_StanceSource.CurrentStance != LocomotionStance.Standing)
			return false;

		if (m_RequireNavAgentMoving && !IsNavAgentMoving())
			return false;

		if (IsRunOrSprintMoveMode())
		{
			_intervalMin = m_RunIntervalMinSeconds;
			_intervalMax = m_RunIntervalMaxSeconds;
			_baseVolume = m_RunBaseVolume;
			return true;
		}

		if (IsNavAgentMoving())
		{
			_intervalMin = m_WalkIntervalMinSeconds;
			_intervalMax = m_WalkIntervalMaxSeconds;
			_baseVolume = m_WalkBaseVolume;
			return true;
		}

		return false;
	}

	private bool IsRunOrSprintMoveMode()
	{
		if (m_ClickToMove != null && m_ClickToMove.enabled)
			return m_ClickToMove.IsRunMoveMode || m_ClickToMove.IsSprintMoveMode;

		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.IsRunMoveMode || m_LocomotionDriver.IsSprintMoveMode;

		return false;
	}

	private bool IsNavAgentMoving()
	{
		if (m_Agent == null)
			return false;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		if (velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
			return true;

		return m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	private void ScheduleNextLoop(float _intervalMin, float _intervalMax)
	{
		m_NextLoopTime = Time.time + Random.Range(_intervalMin, _intervalMax);
	}

	private void PlayLoopClip(float _baseVolume)
	{
		AudioClip clip = PickRandomLoopClip();
		if (clip == null)
			return;

		PlayClip(clip, _baseVolume);
	}

	private void PlayTransitionClip(AudioClip _clip)
	{
		if (_clip == null)
			return;

		PlayClip(_clip, m_StanceTransitionVolume);
	}

	private AudioClip PickRandomLoopClip()
	{
		if (m_LoopClips == null || m_LoopClips.Length == 0)
			return null;

		AudioClip clip = m_LoopClips[Random.Range(0, m_LoopClips.Length)];
		return clip != null ? clip : null;
	}

	private void PlayClip(AudioClip _clip, float _baseVolume)
	{
		if (_clip == null || m_AudioSource == null || _baseVolume <= 0f)
			return;

		PrepareAudioSourceWorldPositionFor3D();

		float jitteredVolume = _baseVolume * (1f + Random.Range(-m_VolumeJitter, m_VolumeJitter));
		float volume = UnitNonFireAudioUtility.ScaleVolume(Mathf.Clamp01(jitteredVolume));
		float pitch = Mathf.Clamp(1f + Random.Range(-m_PitchJitter, m_PitchJitter), 0.5f, 1.5f);

		float savedPitch = m_AudioSource.pitch;
		m_AudioSource.pitch = pitch;
		m_AudioSource.PlayOneShot(_clip, volume);
		m_AudioSource.pitch = savedPitch;
	}

	private void PrepareAudioSourceWorldPositionFor3D()
	{
		if (!m_SyncAudioSourceWorldPositionToEmitPoint || m_AudioSource == null || m_EmitFrom == null)
			return;

		Transform audioTransform = m_AudioSource.transform;
		Transform emitTransform = m_EmitFrom;
		if (audioTransform == emitTransform || audioTransform.IsChildOf(emitTransform))
			return;

		audioTransform.position = emitTransform.position;
	}
	#endregion
}
