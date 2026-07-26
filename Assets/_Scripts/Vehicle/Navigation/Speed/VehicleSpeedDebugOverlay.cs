using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	[DisallowMultipleComponent]
	public sealed class VehicleSpeedDebugOverlay : MonoBehaviour
	{
		[SerializeField] private bool m_ShowOverlay = true;
		[SerializeField] private VehicleNavigation m_Nav;

		private void Awake()
		{
			if (m_Nav == null)
				m_Nav = GetComponent<VehicleNavigation>();
		}

		private void OnGUI()
		{
			if (!m_ShowOverlay || m_Nav == null)
				return;

			NavigationContext ctx = m_Nav.Context;
			if (ctx == null || !m_Nav.HasDestination)
				return;

			GUILayout.BeginArea(new Rect(10, 10, 340, 400));
			GUILayout.BeginVertical("box");

			GUILayout.Label($"<b>{name}</b>", GUI.skin.box);
			GUILayout.Label($"State: <b>{m_Nav.DriverState}</b>");
			GUILayout.Label($"Speed: <b>{ctx.State.SpeedKmh:F1}</b> / {ctx.DesiredSpeedKmh:F1} km/h");
			if (ctx.TargetSpeedKmh < 999f)
				GUILayout.Label($"SpeedPlanner Target: <b>{ctx.TargetSpeedKmh:F1}</b> km/h");

			SpeedLimitResult active = ctx.ActiveLimit;
			if (active.SpeedKmh < 999f)
			{
				GUILayout.Label($"Active Limiter: <b>{active.Reason}</b> (prio:{active.Priority})");
				GUILayout.Label($"Limit Speed: <b>{active.SpeedKmh:F1}</b> km/h");
			}

			GUILayout.Label($"Remaining: <b>{ctx.RemainingDistance:F1}</b> m");
			GUILayout.Label($"Curvature: <b>{ctx.CurrentCurvature:F3}</b>");

			VehicleCommand cmd = m_Nav.LastCommand;
			GUILayout.Label($"Throttle: <b>{cmd.Throttle:F2}</b>  Steer: <b>{cmd.Steer:F2}</b>");
			GUILayout.Label($"BrakeMode: <b>{cmd.BrakeMode}</b>  Reverse: <b>{m_Nav.IsReversing}</b>");

			if (m_Nav.IsStuck)
				GUILayout.Label("<color=red>STUCK!</color>");

			GUILayout.EndVertical();
			GUILayout.EndArea();
		}
	}
}
