using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase A MATH checks for M4 ModA1. Does not retune recoil fields.
/// </summary>
public sealed class RecoilPlayBaselineMathTests
{
	#region Tests
	[Test]
	public void Protocol_Median3_PicksMiddle()
	{
		Assert.AreEqual(5f, RecoilPlayBaselineProtocol.Median3(9f, 1f, 5f), 0.0001f);
	}

	[Test]
	public void M4_FiveShotOffset_MatchesRecoilContractOrientation()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		Assert.IsNotNull(m4, RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		RecoilPlayBaselineMath.CaseMath a1 = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);
		Assert.AreEqual(0f, a1.After1.MeanHitOffsetDeg.magnitude, 0.0001f);
		Assert.That(a1.After5.OffsetAfterShotsDeg.magnitude, Is.EqualTo(0.313f).Within(0.02f));
		Assert.That(a1.After5.OffsetAfterCm, Is.EqualTo(27.3f).Within(3f));
		Assert.AreEqual(RecoilPlayBaselineProtocol.Verdict.Pass, RecoilPlayBaselineMath.EvaluateA1Form(in a1));
	}

	[Test]
	public void M4_WalkHipCrouchPause_HaveExpectedShape()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		Assert.IsNotNull(m4);
		RecoilPlayBaselineMath.CaseMath a1 = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);
		RecoilPlayBaselineMath.CaseMath a2 = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50);
		RecoilPlayBaselineMath.CaseMath a3 = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15);
		RecoilPlayBaselineMath.CaseMath a4 = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50);
		Assert.AreEqual(RecoilPlayBaselineProtocol.Verdict.Pass, RecoilPlayBaselineMath.EvaluateA2Form(in a1, in a2));
		Assert.AreNotEqual(RecoilPlayBaselineProtocol.Verdict.Fail, RecoilPlayBaselineMath.EvaluateA3Form(in a1, in a3));
		Assert.AreEqual(RecoilPlayBaselineProtocol.Verdict.Pass, RecoilPlayBaselineMath.EvaluateA4Form(in a1, in a4));
		Assert.AreEqual(RecoilPlayBaselineProtocol.Verdict.Pass, RecoilPlayBaselineMath.EvaluateA5Form(in a1));
		Assert.LessOrEqual(a1.OffsetAfter3Pause04Deg.magnitude, a1.After3.OffsetAfterShotsDeg.magnitude + 0.001f);
	}

	[Test]
	public void MathVsPlay_Pending_WhenNoPlayMedian()
	{
		RecoilPlayBaselineProtocol.Verdict verdict =
			RecoilPlayBaselineMath.EvaluateMathVsPlay(27.3f, -1f, out string note);
		Assert.AreEqual(RecoilPlayBaselineProtocol.Verdict.PlayPending, verdict);
		Assert.AreEqual("PLAY_PENDING", note);
	}
	#endregion

	#region Private Methods
	private static WeaponDefinition LoadWeapon(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName);
		for (int i = 0; i < guids.Length; i++)
		{
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
				AssetDatabase.GUIDToAssetPath(guids[i]));
			if (weapon != null && weapon.name == _assetName)
				return weapon;
		}

		return null;
	}
	#endregion
}
