#if UNITY_EDITOR
using UnityEditor;

namespace CombatVehicleSystem.Editor
{
	public static class CombatVehicleBuildMenu
	{
		public static void Build()
		{
			CombatVehiclePrefabBuilder.BuildFullPackage();
		}

		public static void Tunings()
		{
			CombatVehiclePrefabBuilder.CreateTuningsMenu();
		}
	}
}
#endif
