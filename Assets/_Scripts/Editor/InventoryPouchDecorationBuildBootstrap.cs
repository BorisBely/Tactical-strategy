#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class InventoryPouchDecorationBuildBootstrap
{
	private const string c_MarkerPath = "Assets/.inventory_pouch_decoration_build_marker";

	static InventoryPouchDecorationBuildBootstrap()
	{
		EditorApplication.delayCall += TryRunFromMarker;
	}

	private static void TryRunFromMarker()
	{
		if (!File.Exists(c_MarkerPath))
			return;

		try
		{
			File.Delete(c_MarkerPath);
			if (File.Exists(c_MarkerPath + ".meta"))
				File.Delete(c_MarkerPath + ".meta");

			GrenadeContentBuilder.BuildGrenadeContent();
			UnitPouchDecorationContentBuilder.BuildUnitPouchDecorationContent();
			Debug.Log("[InventoryPouchDecorationBuildBootstrap] Inventory pouch decoration content built from marker.");
		}
		catch (Exception exception)
		{
			Debug.LogError($"[InventoryPouchDecorationBuildBootstrap] Auto-run failed: {exception}");
		}
	}
}
#endif
