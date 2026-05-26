public struct RuntimeInventoryModificationUiState
{
	public bool HasSelection;
	public RuntimeModifiableWeaponDisplayState DisplayState;
	public ItemInstanceState SelectedWeaponInstanceState;
	public bool IsMainHand;
	public int BagIndex;
	public bool IsGroundSlot;
	public int GroundSlotIndex;

	public bool IsExpanded => DisplayState == RuntimeModifiableWeaponDisplayState.Expanded;

	public bool MatchesCharacter(bool _isMainHand, int _bagIndex)
	{
		return HasSelection && !IsGroundSlot && IsMainHand == _isMainHand && BagIndex == _bagIndex;
	}

	public bool MatchesGround(int _groundSlotIndex)
	{
		return HasSelection && IsGroundSlot && GroundSlotIndex == _groundSlotIndex;
	}

	public static RuntimeInventoryModificationUiState CreateCharacterSelection(
		bool _isMainHand,
		int _bagIndex,
		ItemInstanceState _weaponInstanceState,
		RuntimeModifiableWeaponDisplayState _displayState)
	{
		return new RuntimeInventoryModificationUiState
		{
			HasSelection = true,
			DisplayState = _displayState,
			SelectedWeaponInstanceState = _weaponInstanceState,
			IsMainHand = _isMainHand,
			BagIndex = _bagIndex,
			IsGroundSlot = false,
			GroundSlotIndex = -1
		};
	}

	public static RuntimeInventoryModificationUiState CreateGroundSelection(
		int _groundSlotIndex,
		ItemInstanceState _weaponInstanceState,
		RuntimeModifiableWeaponDisplayState _displayState)
	{
		return new RuntimeInventoryModificationUiState
		{
			HasSelection = true,
			DisplayState = _displayState,
			SelectedWeaponInstanceState = _weaponInstanceState,
			IsMainHand = false,
			BagIndex = -1,
			IsGroundSlot = true,
			GroundSlotIndex = _groundSlotIndex
		};
	}
}
