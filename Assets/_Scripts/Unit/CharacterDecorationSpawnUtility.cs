using UnityEngine;

/// <summary>
/// Общий spawn/cleanup для визуального декора на костях персонажа.
/// </summary>
public static class CharacterDecorationSpawnUtility
{
	#region Public Methods
	public static GameObject SpawnDecoration(Transform _anchor, CharacterBodyDecorationVariant _config)
	{
		if (_anchor == null || _config.Prefab == null)
			return null;

		GameObject instance = Object.Instantiate(_config.Prefab, _anchor);
		Transform instanceTransform = instance.transform;
		instanceTransform.localPosition = _config.LocalPosition;
		instanceTransform.localRotation = Quaternion.Euler(_config.LocalEulerAngles);
		instanceTransform.localScale = Vector3.one;
		StripPickupAndPhysics(instance);
		return instance;
	}

	public static GameObject SpawnPrefab(Transform _anchor, GameObject _prefab)
	{
		if (_anchor == null || _prefab == null)
			return null;

		GameObject instance = Object.Instantiate(_prefab, _anchor);
		Transform instanceTransform = instance.transform;
		instanceTransform.localPosition = Vector3.zero;
		instanceTransform.localRotation = Quaternion.identity;
		instanceTransform.localScale = Vector3.one;
		StripPickupAndPhysics(instance);
		return instance;
	}

	public static void StripPickupAndPhysics(GameObject _root)
	{
		if (_root == null)
			return;

		_root.layer = _root.transform.parent != null ? _root.transform.parent.gameObject.layer : _root.layer;

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			pickups[i].enabled = false;
	}

	/// <summary>
	/// Визуал оружия за спиной: без лута/физики/геймплей-скриптов, только меш-рендер.
	/// </summary>
	public static void StripForBackWeaponHolster(GameObject _root)
	{
		if (_root == null)
			return;

		StripPickupAndPhysics(_root);

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
		{
			if (pickups[i] != null)
				Object.Destroy(pickups[i]);
		}

		EquippedWeapon[] equippedWeapons = _root.GetComponentsInChildren<EquippedWeapon>(true);
		for (int i = 0; i < equippedWeapons.Length; i++)
		{
			if (equippedWeapons[i] != null)
				Object.Destroy(equippedWeapons[i]);
		}

		MonoBehaviour[] behaviours = _root.GetComponentsInChildren<MonoBehaviour>(true);
		for (int i = 0; i < behaviours.Length; i++)
		{
			MonoBehaviour behaviour = behaviours[i];
			if (behaviour != null)
				behaviour.enabled = false;
		}

		Animator[] animators = _root.GetComponentsInChildren<Animator>(true);
		for (int i = 0; i < animators.Length; i++)
		{
			if (animators[i] != null)
				animators[i].enabled = false;
		}

		AudioSource[] audioSources = _root.GetComponentsInChildren<AudioSource>(true);
		for (int i = 0; i < audioSources.Length; i++)
		{
			if (audioSources[i] != null)
				audioSources[i].enabled = false;
		}

		Light[] lights = _root.GetComponentsInChildren<Light>(true);
		for (int i = 0; i < lights.Length; i++)
		{
			if (lights[i] != null)
				lights[i].enabled = false;
		}

		ParticleSystem[] particles = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < particles.Length; i++)
		{
			if (particles[i] == null)
				continue;

			particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			particles[i].gameObject.SetActive(false);
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null)
				Object.Destroy(colliders[i]);
		}

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			if (bodies[i] != null)
				Object.Destroy(bodies[i]);
		}
	}

	public static GameObject SpawnBackWeaponHolsterVisual(Transform _anchor, GameObject _prefab)
	{
		if (_anchor == null || _prefab == null)
			return null;

		GameObject instance = Object.Instantiate(_prefab, _anchor);
		Transform instanceTransform = instance.transform;
		instanceTransform.localPosition = Vector3.zero;
		instanceTransform.localRotation = Quaternion.identity;
		instanceTransform.localScale = Vector3.one;
		StripForBackWeaponHolster(instance);
		return instance;
	}

	public static void ClearDecoration(ref GameObject _instance)
	{
		if (_instance == null)
			return;

		Object.Destroy(_instance);
		_instance = null;
	}

	public static void ClearDecorationImmediate(ref GameObject _instance)
	{
		if (_instance == null)
			return;

		Object.DestroyImmediate(_instance);
		_instance = null;
	}

	public static void ClearAllChildrenImmediate(Transform _parent)
	{
		if (_parent == null)
			return;

		for (int i = _parent.childCount - 1; i >= 0; i--)
		{
			Transform child = _parent.GetChild(i);
			if (child != null)
				Object.DestroyImmediate(child.gameObject);
		}
	}
	#endregion
}
