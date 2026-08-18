#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Dumps approximate Auto HipFire/PointAim caps for main weapons × skill ranks.</summary>
public static class WeaponPoseAutoRangesReport
{
	private static readonly (string Name, float Marksmanship, float Handling)[] s_Ranks =
	{
		("Recruit", 25f, 25f),
		("Soldier", 50f, 50f),
		("Veteran", 75f, 75f),
		("Elite", 90f, 90f),
	};

	private static readonly string[] s_WeaponNames =
	{
		"Weapon_AK74",
		"Weapon_AK74U",
		"Weapon_M4_ModA_2",
		"Weapon_MK18",
		"Weapon_MK12",
		"Weapon_SVD",
		"Weapon_M249",
		"Weapon_PKM",
		"Weapon_BenelliM4",
	};

	[MenuItem("Polygone/Weapons/Report Auto Pose Ranges")]
	public static void Report()
	{
		var sb = new StringBuilder();
		sb.AppendLine("Weapon | Rank | HipFireMax m | PointAimMax m (w/ LCU)");
		sb.AppendLine("--- | --- | --- | ---");

		foreach (string weaponName in s_WeaponNames)
		{
			string[] guids = AssetDatabase.FindAssets($"{weaponName} t:WeaponDefinition");
			WeaponDefinition weapon = null;
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
				if (w != null && w.name == weaponName)
				{
					weapon = w;
					break;
				}
			}

			if (weapon == null)
			{
				sb.AppendLine($"{weaponName} | — | missing | missing");
				continue;
			}

			foreach (var rank in s_Ranks)
			{
				float skillDisp = EvaluateSkillDispersion(rank.Marksmanship);
				float hip = WeaponPoseAutoCapabilityBaker.FindMaxAcceptableDistance(
					weapon,
					null,
					_includeOptics: false,
					weapon.BaseShotDispersion,
					skillDisp,
					WeaponPoseAutoCapabilityBaker.DefaultHipFireSpreadMult,
					0.35f,
					WeaponPoseAutoCapabilityBaker.DefaultAcceptableHitRadiusMeters,
					500f);
				float point = WeaponPoseAutoCapabilityBaker.FindMaxAcceptableDistance(
					weapon,
					null,
					_includeOptics: false,
					weapon.BaseShotDispersion,
					skillDisp,
					WeaponPoseAutoCapabilityBaker.DefaultPointAimSpreadMult,
					0.35f,
					WeaponPoseAutoCapabilityBaker.DefaultAcceptableHitRadiusMeters,
					500f);
				sb.AppendLine($"{weapon.name} | {rank.Name} | {hip:0} | {point:0}");
			}
		}

		Debug.Log(sb.ToString());
		EditorUtility.DisplayDialog("Auto Pose Ranges", "Report written to Console.", "OK");
	}

	private static float EvaluateSkillDispersion(float _marksmanship)
	{
		const float worst = 1.25f;
		const float best = 0.75f;
		float t = Mathf.Clamp01(_marksmanship / 100f);
		return Mathf.Lerp(worst, best, t);
	}
}
#endif
