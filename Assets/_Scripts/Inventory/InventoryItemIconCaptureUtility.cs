using UnityEngine;

/// <summary>
/// Общая логика кадрирования иконок: bounds из renderer + collider, ориентация по длинной оси.
/// Если в префабе есть child <c>IconBounds</c>/<c>IconCollider</c> — используются только они
/// (пустышка с коллайдером под угол иконки, без влияния gameplay-коллайдеров).
/// </summary>
public static class InventoryItemIconCaptureUtility
{
	public const int IconSize = 128;
	public const float BoundsPadding = 1.05f;
	public const string IconBoundsChildName = "IconBounds";
	public const string IconColliderChildName = "IconCollider";

	private const float c_WeaponPitchDegrees = 12f;
	private const float c_WeaponYawBiasDegrees = -10f;
	private const float c_CompactPitchDegrees = 20f;
	private const float c_CompactYawDegrees = 25f;
	/// <summary>Фронт для IconBounds (рюкзак/короб/щит): почти без yaw, лёгкий наклон сверху.</summary>
	private const float c_FrontalPitchDegrees = 18f;
	private const float c_FrontalYawDegrees = 0f;
	private const float c_ElongationRatio = 1.25f;

	public static Vector3 CompactViewDirection => new Vector3(0.45f, 0.35f, -1f).normalized;
	public static Vector3 WeaponViewDirection => new Vector3(0.12f, 0.28f, -1f).normalized;
	public static Vector3 FrontalViewDirection => new Vector3(0.05f, 0.22f, -1f).normalized;

	/// <summary>
	/// При identity измеряет bounds и выбирает поворот.
	/// IconBounds: Inverse(пустышка) выравнивает меш (кость/наклон), затем фронт без бокового yaw
	/// (рюкзаки, коробы, щиты — ровно «в лицо»).
	/// </summary>
	public static Quaternion ResolvePresentationRotation(GameObject _instance, bool _isWeapon)
	{
		if (_instance == null)
			return Quaternion.identity;

		Transform t = _instance.transform;
		t.localRotation = Quaternion.identity;
		Physics.SyncTransforms();

		if (TryFindIconBoundsTransform(_instance, out Transform iconBounds))
		{
			Quaternion alignUpright = Quaternion.Inverse(iconBounds.localRotation);
			Quaternion iconTilt = _isWeapon
				? Quaternion.Euler(c_WeaponPitchDegrees, c_WeaponYawBiasDegrees, 0f)
				: Quaternion.Euler(c_FrontalPitchDegrees, c_FrontalYawDegrees, 0f);
			return alignUpright * iconTilt;
		}

		Bounds bounds = CalculateCaptureBounds(_instance);
		Vector3 size = bounds.size;
		bool elongated = IsElongated(size);

		if (_isWeapon || elongated)
		{
			float yawAlign = 0f;
			if (size.z >= size.x)
				yawAlign = -90f;

			return Quaternion.Euler(c_WeaponPitchDegrees, yawAlign + c_WeaponYawBiasDegrees, 0f);
		}

		return Quaternion.Euler(c_CompactPitchDegrees, c_CompactYawDegrees, 0f);
	}

	public static Vector3 ResolveViewDirection(GameObject _instance, bool _isWeapon)
	{
		if (_isWeapon)
			return WeaponViewDirection;

		if (TryFindIconBoundsTransform(_instance, out _))
			return FrontalViewDirection;

		return CompactViewDirection;
	}

	/// <summary>Совместимость: без инстанса — прежний compact/weapon.</summary>
	public static Vector3 ResolveViewDirection(bool _isWeapon)
	{
		return _isWeapon ? WeaponViewDirection : CompactViewDirection;
	}

	public static Bounds CalculateCaptureBounds(GameObject _root)
	{
		bool has = false;
		Bounds bounds = new Bounds(_root != null ? _root.transform.position : Vector3.zero, Vector3.zero);
		if (_root == null)
			return bounds;

		if (TryFindIconBoundsTransform(_root, out Transform iconBounds))
		{
			Collider[] iconColliders = iconBounds.GetComponentsInChildren<Collider>(true);
			for (int i = 0; i < iconColliders.Length; i++)
			{
				if (!TryGetColliderWorldBounds(iconColliders[i], out Bounds cb))
					continue;

				if (!has)
				{
					bounds = cb;
					has = true;
				}
				else
					bounds.Encapsulate(cb);
			}

			if (has)
				return bounds;
		}

		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer)
				continue;
			if (IsUnderIconBoundsProxy(renderer.transform))
				continue;

			if (!has)
			{
				bounds = renderer.bounds;
				has = true;
			}
			else
				bounds.Encapsulate(renderer.bounds);
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			Collider collider = colliders[i];
			if (collider == null || IsUnderIconBoundsProxy(collider.transform))
				continue;

			if (!TryGetColliderWorldBounds(collider, out Bounds cb))
				continue;

			if (!has)
			{
				bounds = cb;
				has = true;
			}
			else
				bounds.Encapsulate(cb);
		}

		return bounds;
	}

	public static void FitOrthographicCamera(Camera _camera, GameObject _instance, Vector3 _viewDir)
	{
		if (_camera == null || _instance == null)
			return;

		Physics.SyncTransforms();
		Bounds bounds = CalculateCaptureBounds(_instance);
		if (bounds.size.sqrMagnitude < 0.0001f)
		{
			_camera.orthographicSize = 0.35f;
			_camera.transform.position = _instance.transform.position - _viewDir * 2f;
			_camera.transform.rotation = Quaternion.LookRotation(_viewDir, Vector3.up);
			return;
		}

		Vector3 center = bounds.center;
		float radius = bounds.extents.magnitude * BoundsPadding;
		_camera.orthographicSize = Mathf.Max(radius, 0.05f);
		_camera.transform.position = center - _viewDir * (radius * 2.5f + 0.5f);
		_camera.transform.rotation = Quaternion.LookRotation(_viewDir, Vector3.up);
	}

	public static void ApplyWeaponVisualState(
		GameObject _instance,
		ItemDefinition _definition,
		WeaponRuntimeState _weaponState)
	{
		if (_instance == null || _definition == null || _weaponState == null)
			return;

		WeaponDefinition weaponDefinition = _definition.WeaponDefinition;
		EquippedWeapon equippedWeapon = _instance.GetComponentInChildren<EquippedWeapon>(true);
		if (equippedWeapon == null || weaponDefinition == null)
			return;

		equippedWeapon.RefreshAttachmentVisualsFromState(weaponDefinition, _weaponState);

		if (_weaponState.IsMagazineNonRemovable)
		{
			equippedWeapon.ClearAllMagazineVisuals();
			return;
		}

		InventorySlotRuntimeData primaryMag = _weaponState.CurrentMagazineItem;
		ItemDefinition primaryDef = primaryMag.Definition;
		if (primaryDef != null &&
		    primaryMag.InstanceState != null &&
		    primaryMag.InstanceState.MagazineState != null)
			equippedWeapon.SetInsertedMagazineVisual(primaryDef);
		else
			equippedWeapon.ClearInsertedMagazineVisual();

		if (weaponDefinition.UsesDualMagazineSlots)
		{
			InventorySlotRuntimeData secondaryMag = _weaponState.CurrentSecondaryMagazineItem;
			ItemDefinition secondaryDef = secondaryMag.Definition;
			if (secondaryDef != null &&
			    secondaryMag.InstanceState != null &&
			    secondaryMag.InstanceState.MagazineState != null)
				equippedWeapon.SetSecondaryMagazineVisual(secondaryDef);
			else
				equippedWeapon.ClearSecondaryMagazineVisual();
		}
	}

	public static void SetLayerRecursively(GameObject _root, int _layer)
	{
		if (_root == null)
			return;

		_root.layer = _layer;
		Transform t = _root.transform;
		for (int i = 0; i < t.childCount; i++)
			SetLayerRecursively(t.GetChild(i).gameObject, _layer);
	}

	public static void DisablePhysicsAndAudio(GameObject _root)
	{
		if (_root == null)
			return;

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null)
				colliders[i].enabled = false;
		}

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			if (bodies[i] != null)
			{
				bodies[i].isKinematic = true;
				bodies[i].detectCollisions = false;
			}
		}

		AudioSource[] audio = _root.GetComponentsInChildren<AudioSource>(true);
		for (int i = 0; i < audio.Length; i++)
		{
			if (audio[i] != null)
				audio[i].enabled = false;
		}

		ParticleSystem[] particles = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < particles.Length; i++)
		{
			if (particles[i] != null)
				particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
	}

	public static int ComputeWeaponBuildHash(InventorySlotRuntimeData _data)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + (_data.Definition != null ? _data.Definition.GetEntityId().GetHashCode() : 0);
			WeaponRuntimeState weaponState = _data.InstanceState != null ? _data.InstanceState.WeaponState : null;
			if (weaponState == null)
				return hash;

			WeaponAttachmentDefinition[] attachments = weaponState.EquippedAttachments;
			if (attachments != null)
			{
				for (int i = 0; i < attachments.Length; i++)
					hash = hash * 31 + (attachments[i] != null ? attachments[i].GetEntityId().GetHashCode() : 0);
			}

			ItemDefinition[] attachmentItems = weaponState.EquippedAttachmentItems;
			if (attachmentItems != null)
			{
				for (int i = 0; i < attachmentItems.Length; i++)
					hash = hash * 31 + (attachmentItems[i] != null ? attachmentItems[i].GetEntityId().GetHashCode() : 0);
			}

			ItemDefinition magazine = weaponState.InsertedMagazineDefinition;
			hash = hash * 31 + (magazine != null ? magazine.GetEntityId().GetHashCode() : 0);

			InventorySlotRuntimeData secondary = weaponState.CurrentSecondaryMagazineItem;
			hash = hash * 31 + (secondary.Definition != null ? secondary.Definition.GetEntityId().GetHashCode() : 0);
			return hash;
		}
	}

	public static void DestroyCaptureInstanceImmediate(GameObject _instance)
	{
		if (_instance == null)
			return;

		_instance.SetActive(false);
#if UNITY_EDITOR
		if (!Application.isPlaying)
		{
			Object.DestroyImmediate(_instance);
			return;
		}
#endif
		Object.DestroyImmediate(_instance);
	}

	public static bool TryFindIconBoundsTransform(GameObject _root, out Transform _iconBounds)
	{
		_iconBounds = null;
		if (_root == null)
			return false;

		Transform named = FindChildRecursive(_root.transform, IconBoundsChildName);
		if (named == null)
			named = FindChildRecursive(_root.transform, IconColliderChildName);
		if (named == null)
			return false;

		_iconBounds = named;
		return true;
	}

	private static Transform FindChildRecursive(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		if (_parent.name == _name)
			return _parent;

		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform found = FindChildRecursive(_parent.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}

	private static bool IsUnderIconBoundsProxy(Transform _t)
	{
		while (_t != null)
		{
			if (_t.name == IconBoundsChildName || _t.name == IconColliderChildName)
				return true;
			_t = _t.parent;
		}

		return false;
	}

	private static bool TryGetColliderWorldBounds(Collider _collider, out Bounds _bounds)
	{
		_bounds = default;
		if (_collider == null)
			return false;

		if (_collider is BoxCollider box)
		{
			_bounds = CalculateOrientedBoxWorldBounds(box);
			return _bounds.size.sqrMagnitude > 0.0000001f;
		}

		_bounds = _collider.bounds;
		return _bounds.size.sqrMagnitude > 0.0000001f;
	}

	/// <summary>AABB ориентированного BoxCollider (работает и при disabled).</summary>
	private static Bounds CalculateOrientedBoxWorldBounds(BoxCollider _box)
	{
		Transform t = _box.transform;
		Vector3 center = t.TransformPoint(_box.center);
		Vector3 lossy = t.lossyScale;
		Vector3 half = new Vector3(
			Mathf.Abs(_box.size.x * lossy.x) * 0.5f,
			Mathf.Abs(_box.size.y * lossy.y) * 0.5f,
			Mathf.Abs(_box.size.z * lossy.z) * 0.5f);

		Vector3 axisX = t.right * half.x;
		Vector3 axisY = t.up * half.y;
		Vector3 axisZ = t.forward * half.z;

		Vector3 extents = new Vector3(
			Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
			Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
			Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

		return new Bounds(center, extents * 2f);
	}

	private static bool IsElongated(Vector3 _size)
	{
		float horizontal = Mathf.Max(_size.x, _size.z);
		float vertical = Mathf.Max(_size.y, 0.0001f);
		return horizontal >= vertical * c_ElongationRatio || horizontal >= Mathf.Min(_size.x, _size.z) * c_ElongationRatio;
	}
}
