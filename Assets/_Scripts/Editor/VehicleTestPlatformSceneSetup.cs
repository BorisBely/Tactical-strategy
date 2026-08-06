#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VehicleNavigation.Editor
{
	public static class VehicleTestPlatformSceneSetup
	{
		struct PlatformSpec
		{
			public string Name;
			public VehicleTestVariant Variant;
			public int PlatformId;
			public int ShardIndex;
			public int ShardCount;
			public Vector3 Position;
		}

		[MenuItem("Tools/Tests/Setup Five Test Platforms")]
		public static void SetupFivePlatforms()
		{
			var scene = SceneManager.GetActiveScene();
			if (!scene.IsValid())
			{
				Debug.LogError("[TestPlatformSetup] No active scene.");
				return;
			}

			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
				"Assets/Prefabs/Vehicles/Light_Armored_Car.prefab");
			if (prefab == null)
			{
				Debug.LogError("[TestPlatformSetup] Light_Armored_Car.prefab not found.");
				return;
			}

			var specs = new[]
			{
				new PlatformSpec
				{
					Name = "VehicleTestPlatform V1 P1",
					Variant = VehicleTestVariant.Variant1_OpenField,
					PlatformId = 1, ShardIndex = 0, ShardCount = 2,
					Position = new Vector3(236.8f, 0f, 164.8f)
				},
				new PlatformSpec
				{
					Name = "VehicleTestPlatform V2 P2",
					Variant = VehicleTestVariant.Variant2_PoseArrival,
					PlatformId = 2, ShardIndex = 0, ShardCount = 2,
					Position = new Vector3(322.3f, 0f, 164.8f)
				},
				new PlatformSpec
				{
					Name = "VehicleTestPlatform V1 P3",
					Variant = VehicleTestVariant.Variant1_OpenField,
					PlatformId = 3, ShardIndex = 1, ShardCount = 2,
					Position = new Vector3(407.8f, 0f, 164.8f)
				},
				new PlatformSpec
				{
					Name = "VehicleTestPlatform V2 P4",
					Variant = VehicleTestVariant.Variant2_PoseArrival,
					PlatformId = 4, ShardIndex = 1, ShardCount = 2,
					Position = new Vector3(493.3f, 0f, 164.8f)
				},
				new PlatformSpec
				{
					Name = "VehicleTestPlatform V5 Calib",
					Variant = VehicleTestVariant.Variant5_KinematicsCalibration,
					PlatformId = 5, ShardIndex = 0, ShardCount = 1,
					Position = new Vector3(578.8f, 0f, 164.8f)
				}
			};

			foreach (var spec in specs)
				EnsurePlatform(spec, prefab);

			RemoveLegacyPlatforms();
			EditorSceneManager.MarkSceneDirty(scene);
			Debug.Log("[TestPlatformSetup] Five test platforms configured. Save scene.");
		}

		static void EnsurePlatform(PlatformSpec _spec, GameObject _prefab)
		{
			var existing = GameObject.Find(_spec.Name);
			GameObject go = existing != null ? existing : new GameObject(_spec.Name);
			go.transform.position = _spec.Position;

			var platform = go.GetComponent<VehicleTestPlatform>();
			if (platform == null)
				platform = go.AddComponent<VehicleTestPlatform>();

			var so = new SerializedObject(platform);
			so.FindProperty("m_VehiclePrefab").objectReferenceValue = _prefab;
			so.FindProperty("m_TestVariant").intValue = (int)_spec.Variant;

			so.FindProperty("m_PlatformId").intValue = _spec.PlatformId;
			so.FindProperty("m_ShardIndex").intValue = _spec.ShardIndex;
			so.FindProperty("m_ShardCount").intValue = _spec.ShardCount;
			so.FindProperty("m_AutoStart").boolValue = true;
			so.FindProperty("m_LoopTests").boolValue = false;
			so.FindProperty("m_RespawnBetweenTests").boolValue = true;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		static void RemoveLegacyPlatforms()
		{
			foreach (var go in Object.FindObjectsByType<VehicleTestPlatform>(FindObjectsSortMode.None))
			{
				if (go.name.StartsWith("VehicleTestPlatform V"))
					continue;
				if (go.name.Contains("VehicleTestPlatform"))
					Object.DestroyImmediate(go.gameObject);
			}
		}
	}
}
#endif
