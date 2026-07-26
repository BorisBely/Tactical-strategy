#if UNITY_EDITOR
using UnityEditor;

namespace CombatVehicleSystem.Editor
{
	public static class CombatVehicleBuildMenu
	{
		[MenuItem("Tools/Combat Vehicle System/Build Full Package Prefabs")]
		public static void Build()
		{
			CombatVehiclePrefabBuilder.BuildFullPackage();
		}

		[MenuItem("Tools/Combat Vehicle System/Create Tuning Assets Only")]
		public static void Tunings()
		{
			CombatVehiclePrefabBuilder.CreateTuningsMenu();
		}
	}
}
#endif
