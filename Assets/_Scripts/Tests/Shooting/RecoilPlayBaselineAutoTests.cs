using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase A SIM_PLAY: self-contained weapon bursts. Does not hang components on a unit.
/// Does not retune recoil fields.
/// </summary>
public sealed class RecoilPlayBaselineAutoTests
{
	#region Tests
	[Test]
	public void Auto_M4_A1FiveShot_GroupNearMeanHit()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		Assert.IsNotNull(m4, RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		RecoilPlayBaselineMath.CaseMath math = RecoilPlayBaselineMath.EvaluateCase(
			m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);
		RecoilPlayBaselineAutoRunner.RunResult auto = RecoilPlayBaselineAutoRunner.Run(m4, null, null);
		Assert.Greater(auto.A1FiveShotGroupCm, 0f);
		RecoilPlayBaselineProtocol.Verdict vsPlay = RecoilPlayBaselineMath.EvaluateMathVsPlay(
			math.After5.MeanHitCm,
			auto.A1FiveShotGroupCm,
			out string note);
		Assert.AreNotEqual(RecoilPlayBaselineProtocol.Verdict.PlayPending, vsPlay, note);
		Assert.AreNotEqual(RecoilPlayBaselineProtocol.Verdict.Fail, vsPlay, note);
	}

	[Test]
	public void Auto_M4_Form_WalkHipCrouchPause()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		Assert.IsNotNull(m4);
		RecoilPlayBaselineAutoRunner.RunResult auto = RecoilPlayBaselineAutoRunner.Run(m4, null, null);
		Assert.Less(auto.A1Shot1.RecoilOffsetDeg, RecoilPlayBaselineProtocol.Shot1OffsetWarnDegrees);
		Assert.Less(auto.A1Shot1.CenterAbsCm, RecoilPlayBaselineProtocol.Ring100Cm * 100f);
		Assert.Greater(auto.A2Shot5.CenterAbsCm, auto.A1Shot5.CenterAbsCm);
		Assert.Less(auto.A4Shot5.CenterAbsCm, auto.A1Shot5.CenterAbsCm + 0.5f);
		Assert.AreEqual(RecoilPlayBaselineProtocol.A5BurstShots, auto.A5Shot4.RecoilShotIndexAtLastShot);
		Assert.Less(auto.A5Shot4.RecoilOffsetDeg, 0.05f);
	}

	[Test]
	public void Auto_N8_M4AndLmgs_RemainingWithinGate()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		Assert.IsNotNull(m4);
		RecoilPlayBaselineAutoRunner.RunResult auto = RecoilPlayBaselineAutoRunner.Run(m4, m249, pkm);
		Assert.IsTrue(auto.M4Gate.WouldFireMuzzleOnTarget);
		Assert.LessOrEqual(auto.M4Gate.RemainingDeg, RecoilPlayBaselineProtocol.BarrelGateIdleDegrees);
		if (m249 != null)
			Assert.LessOrEqual(auto.M249Gate.RemainingDeg, RecoilPlayBaselineProtocol.BarrelGateIdleDegrees);
		if (pkm != null)
			Assert.LessOrEqual(auto.PkmGate.RemainingDeg, RecoilPlayBaselineProtocol.BarrelGateIdleDegrees);
		Assert.IsTrue(auto.N8Section.Contains(RecoilPlayBaselineProtocol.SimPlayLabel));
	}

	[Test]
	public void Auto_A5_RecoilShotIndexNotReset()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		Assert.IsNotNull(m4);
		RecoilPlayBaselineSimulator.BurstResult burst = RecoilPlayBaselineSimulator.SimulateBurst(
			m4,
			RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50,
			4,
			11,
			101);
		Assert.AreEqual(RecoilPlayBaselineProtocol.A5BurstShots, burst.RecoilShotIndexAtLastShot);
		Assert.AreNotEqual(0, burst.RecoilShotIndexAtLastShot);
		Assert.AreNotEqual(1, burst.RecoilShotIndexAtLastShot);
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
