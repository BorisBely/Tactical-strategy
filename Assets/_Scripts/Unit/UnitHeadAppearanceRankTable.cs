using System;
using UnityEngine;

[Serializable]
public struct UnitHeadAppearanceVariantWeight
{
	#region Serialized Fields
	[SerializeField, Min(0)] private int m_VariantIndex;
	[SerializeField, Min(0)] private int m_Weight;
	#endregion

	#region Public Properties
	public int VariantIndex => m_VariantIndex;
	public int Weight => m_Weight;
	#endregion

	#region Constructor
	public UnitHeadAppearanceVariantWeight(int _variantIndex, int _weight)
	{
		m_VariantIndex = Mathf.Max(0, _variantIndex);
		m_Weight = Mathf.Max(0, _weight);
	}
	#endregion
}

[Serializable]
public sealed class UnitHeadAppearanceRankWeights
{
	#region Serialized Fields
	[SerializeField] private string m_RankAssetName;
	[SerializeField] private UnitHeadAppearanceVariantWeight[] m_MaleHairWeights = Array.Empty<UnitHeadAppearanceVariantWeight>();
	[SerializeField] private UnitHeadAppearanceVariantWeight[] m_MaleBeardWeights = Array.Empty<UnitHeadAppearanceVariantWeight>();
	#endregion

	#region Public Properties
	public string RankAssetName => m_RankAssetName;
	public UnitHeadAppearanceVariantWeight[] MaleHairWeights => m_MaleHairWeights;
	public UnitHeadAppearanceVariantWeight[] MaleBeardWeights => m_MaleBeardWeights;
	#endregion

	#region Constructors
	public UnitHeadAppearanceRankWeights()
	{
	}

	public UnitHeadAppearanceRankWeights(
		string _rankAssetName,
		UnitHeadAppearanceVariantWeight[] _maleHairWeights,
		UnitHeadAppearanceVariantWeight[] _maleBeardWeights)
	{
		m_RankAssetName = _rankAssetName ?? string.Empty;
		m_MaleHairWeights = _maleHairWeights ?? Array.Empty<UnitHeadAppearanceVariantWeight>();
		m_MaleBeardWeights = _maleBeardWeights ?? Array.Empty<UnitHeadAppearanceVariantWeight>();
	}
	#endregion

	#region Public Methods
	public bool MatchesRank(UnitCombatRankDefinition _rank)
	{
		return _rank != null &&
		       !string.IsNullOrWhiteSpace(m_RankAssetName) &&
		       string.Equals(m_RankAssetName, _rank.name, StringComparison.Ordinal);
	}
	#endregion
}

[CreateAssetMenu(
	fileName = "HeadAppearanceRankTable",
	menuName = "Polygone/Character/Head Appearance Rank Table",
	order = 25)]
public sealed class UnitHeadAppearanceRankTable : ScriptableObject
{
	#region Constants
	public const string DefaultAssetPath = "Assets/GameData/Character/HeadAppearance/HeadAppearanceRankTable.asset";
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitHeadAppearanceRankWeights[] m_RankWeights = Array.Empty<UnitHeadAppearanceRankWeights>();

	[Header("Shared Hats")]
	[SerializeField] private UnitHeadAppearanceVariantWeight[] m_StandaloneHatWeights =
	{
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.HatNone, 78),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Hat02, 5),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Hat03, 5),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Hat04, 4),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Hat05, 4),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beanie01, 4)
	};

	[Header("Female Hair")]
	[SerializeField] private UnitHeadAppearanceVariantWeight[] m_FemaleHairWeights =
	{
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHair01, 18),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHair02, 18),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHair03, 18),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHair04, 18),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHairCap02, 14),
		new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.FemaleHairCap02Alt, 14)
	};
	#endregion

	#region Public Methods
	public int RollHair(UnitCombatRankDefinition _rank, CharacterGender _gender)
	{
		return _gender == CharacterGender.Female
			? RollWeighted(m_FemaleHairWeights, UnitHeadAppearanceVariantIds.FemaleHair01)
			: RollWeighted(ResolveMaleHairWeights(_rank), UnitHeadAppearanceVariantIds.MaleHairShort04);
	}

	public int RollHat(CharacterGender _gender, int _hairVariant)
	{
		if (!UnitHeadAppearanceVariantIds.CanUseStandaloneHat(_gender, _hairVariant))
			return UnitHeadAppearanceVariantIds.HatNone;

		return RollWeighted(m_StandaloneHatWeights, UnitHeadAppearanceVariantIds.HatNone);
	}

	public int RollBeard(UnitCombatRankDefinition _rank, CharacterGender _gender)
	{
		if (_gender != CharacterGender.Male)
			return UnitHeadAppearanceVariantIds.BeardNone;

		return RollWeighted(ResolveMaleBeardWeights(_rank), UnitHeadAppearanceVariantIds.BeardNone);
	}

	public static UnitHeadAppearanceRankTable CreateDefaultRuntimeInstance()
	{
		UnitHeadAppearanceRankTable table = CreateInstance<UnitHeadAppearanceRankTable>();
		table.m_RankWeights = CreateDefaultRankWeights();
		return table;
	}

	public static UnitHeadAppearanceRankWeights[] CreateDefaultRankWeights()
	{
		return new[]
		{
			CreateWeights(
				"Rank_Recruit",
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairBald, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShort04, 90)
				},
				new[] { new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.BeardNone, 100) }),
			CreateWeights(
				"Rank_Soldier",
				new[] { new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShort04, 100) },
				new[] { new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.BeardNone, 100) }),
			CreateWeights(
				"Rank_Veteran",
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairBald, 8),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShort04, 35),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairLongBack02, 9),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairRaised03, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairCurly05, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairMessy06, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairStylish07, 7),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSides08, 7),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSidesLong10, 4)
				},
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.BeardNone, 90),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Mustache01, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard04Mustache, 2),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard09Mustache, 2)
				}),
			CreateWeights(
				"Rank_Specialist",
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairBald, 5),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShort04, 20),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairLongBack02, 14),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairRaised03, 13),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairCurly05, 13),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairMessy06, 13),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairStylish07, 11),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSides08, 11),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSidesLong10, 10)
				},
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.BeardNone, 62),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Mustache01, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard04Mustache, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard09Mustache, 5),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard01, 5),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard04, 4),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard09, 4),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard10, 2),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard11, 2)
				}),
			CreateWeights(
				"Rank_Elite",
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairBald, 3),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShort04, 10),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairLongBack02, 16),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairRaised03, 14),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairCurly05, 14),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairMessy06, 14),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairStylish07, 13),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSides08, 13),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.MaleHairShavedSidesLong10, 13)
				},
				new[]
				{
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.BeardNone, 38),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Mustache01, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard04Mustache, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard09Mustache, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard01, 9),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard04, 9),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard09, 9),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard10, 8),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard11, 6),
					new UnitHeadAppearanceVariantWeight(UnitHeadAppearanceVariantIds.Beard12, 3)
				})
		};
	}
	#endregion

	#region Private Methods
	private UnitHeadAppearanceVariantWeight[] ResolveMaleHairWeights(UnitCombatRankDefinition _rank)
	{
		UnitHeadAppearanceRankWeights rankWeights = FindRankWeights(_rank);
		return rankWeights != null && rankWeights.MaleHairWeights != null && rankWeights.MaleHairWeights.Length > 0
			? rankWeights.MaleHairWeights
			: CreateDefaultRankWeights()[0].MaleHairWeights;
	}

	private UnitHeadAppearanceVariantWeight[] ResolveMaleBeardWeights(UnitCombatRankDefinition _rank)
	{
		UnitHeadAppearanceRankWeights rankWeights = FindRankWeights(_rank);
		return rankWeights != null && rankWeights.MaleBeardWeights != null && rankWeights.MaleBeardWeights.Length > 0
			? rankWeights.MaleBeardWeights
			: CreateDefaultRankWeights()[0].MaleBeardWeights;
	}

	private UnitHeadAppearanceRankWeights FindRankWeights(UnitCombatRankDefinition _rank)
	{
		if (m_RankWeights == null || _rank == null)
			return null;

		for (int i = 0; i < m_RankWeights.Length; i++)
		{
			UnitHeadAppearanceRankWeights rankWeights = m_RankWeights[i];
			if (rankWeights != null && rankWeights.MatchesRank(_rank))
				return rankWeights;
		}

		return null;
	}

	private static int RollWeighted(UnitHeadAppearanceVariantWeight[] _weights, int _fallback)
	{
		if (_weights == null || _weights.Length == 0)
			return _fallback;

		int total = 0;
		for (int i = 0; i < _weights.Length; i++)
			total += Mathf.Max(0, _weights[i].Weight);

		if (total <= 0)
			return _fallback;

		int roll = UnityEngine.Random.Range(0, total);
		for (int i = 0; i < _weights.Length; i++)
		{
			roll -= Mathf.Max(0, _weights[i].Weight);
			if (roll < 0)
				return _weights[i].VariantIndex;
		}

		return _weights[_weights.Length - 1].VariantIndex;
	}

	private static UnitHeadAppearanceRankWeights CreateWeights(
		string _rankAssetName,
		UnitHeadAppearanceVariantWeight[] _hairWeights,
		UnitHeadAppearanceVariantWeight[] _beardWeights)
	{
		return new UnitHeadAppearanceRankWeights(_rankAssetName, _hairWeights, _beardWeights);
	}
	#endregion
}
