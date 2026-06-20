using UnityEngine;

/// <summary>
/// Отключает физические столкновения между юнитами (слой Unit vs Unit).
/// Raycast/триггеры не затрагиваются — только контакты rigidbody/collider.
/// </summary>
public static class UnitPhysicsCollisionSetup
{
	#region Constants
	private const string c_UnitLayerName = "Unit";
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void DisableUnitLayerSelfCollision()
	{
		int unitLayer = LayerMask.NameToLayer(c_UnitLayerName);
		if (unitLayer < 0)
		{
			Debug.LogWarning(
				$"[{nameof(UnitPhysicsCollisionSetup)}] Layer '{c_UnitLayerName}' not found; inter-unit collision filter skipped.");
			return;
		}

		Physics.IgnoreLayerCollision(unitLayer, unitLayer, true);
	}
	#endregion
}
