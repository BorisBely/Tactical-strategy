public struct MissionPrepModificationUiState
{
	public bool HasSelection;
	public bool ExpandEmptySlots;
	public bool IsMainHand;
	public int BagIndex;

	public bool Matches(bool _isMainHand, int _bagIndex)
	{
		return HasSelection && IsMainHand == _isMainHand && BagIndex == _bagIndex;
	}

	public static MissionPrepModificationUiState CreateSelection(bool _isMainHand, int _bagIndex, bool _expandEmptySlots)
	{
		return new MissionPrepModificationUiState
		{
			HasSelection = true,
			ExpandEmptySlots = _expandEmptySlots,
			IsMainHand = _isMainHand,
			BagIndex = _bagIndex
		};
	}
}
