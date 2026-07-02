using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Выбирает и активирует body-меш юнита в зависимости от архетипа и пола.
/// Поддерживает рантайм-реконфигурацию.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitBodyMeshSelector : MonoBehaviour
{
	#region Private Fields
	private Dictionary<string, GameObject> m_MeshCache;
	private UnitBodyMeshArchetype m_CurrentArchetype;
	private CharacterGender m_CurrentGender;
	private string m_CurrentMeshName;
	private bool m_IsInitialized;
	#endregion

	#region Public Properties
	public UnitBodyMeshArchetype CurrentArchetype => m_CurrentArchetype;
	public CharacterGender CurrentGender => m_CurrentGender;
	public bool IsHeadpieceActive { get; private set; }

	public bool IsHeadCovered
	{
		get
		{
			if (m_CurrentMeshName == null)
				return false;

			return m_CurrentMeshName == "SM_Chr_Civilian_Female_01" ||
			       m_CurrentMeshName == "SM_Chr_Civilian_Female_02";
		}
	}

	public UnitArmorType DefaultArmorType => m_CurrentArchetype switch
	{
		UnitBodyMeshArchetype.Civilian => UnitArmorType.None,
		UnitBodyMeshArchetype.Insurgent => UnitArmorType.Light,
		UnitBodyMeshArchetype.Leader => UnitArmorType.Light,
		UnitBodyMeshArchetype.Contractor => UnitArmorType.None,
		UnitBodyMeshArchetype.Pilot => UnitArmorType.None,
		UnitBodyMeshArchetype.Bombsuit => UnitArmorType.Heavy,
		UnitBodyMeshArchetype.Ghillie => UnitArmorType.None,
		_ => UnitArmorType.None
	};

	public static UnitArmorType GetDefaultArmorType(UnitBodyMeshArchetype _archetype) => _archetype switch
	{
		UnitBodyMeshArchetype.Civilian => UnitArmorType.None,
		UnitBodyMeshArchetype.Insurgent => UnitArmorType.Light,
		UnitBodyMeshArchetype.Leader => UnitArmorType.Light,
		UnitBodyMeshArchetype.Contractor => UnitArmorType.None,
		UnitBodyMeshArchetype.Pilot => UnitArmorType.None,
		UnitBodyMeshArchetype.Bombsuit => UnitArmorType.Heavy,
		UnitBodyMeshArchetype.Ghillie => UnitArmorType.None,
		_ => UnitArmorType.None
	};
	#endregion

	#region Public Methods
	public void SelectMesh(UnitBodyMeshArchetype _archetype, CharacterGender _gender, int _variantIndex = -1)
	{
		EnsureCache();
		DeactivateAllKnownMeshes();

		string[] variants = GetVariantNames(_archetype, _gender);
		if (variants == null || variants.Length == 0)
		{
			Debug.LogWarning($"{nameof(UnitBodyMeshSelector)} on {name}: no body meshes for {_archetype} + {_gender}.", this);
			return;
		}

		int index = _variantIndex >= 0 ? _variantIndex % variants.Length : Random.Range(0, variants.Length);
		string meshName = variants[index];

		if (m_MeshCache.TryGetValue(meshName, out GameObject meshRoot) && meshRoot != null)
			meshRoot.SetActive(true);

		ActivateAttachments(_archetype);

		m_CurrentArchetype = _archetype;
		m_CurrentGender = _gender;
		m_CurrentMeshName = meshName;
		m_IsInitialized = true;
	}
	#endregion

	#region Private Methods
	private void EnsureCache()
	{
		if (m_MeshCache != null)
			return;

		m_MeshCache = new Dictionary<string, GameObject>();
		Transform[] children = GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			string name = children[i].name;
			if (string.IsNullOrEmpty(name))
				continue;

			if (!m_MeshCache.ContainsKey(name))
				m_MeshCache[name] = children[i].gameObject;
		}
	}

	private void DeactivateAllKnownMeshes()
	{
		foreach (string[] list in s_AllMeshNames)
		{
			for (int i = 0; i < list.Length; i++)
			{
				if (m_MeshCache.TryGetValue(list[i], out GameObject go) && go != null)
					go.SetActive(false);
			}
		}
	}

	private void ActivateAttachments(UnitBodyMeshArchetype _archetype)
	{
		IsHeadpieceActive = false;

		switch (_archetype)
		{
			case UnitBodyMeshArchetype.Insurgent:
			case UnitBodyMeshArchetype.Leader:
				ActivateRandomOneOrNone(s_InsurgentNeckAttachments);
				IsHeadpieceActive = ActivateRandomOneOrNone(s_InsurgentHeadpieceAttachments);
				break;

			case UnitBodyMeshArchetype.Ghillie:
				ActivateAll(s_GhillieAttachments);
				break;
		}
	}

	private bool ActivateRandomOneOrNone(string[] _names)
	{
		DeactivateAll(_names);

		if (_names == null || _names.Length == 0)
			return false;

		if (Random.value < 0.5f)
			return false;

		int index = Random.Range(0, _names.Length);
		if (m_MeshCache.TryGetValue(_names[index], out GameObject go) && go != null)
			go.SetActive(true);

		return true;
	}

	private void ActivateAll(string[] _names)
	{
		if (_names == null)
			return;

		for (int i = 0; i < _names.Length; i++)
		{
			if (m_MeshCache.TryGetValue(_names[i], out GameObject go) && go != null)
				go.SetActive(true);
		}
	}

	private void DeactivateAll(string[] _names)
	{
		if (_names == null)
			return;

		for (int i = 0; i < _names.Length; i++)
		{
			if (m_MeshCache.TryGetValue(_names[i], out GameObject go) && go != null)
				go.SetActive(false);
		}
	}
	#endregion

	#region Static Mesh Data
	private static readonly string[] s_CivilianMale = { "SM_Chr_Civilian_Male_01", "SM_Chr_Civilian_Male_02" };
	private static readonly string[] s_CivilianFemale = { "SM_Chr_Civilian_Female_01", "SM_Chr_Civilian_Female_02" };

	private static readonly string[] s_InsurgentMale = { "SM_Chr_Insurgent_Male_01", "SM_Chr_Insurgent_Male_02", "SM_Chr_Insurgent_Male_03", "SM_Chr_Insurgent_Male_04", "SM_Chr_Insurgent_Male_05" };
	private static readonly string[] s_InsurgentFemale = { "SM_Chr_Insurgent_Female_01", "SM_Chr_Insurgent_Female_02" };

	private static readonly string[] s_LeaderMale = { "SM_Chr_Leader_Male_01" };

	private static readonly string[] s_SoldierMaleHeavy = { "SM_Chr_Soldier_Male_01" };
	private static readonly string[] s_SoldierMaleLight = { "SM_Chr_Soldier_Male_02" };
	private static readonly string[] s_SoldierFemaleHeavy = { "SM_Chr_Soldier_Female_01" };
	private static readonly string[] s_SoldierFemaleLight = { "SM_Chr_Soldier_Female_02" };

	private static readonly string[] s_ContractorMale = { "SM_Chr_Contractor_Male_01", "SM_Chr_Contractor_Male_02" };
	private static readonly string[] s_ContractorFemale = { "SM_Chr_Contractor_Female_01" };

	private static readonly string[] s_PilotMale = { "SM_Chr_Pilot_Male_01" };
	private static readonly string[] s_PilotFemale = { "SM_Chr_Pilot_Female_01" };

	private static readonly string[] s_BombsuitMale = { "SM_Chr_Bombsuit_Male_01" };
	private static readonly string[] s_GhillieMale = { "SM_Chr_Ghillie_Male_01" };

	private static readonly string[] s_InsurgentNeckAttachments =
	{
		"SM_Chr_Attach_Insurgent_Neck_01",
		"SM_Chr_Attach_Insurgent_Neck_02",
		"SM_Chr_Attach_Insurgent_Neck_03"
	};

	private static readonly string[] s_InsurgentHeadpieceAttachments =
	{
		"SM_Chr_Attach_Insurgent_Headpiece_01",
		"SM_Chr_Attach_Insurgent_Headpiece_02",
		"SM_Chr_Attach_Insurgent_Headpiece_03",
		"SM_Chr_Attach_Insurgent_Headpiece_04"
	};

	private static readonly string[] s_GhillieAttachments = { "SM_Chr_Attack_Ghillie_Mask_01" };

	private static readonly string[][] s_AllMeshNames =
	{
		s_CivilianMale, s_CivilianFemale,
		s_InsurgentMale, s_InsurgentFemale,
		s_LeaderMale,
		s_SoldierMaleHeavy, s_SoldierMaleLight, s_SoldierFemaleHeavy, s_SoldierFemaleLight,
		s_ContractorMale, s_ContractorFemale,
		s_PilotMale, s_PilotFemale,
		s_BombsuitMale,
		s_GhillieMale,
		s_InsurgentNeckAttachments,
		s_InsurgentHeadpieceAttachments,
		s_GhillieAttachments
	};

	private static string[] GetVariantNames(UnitBodyMeshArchetype _archetype, CharacterGender _gender)
	{
		bool isFemale = _gender == CharacterGender.Female;

		switch (_archetype)
		{
			case UnitBodyMeshArchetype.Civilian:
				return isFemale ? s_CivilianFemale : s_CivilianMale;

			case UnitBodyMeshArchetype.Insurgent:
				return isFemale ? s_InsurgentFemale : s_InsurgentMale;

			case UnitBodyMeshArchetype.Leader:
				if (!isFemale)
					return s_LeaderMale;
				return s_InsurgentFemale;

			case UnitBodyMeshArchetype.Contractor:
				return isFemale ? s_ContractorFemale : s_ContractorMale;

			case UnitBodyMeshArchetype.Pilot:
				return isFemale ? s_PilotFemale : s_PilotMale;

			case UnitBodyMeshArchetype.Bombsuit:
				if (!isFemale)
					return s_BombsuitMale;
				return s_SoldierFemaleHeavy;

			case UnitBodyMeshArchetype.Ghillie:
				if (!isFemale)
					return s_GhillieMale;
				return s_CivilianFemale;

			case UnitBodyMeshArchetype.Soldier:
			default:
		return isFemale ? s_SoldierFemaleLight : s_SoldierMaleLight;
		}
	}
	#endregion
}
