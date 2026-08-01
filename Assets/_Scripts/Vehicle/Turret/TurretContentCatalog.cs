using UnityEngine;

/// <summary>
/// Ссылки на турельные Item/Ammo для runtime (лежит в Resources/Turret).
/// </summary>
[CreateAssetMenu(fileName = "TurretContentCatalog", menuName = "Polygone/Turret/Content Catalog", order = 50)]
public sealed class TurretContentCatalog : ScriptableObject
{
	#region Constants
	public const string ResourcesPath = "Turret/TurretContentCatalog";
	#endregion

	#region Serialized Fields
	[SerializeField] private ItemDefinition m_M2Browning;
	[SerializeField] private ItemDefinition m_Mk19;
	[SerializeField] private ItemDefinition m_FrontalShield;
	[SerializeField] private ItemDefinition m_SurroundShield;
	[SerializeField] private ItemDefinition m_M2MagazineBox;
	[SerializeField] private ItemDefinition m_Mk19MagazineBox;
	[SerializeField] private AmmoDefinition m_Ammo127;
	[SerializeField] private AmmoDefinition m_Ammo40;
	#endregion

	#region Public Properties
	public ItemDefinition M2Browning => m_M2Browning;
	public ItemDefinition Mk19 => m_Mk19;
	public ItemDefinition FrontalShield => m_FrontalShield;
	public ItemDefinition SurroundShield => m_SurroundShield;
	public ItemDefinition M2MagazineBox => m_M2MagazineBox;
	public ItemDefinition Mk19MagazineBox => m_Mk19MagazineBox;
	public AmmoDefinition Ammo127 => m_Ammo127;
	public AmmoDefinition Ammo40 => m_Ammo40;
	#endregion

	#region Static
	private static TurretContentCatalog s_Cached;

	public static TurretContentCatalog Get()
	{
		if (s_Cached == null)
			s_Cached = Resources.Load<TurretContentCatalog>(ResourcesPath);
		return s_Cached;
	}
	#endregion
}
