public struct MissionPrepModificationUiState
{
	public bool HasSelection;
	public RuntimeModifiableWeaponDisplayState DisplayState;
	public ItemInstanceState SelectedWeaponInstanceState;
	public bool IsMainHand;
	public int BagIndex;

	public bool IsExpanded => DisplayState == RuntimeModifiableWeaponDisplayState.Expanded;

	public bool Matches(bool _isMainHand, int _bagIndex)
	{
		return HasSelection && IsMainHand == _isMainHand && BagIndex == _bagIndex;
	}

	public static MissionPrepModificationUiState CreateSelection(
		bool _isMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState,
		RuntimeModifiableWeaponDisplayState _displayState)
	{
		return new MissionPrepModificationUiState
		{
			HasSelection = true,
			DisplayState = _displayState,
			SelectedWeaponInstanceState = _weaponInstanceState,
			IsMainHand = _isMainHand,
			BagIndex = _bagIndex
		};
	}
}
