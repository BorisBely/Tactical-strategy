using UnityEngine;

/// <summary>
/// Phase G run configuration. Presets: Smoke / Reference / Attachments.
/// Does not mutate assets.
/// </summary>
[CreateAssetMenu(fileName = "WeaponBalanceRunConfig", menuName = "Polygone/Shooting/Weapon Balance Run Config", order = 40)]
public sealed class WeaponBalanceRunConfig : ScriptableObject
{
	#region Serialized Fields
	[Header("Weapons (empty = all combat weapons)")]
	[SerializeField] private WeaponDefinition[] m_Weapons;

	[Header("Distances (m)")]
	[SerializeField] private float[] m_RecoilDistancesMeters = { 50f, 100f };
	[SerializeField] private float[] m_AutoDistancesMeters = { 20f, 50f, 80f, 100f, 150f };
	[SerializeField] private float[] m_HipFireDistancesMeters = { 5f, 10f, 15f, 25f, 50f };

	[Header("Shots / pause")]
	[SerializeField] private int[] m_ShotCounts = { 1, 3, 5, 8, 10 };
	[SerializeField] private bool m_IncludeExtendedShotCounts;
	[SerializeField] private float[] m_PauseSeconds = { 0.2f, 0.4f, 0.8f };

	[Header("Passes")]
	[SerializeField] private bool m_EvaluateRecoil = true;
	[SerializeField] private bool m_EvaluateAuto = true;
	[SerializeField] private bool m_EvaluateDiscipline = true;
	[SerializeField] private bool m_EvaluateAccuracy = true;

	[Header("Includes")]
	[SerializeField] private bool m_IncludeAttachments;
	[SerializeField] private bool m_IncludeAmmo = true;
	[SerializeField] private bool m_IncludeSkills;
	[SerializeField] private bool m_IncludeCosmeticAttachments;

	[SerializeField] private bool m_IncludeStanding = true;
	[SerializeField] private bool m_IncludeCrouch = true;
	[SerializeField] private bool m_IncludeWalk = true;
	[SerializeField] private bool m_IncludeSprint;

	[SerializeField] private bool m_IncludeAiming = true;
	[SerializeField] private bool m_IncludePointAim;
	[SerializeField] private bool m_IncludePreAim;
	[SerializeField] private bool m_IncludeHipFire = true;

	[Header("Filters")]
	[SerializeField] private bool m_SkipProne = true;
	[SerializeField] private bool m_SkipInvalidPoseMove = true;
	[SerializeField] private bool m_AllowSprintWhileAiming;
	[SerializeField] private int m_MaxLoadoutsPerWeapon = 24;
	[SerializeField] private bool m_RunPlayGateOnOutliers = true;
	#endregion

	#region Public Properties
	public WeaponDefinition[] Weapons => m_Weapons;
	public float[] RecoilDistancesMeters => m_RecoilDistancesMeters;
	public float[] AutoDistancesMeters => m_AutoDistancesMeters;
	public float[] HipFireDistancesMeters => m_HipFireDistancesMeters;
	public int[] ShotCounts => ResolveShotCounts();
	public float[] PauseSeconds => m_PauseSeconds;
	public bool EvaluateRecoil => m_EvaluateRecoil;
	public bool EvaluateAuto => m_EvaluateAuto;
	public bool EvaluateDiscipline => m_EvaluateDiscipline;
	public bool EvaluateAccuracy => m_EvaluateAccuracy;
	public bool IncludeAttachments => m_IncludeAttachments;
	public bool IncludeAmmo => m_IncludeAmmo;
	public bool IncludeSkills => m_IncludeSkills;
	public bool IncludeCosmeticAttachments => m_IncludeCosmeticAttachments;
	public bool IncludeStanding => m_IncludeStanding;
	public bool IncludeCrouch => m_IncludeCrouch;
	public bool IncludeWalk => m_IncludeWalk;
	public bool IncludeSprint => m_IncludeSprint;
	public bool IncludeAiming => m_IncludeAiming;
	public bool IncludePointAim => m_IncludePointAim;
	public bool IncludePreAim => m_IncludePreAim;
	public bool IncludeHipFire => m_IncludeHipFire;
	public bool SkipProne => m_SkipProne;
	public bool SkipInvalidPoseMove => m_SkipInvalidPoseMove;
	public bool AllowSprintWhileAiming => m_AllowSprintWhileAiming;
	public int MaxLoadoutsPerWeapon => Mathf.Max(1, m_MaxLoadoutsPerWeapon);
	public bool RunPlayGateOnOutliers => m_RunPlayGateOnOutliers;
	#endregion

	#region Public Methods
	public static WeaponBalanceRunConfig CreateSmokePreset()
	{
		var config = CreateInstance<WeaponBalanceRunConfig>();
		config.name = "WeaponBalance_Smoke";
		config.m_Weapons = null;
		config.m_RecoilDistancesMeters = new[] { 50f, 100f };
		config.m_AutoDistancesMeters = new float[0];
		config.m_HipFireDistancesMeters = new float[0];
		config.m_ShotCounts = new[] { 3, 5, 8, 10 };
		config.m_EvaluateRecoil = true;
		config.m_EvaluateAuto = false;
		config.m_EvaluateDiscipline = false;
		config.m_EvaluateAccuracy = false;
		config.m_IncludeAttachments = false;
		config.m_IncludeWalk = false;
		config.m_IncludeSprint = false;
		config.m_IncludeCrouch = false;
		config.m_IncludeHipFire = false;
		config.m_IncludePointAim = false;
		config.m_IncludePreAim = false;
		config.m_IncludeSkills = false;
		return config;
	}

	public static WeaponBalanceRunConfig CreateReferencePreset()
	{
		var config = CreateInstance<WeaponBalanceRunConfig>();
		config.name = "WeaponBalance_Reference";
		config.m_Weapons = null;
		config.m_EvaluateRecoil = true;
		config.m_EvaluateAuto = true;
		config.m_EvaluateDiscipline = true;
		config.m_EvaluateAccuracy = true;
		config.m_IncludeAttachments = false;
		config.m_IncludeWalk = true;
		config.m_IncludeCrouch = true;
		config.m_IncludeSprint = false;
		config.m_IncludeHipFire = true;
		config.m_IncludePointAim = false;
		config.m_IncludePreAim = false;
		config.m_IncludeSkills = false;
		return config;
	}

	public static WeaponBalanceRunConfig CreateAttachmentsPreset()
	{
		WeaponBalanceRunConfig config = CreateReferencePreset();
		config.name = "WeaponBalance_Attachments";
		config.m_IncludeAttachments = true;
		config.m_EvaluateAuto = false;
		config.m_EvaluateDiscipline = false;
		config.m_IncludeWalk = false;
		config.m_IncludeSprint = false;
		config.m_IncludeHipFire = false;
		return config;
	}

	public static readonly string[] SmokeWeaponAssetNames =
	{
		"Weapon_M4_ModA_1",
		"Weapon_AK47",
		"Weapon_M249",
		"Weapon_PKM"
	};

	public static readonly string[] ReferenceWeaponAssetNames =
	{
		"Weapon_M4_ModA_1",
		"Weapon_AK47",
		"Weapon_AK74",
		"Weapon_M249",
		"Weapon_PKM",
		"Weapon_MK12",
		"Weapon_SVD",
		"Weapon_BenelliM4",
		"Weapon_M2Browning_127",
		"Weapon_MK19"
	};
	#endregion

	#region Private Methods
	private int[] ResolveShotCounts()
	{
		if (!m_IncludeExtendedShotCounts)
			return m_ShotCounts;
		return new[] { 1, 3, 5, 8, 10, 15, 20 };
	}
	#endregion
}
