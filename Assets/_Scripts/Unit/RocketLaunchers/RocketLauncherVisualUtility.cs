using UnityEngine;

/// <summary>
/// Синхронизация визуала ракеты/снаряда на модели гранатомёта с runtime-состоянием IsLoaded.
/// Предпочтительно через дочерний <c>RocketSocket</c>; fallback — меши с Rocket/Missile в имени.
/// </summary>
public static class RocketLauncherVisualUtility
{
	#region Constants
	public const string RocketSocketName = "RocketSocket";
	private const string c_RocketToken = "Rocket";
	private const string c_MissileToken = "Missile";
	#endregion

	#region Public Methods
	public static bool ResolveIsLoaded(InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty || _slot.Definition == null || !_slot.Definition.IsRocketLauncher)
			return false;

		if (_slot.Definition.RocketLauncherType == RocketLauncherType.Disposable)
			return true;

		if (_slot.InstanceState == null)
			return _slot.Definition.RocketLauncherStartsLoaded;

		_slot.InstanceState.EnsureRocketLauncherState(_slot.Definition);
		RocketLauncherRuntimeState state = _slot.InstanceState.RocketLauncherState;
		return state != null && state.IsLoaded;
	}

	public static bool ResolveIsLoaded(ItemDefinition _definition, ItemInstanceState _instanceState)
	{
		if (_definition == null || !_definition.IsRocketLauncher)
			return false;

		if (_definition.RocketLauncherType == RocketLauncherType.Disposable)
			return true;

		if (_instanceState == null)
			return _definition.RocketLauncherStartsLoaded;

		_instanceState.EnsureRocketLauncherState(_definition);
		RocketLauncherRuntimeState state = _instanceState.RocketLauncherState;
		return state != null && state.IsLoaded;
	}

	/// <summary>
	/// Показывает/скрывает RocketSocket или дочерние меши ракеты (имя содержит Rocket/Missile).
	/// </summary>
	public static void ApplyLoadedRocketVisual(GameObject _launcherRoot, bool _isLoaded)
	{
		if (_launcherRoot == null)
			return;

		Transform socket = FindRocketSocket(_launcherRoot.transform);
		if (socket != null)
		{
			if (socket.gameObject.activeSelf != _isLoaded)
				socket.gameObject.SetActive(_isLoaded);
			return;
		}

		Transform[] children = _launcherRoot.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			Transform child = children[i];
			if (child == null || child == _launcherRoot.transform)
				continue;

			string name = child.name;
			bool isRocketPart =
				name.IndexOf(c_RocketToken, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf(c_MissileToken, System.StringComparison.OrdinalIgnoreCase) >= 0;

			if (!isRocketPart)
				continue;

			if (child.gameObject.activeSelf != _isLoaded)
				child.gameObject.SetActive(_isLoaded);
		}
	}

	public static void ApplyLoadedRocketVisual(GameObject _launcherRoot, InventorySlotRuntimeData _slot)
	{
		ApplyLoadedRocketVisual(_launcherRoot, ResolveIsLoaded(_slot));
	}

	public static void ApplyLoadedRocketVisual(GameObject _launcherRoot, ItemDefinition _definition, ItemInstanceState _instanceState)
	{
		ApplyLoadedRocketVisual(_launcherRoot, ResolveIsLoaded(_definition, _instanceState));
	}

	public static Transform FindRocketSocket(Transform _launcherRoot)
	{
		if (_launcherRoot == null)
			return null;

		Transform direct = _launcherRoot.Find(RocketSocketName);
		if (direct != null)
			return direct;

		Transform[] children = _launcherRoot.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i] != null && children[i].name == RocketSocketName)
				return children[i];
		}

		return null;
	}
	#endregion
}
