using UnityEngine;

/// <summary>
/// Vision Stage 15: Detail scan ordering. No raycast. Score is intra- and inter-observer only.
/// RecentlyLost raises scan order; it does not extend memory 5/30.
/// </summary>
public static class VisionDetailPriorityMath
{
	#region Constants
	public const float CurrentTargetBonus = 4f;
	public const float RecentlyLostBonus = 3f;
	public const float ForcedScanBonus = 8f;
	public const float StarveWeight = 1.5f;
	public const int FairnessMaxConsecutiveSkip = 8;
	#endregion

	#region Public Methods
	public static float Score(
		float _attentionMultiplier,
		bool _currentTarget,
		bool _recentlyLost,
		bool _forcedScan,
		int _starveFrames)
	{
		float score = AttentionMath.ClampMultiplier(_attentionMultiplier);
		if (_currentTarget)
			score += CurrentTargetBonus;
		if (_recentlyLost)
			score += RecentlyLostBonus;
		if (_forcedScan)
			score += ForcedScanBonus;
		score += Mathf.Max(0, _starveFrames) * StarveWeight;
		return score;
	}

	public static int CompareIntraObserver(
		float _angleDegreesA,
		bool _selectedA,
		bool _recentlyLostA,
		float _angleDegreesB,
		bool _selectedB,
		bool _recentlyLostB)
	{
		float scoreA = Score(
			AttentionMath.EvaluateMultiplier(_angleDegreesA),
			_selectedA,
			_recentlyLostA,
			false,
			0);
		float scoreB = Score(
			AttentionMath.EvaluateMultiplier(_angleDegreesB),
			_selectedB,
			_recentlyLostB,
			false,
			0);
		int cmp = scoreB.CompareTo(scoreA);
		if (cmp != 0)
			return cmp;
		return Mathf.Abs(_angleDegreesA).CompareTo(Mathf.Abs(_angleDegreesB));
	}
	#endregion
}
