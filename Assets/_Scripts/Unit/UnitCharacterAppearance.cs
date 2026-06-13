using UnityEngine;

/// <summary>
/// Внешний вид тела юнита: пол для gender-specific декора экипировки.
/// При спавне пол бросается через <see cref="RollInitialGender"/>; SetGender — для ручной смены позже.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitCharacterAppearance : MonoBehaviour
{
	#region Constants
	public const float DefaultFemaleSpawnChance = 0.25f;
	#endregion

	#region Serialized Fields
	[SerializeField] private CharacterGender m_Gender = CharacterGender.Male;
	[SerializeField] private bool m_IsGenderInitialized;
	#endregion

	#region Public Properties
	public CharacterGender Gender => m_Gender;
	public bool IsGenderInitialized => m_IsGenderInitialized;
	#endregion

	#region Public Methods
	public void EnsureDefaultMale()
	{
		if (m_IsGenderInitialized)
			return;

		InitializeGender(CharacterGender.Male);
	}

	/// <summary>Случайно выбрать пол при первом спавне. Повторные вызовы игнорируются.</summary>
	public void RollInitialGender(float _femaleChance = DefaultFemaleSpawnChance)
	{
		if (m_IsGenderInitialized)
			return;

		float clampedChance = Mathf.Clamp01(_femaleChance);
		CharacterGender rolledGender = UnityEngine.Random.value < clampedChance
			? CharacterGender.Female
			: CharacterGender.Male;

		InitializeGender(rolledGender);
	}

	public void InitializeGender(CharacterGender _gender)
	{
		if (m_IsGenderInitialized && m_Gender == _gender)
			return;

		m_Gender = _gender;
		m_IsGenderInitialized = true;
		RefreshDependentEquipmentVisuals();
	}

	public void SetGender(CharacterGender _gender)
	{
		InitializeGender(_gender);
	}

	public static UnitCharacterAppearance GetOrCreate(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out UnitCharacterAppearance appearance))
			appearance = _unitRoot.AddComponent<UnitCharacterAppearance>();

		return appearance;
	}
	#endregion

	#region Private Methods
	private void RefreshDependentEquipmentVisuals()
	{
		UnitIndividualTraits traits = GetComponentInChildren<UnitIndividualTraits>(true);

		if (TryGetComponent(out MissionPrepUnitArmorVisualController armorVisual))
			armorVisual.ApplyArmorVisual(armorVisual.CurrentArmorIndex, m_Gender);

		UnitHeadEquipment headEquipment = GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment != null && headEquipment.EquippedDefinition != null)
			headEquipment.RefreshEquippedVisual(traits, this);

		UnitCharacterBodyDecorations bodyDecorations = GetComponentInChildren<UnitCharacterBodyDecorations>(true);
		if (bodyDecorations != null)
			bodyDecorations.RefreshDecorations(traits, this);

		UnitCharacterHeadAppearance headAppearance = GetComponentInChildren<UnitCharacterHeadAppearance>(true);
		if (headAppearance != null)
			headAppearance.RefreshFromTraits(traits, this);

		UnitCharacterMaterialAppearance materialAppearance = GetComponent<UnitCharacterMaterialAppearance>();
		if (materialAppearance != null)
			materialAppearance.ApplyCurrentVisual();
	}
	#endregion
}
