using System;
using UnityEngine;

/// <summary>
/// Runtime-внешность головы: причёска, борода и лёгкие головные уборы.
/// Шлем временно заменяет несовместимые причёски на короткий вариант, не меняя сохранённый roll.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class UnitCharacterHeadAppearance : MonoBehaviour
{
	#region Serialized Fields
	[Header("Anchors")]
	[SerializeField] private Transform m_HeadAnchor;

	[Header("Male Hair")]
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairShort04;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairLongBack02;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairRaised03;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairCurly05;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairMessy06;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairStylish07;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairShavedSides08;
	[SerializeField] private CharacterBodyDecorationVariant m_MaleHairShavedSidesLong10;

	[Header("Female Hair")]
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHair01;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHair02;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHair03;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHair04;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHairCap02;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHairCap02Alt;
	[SerializeField] private CharacterBodyDecorationVariant m_FemaleHairHelmetShort05;

	[Header("Standalone Hats")]
	[SerializeField] private CharacterBodyDecorationVariant m_Hat02;
	[SerializeField] private CharacterBodyDecorationVariant m_Hat03;
	[SerializeField] private CharacterBodyDecorationVariant m_Hat04;
	[SerializeField] private CharacterBodyDecorationVariant m_Hat05;
	[SerializeField] private CharacterBodyDecorationVariant m_Beanie01;

	[Header("Beards")]
	[SerializeField] private CharacterBodyDecorationVariant m_Beard01;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard04Mustache;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard04;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard09Mustache;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard09;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard10;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard11;
	[SerializeField] private CharacterBodyDecorationVariant m_Beard12;
	[SerializeField] private CharacterBodyDecorationVariant m_Mustache01;
	#endregion

	#region Private Fields
	private GameObject m_HairInstance;
	private GameObject m_HatInstance;
	private GameObject m_BeardInstance;
	private UnitHeadEquipment m_HeadEquipment;
	private UnitCombatStats m_CombatStats;
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		Subscribe();
	}

	private void Start()
	{
		RefreshFromCurrentState();
	}

	private void OnDisable()
	{
		Unsubscribe();
	}
	#endregion

	#region Public Methods
	public void RefreshFromCurrentState()
	{
		UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = GetComponentInParent<UnitCharacterAppearance>(true);
		RefreshFromTraits(traits, appearance);
	}

	public void RefreshFromTraits(UnitIndividualTraits _traits, UnitCharacterAppearance _appearance)
	{
		if (!enabled)
			return;

		CharacterGender gender = _appearance != null ? _appearance.Gender : CharacterGender.Male;
		bool hasHelmet = m_HeadEquipment != null && m_HeadEquipment.EquippedDefinition != null;

		ApplyHair(_traits, gender, hasHelmet);
		ApplyHat(_traits, gender, hasHelmet);
		ApplyBeard(_traits, gender);
	}
	#endregion

	#region Private Methods
	private void Subscribe()
	{
		if (m_HeadEquipment == null)
			m_HeadEquipment = GetComponentInParent<UnitHeadEquipment>(true) ??
			                  GetComponentInChildren<UnitHeadEquipment>(true);
		if (m_HeadEquipment != null)
			m_HeadEquipment.HeadEquipmentChanged += HandleHeadEquipmentChanged;

		if (m_CombatStats == null)
			m_CombatStats = GetComponentInParent<UnitCombatStats>(true) ??
			                 GetComponentInChildren<UnitCombatStats>(true);
		if (m_CombatStats != null)
			m_CombatStats.RankPresetChanged += HandleRankPresetChanged;
	}

	private void Unsubscribe()
	{
		if (m_HeadEquipment != null)
			m_HeadEquipment.HeadEquipmentChanged -= HandleHeadEquipmentChanged;
		if (m_CombatStats != null)
			m_CombatStats.RankPresetChanged -= HandleRankPresetChanged;
	}

	private void HandleHeadEquipmentChanged()
	{
		RefreshFromCurrentState();
	}

	private void HandleRankPresetChanged(UnitCombatRankDefinition _rankPreset)
	{
		UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = GetComponentInParent<UnitCharacterAppearance>(true);
		if (traits != null)
			traits.RollHeadAppearance(_rankPreset, appearance != null ? appearance.Gender : CharacterGender.Male);

		RefreshFromTraits(traits, appearance);
	}

	private void ApplyHair(UnitIndividualTraits _traits, CharacterGender _gender, bool _hasHelmet)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref m_HairInstance);
		if (m_HeadAnchor == null || _traits == null)
			return;

		CharacterBodyDecorationVariant config = ResolveHairVariant(_traits.HeadHairVariant, _gender, _hasHelmet);
		if (config.Prefab == null)
			return;

		m_HairInstance = CharacterDecorationSpawnUtility.SpawnDecoration(m_HeadAnchor, config);
	}

	private void ApplyHat(UnitIndividualTraits _traits, CharacterGender _gender, bool _hasHelmet)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref m_HatInstance);
		if (m_HeadAnchor == null || _traits == null || _hasHelmet)
			return;

		if (!UnitHeadAppearanceVariantIds.CanUseStandaloneHat(_gender, _traits.HeadHairVariant))
			return;

		CharacterBodyDecorationVariant config = ResolveHatVariant(_traits.HeadHatVariant);
		if (config.Prefab == null)
			return;

		m_HatInstance = CharacterDecorationSpawnUtility.SpawnDecoration(m_HeadAnchor, config);
	}

	private void ApplyBeard(UnitIndividualTraits _traits, CharacterGender _gender)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref m_BeardInstance);
		if (m_HeadAnchor == null || _traits == null || _gender != CharacterGender.Male)
			return;

		CharacterBodyDecorationVariant config = ResolveBeardVariant(_traits.HeadBeardVariant);
		if (config.Prefab == null)
			return;

		m_BeardInstance = CharacterDecorationSpawnUtility.SpawnDecoration(m_HeadAnchor, config);
	}

	private CharacterBodyDecorationVariant ResolveHairVariant(int _variant, CharacterGender _gender, bool _hasHelmet)
	{
		if (_gender == CharacterGender.Female)
		{
			if (_hasHelmet)
				return m_FemaleHairHelmetShort05;

			return _variant switch
			{
				UnitHeadAppearanceVariantIds.FemaleHair02 => m_FemaleHair02,
				UnitHeadAppearanceVariantIds.FemaleHair03 => m_FemaleHair03,
				UnitHeadAppearanceVariantIds.FemaleHair04 => m_FemaleHair04,
				UnitHeadAppearanceVariantIds.FemaleHairCap02 => m_FemaleHairCap02,
				UnitHeadAppearanceVariantIds.FemaleHairCap02Alt => m_FemaleHairCap02Alt,
				_ => m_FemaleHair01
			};
		}

		if (_hasHelmet && _variant != UnitHeadAppearanceVariantIds.MaleHairBald)
			return m_MaleHairShort04;

		return _variant switch
		{
			UnitHeadAppearanceVariantIds.MaleHairBald => default,
			UnitHeadAppearanceVariantIds.MaleHairLongBack02 => m_MaleHairLongBack02,
			UnitHeadAppearanceVariantIds.MaleHairRaised03 => m_MaleHairRaised03,
			UnitHeadAppearanceVariantIds.MaleHairCurly05 => m_MaleHairCurly05,
			UnitHeadAppearanceVariantIds.MaleHairMessy06 => m_MaleHairMessy06,
			UnitHeadAppearanceVariantIds.MaleHairStylish07 => m_MaleHairStylish07,
			UnitHeadAppearanceVariantIds.MaleHairShavedSides08 => m_MaleHairShavedSides08,
			UnitHeadAppearanceVariantIds.MaleHairShavedSidesLong10 => m_MaleHairShavedSidesLong10,
			_ => m_MaleHairShort04
		};
	}

	private CharacterBodyDecorationVariant ResolveHatVariant(int _variant)
	{
		return _variant switch
		{
			UnitHeadAppearanceVariantIds.Hat02 => m_Hat02,
			UnitHeadAppearanceVariantIds.Hat03 => m_Hat03,
			UnitHeadAppearanceVariantIds.Hat04 => m_Hat04,
			UnitHeadAppearanceVariantIds.Hat05 => m_Hat05,
			UnitHeadAppearanceVariantIds.Beanie01 => m_Beanie01,
			_ => default
		};
	}

	private CharacterBodyDecorationVariant ResolveBeardVariant(int _variant)
	{
		return _variant switch
		{
			UnitHeadAppearanceVariantIds.Beard01 => m_Beard01,
			UnitHeadAppearanceVariantIds.Beard04Mustache => m_Beard04Mustache,
			UnitHeadAppearanceVariantIds.Beard04 => m_Beard04,
			UnitHeadAppearanceVariantIds.Beard09Mustache => m_Beard09Mustache,
			UnitHeadAppearanceVariantIds.Beard09 => m_Beard09,
			UnitHeadAppearanceVariantIds.Beard10 => m_Beard10,
			UnitHeadAppearanceVariantIds.Beard11 => m_Beard11,
			UnitHeadAppearanceVariantIds.Beard12 => m_Beard12,
			UnitHeadAppearanceVariantIds.Mustache01 => m_Mustache01,
			_ => default
		};
	}
	#endregion
}
