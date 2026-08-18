/// <summary>
/// Per-weapon visual recoil multipliers for WeaponDefinition.VisualRecoilKickScale.
/// Relative to a 5.56 carbine baseline of 1.2 with UnitWeaponRecoil.ShotPitch ≈ 2.5.
/// </summary>
internal static class WeaponVisualRecoilKickScaleTable
{
	public static float ForAsset(string _weaponAssetName)
	{
		switch (_weaponAssetName)
		{
			case "Weapon_M4_ModA_1":
			case "Weapon_M16A_ModA_1":
				return 1.20f;
			case "Weapon_M4_ModA_2":
				return 1.15f;
			case "Weapon_M16A4_ModA_2":
				return 1.10f;
			case "Weapon_MK18":
				return 1.45f;
			case "Weapon_MK12":
				return 1.55f;
			case "Weapon_AK74":
				return 1.25f;
			case "Weapon_AK74MOD1":
				return 1.15f;
			case "Weapon_AK74U":
				return 1.55f;
			case "Weapon_AK74UMOD1":
				return 1.45f;
			case "Weapon_AK47":
				return 1.55f;
			case "Weapon_AK47_1":
				return 1.45f;
			case "Weapon_AK47S":
				return 1.60f;
			case "Weapon_AK47MOD1":
				return 1.40f;
			case "Weapon_RPK47":
				return 1.25f;
			case "Weapon_RPK47MOD1":
				return 1.20f;
			case "Weapon_RPK74":
				return 1.10f;
			case "Weapon_RPK74MOD1":
				return 1.05f;
			case "Weapon_M249":
				return 1.05f;
			case "Weapon_PKM":
				return 1.40f;
			case "Weapon_SVD":
				return 2.10f;
			case "Weapon_Mosin":
				return 2.40f;
			case "Weapon_Sniper762x51":
				return 2.20f;
			case "Weapon_BenelliM4":
				return 1.45f;
			case "Weapon_MK19":
				return 1.50f;
			case "Weapon_M2Browning_127":
				return 1.35f;
			default:
				return 1.20f;
		}
	}
}
