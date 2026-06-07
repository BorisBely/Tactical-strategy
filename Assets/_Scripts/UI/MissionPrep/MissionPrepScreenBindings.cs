using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Открытие/закрытие экрана предмиссии по U — по тому же принципу, что инвентарь по I.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class MissionPrepScreenBindings : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Корневой объект экрана предмиссии на Canvas.")]
	[SerializeField] private GameObject m_MissionPrepCanvasRoot;
	[SerializeField] private MissionPrepScreenController m_ScreenController;
	[SerializeField] private bool m_StartWithMissionPrepClosed = true;
	#endregion

	#region Static Access
	private static MissionPrepScreenBindings s_Instance;

	public static MissionPrepScreenBindings Instance => s_Instance;
	#endregion

	#region Public Properties
	public bool IsMissionPrepOpen =>
		m_MissionPrepCanvasRoot != null && m_MissionPrepCanvasRoot.activeSelf;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (!TryClaimSingletonInstance())
			return;

		if (m_ScreenController == null && m_MissionPrepCanvasRoot != null)
			m_MissionPrepCanvasRoot.TryGetComponent(out m_ScreenController);

		if (m_StartWithMissionPrepClosed && m_MissionPrepCanvasRoot != null)
			m_MissionPrepCanvasRoot.SetActive(false);
	}

	private void Update()
	{
		if (PauseMenuController.IsPaused)
			return;

		Keyboard keyboard = Keyboard.current;
		if (keyboard == null)
			return;

		if (keyboard.uKey.wasPressedThisFrame)
			ToggleMissionPrepWindow();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Methods
	public void ToggleMissionPrepWindow()
	{
		SetMissionPrepWindowOpen(!IsMissionPrepOpen);
	}

	public void SetMissionPrepWindowOpen(bool _open)
	{
		if (m_MissionPrepCanvasRoot == null)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepScreenBindings)} on '{gameObject.name}' has no {nameof(m_MissionPrepCanvasRoot)} assigned; mission prep window cannot open.",
				this);
			return;
		}

		if (_open && InventoryScreenBindings.Instance != null && InventoryScreenBindings.Instance.IsInventoryOpen)
			InventoryScreenBindings.Instance.SetInventoryWindowOpen(false);

		m_MissionPrepCanvasRoot.SetActive(_open);
		if (_open)
		{
			if (m_ScreenController == null)
				m_MissionPrepCanvasRoot.TryGetComponent(out m_ScreenController);

			m_ScreenController?.RefreshInventoryPanel();
		}
	}
	#endregion

	#region Private Methods
	private bool TryClaimSingletonInstance()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Debug.LogWarning(
				$"Duplicate {nameof(MissionPrepScreenBindings)} on '{gameObject.name}'. Destroying duplicate.",
				this);
			Destroy(this);
			return false;
		}

		s_Instance = this;
		return true;
	}
	#endregion
}
