using System;
using UnityEngine;

namespace CombatVehicleSystem
{
	/// <summary>
	/// Single entry point for external control. Routes command to drive / turret / weapon.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Rigidbody))]
	public class VehicleBrain : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private VehicleTuning m_Tuning;
		[SerializeField] private AudioSource m_EngineAudio;
		[SerializeField] private WheeledMotor m_WheeledMotor;
		[SerializeField] private TrackedMotor m_TrackedMotor;
		[SerializeField] private TurretAim m_TurretAim;
		[SerializeField] private WeaponMount m_WeaponMount;
		#endregion

		#region Private Fields
		private IAdvancedEngineAudio m_AdvancedEngineAudio;
		private Rigidbody m_Body;
		private IVehicleDriveGating m_Vehicle;
		private VehicleCommand m_Command = VehicleCommand.Idle;
		private bool m_ControlActive;
		private bool m_EngineRunning;
		private bool m_EngineReady;
		private float m_EngineReadyAt = -1f;
		#endregion

		#region Events
		public event Action<bool> EngineStateChanged;
		#endregion

		#region Public Properties
		public VehicleTuning Tuning => m_Tuning;
		public bool ControlActive => m_ControlActive;
		public bool EngineRunning => m_EngineRunning;
		public bool EngineReady => m_EngineRunning && m_EngineReady;
		public bool CanDrive => m_ControlActive && m_EngineRunning && m_EngineReady;
		public VehicleCommand CurrentCommand => m_Command;
		public WheeledMotor WheeledMotor => m_WheeledMotor;
		public float CurrentSpeedKmh
		{
			get
			{
				if (m_WheeledMotor != null)
					return m_WheeledMotor.CurrentSpeedKmh;
				if (m_TrackedMotor != null)
					return m_TrackedMotor.CurrentSpeedKmh;
				return 0f;
			}
		}
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Body = GetComponent<Rigidbody>();
			CacheModules();
			ApplyCenterOfMass();
			ApplyModuleTuning();
			ApplyRigidbodyMass();
			SetControlActive(false);
		}

		private void Update()
		{
			UpdateEngineReadyState();

			if (!m_ControlActive)
				return;

			if (!CanDrive)
			{
				m_Command = ResolveParkCommand();
			}

			VehicleCommand driveCommand = ResolveActiveDriveCommand();

			if (m_WheeledMotor != null)
				m_WheeledMotor.TickDrive(driveCommand);
			if (m_TrackedMotor != null)
				m_TrackedMotor.TickDrive(driveCommand);
			if (m_WeaponMount != null && CanDrive)
				m_WeaponMount.TickFire(m_Command);

			UpdateEngineAudio();
		}

		private void FixedUpdate()
		{
			VehicleCommand physicsCommand = ResolveActiveDriveCommand();

			if (m_WheeledMotor != null)
				m_WheeledMotor.TickPhysics(m_ControlActive, physicsCommand);
			if (m_TrackedMotor != null)
				m_TrackedMotor.TickPhysics(m_ControlActive, physicsCommand);

			if (!m_ControlActive || !CanDrive)
				return;

			if (m_TurretAim != null)
				m_TurretAim.TickAim(m_Command);
		}
		#endregion

		#region Public Methods
		public void SetTuning(VehicleTuning _tuning)
		{
			m_Tuning = _tuning;
			ApplyCenterOfMass();
			ApplyModuleTuning();
			ApplyRigidbodyMass();
		}

		public void SetControlActive(bool _active)
		{
			m_ControlActive = _active;
			if (!m_ControlActive)
			{
				m_Command = VehicleCommand.Idle;
				if (m_EngineRunning)
					StopEngine();
			}

			if (m_TurretAim != null)
				m_TurretAim.SetActive(m_ControlActive);
			if (m_WeaponMount != null)
				m_WeaponMount.SetActive(m_ControlActive);
		}

		public bool StartEngine()
		{
			if (!m_ControlActive)
				return false;
			if (m_EngineRunning)
				return true;

			m_EngineRunning = true;
			float delay = m_Tuning != null ? Mathf.Max(0f, m_Tuning.EngineStartDelay) : 0.5f;
			m_EngineReadyAt = Time.time + delay;
			m_EngineReady = delay <= 0.001f;

			if (!UsesAdvancedEngineAudio())
			{
				if (m_EngineAudio != null)
				{
					m_EngineAudio.enabled = true;
					if (!m_EngineAudio.isPlaying && m_EngineAudio.clip != null)
						m_EngineAudio.Play();
				}
			}

			EngineStateChanged?.Invoke(true);
			return true;
		}

		public void StopEngine()
		{
			if (!m_EngineRunning && !m_EngineReady)
			{
				SyncEngineAudio(false);
				return;
			}

			m_EngineRunning = false;
			m_EngineReady = false;
			m_EngineReadyAt = -1f;
			m_Command = ResolveParkCommand();
			SyncEngineAudio(false);
			EngineStateChanged?.Invoke(false);
		}

		public bool ToggleEngine()
		{
			if (m_EngineRunning)
			{
				StopEngine();
				return false;
			}

			return StartEngine();
		}

		public void SetCommand(VehicleCommand _command)
		{
			if (!m_ControlActive)
			{
				m_Command = ResolveParkCommand();
				return;
			}

			if (!CanDrive)
			{
				m_Command = ResolveParkCommand();
				return;
			}

			m_Command = _command;
		}

		public void AutoWire()
		{
			CacheModules();
		}
		#endregion

		#region Private Methods
		private void UpdateEngineReadyState()
		{
			if (!m_EngineRunning || m_EngineReady)
				return;
			if (m_EngineReadyAt < 0f || Time.time < m_EngineReadyAt)
				return;
			m_EngineReady = true;
		}

		private VehicleCommand ResolveParkCommand()
		{
			if (m_Vehicle != null && !m_Vehicle.IsDriveMotorAllowed)
				return VehicleCommand.Idle;

			if (m_Tuning != null && !m_Tuning.IdleParkBrake)
				return VehicleCommand.SoftPark;
			return VehicleCommand.Idle;
		}

		private VehicleCommand ResolveActiveDriveCommand()
		{
			if (m_Vehicle != null && !m_Vehicle.IsDriveMotorAllowed)
				return VehicleCommand.Idle;

			return m_Command;
		}

		private void CacheModules()
		{
			if (m_Vehicle == null)
				TryGetComponent(out m_Vehicle);
			if (m_WheeledMotor == null)
				TryGetComponent(out m_WheeledMotor);
			if (m_TrackedMotor == null)
				TryGetComponent(out m_TrackedMotor);
			if (m_TurretAim == null)
				TryGetComponent(out m_TurretAim);
			if (m_WeaponMount == null)
				TryGetComponent(out m_WeaponMount);
			if (m_EngineAudio == null)
				TryGetComponent(out m_EngineAudio);
			if (m_AdvancedEngineAudio == null)
				TryGetComponent(out m_AdvancedEngineAudio);
		}

		private bool UsesAdvancedEngineAudio() => m_AdvancedEngineAudio != null;

	private void ApplyCenterOfMass()
	{
		if (m_Body == null || m_Tuning == null)
			return;
		Vector3 com = m_Tuning.CenterOfMass;
		// A wheeled vehicle with COM below the wheel hubs becomes a pendulum and
		// jumps/rocks violently. Force a sane minimum height until the tuning asset
		// is updated and re-imported.
		if (com.y < 0.3f)
			com.y = 0.55f;
		m_Body.centerOfMass = com;
	}

		private void ApplyRigidbodyMass()
		{
			if (m_Body == null || m_Tuning == null)
				return;
			m_Body.mass = m_Tuning.RigidbodyMass;
		}

		private void ApplyModuleTuning()
		{
			if (m_Tuning == null)
				return;

			if (m_WheeledMotor != null)
				m_WheeledMotor.ApplyTuning(m_Tuning);
			if (m_TrackedMotor != null)
				m_TrackedMotor.ApplyTuning(m_Tuning);
			if (m_TurretAim != null)
				m_TurretAim.ApplyTuning(m_Tuning);
			if (m_WeaponMount != null)
				m_WeaponMount.ApplyTuning(m_Tuning);
		}

		private void UpdateEngineAudio()
		{
			if (UsesAdvancedEngineAudio())
				return;
			if (m_EngineAudio == null || m_Tuning == null || !m_EngineRunning)
				return;

			float top = Mathf.Max(1f, m_Tuning.TopSpeedKmh);
			float ratio = Mathf.Clamp01(CurrentSpeedKmh / top);
			float idlePitch = m_EngineReady ? 1f : 0.85f;
			m_EngineAudio.pitch = idlePitch + ratio * (m_TrackedMotor != null ? 2f : 1f);
		}

		private void SyncEngineAudio(bool _running)
		{
			if (UsesAdvancedEngineAudio())
				return;
			if (m_EngineAudio == null)
				return;
			if (_running)
			{
				m_EngineAudio.enabled = true;
				if (!m_EngineAudio.isPlaying && m_EngineAudio.clip != null)
					m_EngineAudio.Play();
			}
			else
			{
				if (m_EngineAudio.isPlaying)
					m_EngineAudio.Stop();
				m_EngineAudio.enabled = false;
			}
		}
		#endregion

#if UNITY_EDITOR
		[ContextMenu("Auto Wire Modules")]
		private void EditorAutoWire()
		{
			AutoWire();
		}
#endif
	}
}
