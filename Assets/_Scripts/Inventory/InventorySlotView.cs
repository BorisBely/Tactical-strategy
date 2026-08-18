using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Одна ячейка инвентаря: имя в TMP, иконка слева (bake Sprite или runtime-снимок оружия с модами).
/// </summary>
[DisallowMultipleComponent]
public class InventorySlotView : MonoBehaviour
{
	#region Constants
	private const string c_IconChildName = "Icon";
	#endregion

	#region Serialized Fields
	[SerializeField] private TMP_Text m_NameText;
	[Tooltip("Дочерний Image под иконку (Sprite); если пусто — ищется child Icon.")]
	[SerializeField] private Image m_IconImage;
	[SerializeField] private GameObject m_OccupiedRoot;
	[SerializeField] private GameObject m_EmptyRoot;
	[Tooltip("Ключ локализации подписи пустого слота экипировки (например inventory.equip_slot.empty.weapon).")]
	[SerializeField] private string m_EmptyLocalizationKey;
	[Tooltip("Каталог: bake ItemDefinition.Icon, если есть; иначе runtime-студия.")]
	[SerializeField] private bool m_UseDefinitionIconOnly;
	#endregion

	#region Private Fields
	private InventorySlotRuntimeData m_Data;
	private bool m_HasItem;
	private bool m_RuntimeSpawned;
	private int m_CachedIconHash;
	private Sprite m_CachedIconSprite;
	private bool m_HasCachedIcon;
	#endregion

	#region Public Properties
	public bool HasItem => m_HasItem;
	public InventorySlotRuntimeData Data => m_Data;
	/// <summary>Ячейка создана из префаба в рантайме (учёт при Clear / переносе).</summary>
	public bool IsRuntimeSpawned => m_RuntimeSpawned;
	public string EmptyLocalizationKey => m_EmptyLocalizationKey;
	public bool UseDefinitionIconOnly => m_UseDefinitionIconOnly;
	/// <summary>Пустой слот экипировки с заголовком (оружие / шлем / рюкзак).</summary>
	public bool IsEmptyEquipmentSlot => !m_HasItem && !string.IsNullOrWhiteSpace(m_EmptyLocalizationKey);
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		if (m_NameText == null)
			m_NameText = GetComponentInChildren<TMP_Text>(true);
		if (m_IconImage == null)
			m_IconImage = FindChildIconImage();
	}

	private void Awake()
	{
		if (m_IconImage == null)
			m_IconImage = FindChildIconImage();

		ApplyRowLayout(IsEmptyEquipmentSlot);
		InventorySlotUiUtility.EnsureDescriptionHover(this);
	}

	/// <summary>Обычная ячейка 48 / компактная пустая экипировка 32 как место в машине.</summary>
	private void ApplyRowLayout(bool _compactEmpty)
	{
		float height = _compactEmpty ? InventoryUiTheme.CompactEmptyRowHeight : InventoryUiTheme.CellHeight;
		float iconSize = _compactEmpty ? InventoryUiTheme.CompactEmptyIconSize : InventoryUiTheme.IconSize;
		float fontSize = _compactEmpty ? InventoryUiTheme.CompactEmptyFontSize : InventoryUiTheme.CellFontSize;

		RectTransform rt = transform as RectTransform;
		if (rt != null)
		{
			Vector2 size = rt.sizeDelta;
			if (Mathf.Abs(size.y - height) > 0.1f)
			{
				size.y = height;
				rt.sizeDelta = size;
			}
		}

		LayoutElement layout = GetComponent<LayoutElement>();
		if (layout == null)
			layout = gameObject.AddComponent<LayoutElement>();
		layout.minHeight = height;
		layout.preferredHeight = height;
		layout.flexibleHeight = 0f;

		if (m_IconImage != null)
		{
			RectTransform iconRt = m_IconImage.rectTransform;
			iconRt.anchorMin = new Vector2(0f, 0.5f);
			iconRt.anchorMax = new Vector2(0f, 0.5f);
			iconRt.pivot = new Vector2(0.5f, 0.5f);
			float iconX = _compactEmpty
				? InventoryUiTheme.CompactEmptyLeftIndent + iconSize * 0.5f
				: iconSize * 0.5f + 4f;
			iconRt.anchoredPosition = new Vector2(iconX, 0f);
			iconRt.sizeDelta = new Vector2(iconSize, iconSize);
		}

		if (m_NameText != null)
		{
			m_NameText.fontSize = fontSize;
			m_NameText.color = InventoryUiTheme.PrimaryText;

			RectTransform textRt = m_NameText.rectTransform;
			textRt.anchorMin = Vector2.zero;
			textRt.anchorMax = Vector2.one;
			textRt.pivot = new Vector2(0.5f, 0.5f);
			if (_compactEmpty)
			{
				float leftInset = InventoryUiTheme.CompactEmptyLeftIndent + iconSize + 6f;
				textRt.anchoredPosition = new Vector2((leftInset - 6f) * 0.5f, 0f);
				textRt.sizeDelta = new Vector2(-(leftInset + 6f), -4f);
				m_NameText.textWrappingMode = TextWrappingModes.NoWrap;
			}
			else
			{
				textRt.anchoredPosition = new Vector2(22f, 0f);
				textRt.sizeDelta = new Vector2(-60f, -6f);
				m_NameText.textWrappingMode = TextWrappingModes.Normal;
			}

			m_NameText.alignment = TextAlignmentOptions.MidlineLeft;
			m_NameText.overflowMode = TextOverflowModes.Ellipsis;
		}

		Transform divider = transform.Find("Image");
		if (divider != null && divider.TryGetComponent(out Image dividerImage) && divider != m_IconImage?.transform)
		{
			divider.gameObject.SetActive(!_compactEmpty);
			RectTransform divRt = divider as RectTransform;
			if (divRt != null)
			{
				divRt.anchorMin = new Vector2(0f, 0f);
				divRt.anchorMax = new Vector2(1f, 0f);
				divRt.pivot = new Vector2(0.5f, 0.5f);
				divRt.anchoredPosition = new Vector2(0f, 0.5f);
				divRt.sizeDelta = new Vector2(0f, InventoryUiTheme.DividerHeight);
			}

			dividerImage.color = InventoryUiTheme.Divider;
			dividerImage.raycastTarget = false;
		}

		if (rt != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		if (!m_HasItem)
			RefreshVisuals();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (Application.isPlaying || m_HasItem || m_NameText == null)
			return;

		if (string.IsNullOrWhiteSpace(m_EmptyLocalizationKey))
			return;

		if (!LocalizationManager.HasInstance)
			return;

		m_NameText.text = LocalizationManager.Get(m_EmptyLocalizationKey, m_NameText.text);
	}
#endif
	#endregion

	#region Public Methods
	public void MarkRuntimeSpawned()
	{
		m_RuntimeSpawned = true;
	}

	/// <summary>Ключ подписи пустого слота экипировки; пустая строка — без подписи.</summary>
	public void SetEmptyLocalizationKey(string _localizationKey)
	{
		m_EmptyLocalizationKey = _localizationKey ?? string.Empty;
		if (!m_HasItem)
			RefreshVisuals();
	}

	/// <summary>
	/// Каталог доступного снаряжения: брать bake <see cref="ItemDefinition.Icon"/>, если он есть.
	/// Если bake нет (почти все стволы) — runtime-студия, иначе ячейка будет пустой.
	/// </summary>
	public void SetUseDefinitionIconOnly(bool _enabled)
	{
		m_UseDefinitionIconOnly = _enabled;
		InvalidateIconCache();
		if (m_HasItem)
			RefreshVisuals();
	}

	public void SetItem(InventorySlotRuntimeData _data)
	{
		m_Data = _data;
		m_HasItem = !_data.IsEmpty;
		if (!m_HasItem)
			InvalidateIconCache();

		RefreshVisuals();
	}

	public void Clear()
	{
		m_Data = default;
		m_HasItem = false;
		InvalidateIconCache();
		RefreshVisuals();
	}

	public bool TryTakeItem(out InventorySlotRuntimeData _data)
	{
		if (!m_HasItem)
		{
			_data = default;
			return false;
		}

		_data = m_Data;
		Clear();
		return true;
	}

	/// <summary>Отключить raycast у дочерних Image (кроме фона), чтобы drop/popup попадали по всей ячейке.</summary>
	public void SetChildImagesRaycastTarget(bool _enabled, Image _exceptImage = null)
	{
		Image[] images = GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			Image image = images[i];
			if (image == null || image == _exceptImage)
				continue;

			image.raycastTarget = _enabled;
		}
	}
	#endregion

	#region Private Methods
	/// <summary>Ищет child с именем Icon; не берёт фон ячейки и декоративную полоску.</summary>
	private Image FindChildIconImage()
	{
		Transform iconTransform = transform.Find(c_IconChildName);
		if (iconTransform != null && iconTransform.TryGetComponent(out Image namedIcon))
			return namedIcon;

		foreach (Image image in GetComponentsInChildren<Image>(true))
		{
			if (image == null || image.gameObject == gameObject)
				continue;
			if (image.gameObject.name == c_IconChildName)
				return image;
		}

		return null;
	}

	private void RefreshVisuals()
	{
		if (m_IconImage == null)
			m_IconImage = FindChildIconImage();

		bool compactEmpty = IsEmptyEquipmentSlot;
		ApplyRowLayout(compactEmpty);

		if (m_NameText != null)
			m_NameText.text = m_HasItem ? FormatLabel(m_Data) : FormatEmptyLabel();

		if (m_OccupiedRoot != null)
			m_OccupiedRoot.SetActive(m_HasItem);
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(!m_HasItem);

		Sprite iconSprite = ResolveIconSprite();
		if (m_IconImage != null)
		{
			m_IconImage.sprite = iconSprite;
			bool showIcon = m_HasItem && iconSprite != null;
			m_IconImage.enabled = showIcon;
			m_IconImage.gameObject.SetActive(showIcon);
		}

		if (compactEmpty)
			InventorySlotUiUtility.ApplyEmptyEquipmentSlotBackground(this);
		else if (!string.IsNullOrWhiteSpace(m_EmptyLocalizationKey))
			InventorySlotUiUtility.ResolveEquipmentSlotAppearance(this).ApplyNormal(this);
	}

	private Sprite ResolveIconSprite()
	{
		if (!m_HasItem || m_Data.Definition == null)
			return null;

		Sprite baked = m_Data.Definition.Icon;
		if (m_UseDefinitionIconOnly && baked != null)
			return baked;

		if (!InventoryItemIconStudio.ShouldUseRuntimeIcon(m_Data))
			return baked;

		int hash = InventoryItemIconStudio.ComputeBuildHash(m_Data);
		if (m_HasCachedIcon && m_CachedIconHash == hash && m_CachedIconSprite != null)
			return m_CachedIconSprite;

		InventoryItemIconStudio studio = InventoryItemIconStudio.Instance;
		if (studio == null)
			return m_Data.Definition.Icon;

		Sprite runtimeIcon = studio.GetOrRender(m_Data);
		m_CachedIconHash = hash;
		m_CachedIconSprite = runtimeIcon != null ? runtimeIcon : m_Data.Definition.Icon;
		m_HasCachedIcon = true;
		return m_CachedIconSprite;
	}

	private void InvalidateIconCache()
	{
		m_HasCachedIcon = false;
		m_CachedIconHash = 0;
		m_CachedIconSprite = null;
	}

	private string FormatEmptyLabel()
	{
		if (string.IsNullOrWhiteSpace(m_EmptyLocalizationKey))
			return string.Empty;

		return LocalizationManager.Get(m_EmptyLocalizationKey, m_EmptyLocalizationKey);
	}

	private static string FormatLabel(InventorySlotRuntimeData _data)
	{
		string baseLabel;
		if (!string.IsNullOrWhiteSpace(_data.LocalizationKey))
			baseLabel = LocalizationManager.Get(_data.LocalizationKey, _data.DisplayName);
		else if (_data.Definition != null)
			baseLabel = _data.Definition.GetLocalizedDisplayName();
		else
			baseLabel = _data.DisplayName;

		if (_data.InstanceState != null)
		{
			MagazineRuntimeState magazineState = _data.InstanceState.MagazineState;
			if (magazineState != null && magazineState.Definition != null)
				return $"{baseLabel} [{magazineState.CurrentAmmoCount}/{magazineState.Definition.Capacity}]";

			AmmoContainerRuntimeState ammoContainerState = _data.InstanceState.AmmoContainerState;
			if (ammoContainerState != null && ammoContainerState.AmmoDefinition != null)
				return $"{baseLabel} x{ammoContainerState.CurrentAmmoCount}";

			MedkitRuntimeState medkitState = _data.InstanceState.MedkitState;
			if (medkitState != null && medkitState.Definition != null)
				return $"{baseLabel} [{medkitState.CurrentResourcePoints}/{medkitState.MaxResourcePoints}]";
		}

		return baseLabel;
	}

	private void HandleLanguageChanged()
	{
		if (!isActiveAndEnabled)
			return;

		RefreshVisuals();
	}
	#endregion
}
