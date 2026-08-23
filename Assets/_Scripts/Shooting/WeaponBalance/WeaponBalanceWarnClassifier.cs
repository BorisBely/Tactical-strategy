using UnityEngine;

/// <summary>Phase H9 — classify WARN as diagnostic or balance review.</summary>
public static class WeaponBalanceWarnClassifier
{
	#region Constants
	private const float c_AttachmentDeltaMinDegrees = 0.01f;
	private const float c_PeerSpreadRatio = 1.35f;
	#endregion

	#region Public Methods
	public static WeaponBalanceWarnKind Classify(
		in WeaponBalanceCase _case,
		in RecoilSampleResult _recoil,
		string _outlierReason,
		float _peerMedianOffsetMag5)
	{
		if (string.IsNullOrEmpty(_outlierReason) &&
		    _case.FireMode == WeaponFireMode.FullAuto)
			return WeaponBalanceWarnKind.None;

		if (_case.FireMode == WeaponFireMode.SemiAuto)
			return WeaponBalanceWarnKind.Diagnostic;

		if (_case.Pose.IsHipFireHold())
			return WeaponBalanceWarnKind.Diagnostic;

		if (_case.Movement != WeaponBalanceMovement.Idle)
			return WeaponBalanceWarnKind.Diagnostic;

		if (!string.IsNullOrEmpty(_outlierReason) &&
		    (_outlierReason.Contains("below perception") ||
		     _outlierReason.Contains("NetDrift")))
			return WeaponBalanceWarnKind.Diagnostic;

		if (_case.WeaponClass == WeaponClassType.SniperRifle &&
		    _case.FireMode == WeaponFireMode.SemiAuto)
			return WeaponBalanceWarnKind.Diagnostic;

		if (_peerMedianOffsetMag5 > 1e-4f &&
		    _recoil.OffsetMagAfter5 > _peerMedianOffsetMag5 * c_PeerSpreadRatio)
			return WeaponBalanceWarnKind.Balance;

		if (_case.LoadoutLabel != WeaponBalanceComparableKey.CanonicalLoadoutLabel &&
		    Mathf.Abs(_recoil.OffsetMagAfter5) < c_AttachmentDeltaMinDegrees)
			return WeaponBalanceWarnKind.Balance;

		if (!string.IsNullOrEmpty(_outlierReason) &&
		    (_outlierReason.Contains("unexpectedly high") ||
		     _outlierReason.Contains("unexpectedly low") ||
		     _outlierReason.Contains("Recovery outside")))
			return WeaponBalanceWarnKind.Balance;

		return WeaponBalanceWarnKind.Diagnostic;
	}
	#endregion
}
