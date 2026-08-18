using UnityEngine;

/// <summary>Совместимость: цвета теперь в <see cref="InventoryUiTheme"/>.</summary>
public static class MissionPrepInventoryUiColors
{
	public static Color CellBackground => InventoryUiTheme.CellBackground;
	public static Color CompatibleHighlight => InventoryUiTheme.CompatibleHighlight;
}
