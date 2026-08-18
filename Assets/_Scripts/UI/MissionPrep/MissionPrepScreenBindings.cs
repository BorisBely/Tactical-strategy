using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
	[Header("Закрытие")]
	[SerializeField] private Button m_CloseButton;
	#endregion

	#region Static Access
	private static MissionPrepScreenBindings s_Instance;

	public static MissionPrepScreenBindings Instance => s_Instance;
	#endregion

	#region Private Fields
	private Button m_RuntimeCloseButton;
	#endregion

	#region Public Properties
	public bool IsMissionPrepOpen
	{
		get
		{
			if (m_MissionPrepCanvasRoot == null)
				return false;

			InventoryUiWindowMotion motion = m_MissionPrepCanvasRoot.GetComponent<InventoryUiWindowMotion>();
			if (motion != null)
				return motion.IsOpen;

			return m_MissionPrepCanvasRoot.activeSelf;
		}
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (!TryClaimSingletonInstance())
			return;

		if (m_ScreenController == null && m_MissionPrepCanvasRoot != null)
			m_MissionPrepCanvasRoot.TryGetComponent(out m_ScreenController);

		EnsureCloseButton();

		if (m_StartWithMissionPrepClosed && m_MissionPrepCanvasRoot != null)
			InventoryUiWindowMotion.Ensure(m_MissionPrepCanvasRoot)?.SnapClosed();
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

		InventoryUiWindowMotion motion = InventoryUiWindowMotion.Ensure(m_MissionPrepCanvasRoot);
		if (_open)
		{
			motion.Open();
			if (m_ScreenController == null)
				m_MissionPrepCanvasRoot.TryGetComponent(out m_ScreenController);

			m_ScreenController?.RefreshInventoryPanel();
			RefreshEquipmentTitle();
		}
		else
		{
			GameInputGate.ReleaseUiInputCapture();
			RtsUnitSelectionManager.Instance?.CancelRouteEditInputState();
			InventoryItemTooltip.Instance.HideImmediate();
			motion.Close();
			// После закрытия prep юниты снова RTS-controllable — восстановим выделение.
			RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
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

		MissionPrepLoadoutCoordinator coordinator = MissionPrepLoadoutCoordinator.Instance;
		if (coordinator != null && coordinator.IsBoundToVehicle && coordinator.BoundVehicle != null)
		{
			string vehicleTitle = LocalizationManager.Get("mission_prep.vehicle.inventory_title", "Инвентарь машины");
			string vehicleName = VehicleCellDisplayBinder.ResolveVehicleName(coordinator.BoundVehicle);
			m_EquipmentTitleText.text = string.IsNullOrWhiteSpace(vehicleName)
				? vehicleTitle
				: $"{vehicleTitle}: {vehicleName}";
			return;
		}

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

	private void EnsureCloseButton()
	{
		if (m_MissionPrepCanvasRoot == null)
			return;

		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.RemoveListener(HandleCloseClicked);
			m_CloseButton.onClick.AddListener(HandleCloseClicked);
			return;
		}

		if (m_RuntimeCloseButton != null)
			return;

		Transform existing = m_MissionPrepCanvasRoot.transform.Find("PrepCloseButton");
		if (existing != null && existing.TryGetComponent(out Button existingButton))
		{
			m_RuntimeCloseButton = existingButton;
			m_RuntimeCloseButton.onClick.RemoveListener(HandleCloseClicked);
			m_RuntimeCloseButton.onClick.AddListener(HandleCloseClicked);
			return;
		}

		GameObject go = new GameObject("PrepCloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
		go.transform.SetParent(m_MissionPrepCanvasRoot.transform, false);
		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = new Vector2(1f, 1f);
		rt.anchorMax = new Vector2(1f, 1f);
		rt.pivot = new Vector2(1f, 1f);
		rt.anchoredPosition = new Vector2(-24f, -24f);
		rt.sizeDelta = new Vector2(36f, 36f);

		Image image = go.GetComponent<Image>();
		InventoryUiTheme.ApplyImageColor(image, InventoryUiTheme.TitleBar);
		image.raycastTarget = true;

		GameObject labelGo = new GameObject("Label", typeof(RectTransform));
		labelGo.transform.SetParent(go.transform, false);
		RectTransform labelRt = labelGo.transform as RectTransform;
		labelRt.anchorMin = Vector2.zero;
		labelRt.anchorMax = Vector2.one;
		labelRt.offsetMin = Vector2.zero;
		labelRt.offsetMax = Vector2.zero;
		TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
		label.text = "×";
		label.fontSize = 26f;
		label.alignment = TextAlignmentOptions.Midline;
		label.color = Color.white;
		label.raycastTarget = false;

		m_RuntimeCloseButton = go.GetComponent<Button>();
		ColorBlock colors = m_RuntimeCloseButton.colors;
		colors.highlightedColor = InventoryUiTheme.CellHover;
		colors.pressedColor = InventoryUiTheme.UnitCellSelected;
		m_RuntimeCloseButton.colors = colors;
		m_RuntimeCloseButton.onClick.AddListener(HandleCloseClicked);
	}

	private void HandleCloseClicked()
	{
		SetMissionPrepWindowOpen(false);
	}
	#endregion
}
