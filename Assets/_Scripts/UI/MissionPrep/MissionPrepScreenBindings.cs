using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

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
	[Header("Заголовок инвентаря")]
	[SerializeField] private TMP_Text m_EquipmentTitleText;
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
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		if (s_Instance == this)
			s_Instance = null;
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
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
			RefreshEquipmentTitle();
		}
		else
		{
			GameInputGate.ReleaseUiInputCapture();
			RtsUnitSelectionManager.Instance?.CancelRouteEditInputState();
		}
	}

	public void RefreshEquipmentTitle()
	{
		try
		{
			RefreshEquipmentTitleInternal();
		}
		catch (System.Exception _e)
		{
			Debug.LogError($"[MissionPrep] Ошибка обновления заголовка веса: {_e.Message}\n{_e.StackTrace}", this);
		}
	}

	private void RefreshEquipmentTitleInternal()
	{
		if (m_EquipmentTitleText == null)
		{
			TryResolveEquipmentTitleText();
			if (m_EquipmentTitleText == null)
				return;
		}

		if (m_ScreenController == null)
			return;

		MissionPrepPresetSnapshot snapshot = m_ScreenController.GetCurrentPresetSnapshot();
		if (snapshot != null)
		{
			float total = snapshot.TotalWeightKg;
			float max = snapshot.TotalMaxWeightKg;
			string title = LocalizationManager.Get("mission_prep.equipment.title");
			m_EquipmentTitleText.text = $"{title} ({total:F1}/{max:F1} кг)";
		}
		else
			m_EquipmentTitleText.text = LocalizationManager.Get("mission_prep.equipment.title");
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

	private void HandleLanguageChanged()
	{
		if (IsMissionPrepOpen)
			RefreshEquipmentTitle();
	}

	private void TryResolveEquipmentTitleText()
	{
		if (m_MissionPrepCanvasRoot == null)
			return;

		LocalizedTextMeshProUGUI[] components = m_MissionPrepCanvasRoot.GetComponentsInChildren<LocalizedTextMeshProUGUI>(true);
		for (int i = 0; i < components.Length; i++)
		{
			if (components[i] == null)
				continue;

			if (components[i].TryGetLocalizationKey(out string key) && key == "mission_prep.equipment.title")
			{
				m_EquipmentTitleText = components[i].GetComponent<TMP_Text>();
				components[i].enabled = false;
				break;
			}
		}
	}
	#endregion
}
