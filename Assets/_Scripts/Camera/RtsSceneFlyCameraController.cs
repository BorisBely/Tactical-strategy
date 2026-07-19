using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Перемещение камеры в рантайме в стиле Fly Mode окна Scene: WASD, Q/E, Shift, колёсико.
/// Направление взгляда — ПКМ + мышь (без выделения или Alt+ПКМ), либо СКМ + мышь при выделенных юнитах.
/// СКМ + мышь без выделения — панорамирование по плоскости вида.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsSceneFlyCameraController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Camera m_Camera;
	[SerializeField] private float m_MoveSpeed = 10f;
	[SerializeField] private float m_FastMoveMultiplier = 3f;
	[SerializeField, Range(0.01f, 2f)] private float m_LookSensitivity = 0.2f;
	[SerializeField, Range(0.001f, 1f)] private float m_PanSensitivity = 0.05f;
	[SerializeField] private float m_MinPitch = -89f;
	[SerializeField] private float m_MaxPitch = 89f;
	[SerializeField] private float m_MinSpeedMultiplier = 0.25f;
	[SerializeField] private float m_MaxSpeedMultiplier = 4f;
	[SerializeField] private float m_ScrollSpeedStep = 0.15f;
	[Tooltip("Не двигать/поворачивать камеру, когда курсор над UI.")]
	[SerializeField] private bool m_BlockInputOverUi = true;
	#endregion

	#region Private Fields
	private float m_Yaw;
	private float m_Pitch;
	private float m_SpeedMultiplier = 1f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Camera == null)
			TryGetComponent(out m_Camera);

		Vector3 euler = transform.eulerAngles;
		m_Yaw = euler.y;
		m_Pitch = euler.x;
		if (m_Pitch > 180f)
			m_Pitch -= 360f;
	}

	private void Update()
	{
		if (PauseMenuController.IsPaused)
			return;

		if (GameInputGate.ShouldBlockGameplayInput())
			return;

		if (m_BlockInputOverUi && IsPointerOverUi())
			return;

		UpdateLook();
		UpdateMousePan();
		UpdateMove();
		UpdateSpeedFromScroll();
	}
	#endregion

	#region Private Methods
	private static bool IsPointerOverUi()
	{
		return UiPointerUtility.IsPointerOverUi();
	}

	private bool HasSelectedUnits()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		return selection != null && selection.SelectedUnitCount > 0;
	}

	private bool CanLookWithRightMouse()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection != null && selection.ShouldSuppressCameraInput)
			return false;

		if (!HasSelectedUnits())
			return true;

		Keyboard keyboard = Keyboard.current;
		return keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
	}

	private bool CanLookWithMiddleMouse()
	{
		return HasSelectedUnits();
	}

	private void ApplyLookDelta(Vector2 _delta)
	{
		if (_delta.sqrMagnitude <= 0f)
			return;

		m_Yaw += _delta.x * m_LookSensitivity;
		m_Pitch -= _delta.y * m_LookSensitivity;
		m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitch, m_MaxPitch);
		transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
	}

	private void UpdateLook()
	{
		Mouse mouse = Mouse.current;
		if (mouse == null)
			return;

		Vector2 delta = Vector2.zero;
		if (mouse.middleButton.isPressed && CanLookWithMiddleMouse())
			delta = mouse.delta.ReadValue();
		else if (mouse.rightButton.isPressed && CanLookWithRightMouse())
			delta = mouse.delta.ReadValue();

		ApplyLookDelta(delta);
	}

	private void UpdateMousePan()
	{
		if (HasSelectedUnits())
			return;

		Mouse mouse = Mouse.current;
		if (mouse == null || !mouse.middleButton.isPressed)
			return;

		Vector2 delta = mouse.delta.ReadValue();
		if (delta.sqrMagnitude <= 0f)
			return;

		float panScale = m_PanSensitivity * m_SpeedMultiplier;
		transform.position += (-transform.right * delta.x + transform.up * delta.y) * panScale;
	}

	private void UpdateMove()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection != null && selection.ShouldSuppressCameraInput)
			return;

		Keyboard keyboard = Keyboard.current;
		if (keyboard == null)
			return;

		Vector3 move = Vector3.zero;
		if (keyboard.wKey.isPressed)
			move += transform.forward;
		if (keyboard.sKey.isPressed)
			move -= transform.forward;
		if (keyboard.dKey.isPressed)
			move += transform.right;
		if (keyboard.aKey.isPressed)
			move -= transform.right;
		bool allowVerticalFly = !HasSelectedUnits() || keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
		if (allowVerticalFly && keyboard.eKey.isPressed)
			move += Vector3.up;
		if (allowVerticalFly && keyboard.qKey.isPressed)
			move -= Vector3.up;

		if (move.sqrMagnitude <= 0f)
			return;

		move.Normalize();
		float speed = m_MoveSpeed * m_SpeedMultiplier;
		if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
			speed *= m_FastMoveMultiplier;

		transform.position += move * (speed * Time.unscaledDeltaTime);
	}

	private void UpdateSpeedFromScroll()
	{
		Mouse mouse = Mouse.current;
		if (mouse == null)
			return;

		float scrollY = mouse.scroll.ReadValue().y;
		if (Mathf.Abs(scrollY) <= 0.01f)
			return;

		float direction = Mathf.Sign(scrollY);
		m_SpeedMultiplier = Mathf.Clamp(
			m_SpeedMultiplier + direction * m_ScrollSpeedStep,
			m_MinSpeedMultiplier,
			m_MaxSpeedMultiplier);
	}
	#endregion
}
