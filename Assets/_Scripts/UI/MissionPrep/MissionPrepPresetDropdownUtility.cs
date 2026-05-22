using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Замена стандартного TMP_Dropdown на <see cref="MissionPrepPresetDropdown"/> с сохранением настроек.
/// </summary>
public static class MissionPrepPresetDropdownUtility
{
	#region Private Types
	private struct DropdownState
	{
		public RectTransform Template;
		public TMP_Text CaptionText;
		public Image CaptionImage;
		public TMP_Text ItemText;
		public Image ItemImage;
		public bool Interactable;
		public int Value;
		public List<TMP_Dropdown.OptionData> Options;
	}
	#endregion

	#region Public Methods
	public static MissionPrepPresetDropdown EnsureOn(GameObject _host, ref TMP_Dropdown _dropdownReference)
	{
		if (_host == null)
			return _dropdownReference as MissionPrepPresetDropdown;

		if (_dropdownReference is MissionPrepPresetDropdown presetDropdown)
			return presetDropdown;

		TMP_Dropdown source = _dropdownReference != null
			? _dropdownReference
			: _host.GetComponent<TMP_Dropdown>();

		if (source == null)
			return null;

		if (source is MissionPrepPresetDropdown existing)
		{
			_dropdownReference = existing;
			return existing;
		}

		DropdownState state = CaptureState(source);
		DestroyDropdownComponent(source);

		MissionPrepPresetDropdown replacement = _host.AddComponent<MissionPrepPresetDropdown>();
		ApplyState(replacement, state);

		_dropdownReference = replacement;
		return replacement;
	}
	#endregion

	#region Private Methods
	private static DropdownState CaptureState(TMP_Dropdown _from)
	{
		return new DropdownState
		{
			Template = _from.template,
			CaptionText = _from.captionText,
			CaptionImage = _from.captionImage,
			ItemText = _from.itemText,
			ItemImage = _from.itemImage,
			Interactable = _from.interactable,
			Value = _from.value,
			Options = new List<TMP_Dropdown.OptionData>(_from.options)
		};
	}

	private static void ApplyState(TMP_Dropdown _to, DropdownState _state)
	{
		if (_to == null)
			return;

		_to.template = _state.Template;
		_to.captionText = _state.CaptionText;
		_to.captionImage = _state.CaptionImage;
		_to.itemText = _state.ItemText;
		_to.itemImage = _state.ItemImage;
		_to.interactable = _state.Interactable;
		_to.options.Clear();
		_to.options.AddRange(_state.Options);
		_to.SetValueWithoutNotify(_state.Value);
		_to.RefreshShownValue();
	}

	private static void DestroyDropdownComponent(TMP_Dropdown _component)
	{
		if (_component == null)
			return;

		Object.DestroyImmediate(_component);
	}
	#endregion
}
