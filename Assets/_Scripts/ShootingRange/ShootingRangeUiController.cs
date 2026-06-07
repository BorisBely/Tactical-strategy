using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI полигона: сброс мишеней и включение/выключение по одной и всех сразу.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShootingRangeUiController : MonoBehaviour
{
	#region Constants
	private static readonly int[] c_QuickResetDistancesMeters = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
	#endregion

	#region Private Types
	private sealed class TargetRowUi
	{
		public ShootingRangeTarget Target;
		public TextMeshProUGUI Label;
		public Button ResetButton;
		public Button ToggleButton;
		public System.Action<ShootingRangeTarget> StateChangedHandler;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private ShootingRangeManager m_Manager;
	[SerializeField] private RectTransform m_PanelRoot;
	[SerializeField] private RectTransform m_TargetListRoot;
	[SerializeField] private Button m_ResetAllButton;
	[SerializeField] private Button m_EnableAllButton;
	[SerializeField] private Button m_DisableAllButton;
	[SerializeField] private Button m_CycleRankButton;
	#endregion

	#region Private Fields
	private readonly List<TargetRowUi> m_Rows = new List<TargetRowUi>(16);
	private readonly List<Button> m_QuickResetButtons = new List<Button>(10);
	private bool m_Built;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Manager == null)
			m_Manager = GetComponent<ShootingRangeManager>();

		BuildUiIfNeeded();
	}

	private void Start()
	{
		if (m_Manager != null && m_TargetListRoot != null && m_Rows.Count != m_Manager.Targets.Count)
			BuildTargetRows();

		RefreshAllRows();
		RefreshRankButtonLabel();
	}

	private void OnEnable()
	{
		if (m_Manager != null)
			m_Manager.TargetsChanged += HandleTargetsChanged;

		WireGlobalButtons();
		RefreshAllRows();
		RefreshRankButtonLabel();
	}

	private void OnDisable()
	{
		if (m_Manager != null)
			m_Manager.TargetsChanged -= HandleTargetsChanged;

		UnwireGlobalButtons();
		UnwireRowButtons();
	}
	#endregion

	#region Private Methods
	private void BuildUiIfNeeded()
	{
		if (m_Built)
			return;

		m_Built = true;

		if (m_PanelRoot == null)
		{
			Canvas canvas = FindSceneCanvas();
			if (canvas == null)
				return;

			GameObject panelGo = new GameObject("ShootingRangePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			panelGo.transform.SetParent(canvas.transform, false);
			m_PanelRoot = panelGo.GetComponent<RectTransform>();
			m_PanelRoot.anchorMin = new Vector2(1f, 1f);
			m_PanelRoot.anchorMax = new Vector2(1f, 1f);
			m_PanelRoot.pivot = new Vector2(1f, 1f);
			m_PanelRoot.anchoredPosition = new Vector2(-16f, -16f);
			m_PanelRoot.sizeDelta = new Vector2(320f, 0f);

			Image panelImage = panelGo.GetComponent<Image>();
			panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.88f);

			VerticalLayoutGroup layout = panelGo.GetComponent<VerticalLayoutGroup>();
			layout.padding = new RectOffset(12, 12, 12, 12);
			layout.spacing = 8f;
			layout.childAlignment = TextAnchor.UpperRight;
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;

			ContentSizeFitter fitter = panelGo.GetComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			CreateTitle(m_PanelRoot, "Shooting Range");
			CreateGlobalButtonsRow(m_PanelRoot);
			CreateQuickResetButtonsGrid(m_PanelRoot);
			CreateRankButtonRow(m_PanelRoot);
			m_TargetListRoot = CreateScrollList(m_PanelRoot);
		}

		if (m_Manager != null && m_TargetListRoot != null)
			BuildTargetRows();
	}

	private void BuildTargetRows()
	{
		ClearTargetRows();
		IReadOnlyList<ShootingRangeTarget> targets = m_Manager.Targets;
		for (int i = 0; i < targets.Count; i++)
		{
			ShootingRangeTarget target = targets[i];
			if (target == null)
				continue;

			m_Rows.Add(CreateTargetRow(target));
		}

		RefreshAllRows();
	}

	private TargetRowUi CreateTargetRow(ShootingRangeTarget _target)
	{
		GameObject rowGo = new GameObject(_target.DisplayName + "_Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
		rowGo.transform.SetParent(m_TargetListRoot, false);

		HorizontalLayoutGroup rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
		rowLayout.spacing = 6f;
		rowLayout.childAlignment = TextAnchor.MiddleLeft;
		rowLayout.childControlWidth = false;
		rowLayout.childControlHeight = true;
		rowLayout.childForceExpandWidth = false;
		rowLayout.childForceExpandHeight = false;

		LayoutElement rowElement = rowGo.GetComponent<LayoutElement>();
		rowElement.minHeight = 30f;
		rowElement.preferredHeight = 30f;

		TextMeshProUGUI label = CreateText(rowGo.transform, _target.DisplayName, 110f, TextAlignmentOptions.MidlineLeft);
		Button resetButton = CreateButton(rowGo.transform, "Reset", 64f);
		Button toggleButton = CreateButton(rowGo.transform, "ON", 56f);

		var row = new TargetRowUi
		{
			Target = _target,
			Label = label,
			ResetButton = resetButton,
			ToggleButton = toggleButton
		};

		resetButton.onClick.AddListener(() => HandleResetTarget(row));
		toggleButton.onClick.AddListener(() => HandleToggleTarget(row));
		row.StateChangedHandler = _ => RefreshRow(row);
		_target.StateChanged += row.StateChangedHandler;

		return row;
	}

	private void HandleResetTarget(TargetRowUi _row)
	{
		if (m_Manager == null || _row?.Target == null)
			return;

		m_Manager.ResetTarget(_row.Target);
		RefreshRow(_row);
	}

	private void HandleToggleTarget(TargetRowUi _row)
	{
		if (m_Manager == null || _row?.Target == null)
			return;

		bool next = !_row.Target.IsUserEnabled;
		m_Manager.SetTargetEnabled(_row.Target, next);
		RefreshRow(_row);
	}

	private void HandleTargetsChanged()
	{
		if (m_Manager == null || m_TargetListRoot == null)
			return;

		UnwireRowButtons();
		BuildTargetRows();
	}

	private void RefreshAllRows()
	{
		for (int i = 0; i < m_Rows.Count; i++)
			RefreshRow(m_Rows[i]);
	}

	private void RefreshRow(TargetRowUi _row)
	{
		if (_row?.Target == null)
			return;

		ShootingRangeTarget target = _row.Target;
		_row.Label.text = $"{target.DisplayName}  {target.HitCount}/{target.HitsToDefeat}";

		string toggleLabel;
		if (!target.IsUserEnabled)
			toggleLabel = "OFF";
		else if (target.IsDefeated)
			toggleLabel = "DOWN";
		else
			toggleLabel = "ON";

		SetButtonLabel(_row.ToggleButton, toggleLabel);
		_row.ResetButton.interactable = true;
		_row.ToggleButton.interactable = true;
	}

	private void WireGlobalButtons()
	{
		UnwireGlobalButtons();

		if (m_ResetAllButton != null)
			m_ResetAllButton.onClick.AddListener(HandleResetAll);
		if (m_EnableAllButton != null)
			m_EnableAllButton.onClick.AddListener(HandleEnableAll);
		if (m_DisableAllButton != null)
			m_DisableAllButton.onClick.AddListener(HandleDisableAll);
		if (m_CycleRankButton != null)
			m_CycleRankButton.onClick.AddListener(HandleCycleRank);
	}

	private void UnwireGlobalButtons()
	{
		if (m_ResetAllButton != null)
			m_ResetAllButton.onClick.RemoveListener(HandleResetAll);
		if (m_EnableAllButton != null)
			m_EnableAllButton.onClick.RemoveListener(HandleEnableAll);
		if (m_DisableAllButton != null)
			m_DisableAllButton.onClick.RemoveListener(HandleDisableAll);
		if (m_CycleRankButton != null)
			m_CycleRankButton.onClick.RemoveListener(HandleCycleRank);
	}

	private void UnwireQuickResetButtons()
	{
		for (int i = 0; i < m_QuickResetButtons.Count; i++)
		{
			Button button = m_QuickResetButtons[i];
			if (button != null)
				button.onClick.RemoveAllListeners();
		}

		m_QuickResetButtons.Clear();
	}

	private void UnwireRowButtons()
	{
		for (int i = 0; i < m_Rows.Count; i++)
		{
			TargetRowUi row = m_Rows[i];
			if (row?.ResetButton != null)
				row.ResetButton.onClick.RemoveAllListeners();
			if (row?.ToggleButton != null)
				row.ToggleButton.onClick.RemoveAllListeners();
			if (row?.Target != null && row.StateChangedHandler != null)
				row.Target.StateChanged -= row.StateChangedHandler;
		}

		m_Rows.Clear();
	}

	private void HandleResetAll()
	{
		m_Manager?.ResetAllTargets();
		RefreshAllRows();
	}

	private void HandleEnableAll()
	{
		m_Manager?.SetAllTargetsEnabled(true);
		RefreshAllRows();
	}

	private void HandleDisableAll()
	{
		m_Manager?.SetAllTargetsEnabled(false);
		RefreshAllRows();
	}

	private void HandleQuickResetTarget(int _distanceMeters)
	{
		m_Manager?.ResetTargetByDistanceMeters(_distanceMeters);
		RefreshAllRows();
	}

	private void HandleCycleRank()
	{
		if (m_Manager == null)
			return;

		if (m_Manager.TryCyclePlayerUnitRank(out string newRankLabel))
		{
			RefreshRankButtonLabel();
			return;
		}

		Debug.LogWarning("[Полигон] Не удалось сменить ранг: не найден UnitCombatStats у активного игрока или не задан Rank Cycle Order.", m_Manager);
	}

	private void RefreshRankButtonLabel()
	{
		if (m_CycleRankButton == null || m_Manager == null)
			return;

		SetButtonLabel(m_CycleRankButton, $"Rank: {m_Manager.GetPlayerUnitRankLabel()}");
	}

	private void CreateQuickResetButtonsGrid(Transform _parent)
	{
		UnwireQuickResetButtons();

		GameObject rowGo = new GameObject("QuickResetButtons", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
		rowGo.transform.SetParent(_parent, false);

		LayoutElement rowElement = rowGo.GetComponent<LayoutElement>();
		rowElement.minHeight = 64f;
		rowElement.preferredHeight = 64f;

		GridLayoutGroup grid = rowGo.GetComponent<GridLayoutGroup>();
		grid.cellSize = new Vector2(58f, 28f);
		grid.spacing = new Vector2(4f, 4f);
		grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		grid.constraintCount = 5;
		grid.childAlignment = TextAnchor.UpperCenter;

		for (int i = 0; i < c_QuickResetDistancesMeters.Length; i++)
		{
			int distanceMeters = c_QuickResetDistancesMeters[i];
			Button button = CreateButton(rowGo.transform, $"{distanceMeters}m", 0f);
			button.onClick.AddListener(() => HandleQuickResetTarget(distanceMeters));
			m_QuickResetButtons.Add(button);
		}
	}

	private void CreateRankButtonRow(Transform _parent)
	{
		GameObject rowGo = new GameObject("RankButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
		rowGo.transform.SetParent(_parent, false);

		HorizontalLayoutGroup layout = rowGo.GetComponent<HorizontalLayoutGroup>();
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		LayoutElement rowElement = rowGo.GetComponent<LayoutElement>();
		rowElement.minHeight = 32f;
		rowElement.preferredHeight = 32f;

		m_CycleRankButton = CreateButton(rowGo.transform, "Rank: —", 0f);
	}

	private void CreateGlobalButtonsRow(Transform _parent)
	{
		GameObject rowGo = new GameObject("GlobalButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
		rowGo.transform.SetParent(_parent, false);

		HorizontalLayoutGroup layout = rowGo.GetComponent<HorizontalLayoutGroup>();
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		m_ResetAllButton = CreateButton(rowGo.transform, "Reset All", 0f);
		m_EnableAllButton = CreateButton(rowGo.transform, "Enable All", 0f);
		m_DisableAllButton = CreateButton(rowGo.transform, "Disable All", 0f);
	}

	private static RectTransform CreateScrollList(Transform _parent)
	{
		GameObject scrollGo = new GameObject(
			"TargetListScroll",
			typeof(RectTransform),
			typeof(Image),
			typeof(ScrollRect),
			typeof(LayoutElement));
		scrollGo.transform.SetParent(_parent, false);

		LayoutElement scrollElement = scrollGo.GetComponent<LayoutElement>();
		scrollElement.minHeight = 180f;
		scrollElement.preferredHeight = 220f;

		Image scrollBackground = scrollGo.GetComponent<Image>();
		scrollBackground.color = new Color(0f, 0f, 0f, 0.18f);

		GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
		viewportGo.transform.SetParent(scrollGo.transform, false);
		RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = Vector2.zero;
		viewportRect.offsetMax = Vector2.zero;

		GameObject contentGo = new GameObject("TargetList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		contentGo.transform.SetParent(viewportGo.transform, false);
		RectTransform contentRect = contentGo.GetComponent<RectTransform>();
		contentRect.anchorMin = new Vector2(0f, 1f);
		contentRect.anchorMax = new Vector2(1f, 1f);
		contentRect.pivot = new Vector2(0.5f, 1f);
		contentRect.anchoredPosition = Vector2.zero;
		contentRect.sizeDelta = new Vector2(0f, 0f);

		VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
		layout.spacing = 4f;
		layout.childAlignment = TextAnchor.UpperRight;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		ScrollRect scrollRect = scrollGo.GetComponent<ScrollRect>();
		scrollRect.viewport = viewportRect;
		scrollRect.content = contentRect;
		scrollRect.horizontal = false;
		scrollRect.vertical = true;
		scrollRect.movementType = ScrollRect.MovementType.Clamped;

		return contentRect;
	}

	private static void CreateTitle(Transform _parent, string _text)
	{
		CreateText(_parent, _text, 0f, TextAlignmentOptions.Center, 20f);
	}

	private static TextMeshProUGUI CreateText(
		Transform _parent,
		string _text,
		float _width,
		TextAlignmentOptions _alignment,
		float _fontSize = 16f)
	{
		GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
		textGo.transform.SetParent(_parent, false);

		if (_width > 0f)
		{
			LayoutElement element = textGo.GetComponent<LayoutElement>();
			element.minWidth = _width;
			element.preferredWidth = _width;
		}

		TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
		tmp.text = _text;
		tmp.fontSize = _fontSize;
		tmp.color = Color.white;
		tmp.alignment = _alignment;
		tmp.textWrappingMode = TextWrappingModes.NoWrap;
		tmp.raycastTarget = false;
		return tmp;
	}

	private static Button CreateButton(Transform _parent, string _label, float _width)
	{
		GameObject buttonGo = new GameObject(_label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
		buttonGo.transform.SetParent(_parent, false);

		LayoutElement element = buttonGo.GetComponent<LayoutElement>();
		if (_width > 0f)
		{
			element.minWidth = _width;
			element.preferredWidth = _width;
		}
		else
		{
			element.flexibleWidth = 1f;
		}

		element.minHeight = 28f;
		element.preferredHeight = 28f;

		Image image = buttonGo.GetComponent<Image>();
		image.color = new Color(0.2f, 0.28f, 0.36f, 1f);

		Button button = buttonGo.GetComponent<Button>();
		CreateText(buttonGo.transform, _label, 0f, TextAlignmentOptions.Center, 14f);
		return button;
	}

	private static void SetButtonLabel(Button _button, string _label)
	{
		if (_button == null)
			return;

		TextMeshProUGUI tmp = _button.GetComponentInChildren<TextMeshProUGUI>();
		if (tmp != null)
			tmp.text = _label;
	}

	private static Canvas FindSceneCanvas()
	{
#if UNITY_2023_1_OR_NEWER
		Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		Canvas[] canvases = FindObjectsOfType<Canvas>();
#endif
		for (int i = 0; i < canvases.Length; i++)
		{
			Canvas canvas = canvases[i];
			if (canvas != null && canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
				return canvas;
		}

		return canvases.Length > 0 ? canvases[0] : null;
	}

	private void ClearTargetRows()
	{
		UnwireRowButtons();
		if (m_TargetListRoot == null)
			return;

		for (int i = m_TargetListRoot.childCount - 1; i >= 0; i--)
			Destroy(m_TargetListRoot.GetChild(i).gameObject);
	}
	#endregion
}
