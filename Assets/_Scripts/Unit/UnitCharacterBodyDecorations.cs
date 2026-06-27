using System;
using UnityEngine;

/// <summary>
/// Визуальные декорации тела (рация на груди, очки на голове). Только внешний вид, без механики.
/// Не создаёт ItemDefinition, не участвует в инвентаре и луте — только Instantiate на кости юнита.
/// Варианты задаются предпочтениями <see cref="UnitIndividualTraits"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class UnitCharacterBodyDecorations : MonoBehaviour
{
	#region Constants
	public const string RadioProfileId = "body_radio";
	public const string GlassesProfileId = "body_glasses";

	public const int VariantNone = 0;
	public const int RadioVariantRadio = 1;
	public const int RadioVariantWalkieTalkiePouch = 2;
	public const int GlassesVariant01 = 1;
	public const int GlassesVariant02 = 2;
	public const int GlassesVariant03 = 3;
	public const int GlassesVariantSunGlasses = 4;
	#endregion

	#region Serialized Fields
	[Header("Anchors")]
	[SerializeField] private Transform m_ChestAnchor;
	[SerializeField] private Transform m_HeadAnchor;

	[Header("Chest — Radio")]
	[SerializeField] private CharacterBodyDecorationVariant m_RadioVariant;
	[SerializeField] private CharacterBodyDecorationVariant m_WalkieTalkiePouchVariant;

	[Header("Head — Glasses")]
	[SerializeField] private CharacterBodyDecorationVariant m_Glasses01Variant;
	[SerializeField] private CharacterBodyDecorationVariant m_Glasses02Variant;
	[SerializeField] private CharacterBodyDecorationVariant m_Glasses03Variant;
	[SerializeField] private CharacterBodyDecorationVariant m_SunGlassesMaleVariant;
	[SerializeField] private CharacterBodyDecorationVariant m_SunGlassesFemaleVariant;
	#endregion

	#region Private Fields
	private GameObject m_ChestDecorationInstance;
	private GameObject m_HeadDecorationInstance;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		ApplyFromUnitTraits();
	}
	#endregion

	#region Public Methods
	public void ApplyFromUnitTraits()
	{
		UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
		UnitCharacterAppearance appearance = GetComponentInParent<UnitCharacterAppearance>(true);
		RefreshDecorations(traits, appearance);
	}

	public void RefreshDecorations(UnitIndividualTraits _traits, UnitCharacterAppearance _appearance)
	{
		if (!enabled)
			return;

		CharacterGender gender = _appearance != null ? _appearance.Gender : CharacterGender.Male;
		ApplyChestDecoration(ResolveVariant(_traits, RadioProfileId));
		ApplyHeadDecoration(ResolveVariant(_traits, GlassesProfileId), gender);
	}
	#endregion

	#region Private Methods
	private static int ResolveVariant(UnitIndividualTraits _traits, string _profileId)
	{
		if (_traits != null && _traits.TryGetPreference(_profileId, out UnitEquipmentVisualPreferenceEntry preference))
			return preference.PrimaryVariant;

		return VariantNone;
	}

	private void ApplyChestDecoration(int _variant)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref m_ChestDecorationInstance);

		CharacterBodyDecorationVariant config = _variant switch
		{
			RadioVariantRadio => m_RadioVariant,
			RadioVariantWalkieTalkiePouch => m_WalkieTalkiePouchVariant,
			_ => default
		};

		if (config.Prefab == null || m_ChestAnchor == null)
			return;

		m_ChestDecorationInstance = CharacterDecorationSpawnUtility.SpawnDecoration(m_ChestAnchor, config);
	}

	private void ApplyHeadDecoration(int _variant, CharacterGender _gender)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref m_HeadDecorationInstance);

		CharacterBodyDecorationVariant config = _variant switch
		{
			GlassesVariant01 => m_Glasses01Variant,
			GlassesVariant02 => m_Glasses02Variant,
			GlassesVariant03 => m_Glasses03Variant,
			GlassesVariantSunGlasses => _gender == CharacterGender.Female
				? m_SunGlassesFemaleVariant
				: m_SunGlassesMaleVariant,
			_ => default
		};

		if (config.Prefab == null || m_HeadAnchor == null)
			return;

		m_HeadDecorationInstance = CharacterDecorationSpawnUtility.SpawnDecoration(m_HeadAnchor, config);
	}
	#endregion
}

/// <summary>
/// Префаб и локальная поза декора на кости персонажа.
/// </summary>
[Serializable]
public struct CharacterBodyDecorationVariant
{
	[SerializeField] private GameObject m_Prefab;
	[SerializeField] private Vector3 m_LocalPosition;
	[SerializeField] private Vector3 m_LocalEulerAngles;

	public GameObject Prefab => m_Prefab;
	public Vector3 LocalPosition => m_LocalPosition;
	public Vector3 LocalEulerAngles => m_LocalEulerAngles;

	public CharacterBodyDecorationVariant(GameObject _prefab, Vector3 _localPosition, Vector3 _localEulerAngles)
	{
		m_Prefab = _prefab;
		m_LocalPosition = _localPosition;
		m_LocalEulerAngles = _localEulerAngles;
	}
}
