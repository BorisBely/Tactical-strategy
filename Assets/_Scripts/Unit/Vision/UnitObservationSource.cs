using UnityEngine;

/// <summary>
/// Provides the world-space origin (and optional eye point) from which vision observes.
/// Answers only “from where does this unit observe?” — eyes, sight, later vehicle mounts.
/// Does not scan the world.
/// </summary>
public interface IObservationSource
{
	Vector3 GetOriginWorld();
	Vector3 GetEyeWorldPosition();
	bool TryGetSightTransform(out Transform _sight);
	bool IsUsingWeaponSight { get; }
}

/// <summary>
/// Default soldier observation source: eye height, or equipped weapon sight pivot while high-ready.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitObservationSource : MonoBehaviour, IObservationSource
{
	#region Private Fields
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField, Min(0f)] private float m_EyeHeight = 1.6f;

	[Header("Sight (weapon in high ready)")]
	[Tooltip("Rare per-unit override. Usually set on EquippedWeapon → Sight Pivot.")]
	[SerializeField] private Transform m_SightPivotOverride;
	[Tooltip("If Override empty and EquippedWeapon has no Sight Pivot: find child under weapon visual with this name.")]
	[SerializeField] private string m_SightPivotChildName = "";

	private Transform m_CachedSightFromWeapon;
	private ItemDefinition m_CachedSightWeaponDef;
	private Transform m_MountOriginOverride;
	#endregion

	#region Public Properties
	public float EyeHeight
	{
		get => m_EyeHeight;
		set => m_EyeHeight = Mathf.Max(0f, value);
	}

	public bool IsUsingWeaponSight => IsWeaponReadyForSightCone() && TryGetSightTransform(out _);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}
	#endregion

	#region Public Methods
	public void ApplyConfig(
		float _eyeHeight,
		Transform _sightPivotOverride,
		string _sightPivotChildName,
		UnitEquipment _equipment,
		UnitWeaponReadyHandsLayer _readyHands)
	{
		m_EyeHeight = Mathf.Max(0f, _eyeHeight);
		m_SightPivotOverride = _sightPivotOverride;
		m_SightPivotChildName = _sightPivotChildName ?? string.Empty;
		if (_equipment != null)
			m_Equipment = _equipment;
		if (_readyHands != null)
			m_ReadyHands = _readyHands;
		InvalidateSightCache();
	}

	public void SetMountOriginOverride(Transform _origin)
	{
		m_MountOriginOverride = _origin;
	}

	public Vector3 GetEyeWorldPosition()
	{
		if (m_MountOriginOverride != null)
			return m_MountOriginOverride.position;
		return transform.position + Vector3.up * m_EyeHeight;
	}

	public Vector3 GetOriginWorld()
	{
		if (m_MountOriginOverride != null)
			return m_MountOriginOverride.position;
		if (TryGetSightTransform(out Transform sight))
			return sight.position;
		return GetEyeWorldPosition();
	}

	public bool TryGetSightTransform(out Transform _sight)
	{
		_sight = GetActiveSightTransform();
		return _sight != null;
	}

	/// <summary>Called when high/low ready changes so sight cache stays coherent.</summary>
	public void InvalidateSightCache()
	{
		m_CachedSightWeaponDef = null;
		m_CachedSightFromWeapon = null;
	}

	public bool IsWeaponReadyForSightCone()
	{
		if (m_ReadyHands == null || m_Equipment == null)
			return false;

		UnitRocketLauncherOrderController rocketOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (rocketOrder != null &&
		    rocketOrder.IsBusy &&
		    (rocketOrder.CurrentPhase == RocketLauncherOrderPhase.Aiming ||
		     rocketOrder.CurrentPhase == RocketLauncherOrderPhase.Firing))
			return false;

		ItemDefinition def = m_Equipment.EquippedDefinition;
		if (def == null || !def.IsEquipment || def.EquipmentKind != EquipmentKind.Weapon)
			return false;
		return m_ReadyHands.IsWeaponEquippedAndReady();
	}
	#endregion

	#region Private Methods
	private Transform GetActiveSightTransform()
	{
		if (!IsWeaponReadyForSightCone())
		{
			m_CachedSightWeaponDef = null;
			m_CachedSightFromWeapon = null;
			return null;
		}

		if (m_SightPivotOverride != null)
			return m_SightPivotOverride;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon != null && weapon.SightPivotTransform != null)
			return weapon.SightPivotTransform;

		if (string.IsNullOrWhiteSpace(m_SightPivotChildName))
			return null;

		Transform weaponRoot = m_Equipment.MainWeaponRoot;
		if (weaponRoot == null)
			return null;

		ItemDefinition def = m_Equipment.EquippedDefinition;
		if (def != m_CachedSightWeaponDef)
		{
			m_CachedSightWeaponDef = def;
			m_CachedSightFromWeapon = FindChildTransformByName(weaponRoot, m_SightPivotChildName);
		}

		return m_CachedSightFromWeapon;
	}

	private static Transform FindChildTransformByName(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}
	#endregion
}
