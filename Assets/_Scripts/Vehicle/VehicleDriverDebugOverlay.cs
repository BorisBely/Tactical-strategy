using UnityEngine;

/// <summary>
/// Runtime debug for the new virtual driver (VehicleNavigation).
/// Toggle with serialized flag or context menu.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleDriverDebugOverlay : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleNavigation.VehicleNavigation m_Navigation;
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private bool m_DrawGizmos = true;
	[SerializeField] private bool m_DrawScreenLabel = true;
	[SerializeField] private Color m_PathColor = new Color(0.2f, 0.85f, 1f, 0.9f);
	[SerializeField] private Color m_LookAheadColor = new Color(1f, 0.85f, 0.15f, 1f);
	[SerializeField] private Color m_DestinationColor = new Color(1f, 0.3f, 0.2f, 1f);
	#endregion

	#region Public Properties
	public bool DrawGizmos
	{
		get => m_DrawGizmos;
		set => m_DrawGizmos = value;
	}

	public bool DrawScreenLabel
	{
		get => m_DrawScreenLabel;
		set => m_DrawScreenLabel = value;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Navigation == null)
			TryGetComponent(out m_Navigation);
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawGizmos || m_Navigation == null)
			return;

		var corners = m_Navigation.PathCorners;
		if (corners != null && corners.Count > 1)
		{
			Gizmos.color = m_PathColor;
			for (int i = 0; i < corners.Count - 1; i++)
				Gizmos.DrawLine(corners[i] + Vector3.up * 0.35f, corners[i + 1] + Vector3.up * 0.35f);
		}

		if (m_Navigation.HasDestination || m_Navigation.DriverState == VehicleNavigation.DriverFSM.State.Arrival)
		{
			Gizmos.color = m_DestinationColor;
			Gizmos.DrawWireSphere(m_Navigation.Destination + Vector3.up * 0.2f, 0.45f);
		}

		if (m_Navigation.HasLookAheadPoint)
		{
			Vector3 la = m_Navigation.LookAheadPoint + Vector3.up * 0.4f;
			Gizmos.color = m_LookAheadColor;
			Gizmos.DrawSphere(la, 0.28f);
			Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, la);
		}

		if (m_Navigation.HasGoalHeading)
		{
			Vector3 dest = m_Navigation.Destination + Vector3.up * 0.25f;
			Vector3 dir = Quaternion.Euler(0f, m_Navigation.GoalHeadingYaw, 0f) * Vector3.forward;
			Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
			Gizmos.DrawLine(dest, dest + dir * 3f);
		}
	}

	private void OnGUI()
	{
		if (!Application.isPlaying || !m_DrawScreenLabel || m_Navigation == null)
			return;

		if (m_Vehicle != null && !m_Vehicle.IsSelected)
			return;

		Camera cam = Camera.main;
		if (cam == null)
			return;

		Vector3 world = transform.position + Vector3.up * 3.2f;
		Vector3 screen = cam.WorldToScreenPoint(world);
		if (screen.z < 0.1f)
			return;

		VehicleNavigation.Maneuver maneuver = m_Navigation.CurrentManeuver;
		string heading = m_Navigation.HasGoalHeading
			? $"{m_Navigation.GoalHeadingYaw:0}"
			: "none";
		string text =
			$"State: {m_Navigation.DriverState}\n" +
			$"Plan: {m_Navigation.ActivePlanReason}\n" +
			$"Man: {(maneuver != null ? maneuver.Type.ToString() : "-")}\n" +
			$"GoalYaw: {heading}\n" +
			$"Speed: {m_Navigation.CurrentSpeed * 3.6f:0.0} km/h  Mode: {m_Navigation.ActiveSpeedMode}\n" +
			$"Steer: {m_Navigation.SteerCommand:0.00}  Thr: {m_Navigation.ThrottleCommand:0.00}" +
			(m_Navigation.IsReversing ? "  REV" : string.Empty);

		float x = screen.x;
		float y = Screen.height - screen.y;
		GUI.color = Color.black;
		GUI.Label(new Rect(x + 1f, y + 1f, 340f, 120f), text);
		GUI.color = Color.white;
		GUI.Label(new Rect(x, y, 340f, 120f), text);
	}
	#endregion

#if UNITY_EDITOR
	[ContextMenu("Toggle Driver Debug")]
	private void ToggleDebug()
	{
		m_DrawGizmos = !m_DrawGizmos;
		m_DrawScreenLabel = m_DrawGizmos;
	}
#endif
}
