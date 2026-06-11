using UnityEngine;

/// <summary>
/// Каталог профилей визуала экипировки для прокрутки предпочтений юнита при старте.
/// </summary>
[CreateAssetMenu(
	fileName = "EquipmentVisualProfileCatalog",
	menuName = "Polygone/Inventory/Equipment Visual Profile Catalog",
	order = 16)]
public sealed class EquipmentVisualProfileCatalog : ScriptableObject
{
	#region Constants
	public const string DefaultAssetPath = "Assets/GameData/Inventory/Helmets/EquipmentVisualProfileCatalog.asset";
	#endregion

	#region Serialized Fields
	[SerializeField] private EquipmentVisualProfileDefinition[] m_Profiles = System.Array.Empty<EquipmentVisualProfileDefinition>();
	#endregion

	#region Public Properties
	public EquipmentVisualProfileDefinition[] Profiles => m_Profiles ?? System.Array.Empty<EquipmentVisualProfileDefinition>();
	#endregion

	#region Public Methods
	public bool TryGetProfile(string _profileId, out EquipmentVisualProfileDefinition _profile)
	{
		_profile = null;
		if (string.IsNullOrWhiteSpace(_profileId) || m_Profiles == null)
			return false;

		for (int i = 0; i < m_Profiles.Length; i++)
		{
			EquipmentVisualProfileDefinition candidate = m_Profiles[i];
			if (candidate == null || !string.Equals(candidate.ProfileId, _profileId, System.StringComparison.Ordinal))
				continue;

			_profile = candidate;
			return true;
		}

		return false;
	}
	#endregion
}
