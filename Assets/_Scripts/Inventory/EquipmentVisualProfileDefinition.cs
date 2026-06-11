using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вес одного основного визуального варианта (0 = default без декора).
/// </summary>
[Serializable]
public struct EquipmentVisualVariantWeight
{
	[SerializeField, Min(0)] private int m_VariantIndex;
	[SerializeField, Min(0)] private int m_Weight;

	public int VariantIndex => m_VariantIndex;
	public int Weight => m_Weight;

	public EquipmentVisualVariantWeight(int _variantIndex, int _weight)
	{
		m_VariantIndex = Mathf.Max(0, _variantIndex);
		m_Weight = Mathf.Max(0, _weight);
	}
}

/// <summary>
/// Таблица весов и независимых опций декора для типа экипировки (шлем и т.д.).
/// </summary>
[CreateAssetMenu(
	fileName = "EquipmentVisualProfile",
	menuName = "Polygone/Inventory/Equipment Visual Profile",
	order = 15)]
public sealed class EquipmentVisualProfileDefinition : ScriptableObject
{
	#region Serialized Fields
	[Tooltip("Стабильный ключ для save и UnitIndividualTraits.")]
	[SerializeField] private string m_ProfileId = "helmet_default";
	[SerializeField] private EquipmentVisualVariantWeight[] m_PrimaryVariantWeights =
	{
		new EquipmentVisualVariantWeight(0, 100)
	};
	[Tooltip("Независимая вероятность подбородного ремня (0..1). 0 = никогда.")]
	[SerializeField, Range(0f, 1f)] private float m_ChinStrapIndependentChance = 0.5f;
	#endregion

	#region Public Properties
	public string ProfileId => m_ProfileId;
	public float ChinStrapIndependentChance => m_ChinStrapIndependentChance;
	public IReadOnlyList<EquipmentVisualVariantWeight> PrimaryVariantWeights => m_PrimaryVariantWeights;
	#endregion

	#region Public Methods
	public UnitEquipmentVisualPreferenceEntry RollRandomPreference()
	{
		int variant = RollWeightedPrimaryVariant();
		bool chinStrap = m_ChinStrapIndependentChance > 0f &&
		                 UnityEngine.Random.value < m_ChinStrapIndependentChance;
		return new UnitEquipmentVisualPreferenceEntry(m_ProfileId, variant, chinStrap);
	}

	public UnitEquipmentVisualPreferenceEntry CreateDefaultPreference()
	{
		return new UnitEquipmentVisualPreferenceEntry(m_ProfileId, 0, false);
	}
	#endregion

	#region Private Methods
	private int RollWeightedPrimaryVariant()
	{
		if (m_PrimaryVariantWeights == null || m_PrimaryVariantWeights.Length == 0)
			return 0;

		int totalWeight = 0;
		for (int i = 0; i < m_PrimaryVariantWeights.Length; i++)
			totalWeight += Mathf.Max(0, m_PrimaryVariantWeights[i].Weight);

		if (totalWeight <= 0)
			return 0;

		int roll = UnityEngine.Random.Range(0, totalWeight);
		for (int i = 0; i < m_PrimaryVariantWeights.Length; i++)
		{
			roll -= Mathf.Max(0, m_PrimaryVariantWeights[i].Weight);
			if (roll < 0)
				return m_PrimaryVariantWeights[i].VariantIndex;
		}

		return m_PrimaryVariantWeights[m_PrimaryVariantWeights.Length - 1].VariantIndex;
	}
	#endregion
}
