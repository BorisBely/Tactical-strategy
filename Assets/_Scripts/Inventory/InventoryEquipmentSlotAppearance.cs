using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Фон ячейки экипированного оружия: обычное состояние и подсветка при переносе оружия.
/// Задаётся на <see cref="InventoryPanelView"/> (инспектор панели пресета / runtime).
/// </summary>
[Serializable]
public sealed class InventoryEquipmentSlotAppearance
{
	#region Serialized Fields
	[SerializeField] private Color m_NormalBackgroundColor = new Color(0.16470589f, 0.16470589f, 0.16470589f, 1f);
	[SerializeField] private Sprite m_NormalBackgroundSprite;
	[SerializeField] private Color m_HighlightBackgroundColor = new Color(0.29f, 0.42f, 0.33f, 0.50f);
	[SerializeField] private Sprite m_HighlightBackgroundSprite;
	#endregion

	#region Public Properties
	public Color NormalBackgroundColor => m_NormalBackgroundColor;
	public Sprite NormalBackgroundSprite => m_NormalBackgroundSprite;
	public Color HighlightBackgroundColor => m_HighlightBackgroundColor;
	public Sprite HighlightBackgroundSprite => m_HighlightBackgroundSprite;
	#endregion

	#region Public Methods
	public void ApplyNormal(InventorySlotView _slot)
	{
		if (_slot == null || !InventorySlotUiUtility.TryGetSlotBackgroundImage(_slot, out Image background))
			return;

		ApplyToImage(background, m_NormalBackgroundColor, m_NormalBackgroundSprite);
	}

	public void ApplyHighlight(InventorySlotView _slot)
	{
		if (_slot == null || !InventorySlotUiUtility.TryGetSlotBackgroundImage(_slot, out Image background))
			return;

		ApplyToImage(background, m_HighlightBackgroundColor, m_HighlightBackgroundSprite);
	}

	public void ApplyNormal(Image _background)
	{
		ApplyToImage(_background, m_NormalBackgroundColor, m_NormalBackgroundSprite);
	}

	public void ApplyHighlight(Image _background)
	{
		ApplyToImage(_background, m_HighlightBackgroundColor, m_HighlightBackgroundSprite);
	}
	#endregion

	#region Private Methods
	private static void ApplyToImage(Image _background, Color _color, Sprite _sprite)
	{
		if (_background == null)
			return;

		Color color = _color;
		if (color.a < 0.05f)
			color.a = 1f;

		if (_sprite != null)
			_background.sprite = _sprite;

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_background);
		_background.color = color;
		_background.enabled = true;
	}
	#endregion
}
