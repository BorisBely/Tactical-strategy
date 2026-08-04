using UnityEngine;

/// <summary>
/// Точки M2/MK19 на <see cref="VehicleTurretHierarchyBinder.GetActiveWeaponPitch"/>: MuzzleExit, ShellEject, BeltEject и barrel под inner Gun.12.7.
/// </summary>
public static class VehicleTurretCombatSockets
{
	public const string ShellEjectName = "ShellEject";
	public const string BeltEjectName = "BeltEject";
	public const string BarrelRecoilName = "barrel";
	public const string InnerGun127Name = "Gun.12.7";
	public const string Mk19ShellEjectName = "ShellEject_MK19";
	public const string Mk19InnerMeshName = "MK19_1";
	public const string Mk19HandleName = "GameObjectBolt";
	public const string Mk19BoltVisualName = "Bolt";
	public const string LeftHandleIkName = "LeftHandIkTarget_NotReady_Handle";
	public const string RightHandleIkName = "RightHandIkTarget_NotReady_Handle";

	/// <summary>Только создаёт отсутствующие пустышки. Позиции/повороты уже существующих не трогает.</summary>
	public static bool EnsureMissingM2SocketsOnPitch(Transform _pitch)
	{
		if (_pitch == null)
			return false;

		bool created = false;
		created |= EnsureSocket(_pitch, EquippedWeapon.MuzzleExitTransformName, new Vector3(0f, 0.02f, 0.65f), Quaternion.identity) != null;
		created |= EnsureSocket(_pitch, ShellEjectName, new Vector3(0.12f, 0.05f, 0.12f), Quaternion.Euler(0f, 90f, 0f)) != null;
		created |= EnsureSocket(_pitch, BeltEjectName, new Vector3(-0.06f, 0.02f, 0.08f), Quaternion.Euler(35f, 0f, 0f)) != null;

		Transform gunMesh = FindInnerGun127(_pitch);
		if (gunMesh != null)
			created |= EnsureSocket(gunMesh, BarrelRecoilName, new Vector3(0f, 0f, 0.58f), Quaternion.identity) != null;

		return created;
	}

	/// <summary>Создаёт только отсутствующие сокеты и заполняет пустые ссылки EquippedWeapon.</summary>
	public static void EnsureM2OnPitch(Transform _pitch)
	{
		EnsureMissingM2SocketsOnPitch(_pitch);
		TryBindEquippedWeaponIfMissing(_pitch);
	}

	/// <summary>
	/// Runtime: не меняет authored local pose на prefab.
	/// Вешает IK-цели под recoiling <c>Gun.12.7</c> с сохранением world pose,
	/// чтобы кисти ехали с отдачей вместе с орудием.
	/// </summary>
	public static void PrepareM2PitchRuntime(Transform _pitch)
	{
		TryBindEquippedWeaponIfMissing(_pitch);
		AttachHandIkTargetsToRecoilGun(_pitch);
	}

	/// <summary>Tолько создаёт отсутствующие сокеты для MK19.</summary>
	public static bool EnsureMissingMk19SocketsOnPitch(Transform _pitch)
	{
		if (_pitch == null)
			return false;

		bool created = false;
		created |= EnsureSocket(_pitch, EquippedWeapon.MuzzleExitTransformName, new Vector3(0f, 0.02f, 1.0f), Quaternion.identity) != null;
		created |= EnsureSocket(_pitch, Mk19ShellEjectName, new Vector3(0.15f, -0.02f, 0.2f), Quaternion.Euler(0f, 90f, 0f)) != null;
		return created;
	}

	/// <summary>Runtime-подготовка панели MK19.</summary>
	public static void PrepareMk19PitchRuntime(Transform _pitch)
	{
		EnsureMissingMk19SocketsOnPitch(_pitch);
		TryBindEquippedWeaponIfMissing(_pitch);
	}

	/// <summary>
	/// Reparent Left/RightHandIkTarget* under inner gun. World pose kept — prefab coords stay visually the same.
	/// </summary>
	public static void AttachHandIkTargetsToRecoilGun(Transform _pitch)
	{
		Transform gun = FindInnerGun127(_pitch);
		if (gun == null)
			return;

		ReparentKeepWorld(FindChildUnder(_pitch, "LeftHandIkTarget"), gun);
		ReparentKeepWorld(FindChildUnder(_pitch, "RightHandIkTarget"), gun);
		ReparentKeepWorld(FindChildUnder(_pitch, "LeftHandIkTarget_NotReady"), gun);
		ReparentKeepWorld(FindChildUnder(_pitch, "RightHandIkTarget_NotReady"), gun);
	}

	private static void ReparentKeepWorld(Transform _target, Transform _newParent)
	{
		if (_target == null || _newParent == null || _target == _newParent)
			return;
		if (_target.parent == _newParent)
			return;
		_target.SetParent(_newParent, true);
	}

	public static Transform FindInnerGun127(Transform _pitch)
	{
		if (_pitch == null)
			return null;

		Transform direct = _pitch.Find(InnerGun127Name);
		if (direct != null && direct != _pitch)
			return direct;

		return FindChildUnder(_pitch, InnerGun127Name);
	}

	public static Transform FindMuzzleExit(Transform _pitch) =>
		FindChildUnder(_pitch, EquippedWeapon.MuzzleExitTransformName);

	public static Transform FindShellEject(Transform _pitch) =>
		FindChildUnder(_pitch, ShellEjectName);

	public static Transform FindBeltEject(Transform _pitch) =>
		FindChildUnder(_pitch, BeltEjectName);

	public static Transform FindMk19ShellEject(Transform _pitch) =>
		FindChildUnder(_pitch, Mk19ShellEjectName);

	public static Transform FindBarrelRecoil(Transform _pitch)
	{
		Transform gunMesh = FindInnerGun127(_pitch);
		return gunMesh != null ? FindChildUnder(gunMesh, BarrelRecoilName) : null;
	}

	/// <returns>true если хотя бы одна ссылка была пустой и заполнена.</returns>
	public static bool TryBindEquippedWeaponIfMissing(Transform _pitch)
	{
		if (_pitch == null || !_pitch.TryGetComponent(out EquippedWeapon weapon))
			return false;

		return weapon.BindTurretCombatSocketsFromHierarchy();
	}

	/// <returns>true если хотя бы одна ссылка была пустой и заполнена.</returns>
	public static bool TryWireEquippedWeaponIfMissing(EquippedWeapon _weapon, Transform _pitch)
	{
		if (_weapon == null || _pitch == null)
			return false;

		return _weapon.BindTurretCombatSocketsFromHierarchy();
	}

	/// <returns>null если сокет уже существует; иначе созданный Transform.</returns>
	private static Transform EnsureSocket(
		Transform _parent,
		string _name,
		Vector3 _localPosition,
		Quaternion _localRotation)
	{
		Transform existing = FindChildUnder(_parent, _name);
		if (existing != null)
			return null;

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _localPosition;
		t.localRotation = _localRotation;
		return t;
	}

	private static Transform FindChildUnder(Transform _parent, string _name)
	{
		if (_parent == null || string.IsNullOrEmpty(_name))
			return null;

		if (_parent.name == _name)
			return _parent;

		Transform[] all = _parent.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			Transform t = all[i];
			if (t != _parent && t != null && t.name == _name)
				return t;
		}

		return null;
	}
}
