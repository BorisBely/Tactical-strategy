using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Таблица материалов Polygon: камуфляж × оттенок кожи.
/// </summary>
[CreateAssetMenu(
	fileName = "UnitCharacterMaterialPalette",
	menuName = "Polygone/Character/Unit Material Palette",
	order = 0)]
public sealed class UnitCharacterMaterialPalette : ScriptableObject
{
	#region Constants
	public const string DefaultAssetPath = "Assets/GameData/Character/UnitCharacterMaterialPalette.asset";
	#endregion

	#region Serialized Fields
	[FormerlySerializedAs("m_SiroccoMaterials")]
	[SerializeField] private Material[] m_DesertMaterials = new Material[3];
	[FormerlySerializedAs("m_BasaltMaterials")]
	[SerializeField] private Material[] m_UrbanMaterials = new Material[3];
	[FormerlySerializedAs("m_WoodlandMaterials")]
	[SerializeField] private Material[] m_ForestMaterials = new Material[3];
	[FormerlySerializedAs("m_CanopyMaterials")]
	[SerializeField] private Material[] m_JungleMaterials = new Material[3];
	#endregion

	#region Public Methods
	public Material Resolve(UnitCamouflagePattern _pattern, UnitSkinTone _skinTone)
	{
		Material[] row = GetRow(_pattern);
		int skinIndex = Mathf.Clamp((int)_skinTone, 0, 2);
		return row != null && skinIndex < row.Length ? row[skinIndex] : null;
	}

	public bool TryResolve(UnitCamouflagePattern _pattern, UnitSkinTone _skinTone, out Material _material)
	{
		_material = Resolve(_pattern, _skinTone);
		return _material != null;
	}
	#endregion

	#region Private Methods
	private Material[] GetRow(UnitCamouflagePattern _pattern)
	{
		return _pattern switch
		{
			UnitCamouflagePattern.Urban => m_UrbanMaterials,
			UnitCamouflagePattern.Forest => m_ForestMaterials,
			UnitCamouflagePattern.Jungle => m_JungleMaterials,
			_ => m_DesertMaterials
		};
	}
	#endregion

	#if UNITY_EDITOR
	public void SetRow(UnitCamouflagePattern _pattern, Material[] _materials)
	{
		Material[] copy = _materials ?? new Material[3];
		switch (_pattern)
		{
			case UnitCamouflagePattern.Urban:
				m_UrbanMaterials = copy;
				break;
			case UnitCamouflagePattern.Forest:
				m_ForestMaterials = copy;
				break;
			case UnitCamouflagePattern.Jungle:
				m_JungleMaterials = copy;
				break;
			default:
				m_DesertMaterials = copy;
				break;
		}
	}
	#endif
}
