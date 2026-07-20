using UnityEngine;

/// <summary>
/// Логика одноразового гранатомёта: всегда заряжен, после выстрела физический выброс.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitDisposableLauncherHandler : MonoBehaviour
{
	#region Constants
	private const float c_DefaultLifetimeSeconds = 30f;
	#endregion

	#region Public Methods
	public bool CanHandle(ItemDefinition _launcher)
	{
		return _launcher != null && _launcher.RocketLauncherType == RocketLauncherType.Disposable;
	}

	public bool IsAlwaysLoaded => true;

	/// <summary>
	/// Отрывает визуал от руки, включает физику, удаляет из инвентаря, уничтожает через N секунд.
	/// Не создаёт WorldPickupItem.
	/// </summary>
	public GameObject DiscardLauncherVisual(
		GameObject _handInstance,
		CharacterInventory _inventory,
		int _bagIndex,
		RocketLauncherData _data,
		Transform _unitTransform)
	{
		if (_inventory != null && _bagIndex >= 0)
			_inventory.TryRemoveBagAt(_bagIndex, out _);

		if (_handInstance == null)
			return null;

		// После выстрела труба пустая — на выброшенном визуале ракета скрыта.
		RocketLauncherVisualUtility.ApplyLoadedRocketVisual(_handInstance, false);

		Transform handTransform = _handInstance.transform;
		handTransform.SetParent(null, true);

		StripGameplayComponents(_handInstance);
		EnablePhysics(_handInstance, _data, _unitTransform);

		float lifetime = _data != null ? _data.DiscardedLauncherLifetimeSeconds : c_DefaultLifetimeSeconds;
		Object.Destroy(_handInstance, Mathf.Max(1f, lifetime));
		return _handInstance;
	}

	private static void StripGameplayComponents(GameObject _root)
	{
		if (_root == null)
			return;

		MonoBehaviour[] behaviours = _root.GetComponentsInChildren<MonoBehaviour>(true);
		for (int i = 0; i < behaviours.Length; i++)
		{
			MonoBehaviour behaviour = behaviours[i];
			if (behaviour == null)
				continue;

			// Keep only physics-friendly behaviour-free mesh; remove any custom scripts.
			Object.Destroy(behaviour);
		}
	}

	private static void EnablePhysics(GameObject _root, RocketLauncherData _data, Transform _unitTransform)
	{
		if (_root == null)
			return;

		Rigidbody body = _root.GetComponent<Rigidbody>();
		if (body == null)
			body = _root.AddComponent<Rigidbody>();

		body.isKinematic = false;
		body.useGravity = true;
		body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		body.interpolation = RigidbodyInterpolation.Interpolate;
		body.mass = Mathf.Max(1f, body.mass);

		if (_root.GetComponentInChildren<Collider>(true) == null)
		{
			BoxCollider box = _root.AddComponent<BoxCollider>();
			box.size = new Vector3(0.25f, 0.25f, 0.9f);
			box.center = new Vector3(0f, 0f, 0.2f);
		}

		Vector3 impulseLocal = _data != null ? _data.DiscardImpulseLocal : new Vector3(0.4f, 1.2f, 0.8f);
		float torque = _data != null ? _data.DiscardTorque : 2.5f;
		Vector3 worldImpulse = _unitTransform != null
			? _unitTransform.TransformDirection(impulseLocal)
			: impulseLocal;

		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;
		body.AddForce(worldImpulse, ForceMode.Impulse);
		body.AddTorque(Random.onUnitSphere * torque, ForceMode.Impulse);
	}
	#endregion
}
