using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates compatible loadouts without full Cartesian explosion (§20.2).
/// </summary>
public static class WeaponBalanceLoadoutGenerator
{
	#region Nested Types
	private struct Candidate
	{
		public WeaponAttachmentDefinition Attachment;
		public WeaponAttachmentSlotType SlotType;
		public int RailSocketIndex;
		public float ImpactScore;
	}
	#endregion

	#region Public Methods
	public static IReadOnlyList<WeaponBalanceLoadout> Generate(
		WeaponDefinition _weapon,
		IReadOnlyList<WeaponAttachmentDefinition> _catalog,
		WeaponBalanceRunConfig _config)
	{
		var results = new List<WeaponBalanceLoadout>(8) { WeaponBalanceLoadout.Base };
		if (_weapon == null || _config == null || !_config.IncludeAttachments || _catalog == null)
			return results;

		int cap = _config.MaxLoadoutsPerWeapon;
		List<Candidate> singles = CollectSingles(_weapon, _catalog, _config);
		for (int i = 0; i < singles.Count && results.Count < cap; i++)
		{
			Candidate candidate = singles[i];
			if (candidate.Attachment == null)
				continue;
			results.Add(new WeaponBalanceLoadout(
				ShortLabel(candidate.Attachment.name),
				new[] { candidate.Attachment }));
		}

		for (int i = 0; i < singles.Count && results.Count < cap; i++)
		{
			for (int j = i + 1; j < singles.Count && results.Count < cap; j++)
			{
				if (singles[i].SlotType == singles[j].SlotType)
					continue;
				results.Add(new WeaponBalanceLoadout(
					ShortLabel(singles[i].Attachment.name) + "+" + ShortLabel(singles[j].Attachment.name),
					new[] { singles[i].Attachment, singles[j].Attachment }));
			}
		}

		return results;
	}

	public static bool IsAttachmentValidForWeapon(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition _attachment,
		WeaponAttachmentSlotType _slotType,
		int _railSocketIndex)
	{
		if (_weapon == null || _attachment == null)
			return false;
		if (!_attachment.SupportsWeapon(_weapon))
			return false;
		return _attachment.SupportsWeaponSlot(_slotType, _railSocketIndex);
	}
	#endregion

	#region Private Methods
	private static List<Candidate> CollectSingles(
		WeaponDefinition _weapon,
		IReadOnlyList<WeaponAttachmentDefinition> _catalog,
		WeaponBalanceRunConfig _config)
	{
		var singles = new List<Candidate>();
		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null || slots.Length == 0)
			return singles;

		for (int s = 0; s < slots.Length; s++)
		{
			WeaponAttachmentSlotType slotType = slots[s].SlotType;
			int[] railIndices = slotType == WeaponAttachmentSlotType.Rail
				? new[] { 0, 1, 2 }
				: new[] { 0 };
			for (int r = 0; r < railIndices.Length; r++)
			{
				int railIndex = railIndices[r];
				for (int i = 0; i < _catalog.Count; i++)
				{
					WeaponAttachmentDefinition attachment = _catalog[i];
					if (!IsAttachmentValidForWeapon(_weapon, attachment, slotType, railIndex))
						continue;
					if (!_config.IncludeCosmeticAttachments && IsCosmeticOnly(attachment))
						continue;
					if (!HasRecoilImpact(attachment))
						continue;
					singles.Add(new Candidate
					{
						Attachment = attachment,
						SlotType = slotType,
						RailSocketIndex = railIndex,
						ImpactScore = ComputeImpactScore(attachment)
					});
				}
			}
		}

		singles.Sort((a, b) => b.ImpactScore.CompareTo(a.ImpactScore));
		return singles;
	}

	private static bool HasRecoilImpact(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return false;
		return !Mathf.Approximately(_attachment.RecoilVerticalModifier, 1f) ||
		       !Mathf.Approximately(_attachment.RecoilHorizontalModifier, 1f) ||
		       !Mathf.Approximately(_attachment.RecoilRecoveryModifier, 1f) ||
		       !Mathf.Approximately(_attachment.RecoilModifier, 1f);
	}

	private static bool IsCosmeticOnly(WeaponAttachmentDefinition _attachment)
	{
		return _attachment != null &&
		       _attachment.AttachmentType == WeaponAttachmentType.RailCover &&
		       !HasRecoilImpact(_attachment);
	}

	private static float ComputeImpactScore(WeaponAttachmentDefinition _attachment)
	{
		return Mathf.Abs(1f - _attachment.RecoilVerticalModifier) +
		       Mathf.Abs(1f - _attachment.RecoilHorizontalModifier) +
		       Mathf.Abs(1f - _attachment.RecoilRecoveryModifier) +
		       Mathf.Abs(1f - _attachment.RecoilModifier);
	}

	private static string ShortLabel(string _assetName)
	{
		if (string.IsNullOrEmpty(_assetName))
			return "?";
		const string prefix = "Attachment_";
		return _assetName.StartsWith(prefix) ? _assetName.Substring(prefix.Length) : _assetName;
	}
	#endregion
}
