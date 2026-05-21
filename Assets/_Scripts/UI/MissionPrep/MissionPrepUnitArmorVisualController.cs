using UnityEngine;

/// <summary>
/// Переключает визуал тела (лёгкая / тяжёлая броня) на префабе юнита для экрана предмиссии.
/// По умолчанию ищет <c>SM_Chr_Soldier_Male_02</c> (лёгкая) и <c>SM_Chr_Soldier_Male_01</c> (тяжёлая).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitArmorVisualController : MonoBehaviour
{
	#region Constants
	public const int LightArmorIndex = 0;
	public const int HeavyArmorIndex = 1;
	public const int ArmorVariantCount = 2;

	private const string c_LightArmorRootName = "SM_Chr_Soldier_Male_02";
	private const string c_HeavyArmorRootName = "SM_Chr_Soldier_Male_01";
	#endregion

	#region Serialized Fields
	[SerializeField] private GameObject m_LightArmorVisualRoot;
	[SerializeField] private GameObject m_HeavyArmorVisualRoot;
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
		ResolveVisualRootsIfNeeded();

		int clamped = Mathf.Clamp(_armorIndex, 0, ArmorVariantCount - 1);
		m_CurrentArmorIndex = clamped;

		bool useLight = clamped == LightArmorIndex;
		SetBodyVisualActive(m_LightArmorVisualRoot, useLight);
		SetBodyVisualActive(m_HeavyArmorVisualRoot, !useLight);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (m_LightArmorVisualRoot == null || m_HeavyArmorVisualRoot == null)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepUnitArmorVisualController)} on {name}: не найдены меши брони. " +
				$"Лёгкая: '{c_LightArmorRootName}', тяжёлая: '{c_HeavyArmorRootName}'.",
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
		if (m_LightArmorVisualRoot == null)
			m_LightArmorVisualRoot = FindChildByName(transform, c_LightArmorRootName);

		if (m_HeavyArmorVisualRoot == null)
			m_HeavyArmorVisualRoot = FindChildByName(transform, c_HeavyArmorRootName);
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
