using UnityEngine;

/// <summary>
/// Переключает декора на префабе экипированного шлема.
/// Все декора на префабе по умолчанию выключены (default / loot вид).
/// </summary>
[DisallowMultipleComponent]
public sealed class HelmetEquippedVisual : MonoBehaviour
{
	#region Serialized Fields
	[Header("Primary Decor (variant 1 / 2)")]
	[SerializeField] private GameObject m_PrimaryVariant1Root;
	[SerializeField] private GameObject m_PrimaryVariant2Root;

	[Header("Optional Chin Strap")]
	[SerializeField] private GameObject m_ChinStrapMaleRoot;
	[SerializeField] private GameObject m_ChinStrapFemaleRoot;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ApplyDefault();
	}
	#endregion

	#region Public Methods
	public void ApplyDefault()
	{
		SetDecorationActive(m_PrimaryVariant1Root, false);
		SetDecorationActive(m_PrimaryVariant2Root, false);
		SetDecorationActive(m_ChinStrapMaleRoot, false);
		SetDecorationActive(m_ChinStrapFemaleRoot, false);
	}

	public void ApplyPreferences(
		UnitEquipmentVisualPreferenceEntry _preference,
		CharacterGender _gender)
	{
		ApplyDefault();

		switch (_preference.PrimaryVariant)
		{
			case 1:
				SetDecorationActive(m_PrimaryVariant1Root, true);
				break;
			case 2:
				SetDecorationActive(m_PrimaryVariant2Root, true);
				break;
		}

		if (!_preference.UseChinStrap)
			return;

		bool useFemale = _gender == CharacterGender.Female;
		SetDecorationActive(m_ChinStrapMaleRoot, !useFemale && m_ChinStrapMaleRoot != null);
		SetDecorationActive(m_ChinStrapFemaleRoot, useFemale && m_ChinStrapFemaleRoot != null);
	}

	public void ResolveDecorationRootsFromChildren()
	{
		if (m_PrimaryVariant1Root == null)
			m_PrimaryVariant1Root = FindChildContainingAny("Goggles_02", "Torch_01");
		if (m_PrimaryVariant2Root == null)
			m_PrimaryVariant2Root = FindChildContainingAny("Goggles_03", "Headset_01");
		if (m_ChinStrapMaleRoot == null)
			m_ChinStrapMaleRoot = FindChildContaining("Chin_Strap", "_Male");
		if (m_ChinStrapFemaleRoot == null)
			m_ChinStrapFemaleRoot = FindChildContaining("Chin_Strap", "_Female");
	}
	#endregion

	#region Private Methods
	private GameObject FindChildContainingAny(params string[] _nameFragments)
	{
		if (_nameFragments == null || _nameFragments.Length == 0)
			return null;

		Transform[] children = GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			Transform child = children[i];
			if (child == transform)
				continue;

			string name = child.name;
			for (int f = 0; f < _nameFragments.Length; f++)
			{
				if (!string.IsNullOrEmpty(_nameFragments[f]) && name.Contains(_nameFragments[f]))
					return child.gameObject;
			}
		}

		return null;
	}

	private GameObject FindChildContaining(string _requiredFragment, string _alsoRequiredFragment = null)
	{
		Transform[] children = GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			Transform child = children[i];
			if (child == transform)
				continue;

			string name = child.name;
			if (!name.Contains(_requiredFragment))
				continue;

			if (!string.IsNullOrEmpty(_alsoRequiredFragment) && !name.Contains(_alsoRequiredFragment))
				continue;

			return child.gameObject;
		}

		return null;
	}

	private static void SetDecorationActive(GameObject _root, bool _active)
	{
		if (_root == null)
			return;

		_root.SetActive(_active);
		if (!_active)
			return;

		EnableCompositeDecorationChildren(_root.transform);
	}

	private static void EnableCompositeDecorationChildren(Transform _root)
	{
		Transform[] descendants = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < descendants.Length; i++)
		{
			Transform child = descendants[i];
			if (child == null || child == _root)
				continue;

			if (IsCompositeDecorationPartName(child.name))
				child.gameObject.SetActive(true);
		}
	}

	private static bool IsCompositeDecorationPartName(string _objectName)
	{
		return !string.IsNullOrEmpty(_objectName) && _objectName.Contains("_Glass");
	}
	#endregion
}
