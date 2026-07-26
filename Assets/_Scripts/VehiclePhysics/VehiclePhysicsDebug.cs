using UnityEngine;

[DisallowMultipleComponent]
public sealed class VehiclePhysicsDebug : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehiclePhysics m_Physics;
	[SerializeField] private bool m_ShowDebugGUI = true;
	[SerializeField] private int m_FontSize = 14;
	[SerializeField] private Color m_TextColor = Color.white;
	[SerializeField] private Color m_WarningColor = Color.yellow;
	[SerializeField] private Color m_ErrorColor = Color.red;
	#endregion

	#region Private Fields
	private GUIStyle m_Style;
	private GUIStyle m_WarningStyle;
	private GUIStyle m_ErrorStyle;
	private bool m_StylesBuilt;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Physics == null)
			TryGetComponent(out m_Physics);
	}

	private void OnGUI()
	{
		if (!m_ShowDebugGUI || m_Physics == null)
			return;

		BuildStyles();

		var state = m_Physics.Debug;

		float y = 10f;
		float lineH = m_FontSize + 4f;
		float x = 10f;

		Label(ref x, ref y, lineH, $"=== Vehicle Physics [{name}] ===");
		y += 4f;

		Label(ref x, ref y, lineH, $"Speed: {state.SpeedKmh:F1} km/h  |  Gear: {state.Gear}  |  RPM: {state.EngineRPM:F0}");
		Label(ref x, ref y, lineH, $"Throttle: {state.Throttle:F2}  |  Brake: {state.Brake:F2}  |  EngineTorque: {state.EngineTorque:F0} Nm");
		Label(ref x, ref y, lineH, $"DriveshaftTorque: {state.DriveshaftTorque:F0} Nm  |  Drag: {state.CurrentDragForce:F0} N");
		Label(ref x, ref y, lineH, $"Mass: {state.TotalMass:F0} kg  |  COM: {state.CenterOfMass:F2}");
		Label(ref x, ref y, lineH, $"RollAngle: {state.RollAngle:F1}°  |  Airborne: {state.AirborneTime:F2}s");

		y += 4f;

		string stabilityLabel = $"Stability: {state.StabilityLevel}";
		GUIStyle stStyle = state.StabilityLevel >= StabilityController.Level.Recovery ? m_ErrorStyle : m_Style;
		LabelStyled(ref x, ref y, lineH, stabilityLabel, stStyle);

		if (state.StabilityLevel != StabilityController.Level.Inactive)
		{
			Label(ref x, ref y, lineH, $"  Safety: {state.SafetyAction}  |  Recovery: {state.RecoveryAction}");
		}

		Label(ref x, ref y, lineH, $"NumericalGuard: {state.NumericalGuardTrips}");

		y += 4f;

		Label(ref x, ref y, lineH, $"Surface: {state.SurfaceName}  |  Grip: {state.SurfaceGripMultiplier:F2}");

		y += 4f;

		IWheelInterface[] wheels = m_Physics.Wheels;
		Label(ref x, ref y, lineH, $"--- Wheels ({wheels?.Length ?? 0}) ---");

		if (wheels != null && state.WheelLoads != null)
		{
			for (int i = 0; i < wheels.Length && i < state.WheelLoads.Length; i++)
			{
				bool grounded = wheels[i]?.IsGrounded ?? false;
				string icon = grounded ? "O" : "X";
				GUIStyle wStyle = grounded ? m_Style : m_WarningStyle;

				string wheelLine = $"  [{i}] {icon}  Load: {state.WheelLoads[i]:F0}N  Slip: {state.WheelSlips[i]:F2}  " +
				                   $"Susp: {state.SuspensionTravels[i]:F3}m";
				LabelStyled(ref x, ref y, lineH, wheelLine, wStyle);
			}
		}
	}
	#endregion

	#region Private Methods
	private void BuildStyles()
	{
		if (m_StylesBuilt)
			return;

		m_Style = new GUIStyle(GUI.skin.label)
		{
			fontSize = m_FontSize,
			fontStyle = FontStyle.Bold,
			normal = { textColor = m_TextColor },
		};

		m_WarningStyle = new GUIStyle(m_Style)
		{
			normal = { textColor = m_WarningColor },
		};

		m_ErrorStyle = new GUIStyle(m_Style)
		{
			normal = { textColor = m_ErrorColor },
		};

		m_StylesBuilt = true;
	}

	private void Label(ref float x, ref float y, float h, string text)
	{
		GUI.Label(new Rect(x, y, 600f, h), text, m_Style);
		y += h;
	}

	private void LabelStyled(ref float x, ref float y, float h, string text, GUIStyle style)
	{
		GUI.Label(new Rect(x, y, 600f, h), text, style);
		y += h;
	}
	#endregion
}
