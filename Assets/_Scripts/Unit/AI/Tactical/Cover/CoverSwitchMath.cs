using UnityEngine;

/// <summary>
/// #13.3 / #13.5 switching gate. Does not Move. Does not change G5 / #12.
/// First-pass cost: distance + exposure. Extra factors later.
/// </summary>
public static class CoverSwitchMath
{
	#region Constants
	public const float DefaultSwitchingCost = 0.45f;
	#endregion

	#region Public Methods
	public static bool ShouldReposition(float _currentScore, float _candidateScore, float _switchingCost)
	{
		return _candidateScore > _currentScore + Mathf.Max(0f, _switchingCost);
	}

	public static float ComputeSwitchingCost(
		CoverCandidate _from,
		CoverCandidate _to,
		in CoverSituation _situation)
	{
		if (_to == null)
			return 0f;

		Vector3 fromPos = _from != null ? _from.Position : _situation.UnitPosition;
		float meters = Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(fromPos, _to.Position));
		float distance = Mathf.Min(3f, meters / 5f);
		float exposure = CoverScoreMath.ExposureScore(_to, in _situation);
		return distance + exposure;
	}
	#endregion
}
