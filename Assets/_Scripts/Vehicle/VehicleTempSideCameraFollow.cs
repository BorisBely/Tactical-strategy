using System.Collections.Generic;
#pragma warning disable CS0414
using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.InputSystem;
#pragma warning disable CS0414

/// <summary>
/// TEMP debug: camera views for vehicle inspection.
/// Cycle with F4: RTS fly ↔ vehicle side ↔ high top-down.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class VehicleTempSideCameraFollow : MonoBehaviour
{
	private enum ViewMode
	{
		RtsFly,
		VehicleSide,
		TopDown
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		Camera cam = Camera.main;
		if (cam == null)
			cam = Object.FindFirstObjectByType<Camera>();
		if (cam == null)
			return;
		if (cam.GetComponent<VehicleTempSideCameraFollow>() == null)
			cam.gameObject.AddComponent<VehicleTempSideCameraFollow>();
	}

	#region Serialized Fields
	[SerializeField] private Key m_ToggleKey = Key.F4;

	[Header("Side View")]
	[SerializeField] private Vector3 m_SideLocalOffset = new Vector3(-7.5f, 1.8f, 0.5f);
	[SerializeField] private Vector3 m_SideLookAtOffset = new Vector3(0f, 0.6f, 0f);

	[Header("Top-Down View")]
	[SerializeField] private float m_TopDownHeight = 55f;
	[SerializeField] private float m_TopDownPitch = 70f;
	[SerializeField] private float m_TopDownBackOffset = 5f;

	[Header("Smoothing")]
	[SerializeField] private float m_PositionLerp = 12f;
	[SerializeField] private bool m_FollowSelectedOnly = false;
	#endregion

	#region Private Fields
	private Camera m_Camera;
	private RtsSceneFlyCameraController m_FlyCamera;
	private ViewMode m_CurrentMode = ViewMode.RtsFly;
	private Vector3 m_SavedPosition;
	private Quaternion m_SavedRotation;
	private bool m_HasSavedPose;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Camera = Camera.main;
		if (m_Camera == null)
			m_Camera = FindFirstObjectByType<Camera>();

		if (m_Camera != null)
		{
			m_FlyCamera = m_Camera.GetComponent<RtsSceneFlyCameraController>();
			m_SavedPosition = m_Camera.transform.position;
			m_SavedRotation = m_Camera.transform.rotation;
			m_HasSavedPose = true;
		}

		ApplyFlyCameraGate();
	}

	private void OnDisable()
	{
		if (m_CurrentMode != ViewMode.RtsFly)
			RestoreRts();
	}

	private void LateUpdate()
	{
		if (PauseMenuController.IsPaused || GameInputGate.ShouldBlockGameplayInput())
			return;

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard[m_ToggleKey].wasPressedThisFrame)
			CycleMode();

		if (m_CurrentMode == ViewMode.RtsFly || m_Camera == null)
			return;

		VehicleController target = ResolveTarget();
		if (target == null)
			return;

		Transform t = target.transform;

		Vector3 desiredPos;
		Vector3 lookAt;

		if (m_CurrentMode == ViewMode.VehicleSide)
		{
			desiredPos = t.TransformPoint(m_SideLocalOffset);
			lookAt = t.TransformPoint(m_SideLookAtOffset);
		}
		else
		{
			Vector3 flatBack = t.forward;
			flatBack.y = 0f;
			if (flatBack.sqrMagnitude < 0.01f) flatBack = Vector3.forward;
			flatBack = -flatBack.normalized;

			Vector3 topCenter = t.position + Vector3.up * m_TopDownHeight;
			desiredPos = topCenter + flatBack * m_TopDownBackOffset;
			lookAt = t.position;
		}

		float lerp = 1f - Mathf.Exp(-m_PositionLerp * Time.deltaTime);
		m_Camera.transform.position = Vector3.Lerp(m_Camera.transform.position, desiredPos, lerp);
		Vector3 lookDir = lookAt - m_Camera.transform.position;
		if (lookDir.sqrMagnitude > 0.0001f)
		{
			m_Camera.transform.rotation = Quaternion.Slerp(
				m_Camera.transform.rotation,
				Quaternion.LookRotation(lookDir.normalized, Vector3.up),
				lerp);
		}
	}
	#endregion

	#region Private Methods
	private void CycleMode()
	{
		SavePose();
		m_CurrentMode = m_CurrentMode switch
		{
			ViewMode.RtsFly => ViewMode.VehicleSide,
			ViewMode.VehicleSide => ViewMode.TopDown,
			ViewMode.TopDown => ViewMode.RtsFly,
			_ => ViewMode.RtsFly
		};
		ApplyFlyCameraGate();
		LogState();
	}

	private void SavePose()
	{
		if (m_CurrentMode == ViewMode.RtsFly && m_Camera != null)
		{
			m_SavedPosition = m_Camera.transform.position;
			m_SavedRotation = m_Camera.transform.rotation;
			m_HasSavedPose = true;
		}
	}

	private void RestoreRts()
	{
		m_CurrentMode = ViewMode.RtsFly;
		ApplyFlyCameraGate();
		if (m_Camera != null && m_HasSavedPose)
		{
			m_Camera.transform.SetPositionAndRotation(m_SavedPosition, m_SavedRotation);
			SyncFlyCameraAnglesFromTransform();
		}
	}

	private void ApplyFlyCameraGate()
	{
		if (m_FlyCamera == null)
			return;
		m_FlyCamera.enabled = m_CurrentMode == ViewMode.RtsFly;
	}

	private void SyncFlyCameraAnglesFromTransform()
	{
		if (m_FlyCamera == null)
			return;
		m_FlyCamera.SyncAnglesFromTransform();
	}

	private void LogState()
	{
		string label = m_CurrentMode switch
		{
			ViewMode.RtsFly => "RTS (свободная камера)",
			ViewMode.VehicleSide => "БОК МАШИНЫ",
			ViewMode.TopDown => "ВИД СВЕРХУ",
			_ => "?"
		};
		Debug.Log($"[VehicleCam] F4 → {label}");
	}

	private VehicleController ResolveTarget()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection != null && selection.SelectedVehicle != null)
			return selection.SelectedVehicle;

		if (m_FollowSelectedOnly)
			return null;

		IReadOnlyList<VehicleController> instances = VehicleController.Instances;
		return instances != null && instances.Count > 0 ? instances[0] : null;
	}
	#endregion
}
