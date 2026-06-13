using UnityEngine;

/// <summary>
/// Переключает визуал тела (лёгкая / тяжёлая броня) на префабе юнита.
/// Лёгкая: <c>Soldier_*_02</c>, тяжёлая: <c>Soldier_*_01</c> — отдельные пары для мужского и женского меша.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitArmorVisualController : MonoBehaviour
{
	#region Constants
	public const int LightArmorIndex = 0;
	public const int HeavyArmorIndex = 1;
	public const int ArmorVariantCount = 2;

	private const string c_MaleLightArmorRootName = "SM_Chr_Soldier_Male_02";
	private const string c_MaleHeavyArmorRootName = "SM_Chr_Soldier_Male_01";
	private const string c_FemaleLightArmorRootName = "SM_Chr_Soldier_Female_02";
	private const string c_FemaleHeavyArmorRootName = "SM_Chr_Soldier_Female_01";
	#endregion

	#region Serialized Fields
	[SerializeField] private GameObject m_MaleLightArmorVisualRoot;
	[SerializeField] private GameObject m_MaleHeavyArmorVisualRoot;
	[SerializeField] private GameObject m_FemaleLightArmorVisualRoot;
	[SerializeField] private GameObject m_FemaleHeavyArmorVisualRoot;
	[SerializeField, Min(0)] private int m_DefaultArmorIndex = LightArmorIndex;
	#endregion

	#region Private Fields
	private int m_CurrentArmorIndex = -1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveVisualRootsIfNeeded();
	}

	private void OnEnable()
	{
		ResolveVisualRootsIfNeeded();
		if (m_CurrentArmorIndex < 0)
			ApplyArmorVisual(m_DefaultArmorIndex);
	}
	#endregion

	#region Public Methods
	public int CurrentArmorIndex => m_CurrentArmorIndex >= 0 ? m_CurrentArmorIndex : m_DefaultArmorIndex;

	public void ApplyArmorVisual(int _armorIndex)
	{
		CharacterGender gender = ResolveGender();
		ApplyArmorVisual(_armorIndex, gender);
	}

	public void ApplyArmorVisual(int _armorIndex, CharacterGender _gender)
	{
		ResolveVisualRootsIfNeeded();

		int clamped = Mathf.Clamp(_armorIndex, 0, ArmorVariantCount - 1);
		m_CurrentArmorIndex = clamped;

		bool useLight = clamped == LightArmorIndex;
		bool useFemale = _gender == CharacterGender.Female;

		SetBodyVisualActive(m_MaleLightArmorVisualRoot, !useFemale && useLight);
		SetBodyVisualActive(m_MaleHeavyArmorVisualRoot, !useFemale && !useLight);
		SetBodyVisualActive(m_FemaleLightArmorVisualRoot, useFemale && useLight);
		SetBodyVisualActive(m_FemaleHeavyArmorVisualRoot, useFemale && !useLight);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!HasResolvedPair(_gender))
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepUnitArmorVisualController)} on {name}: не найдены меши брони для {_gender}. " +
				$"Муж.: '{c_MaleLightArmorRootName}' / '{c_MaleHeavyArmorRootName}', " +
				$"жен.: '{c_FemaleLightArmorRootName}' / '{c_FemaleHeavyArmorRootName}'.",
				this);
		}
#endif
	}

	public static MissionPrepUnitArmorVisualController GetOrCreate(GameObject _unitRoot, int _defaultArmorIndex = LightArmorIndex)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out MissionPrepUnitArmorVisualController controller))
			controller = _unitRoot.AddComponent<MissionPrepUnitArmorVisualController>();

		controller.m_DefaultArmorIndex = Mathf.Clamp(_defaultArmorIndex, 0, ArmorVariantCount - 1);
		controller.ApplyArmorVisual(controller.m_DefaultArmorIndex);
		return controller;
	}
	#endregion

	#region Private Methods
	private void ResolveVisualRootsIfNeeded()
	{
		if (m_MaleLightArmorVisualRoot == null)
			m_MaleLightArmorVisualRoot = FindChildByName(transform, c_MaleLightArmorRootName);

		if (m_MaleHeavyArmorVisualRoot == null)
			m_MaleHeavyArmorVisualRoot = FindChildByName(transform, c_MaleHeavyArmorRootName);

		if (m_FemaleLightArmorVisualRoot == null)
			m_FemaleLightArmorVisualRoot = FindChildByName(transform, c_FemaleLightArmorRootName);

		if (m_FemaleHeavyArmorVisualRoot == null)
			m_FemaleHeavyArmorVisualRoot = FindChildByName(transform, c_FemaleHeavyArmorRootName);
	}

	private CharacterGender ResolveGender()
	{
		if (TryGetComponent(out UnitCharacterAppearance appearance) && appearance.IsGenderInitialized)
			return appearance.Gender;

		UnitCharacterAppearance childAppearance = GetComponentInChildren<UnitCharacterAppearance>(true);
		return childAppearance != null && childAppearance.IsGenderInitialized
			? childAppearance.Gender
			: CharacterGender.Male;
	}

	private bool HasResolvedPair(CharacterGender _gender)
	{
		if (_gender == CharacterGender.Female)
			return m_FemaleLightArmorVisualRoot != null && m_FemaleHeavyArmorVisualRoot != null;

		return m_MaleLightArmorVisualRoot != null && m_MaleHeavyArmorVisualRoot != null;
	}

	private static void SetBodyVisualActive(GameObject _bodyRoot, bool _active)
	{
		if (_bodyRoot == null)
			return;

		_bodyRoot.SetActive(_active);

		if (_bodyRoot.TryGetComponent(out SkinnedMeshRenderer skinnedRenderer))
			skinnedRenderer.enabled = _active;
	}

	private static GameObject FindChildByName(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i] != null && children[i].name == _name)
				return children[i].gameObject;
		}

		return null;
	}
	#endregion
}
