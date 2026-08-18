using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Click-pin и hover-preview фильтры категорий для панели доступного снаряжения пресета.
/// Hover начинается только с кнопок; пока курсор в колонке (кнопки, зазор, список) — preview держится.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepAvailableEquipmentCategoryFilter : MonoBehaviour
{
	#region Serializable Types
	[Serializable]
	public sealed class CategoryButton
	{
		[SerializeField] private Button m_Button;
		[SerializeField] private MissionPrepAvailableEquipmentFilterCategory m_Category;
		[SerializeField] private Graphic m_TargetGraphic;

		public Button Button => m_Button;
		public MissionPrepAvailableEquipmentFilterCategory Category => m_Category;
		public Graphic TargetGraphic => m_TargetGraphic != null
			? m_TargetGraphic
			: m_Button != null ? m_Button.targetGraphic : null;
	}
	#endregion

	#region Constants
	private const int c_OutsideHoldGraceFrames = 3;
	#endregion

	#region Serialized Fields
	[SerializeField] private CategoryButton[] m_Buttons = Array.Empty<CategoryButton>();
	[SerializeField] private RectTransform m_ButtonsRow;
	[SerializeField] private RectTransform m_ListZone;
	[SerializeField] private RectTransform m_HoldZone;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;

	[Header("Цвета состояний кнопки")]
	[SerializeField] private Color m_NormalColor = new Color(0.22f, 0.22f, 0.22f, 1f);
	[SerializeField] private Color m_PreviewColor = new Color(0.32f, 0.42f, 0.52f, 1f);
	[SerializeField] private Color m_ActiveColor = new Color(0.18f, 0.52f, 0.32f, 1f);
	#endregion

	#region Private Fields
	private MissionPrepAvailableEquipmentFilterCategory? m_PinnedCategory;
	private MissionPrepAvailableEquipmentFilterCategory? m_HoverCategory;
	private MissionPrepAvailableEquipmentFilterCategory? m_LastPaintedCategory;
	private bool m_LastPaintedHadFilter;
	private bool m_ListenersBound;
	private int m_OutsideHoldFrames;
	private static readonly List<RaycastResult> s_RaycastBuffer = new List<RaycastResult>(16);
	#endregion

	#region Public Properties
	/// <summary>Эффективная категория: hover preview имеет приоритет над click-pin.</summary>
	public bool TryGetEffectiveCategory(out MissionPrepAvailableEquipmentFilterCategory _category)
	{
		if (m_HoverCategory.HasValue)
		{
			_category = m_HoverCategory.Value;
			return true;
		}

		if (m_PinnedCategory.HasValue)
		{
			_category = m_PinnedCategory.Value;
			return true;
		}

		_category = default;
		return false;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryResolveReferences();
		RefreshButtonLabels();
		BindButtonListeners();
		EnsureButtonPointerHooks();
		ApplyButtonVisuals();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		RefreshButtonLabels();
		BindButtonListeners();
		ApplyButtonVisuals();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		UnbindButtonListeners();
	}

	private void HandleLanguageChanged()
	{
		RefreshButtonLabels();
	}

	private void LateUpdate()
	{
		if (!m_HoverCategory.HasValue)
		{
			m_OutsideHoldFrames = 0;
			return;
		}

		if (IsPointerOverFilterZone())
		{
			m_OutsideHoldFrames = 0;
			return;
		}

		m_OutsideHoldFrames++;
		if (m_OutsideHoldFrames < c_OutsideHoldGraceFrames)
			return;

		ClearHoverPreview();
	}
	#endregion

	#region Public Methods
	public void NotifyCategoryButtonPointerEnter(MissionPrepAvailableEquipmentFilterCategory _category)
	{
		if (m_HoverCategory == _category)
			return;

		m_HoverCategory = _category;
		m_OutsideHoldFrames = 0;
		ApplyButtonVisuals();
		RequestRepaintIfNeeded();
	}

	public void HandleCategoryClicked(MissionPrepAvailableEquipmentFilterCategory _category)
	{
		if (m_PinnedCategory == _category)
			m_PinnedCategory = null;
		else
			m_PinnedCategory = _category;

		// Клик закрепляет категорию; hover больше не перекрывает pin.
		m_HoverCategory = null;
		m_OutsideHoldFrames = 0;
		ApplyButtonVisuals();
		RequestRepaintIfNeeded();
	}
	#endregion

	#region Private Methods
	private void ClearHoverPreview()
	{
		if (!m_HoverCategory.HasValue)
			return;

		m_HoverCategory = null;
		m_OutsideHoldFrames = 0;
		ApplyButtonVisuals();
		RequestRepaintIfNeeded();
	}

	private void RequestRepaintIfNeeded()
	{
		bool hasFilter = TryGetEffectiveCategory(out MissionPrepAvailableEquipmentFilterCategory category);
		if (hasFilter == m_LastPaintedHadFilter &&
		    (!hasFilter || m_LastPaintedCategory == category))
			return;

		m_LastPaintedHadFilter = hasFilter;
		m_LastPaintedCategory = hasFilter ? category : null;

		if (m_Coordinator == null)
			TryResolveReferences();

		m_Coordinator?.RepaintAvailableEquipmentPanel();
	}

	/// <summary>Сброс кэша после внешнего repaint (координатор уже перерисовал).</summary>
	public void SyncPaintedStateFromCurrent()
	{
		m_LastPaintedHadFilter = TryGetEffectiveCategory(out MissionPrepAvailableEquipmentFilterCategory category);
		m_LastPaintedCategory = m_LastPaintedHadFilter ? category : null;
	}

	private void BindButtonListeners()
	{
		if (m_ListenersBound || m_Buttons == null)
			return;

		for (int i = 0; i < m_Buttons.Length; i++)
		{
			CategoryButton binding = m_Buttons[i];
			if (binding?.Button == null)
				continue;

			MissionPrepAvailableEquipmentFilterCategory category = binding.Category;
			binding.Button.onClick.AddListener(() => HandleCategoryClicked(category));
		}

		m_ListenersBound = true;
	}

	private void UnbindButtonListeners()
	{
		if (!m_ListenersBound || m_Buttons == null)
			return;

		for (int i = 0; i < m_Buttons.Length; i++)
		{
			if (m_Buttons[i]?.Button != null)
				m_Buttons[i].Button.onClick.RemoveAllListeners();
		}

		m_ListenersBound = false;
	}

	private void EnsureButtonPointerHooks()
	{
		if (m_Buttons == null)
			return;

		for (int i = 0; i < m_Buttons.Length; i++)
		{
			CategoryButton binding = m_Buttons[i];
			if (binding?.Button == null)
				continue;

			MissionPrepAvailableEquipmentFilterButtonHook hook =
				binding.Button.GetComponent<MissionPrepAvailableEquipmentFilterButtonHook>();
			if (hook == null)
				hook = binding.Button.gameObject.AddComponent<MissionPrepAvailableEquipmentFilterButtonHook>();

			hook.Bind(this, binding.Category);
		}
	}

	private void ApplyButtonVisuals()
	{
		if (m_Buttons == null)
			return;

		for (int i = 0; i < m_Buttons.Length; i++)
		{
			CategoryButton binding = m_Buttons[i];
			if (binding == null)
				continue;

			Graphic graphic = binding.TargetGraphic;
			if (graphic == null)
				continue;

			bool isPinned = m_PinnedCategory == binding.Category;
			bool isPreview = m_HoverCategory == binding.Category;

			if (isPinned && !m_HoverCategory.HasValue)
				graphic.color = m_ActiveColor;
			else if (isPreview)
				graphic.color = m_PreviewColor;
			else if (isPinned)
				graphic.color = m_ActiveColor;
			else
				graphic.color = m_NormalColor;
		}
	}

	private void RefreshButtonLabels()
	{
		if (m_Buttons == null)
			return;

		for (int i = 0; i < m_Buttons.Length; i++)
		{
			CategoryButton binding = m_Buttons[i];
			if (binding?.Button == null)
				continue;

			string key = MissionPrepAvailableEquipmentFilterClassifier.GetLocalizationKey(binding.Category);
			string fallback = GetCategoryFallbackLabel(binding.Category);

			LocalizedTextMeshProUGUI loc =
				binding.Button.GetComponentInChildren<LocalizedTextMeshProUGUI>(true);
			if (loc != null)
				loc.SetLocalizationKey(key);

			TMP_Text label = binding.Button.GetComponentInChildren<TMP_Text>(true);
			if (label == null)
				continue;

			label.text = LocalizationManager.HasInstance
				? LocalizationManager.Get(key, fallback)
				: fallback;
		}
	}

	private static string GetCategoryFallbackLabel(MissionPrepAvailableEquipmentFilterCategory _category)
	{
		return _category switch
		{
			MissionPrepAvailableEquipmentFilterCategory.Weapons => "Оружие",
			MissionPrepAvailableEquipmentFilterCategory.Mods => "Модули",
			MissionPrepAvailableEquipmentFilterCategory.Ammo => "Боеприпасы",
			MissionPrepAvailableEquipmentFilterCategory.Equipment => "Экипировка",
			MissionPrepAvailableEquipmentFilterCategory.Extra => "Доп. снаряжение",
			_ => "Доп. снаряжение"
		};
	}

	private bool IsPointerOverFilterZone()
	{
		TryResolveReferences();

		Vector2 screenPosition = ResolvePointerScreenPosition();
		if (screenPosition.x < 0f && screenPosition.y < 0f)
			return false;

		if (IsAvailableCatalogDragActive())
			return true;

		Camera eventCamera = ResolveEventCamera();
		return ContainsScreenPoint(transform as RectTransform, screenPosition, eventCamera) ||
		       ContainsScreenPoint(m_HoldZone, screenPosition, eventCamera) ||
		       ContainsScreenPoint(m_ButtonsRow, screenPosition, eventCamera) ||
		       ContainsScreenPoint(m_ListZone, screenPosition, eventCamera) ||
		       IsRaycastOverThisColumn(screenPosition);
	}

	private bool IsRaycastOverThisColumn(Vector2 _screenPosition)
	{
		if (EventSystem.current == null)
			return false;

		s_RaycastBuffer.Clear();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};
		EventSystem.current.RaycastAll(pointerData, s_RaycastBuffer);

		Transform panelRoot = transform;
		for (int i = 0; i < s_RaycastBuffer.Count; i++)
		{
			GameObject hit = s_RaycastBuffer[i].gameObject;
			if (hit == null)
				continue;

			Transform hitT = hit.transform;
			if (hitT == panelRoot || hitT.IsChildOf(panelRoot))
				return true;
		}

		return false;
	}

	private static bool IsAvailableCatalogDragActive()
	{
		MissionPrepModificationDragPayload payload = MissionPrepModificationDragContext.Current;
		if (!payload.HasItem)
			return false;

		return payload.SourceKind == MissionPrepModificationDragSourceKind.AvailableCatalog ||
		       payload.SourceKind == MissionPrepModificationDragSourceKind.AvailableWeapon ||
		       payload.SourceKind == MissionPrepModificationDragSourceKind.AvailableHelmet ||
		       payload.SourceKind == MissionPrepModificationDragSourceKind.AvailableBackpack;
	}

	private static bool ContainsScreenPoint(RectTransform _rect, Vector2 _screenPosition, Camera _eventCamera)
	{
		if (_rect == null || !_rect.gameObject.activeInHierarchy)
			return false;

		return RectTransformUtility.RectangleContainsScreenPoint(_rect, _screenPosition, _eventCamera);
	}

	private Camera ResolveEventCamera()
	{
		Canvas canvas = GetComponentInParent<Canvas>();
		if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
			return canvas.worldCamera;

		return null;
	}

	private static Vector2 ResolvePointerScreenPosition()
	{
		Mouse mouse = Mouse.current;
		if (mouse != null)
			return mouse.position.ReadValue();

		return new Vector2(-1f, -1f);
	}

	private void TryResolveReferences()
	{
		if (m_Coordinator == null)
			m_Coordinator = GetComponentInParent<MissionPrepLoadoutCoordinator>();
		if (m_Coordinator == null)
			m_Coordinator = FindAnyObjectByType<MissionPrepLoadoutCoordinator>();

		if (m_ButtonsRow == null)
		{
			Transform row = transform.Find("CategoryFilterRow");
			if (row == null)
				row = FindDeepChild(transform, "CategoryFilterRow");
			if (row != null)
				m_ButtonsRow = row as RectTransform;
		}

		if (m_ListZone == null)
		{
			Transform scroll = transform.Find("PrepAvailableEquipmentPanelScroll");
			if (scroll == null)
				scroll = FindDeepChild(transform, "PrepAvailableEquipmentPanelScroll");
			if (scroll == null)
				scroll = FindDeepChild(transform, "PrepAvailableScroll");
			if (scroll == null)
				scroll = FindDeepChild(transform, "Scroll View");
			if (scroll == null)
				scroll = FindDeepChild(transform, "AvailableEquipmentPanelScroll");
			if (scroll != null)
				m_ListZone = scroll as RectTransform;
		}

		if (m_HoldZone == null)
		{
			MissionPrepCollapsibleColumn column = GetComponent<MissionPrepCollapsibleColumn>();
			if (column != null && column.ContentRoot != null)
				m_HoldZone = column.ContentRoot;
			else
			{
				Transform content = transform.Find("ColumnContent");
				if (content == null)
					content = FindDeepChild(transform, "ColumnContent");
				if (content != null)
					m_HoldZone = content as RectTransform;
			}
		}
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root == null)
			return null;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform child = _root.GetChild(i);
			if (child != null && child.name == _name)
				return child;

			Transform nested = FindDeepChild(child, _name);
			if (nested != null)
				return nested;
		}

		return null;
	}
	#endregion
}

/// <summary>Pointer enter на кнопке категории — включает hover-preview (старт только с кнопок).</summary>
[DisallowMultipleComponent]
public sealed class MissionPrepAvailableEquipmentFilterButtonHook : MonoBehaviour, IPointerEnterHandler
{
	private MissionPrepAvailableEquipmentCategoryFilter m_Filter;
	private MissionPrepAvailableEquipmentFilterCategory m_Category;

	public void Bind(
		MissionPrepAvailableEquipmentCategoryFilter _filter,
		MissionPrepAvailableEquipmentFilterCategory _category)
	{
		m_Filter = _filter;
		m_Category = _category;
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		m_Filter?.NotifyCategoryButtonPointerEnter(m_Category);
	}
}
