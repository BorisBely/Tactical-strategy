using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Scene helper for Stage G1 detection monotonicity checks (presets A–G).
/// Auto-spawns 1 Player observer + 1 Enemy target via UnitSceneSpawner when refs are empty.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class DetectionTestController : MonoBehaviour
{
	public enum MoveMode
	{
		Idle = 0,
		Walk = 1,
		Run = 2
	}

	[System.Serializable]
	public struct DetectionPreset
	{
		public string Id;
		public float DistanceMeters;
		public float FovOffsetDegrees;
		public MoveMode Movement;
		[Tooltip("Informational only — exposure comes from real LOS / hit-zones.")]
		public float ExpectedBodyExposure01;
	}

	#region Serialized
	[Header("Wiring")]
	[SerializeField] private Transform m_Observer;
	[SerializeField] private Transform m_Target;
	[SerializeField] private DetectionProcessor m_DetectionProcessor;
	[SerializeField] private UnitSceneSpawner m_UnitSpawner;
	[SerializeField] private bool m_AutoSpawnPairIfMissing = true;

	[Header("Scene prep")]
	[SerializeField] private bool m_ForceDisableRangeTargetsOnStart = true;
	[SerializeField] private bool m_AssertMissionSpawnerOff = true;
	[SerializeField] private bool m_KeepEnemyPatrolUnitDisabled = true;

	[SerializeField] private DetectionPreset[] m_Presets =
	{
		new DetectionPreset { Id = "A", DistanceMeters = 10f, FovOffsetDegrees = 0f, Movement = MoveMode.Idle, ExpectedBodyExposure01 = 1f },
		new DetectionPreset { Id = "B", DistanceMeters = 30f, FovOffsetDegrees = 0f, Movement = MoveMode.Idle, ExpectedBodyExposure01 = 1f },
		new DetectionPreset { Id = "C", DistanceMeters = 80f, FovOffsetDegrees = 15f, Movement = MoveMode.Walk, ExpectedBodyExposure01 = 0.5f },
		new DetectionPreset { Id = "D", DistanceMeters = 100f, FovOffsetDegrees = 50f, Movement = MoveMode.Walk, ExpectedBodyExposure01 = 0.5f },
		new DetectionPreset { Id = "E", DistanceMeters = 200f, FovOffsetDegrees = 0f, Movement = MoveMode.Idle, ExpectedBodyExposure01 = 0.2f },
		new DetectionPreset { Id = "F", DistanceMeters = 400f, FovOffsetDegrees = 50f, Movement = MoveMode.Idle, ExpectedBodyExposure01 = 0.1f },
		new DetectionPreset { Id = "G", DistanceMeters = 400f, FovOffsetDegrees = 50f, Movement = MoveMode.Run, ExpectedBodyExposure01 = 0.1f }
	};

	[SerializeField] private float m_WalkSpeed = 1.4f;
	[SerializeField] private float m_RunSpeed = 4.5f;

	[Header("V1.9 calibration pad")]
	[SerializeField] private bool m_UseCalibrationPad = true;
	[SerializeField] private Vector3 m_CalibrationObserverPosition = new Vector3(0f, 0f, 16f);
	[SerializeField] private Vector3 m_CalibrationLookAxis = Vector3.forward;
	[SerializeField, Min(1f)] private float m_CalibrationNavSampleRadius = 8f;
	#endregion

	#region Private Fields
	private int m_PresetIndex;
	private MoveMode m_ActiveMoveMode = MoveMode.Idle;
	private NavMeshAgent m_TargetAgent;
	private NavMeshAgent m_ObserverAgent;
	private Vector3 m_MoveAxis = Vector3.right;
	private UnitVision m_ObserverVision;
	private bool m_AllowTargetStrafe = true;
	private readonly DetectionCalibrationExposureStaging m_ExposureStaging = new DetectionCalibrationExposureStaging();
	private float m_PendingCalibrationExposure01 = 1f;
	private bool m_ApplyCalibrationExposureStaging;
	#endregion

	#region Public Properties
	public Transform Observer => m_Observer;
	public Transform Target => m_Target;
	public DetectionProcessor DetectionProcessor => m_DetectionProcessor;
	public DetectionCalibrationExposureStaging ExposureStaging => m_ExposureStaging;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (DetectionHarnessPlayMode.RunCalibrationStrict &&
		    GetComponent<DetectionCalibrationRuntimeStrictSmoke>() == null)
			gameObject.AddComponent<DetectionCalibrationRuntimeStrictSmoke>();
		if (DetectionHarnessPlayMode.RunMemoryCalibration &&
		    GetComponent<MemoryCalibrationRuntimeSmoke>() == null)
			gameObject.AddComponent<MemoryCalibrationRuntimeSmoke>();
		if (DetectionHarnessPlayMode.RunIdentityCalibration &&
		    GetComponent<IdentityCalibrationRuntimeSmoke>() == null)
			gameObject.AddComponent<IdentityCalibrationRuntimeSmoke>();
		if (DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		    GetComponent<AIPerceptionHandoffSmoke>() == null)
			gameObject.AddComponent<AIPerceptionHandoffSmoke>();
		if (DetectionHarnessPlayMode.RunAITacticalState &&
		    GetComponent<AITacticalStateRuntimeSmoke>() == null)
			gameObject.AddComponent<AITacticalStateRuntimeSmoke>();
		if (DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		    GetComponent<UseOfForcePolicyRuntimeSmoke>() == null)
			gameObject.AddComponent<UseOfForcePolicyRuntimeSmoke>();
		if (DetectionHarnessPlayMode.RunGStage == DetectionHarnessPlayMode.AllGStages &&
		    GetComponent<DetectionGRegressionPlaySmoke>() == null)
			gameObject.AddComponent<DetectionGRegressionPlaySmoke>();
	}

	private void Start()
	{
		PrepareSceneNoise();
		EnsureObserverAndTarget();
		EnsureDetectionProcessor();

		if (m_Target != null)
			m_Target.TryGetComponent(out m_TargetAgent);
		if (m_Observer != null)
		{
			m_Observer.TryGetComponent(out m_ObserverVision);
			m_Observer.TryGetComponent(out m_ObserverAgent);
			PinObserverPerceptionRange();
		}

		DetectionCalibrationRuntimeSmoke runtimeSmoke = GetComponent<DetectionCalibrationRuntimeSmoke>();
		DetectionCalibrationRuntimeStrictSmoke strictSmoke = GetComponent<DetectionCalibrationRuntimeStrictSmoke>();
		MemoryCalibrationRuntimeSmoke memorySmoke = GetComponent<MemoryCalibrationRuntimeSmoke>();
		IdentityCalibrationRuntimeSmoke identitySmoke = GetComponent<IdentityCalibrationRuntimeSmoke>();
		AIPerceptionHandoffSmoke aiPerceptionSmoke = GetComponent<AIPerceptionHandoffSmoke>();
		AITacticalStateRuntimeSmoke aiTacticalSmoke = GetComponent<AITacticalStateRuntimeSmoke>();
		UseOfForcePolicyRuntimeSmoke useOfForceSmoke = GetComponent<UseOfForcePolicyRuntimeSmoke>();
		bool harnessOwnsPlay =
			DetectionHarnessPlayMode.IsGRegressionPlay ||
			(runtimeSmoke != null && runtimeSmoke.WillRunOnStart) ||
			(strictSmoke != null && strictSmoke.WillRunOnStart) ||
			(memorySmoke != null && memorySmoke.WillRunOnStart) ||
			(identitySmoke != null && identitySmoke.WillRunOnStart) ||
			(aiPerceptionSmoke != null && aiPerceptionSmoke.WillRunOnStart) ||
			(aiTacticalSmoke != null && aiTacticalSmoke.WillRunOnStart) ||
			(useOfForceSmoke != null && useOfForceSmoke.WillRunOnStart);
		if (!harnessOwnsPlay)
			ApplyPreset(0);
	}

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (keyboard != null)
		{
			if (keyboard.rightBracketKey.wasPressedThisFrame)
				ApplyPreset((m_PresetIndex + 1) % Mathf.Max(1, m_Presets.Length));
			if (keyboard.leftBracketKey.wasPressedThisFrame)
				ApplyPreset((m_PresetIndex - 1 + m_Presets.Length) % Mathf.Max(1, m_Presets.Length));
			if (keyboard.rKey.wasPressedThisFrame && m_DetectionProcessor != null)
				m_DetectionProcessor.ClearContacts();

			for (int i = 0; i < 7 && i < m_Presets.Length; i++)
			{
				if (WasDigitPressed(keyboard, i + 1))
					ApplyPreset(i);
			}
		}

		TickMovement();
	}

	private void LateUpdate()
	{
		m_ExposureStaging.Follow();
	}

	private void OnDestroy()
	{
		m_ExposureStaging.Clear();
	}

	private void OnGUI()
	{
		if (m_Presets == null || m_Presets.Length == 0)
			return;

		DetectionPreset p = m_Presets[Mathf.Clamp(m_PresetIndex, 0, m_Presets.Length - 1)];
		GUI.Box(new Rect(12f, 230f, 360f, 110f), "DetectionTestController");
		GUI.Label(new Rect(24f, 254f, 330f, 80f),
			$"Preset {p.Id}  ({m_PresetIndex + 1}/{m_Presets.Length})\n" +
			$"Dist={p.DistanceMeters:0}m  FOV={p.FovOffsetDegrees:0}°  Move={p.Movement}\n" +
			$"ExpectedExposure≈{p.ExpectedBodyExposure01:0%} (LOS-dependent)\n" +
			$"Keys: [ ] cycle presets, 1-7 jump, R reset progress");
	}
	#endregion

	#region Public Methods
	public void ApplyPreset(int _index)
	{
		if (m_Presets == null || m_Presets.Length == 0)
			return;
		if (m_Observer == null || m_Target == null)
		{
			Debug.LogWarning("[DetectionTestController] Assign Observer and Target.", this);
			return;
		}

		m_PresetIndex = Mathf.Clamp(_index, 0, m_Presets.Length - 1);
		ApplyCalibrationPreset(m_Presets[m_PresetIndex], false);
	}

	/// <summary>
	/// Idle 10 m on the calibration pad. Stops strafe so G5–G7 forgotten waits do not walk the pair.
	/// </summary>
	public void ResetPairToIdleCalibrationPad()
	{
		if (m_Presets == null || m_Presets.Length == 0 || m_Observer == null || m_Target == null)
			return;

		m_PresetIndex = 0;
		ApplyCalibrationPreset(m_Presets[0], true);
	}

	private void ApplyCalibrationPreset(DetectionPreset preset, bool _useCalibrationPad)
	{
		if (m_Observer == null || m_Target == null)
			return;

		m_ActiveMoveMode = preset.Movement;
		m_AllowTargetStrafe = preset.Movement != MoveMode.Idle;

		if (m_ObserverAgent == null && m_Observer != null)
			m_Observer.TryGetComponent(out m_ObserverAgent);
		if (m_TargetAgent == null && m_Target != null)
			m_Target.TryGetComponent(out m_TargetAgent);
		if (m_ObserverVision == null && m_Observer != null)
			m_Observer.TryGetComponent(out m_ObserverVision);

		bool usePad = _useCalibrationPad && (m_UseCalibrationPad || m_ApplyCalibrationExposureStaging);
		if (usePad)
			m_ExposureStaging.HideCover();
		else
		{
			m_ApplyCalibrationExposureStaging = false;
			m_ExposureStaging.Clear();
		}

		Vector3 lookAxis = m_CalibrationLookAxis;
		lookAxis.y = 0f;
		if (lookAxis.sqrMagnitude < 0.0001f)
			lookAxis = Vector3.forward;
		lookAxis.Normalize();

		Vector3 observerPos;
		Vector3 forward;
		if (usePad)
		{
			observerPos = SampleNavHeight(m_CalibrationObserverPosition);
			forward = lookAxis;
			PlaceUnit(m_Observer, m_ObserverAgent, observerPos, Quaternion.LookRotation(forward, Vector3.up));
		}
		else
		{
			observerPos = m_Observer.position;
			forward = m_Observer.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 0.0001f)
				forward = Vector3.forward;
			forward.Normalize();
		}

		Quaternion yaw = Quaternion.AngleAxis(preset.FovOffsetDegrees, Vector3.up);
		Vector3 dir = yaw * forward;
		Vector3 targetPos = usePad
			? SampleNavHeight(observerPos + dir * preset.DistanceMeters)
			: SampleNavPosition(observerPos + dir * preset.DistanceMeters);

		PlaceUnit(
			m_Target,
			m_TargetAgent,
			targetPos,
			Quaternion.LookRotation(-dir, Vector3.up));

		if (m_Target.TryGetComponent(out UnitNavLocomotionDriver targetDriver))
			targetDriver.enabled = false;
		if (m_Observer.TryGetComponent(out UnitClickToMove clickToMove))
			clickToMove.enabled = false;

		m_MoveAxis = Vector3.Cross(Vector3.up, dir).normalized;
		if (m_MoveAxis.sqrMagnitude < 0.0001f)
			m_MoveAxis = Vector3.right;

		PinObserverPerceptionRange();
		if (m_ObserverVision != null)
			m_ObserverVision.RefreshBodyHitZones();
		if (m_Target.TryGetComponent(out UnitVision targetVision))
			targetVision.RefreshBodyHitZones();

		Physics.SyncTransforms();

		if (usePad)
		{
			m_ExposureStaging.HideCover();
			TryMirrorCalibrationYawIfSceneBlocks(preset, observerPos, forward);
		}

		if (m_ApplyCalibrationExposureStaging)
			m_ExposureStaging.Apply(m_Observer, m_Target, m_PendingCalibrationExposure01);

		m_ExposureStaging.Follow();
		Physics.SyncTransforms();

		if (m_DetectionProcessor != null)
			m_DetectionProcessor.ClearContacts();

		if (m_ObserverVision != null)
			m_ObserverVision.RequestImmediateScan();

		Debug.Log(
			$"[DetectionTestController] Applied preset {preset.Id}: " +
			$"{preset.DistanceMeters}m, FOV {preset.FovOffsetDegrees}°, {preset.Movement} | {m_ExposureStaging.Note}",
			this);
	}

	public void ApplyCalibrationScenario(in DetectionCalibrationScenarios.Scenario _scenario)
	{
		MoveMode move = MoveMode.Idle;
		if (_scenario.MoveSpeedMeters >= DetectionCalibrationScenarios.RunSpeedMeters - 0.01f)
			move = MoveMode.Run;
		else if (_scenario.MoveSpeedMeters >= DetectionCalibrationScenarios.WalkSpeedMeters - 0.01f)
			move = MoveMode.Walk;

		var preset = new DetectionPreset
		{
			Id = _scenario.Id,
			DistanceMeters = _scenario.DistanceMeters,
			FovOffsetDegrees = _scenario.FovOffsetDegrees,
			Movement = move,
			ExpectedBodyExposure01 = _scenario.Exposure01
		};

		m_PendingCalibrationExposure01 = _scenario.Exposure01;
		m_ApplyCalibrationExposureStaging = true;
		m_ExposureStaging.BeginScenario();
		ApplyCalibrationPreset(preset, true);
		m_ApplyCalibrationExposureStaging = false;
	}
	#endregion

	#region Private Methods
	private void PrepareSceneNoise()
	{
		if (m_ForceDisableRangeTargetsOnStart)
		{
			ShootingRangeManager range = Object.FindAnyObjectByType<ShootingRangeManager>();
			if (range != null)
				range.SetAllTargetsEnabled(false);
		}

		if (m_AssertMissionSpawnerOff)
		{
			MissionPrepSquadSpawner spawner = Object.FindAnyObjectByType<MissionPrepSquadSpawner>();
			if (spawner != null)
			{
				var field = typeof(MissionPrepSquadSpawner).GetField(
					"m_SpawnOnStart",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				if (field != null && field.GetValue(spawner) is bool spawnOnStart && spawnOnStart)
				{
					Debug.LogError(
						"[DetectionTestController] MissionPrepSquadSpawner.m_SpawnOnStart is still ON. " +
						"Disable it for G1 detection tests.",
						spawner);
				}
			}
		}

		if (m_KeepEnemyPatrolUnitDisabled)
		{
			GameObject patrol = GameObject.Find("EnemyPatrolUnit");
			if (patrol != null && patrol.activeSelf)
				patrol.SetActive(false);
		}
	}

	private void EnsureObserverAndTarget()
	{
		if (m_Observer != null && m_Target != null)
			return;

		if (!m_AutoSpawnPairIfMissing)
		{
			Debug.LogWarning("[DetectionTestController] Observer/Target missing and auto-spawn disabled.", this);
			return;
		}

		if (m_UnitSpawner == null)
			m_UnitSpawner = Object.FindAnyObjectByType<UnitSceneSpawner>();

		if (m_UnitSpawner == null)
		{
			Debug.LogError("[DetectionTestController] No UnitSceneSpawner found for auto-spawn.", this);
			return;
		}

		if (!m_UnitSpawner.TrySpawnDetectionTestPair(out GameObject player, out GameObject enemy))
			return;

		if (m_Observer == null && player != null)
			m_Observer = player.transform;
		if (m_Target == null && enemy != null)
			m_Target = enemy.transform;

		Debug.Log(
			$"[DetectionTestController] Auto-spawned Observer={m_Observer?.name}, Target={m_Target?.name}",
			this);
	}

	private void EnsureDetectionProcessor()
	{
		if (m_Observer == null)
			return;

		if (m_DetectionProcessor == null)
			m_Observer.TryGetComponent(out m_DetectionProcessor);

		if (m_DetectionProcessor == null)
			m_DetectionProcessor = m_Observer.gameObject.AddComponent<DetectionProcessor>();
	}

	private void PinObserverPerceptionRange()
	{
		if (m_ObserverVision == null && m_Observer != null)
			m_Observer.TryGetComponent(out m_ObserverVision);
		if (m_ObserverVision == null)
			return;

		m_ObserverVision.SetVisionRange(DetectionQualityMath.DefaultFarMeters);
	}

	private static bool WasDigitPressed(Keyboard _keyboard, int _digit)
	{
		return _digit switch
		{
			1 => _keyboard.digit1Key.wasPressedThisFrame || _keyboard.numpad1Key.wasPressedThisFrame,
			2 => _keyboard.digit2Key.wasPressedThisFrame || _keyboard.numpad2Key.wasPressedThisFrame,
			3 => _keyboard.digit3Key.wasPressedThisFrame || _keyboard.numpad3Key.wasPressedThisFrame,
			4 => _keyboard.digit4Key.wasPressedThisFrame || _keyboard.numpad4Key.wasPressedThisFrame,
			5 => _keyboard.digit5Key.wasPressedThisFrame || _keyboard.numpad5Key.wasPressedThisFrame,
			6 => _keyboard.digit6Key.wasPressedThisFrame || _keyboard.numpad6Key.wasPressedThisFrame,
			7 => _keyboard.digit7Key.wasPressedThisFrame || _keyboard.numpad7Key.wasPressedThisFrame,
			_ => false
		};
	}

	private static void PlaceUnit(Transform _unit, NavMeshAgent _agent, Vector3 _position, Quaternion _rotation)
	{
		if (_unit == null)
			return;

		bool onMesh = NavMesh.SamplePosition(_position, out NavMeshHit navHit, 0.35f, NavMesh.AllAreas)
			&& (navHit.position - _position).sqrMagnitude <= 0.25f;

		if (_agent != null && onMesh)
		{
			if (!_agent.enabled)
				_agent.enabled = true;

			if (_agent.enabled)
			{
				bool warped = _agent.Warp(_position);
				if (warped && _agent.isOnNavMesh)
				{
					_agent.ResetPath();
					_agent.isStopped = true;
					_agent.velocity = Vector3.zero;
					_unit.rotation = _rotation;
					return;
				}
			}
		}

		if (_agent != null && _agent.enabled)
			_agent.enabled = false;

		_unit.SetPositionAndRotation(_position, _rotation);
	}

	private Vector3 SampleNavPosition(Vector3 _desired)
	{
		if (NavMesh.SamplePosition(_desired, out NavMeshHit hit, m_CalibrationNavSampleRadius, NavMesh.AllAreas))
			return hit.position;
		return _desired;
	}

	private void TickMovement()
	{
		if (m_Target == null || m_ActiveMoveMode == MoveMode.Idle || !m_AllowTargetStrafe)
			return;

		float speed = m_ActiveMoveMode == MoveMode.Run ? m_RunSpeed : m_WalkSpeed;
		Vector3 delta = m_MoveAxis * (speed * Time.deltaTime);

		if (m_TargetAgent != null && m_TargetAgent.enabled && m_TargetAgent.isOnNavMesh)
		{
			m_TargetAgent.isStopped = false;
			m_TargetAgent.Move(delta);
		}
		else
		{
			m_Target.position += delta;
		}

		m_ExposureStaging.Follow();
	}

	private Vector3 SampleNavHeight(Vector3 _desired)
	{
		if (NavMesh.SamplePosition(_desired, out NavMeshHit hit, m_CalibrationNavSampleRadius, NavMesh.AllAreas))
			return new Vector3(_desired.x, hit.position.y, _desired.z);
		if (NavMesh.SamplePosition(_desired, out hit, 40f, NavMesh.AllAreas))
			return new Vector3(_desired.x, hit.position.y, _desired.z);
		return _desired;
	}

	private void TryMirrorCalibrationYawIfSceneBlocks(DetectionPreset _preset, Vector3 _observerPos, Vector3 _forward)
	{
		m_ExposureStaging.MarkYawMirrored(false, null);
		if (_preset.FovOffsetDegrees < 0.5f)
			return;

		Physics.SyncTransforms();
		if (!m_ExposureStaging.IsSceneFullyBlocking(m_Observer, m_Target))
			return;

		Vector3 dir = Quaternion.AngleAxis(-_preset.FovOffsetDegrees, Vector3.up) * _forward;
		Vector3 targetPos = SampleNavHeight(_observerPos + dir * _preset.DistanceMeters);
		PlaceUnit(m_Target, m_TargetAgent, targetPos, Quaternion.LookRotation(-dir, Vector3.up));
		Physics.SyncTransforms();

		if (m_ExposureStaging.IsSceneFullyBlocking(m_Observer, m_Target))
		{
			dir = Quaternion.AngleAxis(_preset.FovOffsetDegrees, Vector3.up) * _forward;
			targetPos = SampleNavHeight(_observerPos + dir * _preset.DistanceMeters);
			PlaceUnit(m_Target, m_TargetAgent, targetPos, Quaternion.LookRotation(-dir, Vector3.up));
			Physics.SyncTransforms();
			m_ExposureStaging.MarkYawMirrored(false, "scene LOS blocked; mirror yaw also blocked");
			return;
		}

		m_MoveAxis = Vector3.Cross(Vector3.up, dir).normalized;
		if (m_MoveAxis.sqrMagnitude < 0.0001f)
			m_MoveAxis = Vector3.right;
		m_ExposureStaging.MarkYawMirrored(true, "yaw mirrored to clear scene LOS");
	}
	#endregion
}
