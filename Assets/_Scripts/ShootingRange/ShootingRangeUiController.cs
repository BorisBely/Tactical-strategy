using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum PlayerDebugInjuryType
{
	ArmBleeding,
	LegFracture,
	LungDamage
}

/// <summary>
/// UI полигона: сброс мишеней и включение/выключение всех сразу.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShootingRangeUiController : MonoBehaviour
{
	#region Constants
	private static readonly int[] c_QuickResetDistancesMeters = { 50, 100, 150, 200, 250, 300, 350, 400, 450, 500 };
	private const int c_PanelCanvasSortingOrder = 32000;
	#endregion

	#region Private Types
	private sealed class TargetRowUi
	{
		public ShootingRangeTarget Target;
		public TextMeshProUGUI Label;
		public Button ToggleButton;
		public Button ResetButton;
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
	[SerializeField] private Button m_CycleHitCounterButton;
	#endregion

	#region Private Fields
	private readonly List<TargetRowUi> m_Rows = new List<TargetRowUi>(16);
	private readonly List<Button> m_QuickToggleButtons = new List<Button>(10);
	private readonly List<int> m_QuickToggleDistancesMeters = new List<int>(10);
	private readonly List<Button> m_InjuryDebugButtons = new List<Button>(4);
	private readonly List<Button> m_ClickableButtons = new List<Button>(32);
	private static ShootingRangeUiController s_Instance;
	private GameObject m_CanvasRoot;
	private bool m_Built;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		if (m_Manager == null)
			m_Manager = GetComponent<ShootingRangeManager>();

		BuildUiIfNeeded();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;

		if (m_CanvasRoot != null)
			Destroy(m_CanvasRoot);
	}

	private void Start()
	{
		if (m_Manager != null && m_TargetListRoot != null && m_Rows.Count != m_Manager.Targets.Count)
			BuildTargetRows();

		SubscribeSelectionChanges();
		RefreshAllRows();
		RefreshRankButtonLabel();
		RefreshHitCounterButtonLabel();
		RebuildClickableButtons();
	}

	private void OnEnable()
	{
		s_Instance = this;
		if (m_Manager != null)
			m_Manager.TargetsChanged += HandleTargetsChanged;

		SubscribeSelectionChanges();
		WireGlobalButtons();
		RefreshAllRows();
		RefreshQuickToggleButtons();
		RefreshRankButtonLabel();
		RefreshHitCounterButtonLabel();
		RebuildClickableButtons();
	}

	private void OnDisable()
	{
		if (m_Manager != null)
			m_Manager.TargetsChanged -= HandleTargetsChanged;

		UnsubscribeSelectionChanges();
		UnwireGlobalButtons();
		UnwireRowButtons();

		if (s_Instance == this)
			s_Instance = null;
	}

	private void Update()
	{
		if (!m_Built || m_PanelRoot == null || PauseMenuController.IsPaused)
			return;

		if (Mouse.current == null || !Mouse.current.leftButton.wasReleasedThisFrame)
			return;

		if (!IsPointerOverPanelArea())
			return;

		TryClickButtonUnderCursor();
	}
	#endregion

	#region Public Methods
	/// <summary>Курсор над панелью полигона (для блокировки RTS-кликов).</summary>
	public static bool IsPointerOverPanelArea()
	{
		ShootingRangeUiController instance = s_Instance;
		if (instance == null || instance.m_PanelRoot == null || !instance.m_PanelRoot.gameObject.activeInHierarchy)
			return false;

		if (PauseMenuController.IsPaused)
			return false;

		Vector2 mousePosition = Mouse.current?.position.ReadValue() ?? Vector2.zero;
		return RectTransformUtility.RectangleContainsScreenPoint(instance.m_PanelRoot, mousePosition, null);
	}

	/// <summary>Перестраивает список мишеней и подписи после позднего спавна player-юнитов.</summary>
	public void RefreshPanelState()
	{
		if (m_Manager == null)
			m_Manager = GetComponent<ShootingRangeManager>();

		BuildUiIfNeeded();

		if (m_Manager != null && m_TargetListRoot != null && m_Rows.Count != m_Manager.Targets.Count)
			BuildTargetRows();

		WireGlobalButtons();
		SubscribeSelectionChanges();
		RefreshAllRows();
		RefreshQuickToggleButtons();
		RefreshRankButtonLabel();
		RefreshHitCounterButtonLabel();
		RebuildClickableButtons();
	}
	#endregion

	#region Private Methods
	private void BuildUiIfNeeded()
	{
		if (m_Built && m_PanelRoot != null)
			return;

		if (m_PanelRoot == null)
		{
			// Отдельный root-canvas (не под 3D-трансформом и не под ActionPanel).
			m_CanvasRoot = new GameObject("ShootingRangeCanvas", typeof(RectTransform));
			m_CanvasRoot.transform.SetParent(null, false);

			RectTransform canvasRect = m_CanvasRoot.GetComponent<RectTransform>();
			canvasRect.anchorMin = Vector2.zero;
			canvasRect.anchorMax = Vector2.one;
			canvasRect.offsetMin = Vector2.zero;
			canvasRect.offsetMax = Vector2.zero;

			Canvas rootCanvas = m_CanvasRoot.AddComponent<Canvas>();
			rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
			rootCanvas.overrideSorting = true;
			rootCanvas.sortingOrder = c_PanelCanvasSortingOrder;

			CanvasScaler scaler = m_CanvasRoot.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(2560f, 1440f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0f;

			// Без GraphicRaycaster: клики обрабатываем в Update по RectTransform,
			// чтобы RTS-выделение не перехватывало Input System EventSystem.

			GameObject panelGo = new GameObject("ShootingRangePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			panelGo.transform.SetParent(m_CanvasRoot.transform, false);
			m_PanelRoot = panelGo.GetComponent<RectTransform>();
			m_PanelRoot.anchorMin = new Vector2(1f, 1f);
			m_PanelRoot.anchorMax = new Vector2(1f, 1f);
			m_PanelRoot.pivot = new Vector2(1f, 1f);
			m_PanelRoot.anchoredPosition = new Vector2(-16f, -16f);
			m_PanelRoot.sizeDelta = new Vector2(320f, 0f);

			Image panelImage = panelGo.GetComponent<Image>();
			panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.88f);
			panelImage.raycastTarget = false;

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
			CreateQuickToggleButtonsGrid(m_PanelRoot);
			CreateRankButtonRow(m_PanelRoot);
			CreateHitCounterButtonRow(m_PanelRoot);
			CreateInjuryDebugButtonsGrid(m_PanelRoot);
			m_TargetListRoot = CreateScrollList(m_PanelRoot);
		}

		m_Built = true;

		if (m_Manager != null && m_TargetListRoot != null)
			BuildTargetRows();

		RebuildClickableButtons();
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
		RebuildClickableButtons();
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

		TextMeshProUGUI label = CreateText(rowGo.transform, _target.DisplayName, 190f, TextAlignmentOptions.MidlineLeft);
		Button toggleButton = CreateButton(rowGo.transform, "Toggle", 64f);
		Button resetButton = CreateButton(rowGo.transform, "Reset", 64f);

		var row = new TargetRowUi
		{
			Target = _target,
			Label = label,
			ToggleButton = toggleButton,
			ResetButton = resetButton
		};

		toggleButton.onClick.AddListener(() => HandleToggleTarget(row));
		resetButton.onClick.AddListener(() => HandleResetTarget(row));
		row.StateChangedHandler = _ =>
		{
			RefreshRow(row);
			RefreshQuickToggleButtons();
		};
		_target.StateChanged += row.StateChangedHandler;

		return row;
	}

	private void HandleResetTarget(TargetRowUi _row)
	{
		if (m_Manager == null || _row?.Target == null)
			return;

		m_Manager.ResetTargetHealth(_row.Target);
		RefreshRow(_row);
	}

	private void HandleToggleTarget(TargetRowUi _row)
	{
		if (m_Manager == null || _row?.Target == null)
			return;

		m_Manager.ToggleTarget(_row.Target);
		RefreshRow(_row);
		RefreshQuickToggleButtons();
	}

	private void HandleTargetsChanged()
	{
		if (m_Manager == null || m_TargetListRoot == null)
			return;

		UnwireRowButtons();
		BuildTargetRows();
		RefreshQuickToggleButtons();
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
		string statusLabel = target.IsUserEnabled ? "ON" : "OFF";
		if (target.HasHitCounter)
			_row.Label.text = $"{target.DisplayName}  {statusLabel}  {target.CurrentHitCount}/{target.RequiredHitCount}";
		else
			_row.Label.text = $"{target.DisplayName}  {statusLabel}";
		SetButtonLabel(_row.ToggleButton, target.IsUserEnabled ? "OFF" : "ON");
		_row.ResetButton.interactable = true;
	}

	private void WireGlobalButtons()
	{
		UnwireGlobalButtons();

		if (m_ResetAllButton != null)
		{
			m_ResetAllButton.onClick.RemoveAllListeners();
			m_ResetAllButton.onClick.AddListener(HandleResetAll);
		}

		if (m_EnableAllButton != null)
		{
			m_EnableAllButton.onClick.RemoveAllListeners();
			m_EnableAllButton.onClick.AddListener(HandleEnableAll);
		}

		if (m_DisableAllButton != null)
		{
			m_DisableAllButton.onClick.RemoveAllListeners();
			m_DisableAllButton.onClick.AddListener(HandleDisableAll);
		}

		if (m_CycleRankButton != null)
		{
			m_CycleRankButton.onClick.RemoveAllListeners();
			m_CycleRankButton.onClick.AddListener(HandleCycleRank);
		}

		if (m_CycleHitCounterButton != null)
		{
			m_CycleHitCounterButton.onClick.RemoveAllListeners();
			m_CycleHitCounterButton.onClick.AddListener(HandleCycleHitCounter);
		}
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
		if (m_CycleHitCounterButton != null)
			m_CycleHitCounterButton.onClick.RemoveListener(HandleCycleHitCounter);
	}

	private void UnwireQuickResetButtons()
	{
		UnwireQuickToggleButtons();
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
		m_Manager?.ResetAllTargetsHealth();
		RefreshAllRows();
	}

	private void HandleEnableAll()
	{
		m_Manager?.SetAllTargetsEnabled(true);
		RefreshAllRows();
		RefreshQuickToggleButtons();
	}

	private void HandleDisableAll()
	{
		m_Manager?.SetAllTargetsEnabled(false);
		RefreshAllRows();
		RefreshQuickToggleButtons();
	}

	private void HandleToggleTarget(int _distanceMeters)
	{
		m_Manager?.ToggleTargetByDistanceMeters(_distanceMeters);
		RefreshAllRows();
		RefreshQuickToggleButtons();
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

		Debug.LogWarning("[Полигон] Не удалось сменить ранг: выделите юнита игрока или задайте Rank Cycle Order.", m_Manager);
	}

	private void HandleSelectionChanged()
	{
		RefreshRankButtonLabel();
	}

	private void SubscribeSelectionChanges()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection == null)
			return;

		selection.SelectionChanged -= HandleSelectionChanged;
		selection.SelectionChanged += HandleSelectionChanged;
	}

	private void UnsubscribeSelectionChanges()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection != null)
			selection.SelectionChanged -= HandleSelectionChanged;
	}

	private void HandleCycleHitCounter()
	{
		if (m_Manager == null)
			return;

		if (m_Manager.TryCycleHitCounterMode(out _))
		{
			RefreshHitCounterButtonLabel();
			RefreshAllRows();
		}
	}

	private void HandleAddPlayerInjury(PlayerDebugInjuryType _injuryType)
	{
		m_Manager?.TryAddPlayerDebugInjury(_injuryType);
	}

	private void HandleClearPlayerInjuries()
	{
		m_Manager?.TryClearPlayerInjuries();
	}

	private void RefreshRankButtonLabel()
	{
		if (m_CycleRankButton == null || m_Manager == null)
			return;

		bool canCycle = m_Manager.CanCyclePlayerUnitRank();
		m_CycleRankButton.interactable = canCycle;
		SetButtonLabel(m_CycleRankButton, $"Rank: {m_Manager.GetPlayerUnitRankLabel()}");
	}

	private void RefreshHitCounterButtonLabel()
	{
		if (m_CycleHitCounterButton == null || m_Manager == null)
			return;

		SetButtonLabel(m_CycleHitCounterButton, $"Hits: {m_Manager.GetHitCounterModeLabel()}");
	}

	private void CreateQuickToggleButtonsGrid(Transform _parent)
	{
		UnwireQuickToggleButtons();

		GameObject rowGo = new GameObject("QuickToggleButtons", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
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
			button.onClick.AddListener(() => HandleToggleTarget(distanceMeters));
			m_QuickToggleButtons.Add(button);
			m_QuickToggleDistancesMeters.Add(distanceMeters);
		}

		RefreshQuickToggleButtons();
	}

	private void RefreshQuickToggleButtons()
	{
		if (m_Manager == null)
			return;

		for (int i = 0; i < m_QuickToggleButtons.Count; i++)
		{
			Button button = m_QuickToggleButtons[i];
			if (button == null || i >= m_QuickToggleDistancesMeters.Count)
				continue;

			int distanceMeters = m_QuickToggleDistancesMeters[i];
			bool isEnabled = m_Manager.TryGetTargetByDistanceMeters(distanceMeters, out ShootingRangeTarget target)
			                 && target != null
			                 && target.IsUserEnabled;
			SetButtonLabel(button, isEnabled ? $"{distanceMeters}m ON" : $"{distanceMeters}m OFF");
		}
	}

	private void UnwireQuickToggleButtons()
	{
		for (int i = 0; i < m_QuickToggleButtons.Count; i++)
		{
			Button button = m_QuickToggleButtons[i];
			if (button != null)
				button.onClick.RemoveAllListeners();
		}

		m_QuickToggleButtons.Clear();
		m_QuickToggleDistancesMeters.Clear();
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

	private void CreateHitCounterButtonRow(Transform _parent)
	{
		GameObject rowGo = new GameObject("HitCounterButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
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

		m_CycleHitCounterButton = CreateButton(rowGo.transform, "Hits: Off", 0f);
	}

	private void CreateInjuryDebugButtonsGrid(Transform _parent)
	{
		UnwireInjuryDebugButtons();

		CreateText(_parent, "Debug Injuries", 0f, TextAlignmentOptions.Center, 14f);

		GameObject rowGo = new GameObject("InjuryDebugButtons", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
		rowGo.transform.SetParent(_parent, false);

		LayoutElement rowElement = rowGo.GetComponent<LayoutElement>();
		rowElement.minHeight = 64f;
		rowElement.preferredHeight = 64f;

		GridLayoutGroup grid = rowGo.GetComponent<GridLayoutGroup>();
		grid.cellSize = new Vector2(150f, 28f);
		grid.spacing = new Vector2(4f, 4f);
		grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		grid.constraintCount = 2;
		grid.childAlignment = TextAnchor.UpperCenter;

		AddInjuryDebugButton(rowGo.transform, "Arm Bleeding", PlayerDebugInjuryType.ArmBleeding);
		AddInjuryDebugButton(rowGo.transform, "Leg Fracture", PlayerDebugInjuryType.LegFracture);
		AddInjuryDebugButton(rowGo.transform, "Lung Damage", PlayerDebugInjuryType.LungDamage);
		AddInjuryDebugButton(rowGo.transform, "Clear Injuries", null);
	}

	private void AddInjuryDebugButton(Transform _parent, string _label, PlayerDebugInjuryType? _injuryType)
	{
		Button button = CreateButton(_parent, _label, 0f);
		if (_injuryType.HasValue)
		{
			PlayerDebugInjuryType injuryType = _injuryType.Value;
			button.onClick.AddListener(() => HandleAddPlayerInjury(injuryType));
		}
		else
		{
			button.onClick.AddListener(HandleClearPlayerInjuries);
		}

		m_InjuryDebugButtons.Add(button);
	}

	private void UnwireInjuryDebugButtons()
	{
		for (int i = 0; i < m_InjuryDebugButtons.Count; i++)
		{
			Button button = m_InjuryDebugButtons[i];
			if (button != null)
				button.onClick.RemoveAllListeners();
		}

		m_InjuryDebugButtons.Clear();
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
		image.raycastTarget = false;

		Button button = buttonGo.GetComponent<Button>();
		UiInteractionAudioUtility.EnsureHoverSoundOn(buttonGo);
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

	private void ClearTargetRows()
	{
		UnwireRowButtons();
		if (m_TargetListRoot == null)
			return;

		for (int i = m_TargetListRoot.childCount - 1; i >= 0; i--)
			Destroy(m_TargetListRoot.GetChild(i).gameObject);
	}

	private void RebuildClickableButtons()
	{
		m_ClickableButtons.Clear();

		AddClickableButton(m_ResetAllButton);
		AddClickableButton(m_EnableAllButton);
		AddClickableButton(m_DisableAllButton);
		AddClickableButton(m_CycleRankButton);
		AddClickableButton(m_CycleHitCounterButton);

		for (int i = 0; i < m_QuickToggleButtons.Count; i++)
			AddClickableButton(m_QuickToggleButtons[i]);

		for (int i = 0; i < m_InjuryDebugButtons.Count; i++)
			AddClickableButton(m_InjuryDebugButtons[i]);

		for (int i = 0; i < m_Rows.Count; i++)
		{
			TargetRowUi row = m_Rows[i];
			if (row == null)
				continue;

			AddClickableButton(row.ToggleButton);
			AddClickableButton(row.ResetButton);
		}
	}

	private void AddClickableButton(Button _button)
	{
		if (_button != null)
			m_ClickableButtons.Add(_button);
	}

	private void TryClickButtonUnderCursor()
	{
		if (m_ClickableButtons.Count == 0)
			RebuildClickableButtons();

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		for (int i = 0; i < m_ClickableButtons.Count; i++)
		{
			Button button = m_ClickableButtons[i];
			if (button == null || !button.isActiveAndEnabled || !button.interactable)
				continue;

			RectTransform buttonRect = button.transform as RectTransform;
			if (buttonRect == null)
				continue;

			if (!RectTransformUtility.RectangleContainsScreenPoint(buttonRect, mousePosition, null))
				continue;

			button.onClick.Invoke();
			return;
		}
	}
	#endregion
}
