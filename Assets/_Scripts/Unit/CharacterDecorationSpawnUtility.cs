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

	public static void ClearDecoration(ref GameObject _instance)
	{
		if (_instance == null)
			return;

		Object.Destroy(_instance);
		_instance = null;
	}
	#endregion
}
