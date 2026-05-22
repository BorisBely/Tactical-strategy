using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Проброс кликов по UI-элементу пресета (шапка дропдауна или строка списка).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepPresetPointerTarget : MonoBehaviour, IPointerClickHandler
{
	#region Events
	public event Action<PointerEventData> Clicked;
	#endregion

	#region Public Methods
	public void OnPointerClick(PointerEventData _eventData)
	{
		Clicked?.Invoke(_eventData);
	}
	#endregion
}
