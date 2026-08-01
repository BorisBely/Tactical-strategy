using UnityEngine;

/// <summary>
/// Резолв иерархии турели Light Armored Car по именам мешей.
/// Корень yaw — объект <c>Turret</c> или fallback <c>Gun/1</c>. <c>Gunner.005</c> не трогаем.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleTurretHierarchyBinder : MonoBehaviour
{
	#region Constants
	private const string c_TurretPreferred = "Turret";
	private const string c_GunGroup = "Gun";
	private const string c_GunBase127 = "SM_Veh_Pickup_Technical_01_Gun_Base";
	private const string c_Gun127 = "GameObjectGun.12.7";
	private const string c_Mag127 = "SM_Veh_Pickup_Technical_01_Gun_Mag";
	private const string c_Mk19Base = "MK19Base";
	private const string c_Mk19 = "MK19";
	private const string c_MagMk19 = "Mag MK19";
	private const string c_ArmorFrontal = "Armor12.7";
	private const string c_ArmorFrontalDefault = "Armor12.7 (1)";
	private const string c_ArmorSurround = "Armor Gunner";
	private const string c_Plug = "SM_Veh_Light_Armored_Car_01_Plug";
	private const string c_GunnerHatchMesh = "SM_Veh_Light_Armored_Car_01_Gunner.005";
	#endregion

	#region Serialized Fields
	[SerializeField] private Transform m_Turret;
	[SerializeField] private Transform m_GunBase127;
	[SerializeField] private Transform m_Gun127;
	[SerializeField] private Transform m_Mag127;
	[SerializeField] private Transform m_Mk19Base;
	[SerializeField] private Transform m_Mk19;
	[SerializeField] private Transform m_MagMk19;
	[SerializeField] private Transform m_ArmorFrontal127;
	[SerializeField] private Transform m_ArmorFrontalMk19;
	[SerializeField] private Transform m_ArmorFrontalDefault;
	[SerializeField] private Transform m_ArmorSurround;
	[SerializeField] private GameObject m_Plug;
	[SerializeField] private GameObject m_GunnerHatchMesh;
	#endregion

	#region Public Properties
	public Transform Turret => m_Turret;
	public Transform GunBase127 => m_GunBase127;
	public Transform Gun127 => m_Gun127;
	public Transform Mag127 => m_Mag127;
	public Transform Mk19Base => m_Mk19Base;
	public Transform Mk19 => m_Mk19;
	public Transform MagMk19 => m_MagMk19;
	public Transform ArmorFrontal127 => m_ArmorFrontal127;
	public Transform ArmorFrontalMk19 => m_ArmorFrontalMk19;
	public Transform ArmorFrontalDefault => m_ArmorFrontalDefault;
	public Transform ArmorSurround => m_ArmorSurround;
	public GameObject Plug => m_Plug;
	public GameObject GunnerHatchMesh => m_GunnerHatchMesh;
	public bool IsBound => m_Turret != null;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureBound();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying)
			EnsureBound();
	}
#endif
	#endregion

	#region Public Methods
	public void EnsureBound()
	{
		Transform root = transform;
		if (m_Turret == null)
			m_Turret = ResolveTurretRoot(root);

		if (m_GunBase127 == null)
			m_GunBase127 = FindDeep(root, c_GunBase127);
		if (m_Gun127 == null)
			m_Gun127 = FindDeep(root, c_Gun127);
		if (m_Mag127 == null)
			m_Mag127 = FindDeep(root, c_Mag127);
		if (m_Mk19Base == null)
			m_Mk19Base = FindDeep(root, c_Mk19Base);
		if (m_Mk19 == null)
			m_Mk19 = FindDeep(root, c_Mk19);
		if (m_MagMk19 == null)
			m_MagMk19 = FindDeep(root, c_MagMk19);
		if (m_ArmorSurround == null)
			m_ArmorSurround = FindDeep(root, c_ArmorSurround);

		ResolveFrontalShields(root);

		if (m_ArmorFrontalDefault == null)
			m_ArmorFrontalDefault = FindDeep(root, c_ArmorFrontalDefault);

		if (m_Plug == null)
		{
			Transform plug = FindDeep(root, c_Plug);
			if (plug != null)
				m_Plug = plug.gameObject;
		}

		if (m_GunnerHatchMesh == null)
		{
			Transform hatch = FindDeep(root, c_GunnerHatchMesh);
			if (hatch != null)
				m_GunnerHatchMesh = hatch.gameObject;
		}
	}

	public Transform GetActiveWeaponBase(TurretWeaponVariant _variant)
	{
		return _variant switch
		{
			TurretWeaponVariant.Browning127 => m_GunBase127,
			TurretWeaponVariant.Mk19 => m_Mk19Base,
			_ => null
		};
	}

	public Transform GetActiveWeaponPitch(TurretWeaponVariant _variant)
	{
		return _variant switch
		{
			TurretWeaponVariant.Browning127 => m_Gun127,
			TurretWeaponVariant.Mk19 => m_Mk19,
			_ => null
		};
	}

	public Transform GetActiveFrontalArmor(TurretWeaponVariant _variant)
	{
		return _variant switch
		{
			TurretWeaponVariant.Browning127 => m_ArmorFrontal127,
			TurretWeaponVariant.Mk19 => m_ArmorFrontalMk19,
			_ => null
		};
	}
	#endregion

	#region Private Methods
	private static Transform ResolveTurretRoot(Transform _root)
	{
		Transform gun = FindDeep(_root, c_GunGroup);
		if (gun != null)
			return gun;

		return FindDeep(_root, c_TurretPreferred);
	}

	private void ResolveFrontalShields(Transform _root)
	{
		if (m_ArmorFrontal127 != null && m_ArmorFrontalMk19 != null)
			return;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			Transform t = all[i];
			if (t == null || t.name != c_ArmorFrontal)
				continue;

			if (m_GunBase127 != null && t.IsChildOf(m_GunBase127))
			{
				m_ArmorFrontal127 = t;
				continue;
			}

			if (m_Mk19Base != null && t.IsChildOf(m_Mk19Base))
			{
				m_ArmorFrontalMk19 = t;
				continue;
			}

			if (m_ArmorFrontal127 == null)
				m_ArmorFrontal127 = t;
			else if (m_ArmorFrontalMk19 == null && t != m_ArmorFrontal127)
				m_ArmorFrontalMk19 = t;
		}
	}

	private static Transform FindDeep(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		if (_root.name == _name)
			return _root;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		return null;
	}
	#endregion
}
