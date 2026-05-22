using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// TMP_Dropdown для пресетов: открывается только по ЛКМ.
/// </summary>
public class MissionPrepPresetDropdown : TMP_Dropdown
{
	#region Public Methods
	public override void OnPointerClick(PointerEventData _eventData)
	{
		if (_eventData.button != PointerEventData.InputButton.Left)
			return;

		base.OnPointerClick(_eventData);
	}
	#endregion
}
