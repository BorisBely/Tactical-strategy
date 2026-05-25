public struct RuntimeInventoryModificationUiState
{
	public bool HasSelection;
	public bool ExpandEmptySlots;
	public bool IsMainHand;
	public int BagIndex;
	public bool IsGroundSlot;
	public int GroundSlotIndex;

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
		bool _expandEmptySlots)
	{
		return new RuntimeInventoryModificationUiState
		{
			HasSelection = true,
			ExpandEmptySlots = _expandEmptySlots,
			IsMainHand = _isMainHand,
			BagIndex = _bagIndex,
			IsGroundSlot = false,
			GroundSlotIndex = -1
		};
	}

	public static RuntimeInventoryModificationUiState CreateGroundSelection(int _groundSlotIndex, bool _expandEmptySlots)
	{
		return new RuntimeInventoryModificationUiState
		{
			HasSelection = true,
			ExpandEmptySlots = _expandEmptySlots,
			IsMainHand = false,
			BagIndex = -1,
			IsGroundSlot = true,
			GroundSlotIndex = _groundSlotIndex
		};
	}
}
