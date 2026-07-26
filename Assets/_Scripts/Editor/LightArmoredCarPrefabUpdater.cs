using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool to replace the hull mesh in the Light_Armored_Car prefab while
/// keeping all child objects (seats, hinges, approach markers, etc.) visually
/// in the same world positions.
/// </summary>
public static class LightArmoredCarPrefabUpdater
{
	private const string c_PrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_FbxPath = "Assets/Models/Vehicles/Custom/2.1.fbx";
	private const string c_NewMeshName = "SM_Veh_Light_Armored_Car_01.003";

	[MenuItem("Tools/Combat Vehicle System/Rebuild Light Armored Car Hull")]
	public static void RebuildHull()
	{
		// Ensure the new FBX is in the expected folder.
		if (!File.Exists(c_FbxPath))
		{
			string sourceFbx = "Assets/2.1.fbx";
			if (!File.Exists(sourceFbx))
			{
				Debug.LogError($"2.1.fbx not found at {c_FbxPath} or {sourceFbx}.");
				return;
			}

			string dir = Path.GetDirectoryName(c_FbxPath);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			AssetDatabase.MoveAsset(sourceFbx, c_FbxPath);
			AssetDatabase.Refresh();
		}

		// Load the new hull mesh from the FBX.
		Mesh newMesh = AssetDatabase.LoadAllAssetsAtPath(c_FbxPath)
			.OfType<Mesh>()
			.FirstOrDefault(m => m.name == c_NewMeshName);

		if (newMesh == null)
		{
			Debug.LogError($"Mesh '{c_NewMeshName}' not found in {c_FbxPath}. Available meshes:");
			foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(c_FbxPath).OfType<Mesh>())
				Debug.Log("  " + m.name);
			return;
		}

		Debug.Log($"New hull mesh loaded: {newMesh.name} bounds={newMesh.bounds}");

		// Load the prefab for editing.
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(c_PrefabPath);
		if (prefabRoot == null)
		{
			Debug.LogError("Failed to load prefab: " + c_PrefabPath);
			return;
		}

		try
		{
			MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
			if (meshFilters.Length == 0)
			{
				Debug.LogError("No MeshFilters found in prefab.");
				return;
			}

			// Log all candidates so the user can verify the chosen hull.
			Debug.Log("MeshFilter candidates (sorted by size):");
			var sorted = meshFilters
				.OrderByDescending(mf => mf.sharedMesh ? mf.sharedMesh.bounds.size.magnitude : 0f)
				.Take(8)
				.ToArray();
			foreach (MeshFilter mf in sorted)
			{
				Vector3 size = mf.sharedMesh ? mf.sharedMesh.bounds.size : Vector3.zero;
				Debug.Log($"  {mf.name} size={size:F3}");
			}

			MeshFilter hull = sorted[0];
			if (hull.sharedMesh == null)
			{
				Debug.LogError("Selected hull has no mesh.");
				return;
			}

			Debug.Log($"Selected hull: {hull.name} (old mesh bounds={hull.sharedMesh.bounds})");

			// Compute the pivot offset between the old mesh and the new mesh.
			// We want the new mesh to appear exactly where the old mesh was.
			Vector3 oldCenter = hull.sharedMesh.bounds.center;
			Vector3 newCenter = newMesh.bounds.center;
			Vector3 offset = oldCenter - newCenter;

			// Record all children world transforms so we can restore them after moving the parent.
			Transform[] children = hull.GetComponentsInChildren<Transform>(true)
				.Where(t => t != hull.transform)
				.ToArray();

			(int index, Vector3 position, Quaternion rotation)[] worldRecords =
				children.Select((t, i) => (i, t.position, t.rotation)).ToArray();

			// Replace the mesh.
			Undo.RecordObject(hull, "Replace hull mesh");
			hull.sharedMesh = newMesh;

			// Move the hull object so the new mesh aligns with the old visual position.
			Undo.RecordObject(hull.transform, "Offset hull transform");
			hull.transform.localPosition += offset;

			// Restore children world transforms so seats/hinges stay visually in place.
			foreach (var record in worldRecords)
			{
				Transform t = children[record.index];
				Undo.RecordObject(t, "Restore child world transform");
				t.position = record.position;
				t.rotation = record.rotation;
			}

			Debug.Log(
				$"Replaced hull mesh on {hull.name}. " +
				$"Old center={oldCenter:F3}, new center={newCenter:F3}, offset={offset:F3}");

			PrefabUtility.SaveAsPrefabAsset(prefabRoot, c_PrefabPath);
			Debug.Log("Prefab saved: " + c_PrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}
}
