#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public static class VehicleNavTestSceneSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_RootName = "NavTestPolygon";
	private static readonly Vector3 TrackOrigin = new Vector3(80f, 0f, 5f);

	private const float c_Lane = 14f;
	private const float c_Narrow = 5f;
	private const float c_BarrierH = 1.5f;
	private const float c_WallT = 0.5f;
	private const float c_Gap = 12f;
	private const int c_LG = 6;
	private const int c_LO = 9;

	[MenuItem("Polygone/Vehicles/Build NAVIGATION Test Track")]
	public static void Build()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		GameObject old = GameObject.Find(c_RootName);
		if (old != null) Object.DestroyImmediate(old);

		var root = new GameObject(c_RootName);
		root.transform.position = TrackOrigin;
		SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

		Box(root.transform, "Ground", new Vector3(0f, -0.5f, 185f), new Vector3(80f, 1f, 380f), new Color(0.28f, 0.30f, 0.24f), c_LG);

		float x = 0f, z = 0f;

		Pillar(root.transform, "START", x, z, Color.green);
		Label(root.transform, "L0", x, 3f, z, "START");
		z += 14f;

		// 1. Slalom
		Label(root.transform, "L1", x, 4f, z, "1. Slalom");
		float[] pat = { 0f, 6f, -6f, 6f, -6f, 6f, -6f, 6f };
		for (int i = 0; i < pat.Length; i++)
			Cone(root.transform, $"C{i}", x + pat[i], z + i * 9f);
		z += pat.Length * 9f + c_Gap;

		// 2. Narrow
		Label(root.transform, "L2", x, 4f, z, "2. Narrow passage (5m)");
		float nh = c_Narrow * 0.5f, nl = 30f;
		Wall(root.transform, "W2L", x - nh, z + nl * 0.5f, c_WallT, c_BarrierH, nl, Color.yellow);
		Wall(root.transform, "W2R", x + nh, z + nl * 0.5f, c_WallT, c_BarrierH, nl, Color.yellow);
		z += nl + c_Gap;

		// 3. Sharp turn right
		Label(root.transform, "L3", x, 4f, z, "3. Sharp 90 right");
		Wall(root.transform, "W3", x, z + 10f, 16f, c_BarrierH, c_WallT, Color.red);
		x += 12f; z += 16f + c_Gap;

		// 4. Obstacle
		Label(root.transform, "L4", x, 4f, z, "4. Obstacle (detour right)");
		Wall(root.transform, "W4A", x - 1f, z + 6f, 14f, c_BarrierH, c_WallT, Color.red);
		Wall(root.transform, "W4B", x + 8f, z + 6f, c_WallT, c_BarrierH, 16f, Color.yellow);
		x += 9f; z += 18f + c_Gap;

		// 5. Ramp up + down
		Label(root.transform, "L5a", x, 4f, z, "5a. Ramp up 10");
		Ramp(root.transform, "R5U", x, z, 24f, 4.2f);
		z += 24f;
		Box(root.transform, "P5", new Vector3(x, 2.1f, z + 6f), new Vector3(c_Lane, 4.2f, 12f), new Color(0.40f, 0.48f, 0.36f), c_LG);
		z += 12f;
		Label(root.transform, "L5b", x, 4f, z, "5b. Ramp down");
		Ramp(root.transform, "R5D", x, z + 3f, 18f, -4.2f);
		z += 22f + c_Gap;

		// 6. Side slope
		Label(root.transform, "L6", x, 4f, z, "6. Side slope");
		SideSlope(root.transform, "S6", x, z, 22f, 1f);
		z += 24f + c_Gap;

		// 7. Drop edge
		Label(root.transform, "L7", x, 4f, z, "7. Drop edge");
		Box(root.transform, "P7", new Vector3(x, 0.6f, z + 5f), new Vector3(c_Lane, 1.2f, 10f), new Color(0.50f, 0.42f, 0.32f), c_LG);
		Wall(root.transform, "W7", x, z + 10.5f, c_Lane, 0.1f, 0.4f, Color.red);
		z += 12f + c_Gap;

		// 8. Reverse
		Label(root.transform, "L8", x, 4f, z, "8. Reverse target");
		Marker(root.transform, "M8", x, z + 5f, Color.magenta, 2.5f);
		Wall(root.transform, "W8L", x - 5f, z + 4f, c_WallT, c_BarrierH, 7f, Color.yellow);
		Wall(root.transform, "W8R", x + 5f, z + 4f, c_WallT, c_BarrierH, 7f, Color.yellow);
		z += 11f + c_Gap;

		// 9. Heading
		Label(root.transform, "L9", x, 4f, z, "9. Heading arrival");
		Marker(root.transform, "M9", x, z + 5f, Color.cyan, 3f);
		Arrow(root.transform, "A9", x, 0.15f, z + 5f, Vector3.forward, 5f, Color.cyan);
		z += 11f + c_Gap;

		// 10. Waypoints
		Label(root.transform, "L10", x, 4f, z, "10. Waypoint chain");
		for (int i = 0; i < 4; i++)
		{
			float ox = (i % 2 == 0) ? -6f : 6f;
			Marker(root.transform, $"M10{i}", x + ox, z + 5f + i * 10f, new Color(1f, 0.7f, 0.2f), 2f);
			Label(root.transform, $"L10{i}", x + ox, 3f, z + 5f + i * 10f, $"{i + 1}");
		}
		z += 5f + 4 * 10f + c_Gap;

		// Finish
		Pillar(root.transform, "FINISH", x, z, Color.red);
		Label(root.transform, "LF", x, 4f, z, "FINISH");

		// Camera
		Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
		if (cam != null) { cam.transform.position = new Vector3(TrackOrigin.x, 25f, TrackOrigin.z - 8f); cam.transform.rotation = Quaternion.Euler(60f, 0f, 0f); }

		// Vehicle
		VehicleController v = Object.FindAnyObjectByType<VehicleController>();
		if (v != null) { v.transform.position = new Vector3(TrackOrigin.x, 1f, TrackOrigin.z + 5f); v.transform.rotation = Quaternion.identity; }
		else
		{
			GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Vehicles/Light_Armored_Car.prefab");
			if (pf != null) { GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.position = new Vector3(TrackOrigin.x, 1f, TrackOrigin.z + 5f); inst.transform.rotation = Quaternion.identity; SceneManager.MoveGameObjectToScene(inst, scene); }
		}

#pragma warning disable CS0618
		UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore CS0618
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Debug.Log("[Polygon] Ready. X=80, Z=5, length ~370m, 10 sections.");
	}

	static void Box(Transform p, string n, Vector3 pos, Vector3 s, Color c, int l) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.transform.localPosition = pos; g.transform.localScale = s; g.layer = l; g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c); g.GetComponent<Collider>().isTrigger = false; }
	static void Wall(Transform p, string n, float x, float z, float sx, float sy, float sz, Color c) { Box(p, n, new Vector3(x, sy * 0.5f, z), new Vector3(sx, sy, sz), c, c_LO); }
	static void Cone(Transform p, string n, float x, float z) { var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = n; g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(x, 0.8f, z); g.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f); g.layer = c_LO; g.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(1f, 0.45f, 0f)); g.GetComponent<Collider>().isTrigger = false; }
	static void Ramp(Transform p, string n, float x, float z, float len, float rise) { float a = Mathf.Abs(rise); float h = Mathf.Sqrt(len * len + a * a); float ang = Mathf.Atan2(a, len) * Mathf.Rad2Deg; float pitch = rise >= 0f ? -ang : ang; var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.layer = c_LG; g.transform.localPosition = new Vector3(x, a * 0.5f, z + len * 0.5f); g.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f); g.transform.localScale = new Vector3(c_Lane, 0.35f, h); g.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.38f, 0.52f, 0.72f)); g.GetComponent<Collider>().isTrigger = false; }
	static void SideSlope(Transform p, string n, float x, float z, float len, float bank) { float half = c_Lane * 0.5f; float h = Mathf.Sqrt(half * half + bank * bank); float ang = Mathf.Atan2(bank, half) * Mathf.Rad2Deg; var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.layer = c_LG; g.transform.localPosition = new Vector3(x + half * 0.5f, bank * 0.5f, z + len * 0.5f); g.transform.localRotation = Quaternion.Euler(0f, 0f, -ang); g.transform.localScale = new Vector3(h, 0.3f, len); g.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.55f, 0.40f, 0.58f)); g.GetComponent<Collider>().isTrigger = false; }
	static void Marker(Transform p, string n, float x, float z, Color c, float s) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(x, 0.07f, z); g.transform.localScale = new Vector3(s, 0.14f, s); g.layer = c_LG; g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c); g.GetComponent<Collider>().isTrigger = false; }
	static void Pillar(Transform p, string n, float x, float z, Color c) { Box(p, n, new Vector3(x, 1f, z), new Vector3(1.2f, 2f, 1.2f), c, c_LG); }
	static void Arrow(Transform p, string n, float x, float y, float z, Vector3 d, float len, Color c) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(x, y, z) + d * (len * 0.5f); g.transform.localRotation = Quaternion.LookRotation(d, Vector3.up); g.transform.localScale = new Vector3(0.3f, 0.15f, len); g.layer = c_LG; g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c); }
	static void Label(Transform p, string n, float x, float y, float z, string t) { var g = new GameObject(n); g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(x, y, z); g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); var tmp = g.AddComponent<TMPro.TextMeshPro>(); tmp.text = t; tmp.fontSize = 5f; tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.color = Color.white; }
	static Material Mat(Color c) { Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); if (s == null) return null; var m = new Material(s); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else if (m.HasProperty("_Color")) m.color = c; return m; }
}
#endif
