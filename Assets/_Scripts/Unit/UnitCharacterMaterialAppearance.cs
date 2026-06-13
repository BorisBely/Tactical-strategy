using UnityEngine;

/// <summary>
/// Применяет палитру Polygon (камуфляж + оттенок кожи) к рендерерам персонажа и декора.
/// Оттенок кожи бросается при спавне; камуфляж задаётся пресетом / конфигом спавна.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitCharacterMaterialAppearance : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitCharacterMaterialPalette m_Palette;
	[SerializeField] private UnitSkinTone m_SkinTone = UnitSkinTone.Medium;
	[SerializeField] private bool m_IsSkinToneInitialized;
	[SerializeField] private UnitCamouflagePattern m_Camouflage = UnitCamouflagePattern.Desert;
	#endregion

	#region Public Properties
	public UnitSkinTone SkinTone => m_SkinTone;
	public bool IsSkinToneInitialized => m_IsSkinToneInitialized;
	public UnitCamouflagePattern Camouflage => m_Camouflage;
	#endregion

	#region Public Methods
	public void RollInitialSkinTone()
	{
		if (m_IsSkinToneInitialized)
			return;

		int rolled = Random.Range(0, 3);
		InitializeSkinTone((UnitSkinTone)rolled);
	}

	public void InitializeSkinTone(UnitSkinTone _skinTone)
	{
		if (m_IsSkinToneInitialized && m_SkinTone == _skinTone)
			return;

		m_SkinTone = _skinTone;
		m_IsSkinToneInitialized = true;
		ApplyCurrentVisual();
	}

	public void SetCamouflage(UnitCamouflagePattern _pattern)
	{
		m_Camouflage = _pattern;
		ApplyCurrentVisual();
	}

	public void SetCamouflageIndex(int _index)
	{
		SetCamouflage(UnitCamouflagePatternUtility.FromIndex(_index));
	}

	public void ApplyCurrentVisual()
	{
		UnitCharacterMaterialPalette palette = ResolvePalette();
		if (palette == null || !palette.TryResolve(m_Camouflage, m_SkinTone, out Material material) || material == null)
			return;

		ApplyMaterialToCharacterRenderers(material);
	}

	public static UnitCharacterMaterialAppearance GetOrCreate(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out UnitCharacterMaterialAppearance appearance))
			appearance = _unitRoot.AddComponent<UnitCharacterMaterialAppearance>();

		return appearance;
	}
	#endregion

	#region Private Methods
	private UnitCharacterMaterialPalette ResolvePalette()
	{
		if (m_Palette != null)
			return m_Palette;

		m_Palette = Resources.Load<UnitCharacterMaterialPalette>("Character/UnitCharacterMaterialPalette");
		return m_Palette;
	}

	private void ApplyMaterialToCharacterRenderers(Material _material)
	{
		Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null)
				continue;

			Material shared = renderer.sharedMaterial;
			if (!IsPolygonCharacterPaletteMaterial(shared))
				continue;

			renderer.sharedMaterial = _material;
		}
	}

	private static bool IsPolygonCharacterPaletteMaterial(Material _material)
	{
		if (_material == null)
			return false;

		string name = _material.name;
		if (!name.StartsWith("PolygonMilitary_Mat_0"))
			return false;

		return name.Length >= 24 &&
		       name[22] == '_' &&
		       (name[23] == 'A' || name[23] == 'B' || name[23] == 'C');
	}
	#endregion
}
