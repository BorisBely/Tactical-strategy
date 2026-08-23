using System.Collections.Generic;
using UnityEngine;

/// <summary>Expands config into reproducible balance cases (no full Cartesian explosion).</summary>
public static class WeaponBalanceCaseEnumerator
{
	#region Public Methods
	public static List<WeaponBalanceCase> Enumerate(
		WeaponBalanceRunConfig _config,
		IReadOnlyList<WeaponDefinition> _weapons,
		IReadOnlyList<WeaponAttachmentDefinition> _attachmentCatalog)
	{
		var cases = new List<WeaponBalanceCase>(256);
		if (_config == null || _weapons == null)
			return cases;

		for (int w = 0; w < _weapons.Count; w++)
		{
			WeaponDefinition weapon = _weapons[w];
			if (weapon == null)
				continue;

			bool isTurret = WeaponBalanceCaseValidator.IsTurretWeapon(weapon);
			IReadOnlyList<WeaponBalanceLoadout> loadouts = WeaponBalanceLoadoutGenerator.Generate(
				weapon,
				_attachmentCatalog,
				_config);
			WeaponFireMode[] fireModes = ResolveFireModes(weapon);
			WeaponPoseState[] poses = ResolvePoses(_config);
			WeaponBalanceStance[] stances = ResolveStances(_config);
			float[] skills = ResolveSkills(_config);

			for (int l = 0; l < loadouts.Count; l++)
			{
				WeaponBalanceLoadout loadout = loadouts[l];
				for (int f = 0; f < fireModes.Length; f++)
				{
					for (int p = 0; p < poses.Length; p++)
					{
						for (int s = 0; s < stances.Length; s++)
						{
							WeaponBalanceMovement[] movements = ResolveMovements(
								_config,
								poses[p],
								isTurret);
							float[] distances = ResolveDistances(_config, poses[p]);
							for (int m = 0; m < movements.Length; m++)
							{
								for (int d = 0; d < distances.Length; d++)
								{
									for (int k = 0; k < skills.Length; k++)
									{
										AmmoDefinition ammo = _config.IncludeAmmo
											? weapon.BuiltInMagazineDefaultAmmo
											: null;
										var balanceCase = new WeaponBalanceCase(
											weapon,
											fireModes[f],
											ammo,
											loadout.Attachments,
											loadout.Label,
											poses[p],
											stances[s],
											movements[m],
											distances[d],
											skills[k],
											isTurret);
										if (WeaponBalanceCaseValidator.IsValid(in balanceCase, _config))
											cases.Add(balanceCase);
									}
								}
							}
						}
					}
				}
			}
		}

		return cases;
	}
	#endregion

	#region Private Methods
	private static WeaponFireMode[] ResolveFireModes(WeaponDefinition _weapon)
	{
		WeaponFireMode[] modes = _weapon.AvailableFireModes;
		if (modes == null || modes.Length == 0)
			return new[] { WeaponFireMode.SemiAuto };
		return modes;
	}

	private static WeaponPoseState[] ResolvePoses(WeaponBalanceRunConfig _config)
	{
		var list = new List<WeaponPoseState>(4);
		if (_config.IncludeAiming)
			list.Add(WeaponPoseState.Aiming);
		if (_config.IncludePointAim)
			list.Add(WeaponPoseState.PointAim);
		if (_config.IncludePreAim)
			list.Add(WeaponPoseState.PreAim);
		if (_config.IncludeHipFire)
			list.Add(WeaponPoseState.HipFire);
		if (list.Count == 0)
			list.Add(WeaponPoseState.Aiming);
		return list.ToArray();
	}

	private static WeaponBalanceStance[] ResolveStances(WeaponBalanceRunConfig _config)
	{
		var list = new List<WeaponBalanceStance>(2);
		if (_config.IncludeStanding)
			list.Add(WeaponBalanceStance.Standing);
		if (_config.IncludeCrouch)
			list.Add(WeaponBalanceStance.Crouch);
		if (list.Count == 0)
			list.Add(WeaponBalanceStance.Standing);
		return list.ToArray();
	}

	private static WeaponBalanceMovement[] ResolveMovements(
		WeaponBalanceRunConfig _config,
		WeaponPoseState _pose,
		bool _isTurret)
	{
		if (_isTurret)
			return new[] { WeaponBalanceMovement.Idle };

		var list = new List<WeaponBalanceMovement>(3) { WeaponBalanceMovement.Idle };
		if (_config.IncludeWalk && _pose == WeaponPoseState.Aiming)
			list.Add(WeaponBalanceMovement.Walk);
		if (_config.IncludeSprint && (_pose != WeaponPoseState.Aiming || _config.AllowSprintWhileAiming))
			list.Add(WeaponBalanceMovement.Sprint);
		return list.ToArray();
	}

	private static float[] ResolveDistances(WeaponBalanceRunConfig _config, WeaponPoseState _pose)
	{
		if (_pose.IsHipFireHold() && _config.HipFireDistancesMeters != null &&
		    _config.HipFireDistancesMeters.Length > 0)
			return _config.HipFireDistancesMeters;

		if (_config.EvaluateAuto && _config.AutoDistancesMeters != null &&
		    _config.AutoDistancesMeters.Length > 0 &&
		    _pose == WeaponPoseState.Aiming)
			return _config.AutoDistancesMeters;

		if (_config.RecoilDistancesMeters != null && _config.RecoilDistancesMeters.Length > 0)
			return _config.RecoilDistancesMeters;

		return new[] { 50f };
	}

	private static float[] ResolveSkills(WeaponBalanceRunConfig _config)
	{
		return _config.IncludeSkills
			? new[] { 0f, 50f, 100f }
			: new[] { RecoilPlayBaselineProtocol.NeutralRecoilControl };
	}
	#endregion
}
