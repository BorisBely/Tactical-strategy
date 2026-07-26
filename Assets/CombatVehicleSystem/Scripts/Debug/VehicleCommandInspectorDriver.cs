using UnityEngine;

namespace CombatVehicleSystem
{
	/// <summary>
	/// Inspector-only command source for pack verification. No keyboard / Input System.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(VehicleBrain))]
	public class VehicleCommandInspectorDriver : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private bool m_DriveFromInspector = true;
		[SerializeField, Range(-1f, 1f)] private float m_Steer;
		[SerializeField, Range(-1f, 1f)] private float m_Throttle;
		[SerializeField] private bool m_Brake;
		[SerializeField] private bool m_FireHeld;
		[SerializeField] private Transform m_AimTarget;
		[SerializeField] private bool m_ForceControlActive = true;
		#endregion

		#region Private Fields
		private VehicleBrain m_Brain;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Brain = GetComponent<VehicleBrain>();
		}

		private void Update()
		{
			if (!m_DriveFromInspector || m_Brain == null)
				return;

			if (m_ForceControlActive && !m_Brain.ControlActive)
				m_Brain.SetControlActive(true);
			if (m_ForceControlActive && !m_Brain.EngineRunning)
				m_Brain.StartEngine();

			VehicleCommand command = new VehicleCommand
			{
				Steer = m_Steer,
				Throttle = m_Throttle,
				BrakeMode = m_Brake ? VehicleBrakeMode.Hard : VehicleBrakeMode.None,
				FireHeld = m_FireHeld,
				HasAimPoint = m_AimTarget != null,
				AimWorldPoint = m_AimTarget != null ? m_AimTarget.position : Vector3.zero
			};

			m_Brain.SetCommand(command);
		}
		#endregion
	}
}
