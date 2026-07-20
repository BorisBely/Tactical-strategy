using System.Collections.Generic;

public static class ItemWeightDefaults
{
	private static readonly Dictionary<string, float> s_WeightByKey = new Dictionary<string, float>
	{
		{ "item.weapon.m4_moda_1", 3.0f },
		{ "item.weapon.m4_moda_2", 3.1f },
		{ "item.weapon.m16a_moda_1", 3.4f },
		{ "item.weapon.m16a4_moda_2", 3.6f },
		{ "item.weapon.mk12", 4.5f },
		{ "item.weapon.mk18", 2.8f },
		{ "item.weapon.ak47", 3.8f },
		{ "item.weapon.ak47_1", 3.9f },
		{ "item.weapon.ak47mod1", 4.1f },
		{ "item.weapon.ak47s", 3.4f },
		{ "item.weapon.ak74", 3.5f },
		{ "item.weapon.ak74mod1", 3.8f },
		{ "item.weapon.ak74u", 2.8f },
		{ "item.weapon.ak74umod1", 3.1f },
		{ "item.weapon.rpk47", 5.2f },
		{ "item.weapon.rpk47mod1", 5.5f },
		{ "item.weapon.rpk74", 4.8f },
		{ "item.weapon.rpk74mod1", 5.1f },

		{ "item.weapon.mosin", 4.0f },
		{ "item.weapon.benelli_m4", 3.8f },
		{ "item.weapon.m249", 7.5f },
		{ "item.weapon.sniper_762x51", 5.8f },
		{ "item.weapon.pkm", 8.2f },
		{ "item.weapon.svd", 4.3f },

		{ "item.mag.mosin_762_54r_5", 0.25f },
		{ "item.mag.m249_556_200", 2.8f },
		{ "item.mag.sniper_762x51_10", 0.5f },
		{ "item.mag.pkm_762_54r_100", 2.5f },
		{ "item.mag.svd_762_54r_10", 0.4f },

		{ "item.loot.ammo_box.12g", 1.05f },
		{ "item.loot.ammo_box.762x51", 0.55f },
		{ "item.loot.ammo_box.762x54r", 0.50f },

		{ "item.mag.m4_556_20", 0.5f },
		{ "item.mag.m4_556_30", 0.7f },
		{ "item.mag.m4_556_40", 1.0f },
		{ "item.mag.m4_556_drum_60", 1.4f },
		{ "item.mag.m4_556_drum_100", 2.2f },

		{ "item.mag.ak_545_30", 0.6f },
		{ "item.mag.ak_545_45", 0.8f },
		{ "item.mag.ak_762_30", 0.7f },
		{ "item.mag.ak_762_30b", 0.7f },
		{ "item.mag.ak_762_30c", 1.2f },
		{ "item.mag.ak_762_75", 1.8f },

		{ "item.attachment.m4_silencer_556", 0.4f },
		{ "item.attachment.m4_muzzle_brake", 0.15f },
		{ "item.attachment.m4_acog", 0.5f },
		{ "item.attachment.m4_acog_rmr", 0.45f },
		{ "item.attachment.m4_aimpoint", 0.35f },
		{ "item.attachment.m4_eotech_g33", 0.55f },
		{ "item.attachment.m4_elcan_specterdr", 0.6f },
		{ "item.attachment.m4_vortex_razor", 0.65f },
		{ "item.attachment.m4_susat", 0.5f },
		{ "item.attachment.m4_rdc", 0.3f },
		{ "item.attachment.m4_scope1_3x", 0.45f },
		{ "item.attachment.m4_scope4", 0.8f },
		{ "item.attachment.m4_scope5", 0.85f },
		{ "item.attachment.m4_scope9", 0.9f },
		{ "item.attachment.m4_reddot1", 0.3f },
		{ "item.attachment.m4_reddot2", 0.32f },
		{ "item.attachment.m4_reddot3", 0.3f },
		{ "item.attachment.m4_foregrip1", 0.2f },
		{ "item.attachment.m4_foregrip2", 0.18f },
		{ "item.attachment.m4_foregrip3", 0.17f },
		{ "item.attachment.m4_foregrip4", 0.19f },
		{ "item.attachment.m4_foregrip5", 0.15f },
		{ "item.attachment.m4_laser1", 0.1f },
		{ "item.attachment.m4_laser2", 0.08f },
		{ "item.attachment.m4_flashlight1", 0.15f },
		{ "item.attachment.m4_rail_cover1", 0.05f },
		{ "item.attachment.m4_stock1", 0.4f },
		{ "item.attachment.m4_stock2", 0.25f },

		{ "item.attachment.ak_silencer", 0.45f },
		{ "item.attachment.ak_muzzle_brake", 0.18f },
		{ "item.attachment.ak_silencer_545", 0.45f },
		{ "item.attachment.ak_muzzle_brake_545", 0.18f },
		{ "item.attachment.ak_reddot4_rail", 0.35f },
		{ "item.attachment.ak_scope11", 0.55f },
		{ "item.attachment.mosin_scope8", 0.7f },
		{ "item.attachment.svd_silencer", 0.48f },
		{ "item.attachment.svd_muzzle_brake", 0.18f },
		{ "item.attachment.sniper762x51_silencer", 0.5f },
		{ "item.attachment.sniper762x51_muzzle_brake", 0.2f },

		{ "item.grenade.frag_01", 0.4f },
		{ "item.grenade.rgd5", 0.35f },
		{ "item.grenade.f1", 0.6f },
		{ "item.grenade.flash_01", 0.3f },
		{ "item.grenade.smoke_01", 0.5f },
		{ "item.weapon.rpg7", 6.3f },
		{ "item.weapon.disposable_rocket_launcher", 4.5f },
		{ "item.ammo.rpg_rocket", 2.5f },

		{ "item.medkit.ifak", 0.5f },

		{ "item.helmet.kevlar_1", 1.5f },
		{ "item.helmet.kevlar_2", 1.8f },
		{ "item.helmet.tactical", 1.6f },
		{ "item.helmet.crew", 1.2f },

		{ "item.loot.ammo_box.556", 0.5f },
		{ "item.loot.ammo_box.762", 0.6f },
		{ "item.loot.ammo_box.545", 0.55f },

		{ "item.backpack.1", 2.0f },
		{ "item.backpack.2", 1.0f },
	};

	public static float GetWeight(string _localizationKey)
	{
		if (string.IsNullOrEmpty(_localizationKey))
			return 0.5f;

		if (s_WeightByKey.TryGetValue(_localizationKey, out float weight))
			return weight;

		return 0.5f;
	}

	private static readonly Dictionary<string, float> s_BackpackWeightLimitByKey = new Dictionary<string, float>
	{
		{ "item.backpack.1", 45f },
		{ "item.backpack.2", 30f },
	};

	public const float DefaultBagWeightLimitKg = 12f;

	public static float GetBackpackWeightLimit(string _localizationKey)
	{
		if (string.IsNullOrEmpty(_localizationKey))
			return 0f;

		if (s_BackpackWeightLimitByKey.TryGetValue(_localizationKey, out float limit))
			return limit;

		return 0f;
	}

	public static int GetBackpackCapacity(string _localizationKey)
	{
		return (int)GetBackpackWeightLimit(_localizationKey);
	}

	public static float GetWeaponModificationWeight(InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty || _slot.Definition == null)
			return 0f;

		if (_slot.Definition.IsRocketLauncher)
		{
			RocketLauncherRuntimeState rocketState = _slot.InstanceState != null
				? _slot.InstanceState.RocketLauncherState
				: null;
			if (rocketState != null && rocketState.LoadedRocketDefinition != null)
				return rocketState.LoadedRocketDefinition.WeightKg;
			return 0f;
		}

		if (_slot.Definition.WeaponDefinition == null)
			return 0f;

		WeaponRuntimeState state = _slot.InstanceState?.WeaponState;
		if (state == null)
			return 0f;

		float extra = 0f;

		ItemDefinition magDef = state.InsertedMagazineDefinition;
		if (magDef != null)
			extra += magDef.WeightKg;

		if (state.HasSecondaryMagazine)
		{
			ItemDefinition secondaryMagDef = state.CurrentSecondaryMagazineItem.Definition;
			if (secondaryMagDef != null)
				extra += secondaryMagDef.WeightKg;
		}

		ItemDefinition[] attachments = state.EquippedAttachmentItems;
		if (attachments != null)
		{
			for (int i = 0; i < attachments.Length; i++)
			{
				if (attachments[i] != null)
					extra += attachments[i].WeightKg;
			}
		}

		return extra;
	}
}
