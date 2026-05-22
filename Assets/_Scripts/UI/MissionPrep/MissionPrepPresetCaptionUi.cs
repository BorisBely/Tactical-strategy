using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Переименование пресета в шапке TMP_Dropdown и удаление по ПКМ.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class MissionPrepPresetCaptionUi : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private TMP_Dropdown m_PresetDropdown;
	[SerializeField] private MissionPrepEquipmentPanelView m_EquipmentPanel;
	[SerializeField] private MissionPrepLoadoutCoordinator m_LoadoutCoordinator;
	[SerializeField] private float m_DeleteButtonSize = 22f;
	[SerializeField, Min(0.05f)] private float m_DoubleClickWindowSeconds = 0.2f;
	#endregion

	#region Private Fields
	private TMP_Text m_CaptionText;
	private RectTransform m_CaptionRect;
	private TMP_InputField m_RenameInput;
	private Button m_DeleteButton;
	private TextMeshProUGUI m_DeleteButtonLabel;
	private RectTransform m_DeleteButtonRect;
	private bool m_IsRenaming;
	private bool m_IsDeleteVisible;
	private int m_DeleteTargetPresetIndex = -1;
	private bool m_DropdownItemsWired;
	private float m_LastCaptionLeftClickTime = -1f;
	#endregion

	#region Public Properties
	public bool IsRenaming => m_IsRenaming;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		EnsureUi();
		ConfigureCaptionText();
	}

	private void OnDisable()
	{
		ExitRenameMode();
		HideDeleteButton(_reparentHome: false);
		m_DropdownItemsWired = false;
		m_LastCaptionLeftClickTime = -1f;
	}

	private void OnEnable()
	{
		TryReparentDeleteButtonHome();
	}

	private void LateUpdate()
	{
		if (m_PresetDropdown == null)
			return;

		if (m_PresetDropdown.IsExpanded)
		{
			if (!m_DropdownItemsWired)
				TryWireDropdownItems();
		}
		else
		{
			m_DropdownItemsWired = false;
		}
	}

	private void Update()
	{
		HandleCaptionMouseInput();
		HandleOutsideInput();
	}
	#endregion

	#region Public Methods
	public void BeginRenameCaption()
	{
		ResolveReferences();
		int presetIndex = GetActivePresetIndex();
		if (!CanRenamePreset(presetIndex))
			return;

		HideDeleteButton();
		m_LastCaptionLeftClickTime = -1f;
		m_PresetDropdown.Hide();
		m_IsRenaming = true;
		m_PresetDropdown.interactable = false;

		if (m_CaptionText != null)
			m_CaptionText.enabled = false;

		if (m_RenameInput != null)
		{
			m_RenameInput.gameObject.SetActive(true);
			m_RenameInput.characterLimit = MissionPrepPresetNameUtility.MaxLength;
			m_RenameInput.SetTextWithoutNotify(GetPresetLabel(presetIndex));
			m_RenameInput.Select();
			m_RenameInput.ActivateInputField();
		}
	}

	public void SetPresetDropdown(TMP_Dropdown _dropdown)
	{
		m_PresetDropdown = _dropdown;
		ResolveReferences();
		ConfigureCaptionText();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_PresetDropdown == null)
			m_PresetDropdown = GetComponent<TMP_Dropdown>();

		if (m_EquipmentPanel == null)
			m_EquipmentPanel = GetComponent<MissionPrepEquipmentPanelView>();

		if (m_LoadoutCoordinator == null && m_EquipmentPanel != null)
			m_EquipmentPanel.TryGetLoadoutCoordinator(out m_LoadoutCoordinator);

		if (m_LoadoutCoordinator == null)
			m_LoadoutCoordinator = GetComponentInParent<MissionPrepLoadoutCoordinator>();

		if (m_LoadoutCoordinator == null)
			m_LoadoutCoordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_CaptionText == null && m_PresetDropdown != null)
			m_CaptionText = m_PresetDropdown.captionText;

		if (m_CaptionRect == null && m_CaptionText != null)
			m_CaptionRect = m_CaptionText.rectTransform;
	}

	private void EnsureUi()
	{
		if (m_PresetDropdown == null || m_CaptionRect == null)
			return;

		if (m_RenameInput == null)
		{
			m_RenameInput = CreateRenameInput(m_CaptionRect.parent as RectTransform ?? m_PresetDropdown.transform as RectTransform);
			m_RenameInput.onEndEdit.AddListener(HandleRenameInputEndEdit);
		}

		if (m_DeleteButton == null)
		{
			m_DeleteButton = CreateDeleteButton(m_PresetDropdown.transform as RectTransform, out m_DeleteButtonLabel);
			m_DeleteButtonRect = m_DeleteButton.transform as RectTransform;
			m_DeleteButton.onClick.AddListener(HandleDeleteButtonClicked);
			m_DeleteButtonLabel.text = LocalizationManager.Get("mission_prep.equipment.delete_preset_short", "X");
		}

		m_RenameInput.gameObject.SetActive(false);
		m_DeleteButton.gameObject.SetActive(false);
	}

	private void ConfigureCaptionText()
	{
		// Одиночный клик должен проходить в TMP_Dropdown, а двойной клик по тексту ловим вручную.
		if (m_CaptionText != null)
			m_CaptionText.raycastTarget = false;
	}

	private void HandleCaptionMouseInput()
	{
		if (m_IsRenaming || m_CaptionRect == null)
			return;

		Mouse mouse = Mouse.current;
		if (mouse == null)
			return;

		if (!IsPointerOverRect(m_CaptionRect))
			return;

		if (mouse.rightButton.wasPressedThisFrame)
		{
			ShowDeleteButtonForPreset(GetActivePresetIndex(), m_CaptionRect);
			return;
		}

		if (!mouse.leftButton.wasPressedThisFrame)
			return;

		float now = Time.unscaledTime;
		bool isDoubleClick = m_LastCaptionLeftClickTime >= 0f &&
		                     now - m_LastCaptionLeftClickTime <= m_DoubleClickWindowSeconds;

		if (isDoubleClick)
		{
			m_LastCaptionLeftClickTime = -1f;
			BeginRenameCaption();
			return;
		}

		m_LastCaptionLeftClickTime = now;
	}

	private void TryWireDropdownItems()
	{
		Transform dropdownList = FindDropdownListTransform(m_PresetDropdown);
		if (dropdownList == null)
			return;

		Transform content = dropdownList.Find("Viewport/Content");
		if (content == null)
			return;

		int presetCount = m_LoadoutCoordinator != null
			? m_LoadoutCoordinator.GetPresetSlotCount()
			: content.childCount;

		int createRowIndex = m_EquipmentPanel != null ? m_EquipmentPanel.CreateNewPresetRowIndex : -1;
		for (int i = 0; i < content.childCount; i++)
		{
			if (i == createRowIndex || i >= presetCount)
				continue;

			Transform row = content.GetChild(i);
			if (row == null)
				continue;

			MissionPrepPresetDropdownItemBinder binder = row.GetComponent<MissionPrepPresetDropdownItemBinder>();
			if (binder == null)
				binder = row.gameObject.AddComponent<MissionPrepPresetDropdownItemBinder>();

			binder.Bind(this, i);
		}

		m_DropdownItemsWired = true;
	}

	internal void HandleDropdownItemRightClick(int _presetIndex, RectTransform _rowRect)
	{
		ShowDeleteButtonForPreset(_presetIndex, _rowRect);
	}

	private void HandleRenameInputEndEdit(string _text)
	{
		if (!m_IsRenaming)
			return;

		CommitRename(_text);
	}

	private void HandleDeleteButtonClicked()
	{
		if (m_DeleteTargetPresetIndex < 0 || m_LoadoutCoordinator == null)
			return;

		if (m_LoadoutCoordinator.TryDeleteUserPreset(m_DeleteTargetPresetIndex))
			m_EquipmentPanel?.RefreshPresetEditingUi();

		HideDeleteButton();
	}

	private void HandleOutsideInput()
	{
		Mouse mouse = Mouse.current;
		if (mouse == null)
			return;

		if (m_IsRenaming && mouse.leftButton.wasPressedThisFrame)
		{
			if (!IsPointerOverRect(m_RenameInput != null ? m_RenameInput.transform as RectTransform : null))
				CommitRename(m_RenameInput != null ? m_RenameInput.text : string.Empty);
		}

		if (!m_IsDeleteVisible)
			return;

		if (mouse.leftButton.wasPressedThisFrame &&
		    !IsPointerOverRect(m_DeleteButtonRect) &&
		    !IsPointerOverDeleteContextTargets())
		{
			HideDeleteButton();
		}
	}

	private void CommitRename(string _text)
	{
		if (!m_IsRenaming || m_LoadoutCoordinator == null)
			return;

		int presetIndex = GetActivePresetIndex();
		if (CanRenamePreset(presetIndex))
			m_LoadoutCoordinator.TryRenameUserPreset(presetIndex, _text);

		ExitRenameMode();
		m_EquipmentPanel?.RefreshPresetEditingUi();
	}

	private void ExitRenameMode()
	{
		m_IsRenaming = false;

		if (m_PresetDropdown != null)
			m_PresetDropdown.interactable = true;

		if (m_CaptionText != null)
			m_CaptionText.enabled = true;

		if (m_RenameInput != null)
			m_RenameInput.gameObject.SetActive(false);
	}

	private void ShowDeleteButtonForPreset(int _presetIndex, RectTransform _anchor)
	{
		if (_anchor == null || m_DeleteButton == null || !CanDeletePreset(_presetIndex))
		{
			HideDeleteButton();
			return;
		}

		m_DeleteTargetPresetIndex = _presetIndex;
		m_IsDeleteVisible = true;
		m_DeleteButton.gameObject.SetActive(true);
		m_DeleteButton.transform.SetParent(_anchor, false);
		m_DeleteButtonRect.anchorMin = new Vector2(1f, 0f);
		m_DeleteButtonRect.anchorMax = new Vector2(1f, 0f);
		m_DeleteButtonRect.pivot = new Vector2(1f, 1f);
		m_DeleteButtonRect.anchoredPosition = new Vector2(2f, -2f);
		m_DeleteButtonRect.sizeDelta = new Vector2(m_DeleteButtonSize, m_DeleteButtonSize);
		m_DeleteButton.transform.SetAsLastSibling();
	}

	private void HideDeleteButton(bool _reparentHome = true)
	{
		m_IsDeleteVisible = false;
		m_DeleteTargetPresetIndex = -1;

		if (m_DeleteButton == null)
			return;

		if (m_DeleteButton.gameObject.activeSelf)
			m_DeleteButton.gameObject.SetActive(false);

		if (_reparentHome)
			TryReparentDeleteButtonHome();
	}

	private void TryReparentDeleteButtonHome()
	{
		if (m_DeleteButton == null || !isActiveAndEnabled)
			return;

		Transform homeParent = m_PresetDropdown != null ? m_PresetDropdown.transform : transform;
		if (m_DeleteButton.transform.parent == homeParent)
			return;

		m_DeleteButton.transform.SetParent(homeParent, false);
	}

	private int GetActivePresetIndex()
	{
		if (m_LoadoutCoordinator != null)
			return m_LoadoutCoordinator.EditingPresetCatalogIndex;

		return m_PresetDropdown != null ? m_PresetDropdown.value : 0;
	}

	private string GetPresetLabel(int _presetIndex)
	{
		if (m_LoadoutCoordinator != null && m_LoadoutCoordinator.TryGetPresetLabel(_presetIndex, out string label))
			return label;

		return string.Empty;
	}

	private bool CanRenamePreset(int _presetIndex)
	{
		MissionPrepRuntimePresetRegistry registry = m_LoadoutCoordinator?.RuntimePresetRegistry;
		return registry != null && registry.CanRenamePreset(_presetIndex);
	}

	private bool CanDeletePreset(int _presetIndex)
	{
		MissionPrepRuntimePresetRegistry registry = m_LoadoutCoordinator?.RuntimePresetRegistry;
		return registry != null && registry.CanDeletePreset(_presetIndex);
	}

	private bool IsPointerOverRect(RectTransform _rect)
	{
		if (_rect == null || EventSystem.current == null || Mouse.current == null)
			return false;

		return RectTransformUtility.RectangleContainsScreenPoint(
			_rect,
			Mouse.current.position.ReadValue(),
			GetEventCamera(_rect));
	}

	private bool IsPointerOverDeleteContextTargets()
	{
		if (EventSystem.current == null || Mouse.current == null)
			return false;

		PointerEventData pointerData = new PointerEventData(EventSystem.current)
		{
			position = Mouse.current.position.ReadValue()
		};

		var results = new List<RaycastResult>();
		EventSystem.current.RaycastAll(pointerData, results);
		for (int i = 0; i < results.Count; i++)
		{
			GameObject hit = results[i].gameObject;
			if (hit == null)
				continue;

			if (hit.GetComponentInParent<MissionPrepPresetPointerTarget>() != null)
				return true;
		}

		return false;
	}

	private static Camera GetEventCamera(RectTransform _rect)
	{
		Canvas canvas = _rect.GetComponentInParent<Canvas>();
		if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return canvas.worldCamera;
	}

	private static Transform FindDropdownListTransform(TMP_Dropdown _dropdown)
	{
		if (_dropdown == null)
			return null;

		FieldInfo dropdownField = typeof(TMP_Dropdown).GetField(
			"m_Dropdown",
			BindingFlags.NonPublic | BindingFlags.Instance);
		if (dropdownField?.GetValue(_dropdown) is GameObject dropdownGo && dropdownGo != null)
			return dropdownGo.transform;

		return _dropdown.transform.Find("Dropdown List");
	}

	private static TMP_InputField CreateRenameInput(RectTransform _parent)
	{
		var root = new GameObject("PresetCaptionRenameInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
		RectTransform rootRect = root.GetComponent<RectTransform>();
		rootRect.SetParent(_parent, false);
		rootRect.anchorMin = new Vector2(0f, 0.5f);
		rootRect.anchorMax = new Vector2(1f, 0.5f);
		rootRect.pivot = new Vector2(0.5f, 0.5f);
		rootRect.offsetMin = new Vector2(10f, -14f);
		rootRect.offsetMax = new Vector2(-28f, 14f);

		Image image = root.GetComponent<Image>();
		image.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);

		var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
		RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
		textAreaRect.SetParent(rootRect, false);
		textAreaRect.anchorMin = Vector2.zero;
		textAreaRect.anchorMax = Vector2.one;
		textAreaRect.offsetMin = new Vector2(8f, 2f);
		textAreaRect.offsetMax = new Vector2(-8f, -2f);

		var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
		RectTransform textRect = textGo.GetComponent<RectTransform>();
		textRect.SetParent(textAreaRect, false);
		textRect.anchorMin = Vector2.zero;
		textRect.anchorMax = Vector2.one;
		textRect.offsetMin = Vector2.zero;
		textRect.offsetMax = Vector2.zero;
		TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
		text.fontSize = 14f;
		text.color = Color.white;

		TMP_InputField input = root.GetComponent<TMP_InputField>();
		input.textViewport = textAreaRect;
		input.textComponent = text;
		input.lineType = TMP_InputField.LineType.SingleLine;
		return input;
	}

	private static Button CreateDeleteButton(RectTransform _parent, out TextMeshProUGUI _label)
	{
		var root = new GameObject("PresetDeleteButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		RectTransform rootRect = root.GetComponent<RectTransform>();
		rootRect.SetParent(_parent, false);

		Image image = root.GetComponent<Image>();
		image.color = new Color(0.55f, 0.16f, 0.16f, 0.95f);

		var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.SetParent(rootRect, false);
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;
		_label = labelGo.GetComponent<TextMeshProUGUI>();
		_label.alignment = TextAlignmentOptions.Center;
		_label.fontSize = 13f;
		_label.color = Color.white;

		Button button = root.GetComponent<Button>();
		button.targetGraphic = image;
		return button;
	}
	#endregion

	private sealed class MissionPrepPresetDropdownItemBinder : MonoBehaviour
	{
		#region Private Fields
		private MissionPrepPresetCaptionUi m_Owner;
		private int m_PresetIndex = -1;
		private MissionPrepPresetPointerTarget m_PointerTarget;
		#endregion

		#region Public Methods
		public void Bind(MissionPrepPresetCaptionUi _owner, int _presetIndex)
		{
			m_Owner = _owner;
			m_PresetIndex = _presetIndex;

			m_PointerTarget = GetComponent<MissionPrepPresetPointerTarget>();
			if (m_PointerTarget == null)
				m_PointerTarget = gameObject.AddComponent<MissionPrepPresetPointerTarget>();

			m_PointerTarget.Clicked -= HandlePointerClick;
			m_PointerTarget.Clicked += HandlePointerClick;
		}
		#endregion

		#region Private Methods
		private void HandlePointerClick(PointerEventData _eventData)
		{
			if (_eventData.button != PointerEventData.InputButton.Right || m_Owner == null)
				return;

			m_Owner.HandleDropdownItemRightClick(m_PresetIndex, transform as RectTransform);
		}
		#endregion
	}
}
