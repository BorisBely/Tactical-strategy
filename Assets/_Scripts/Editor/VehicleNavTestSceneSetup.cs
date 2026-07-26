#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

/// <summary>
/// Menu: Polygone → Vehicles → Build NAVIGATION Test Track
/// Places a large drivable test polygon to the RIGHT of existing scene content.
/// Includes: ground plane, ramps, narrow passages, obstacles, side slopes, drop edge.
/// All dimensions are scaled for a Humvee (5.2m x 2.8m, turn radius ~6.5m).
/// </summary>
public static class VehicleNavTestSceneSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_RootName = "NavTestPolygon";

	// Track position: 80m to the right of origin
	private static readonly Vector3 TrackOrigin = new Vector3(80f, 0f, 5f);

	// Vehicle clearance
	private const float c_LaneWidth = 12f;        // comfortable lane for Humvee
	private const float c_NarrowWidth = 5f;        // tight but passable
	private const float c_BarrierHeight = 1.2f;    // visible but not impassable
	private const float c_WallThickness = 0.5f;

	// Layers
	private const int c_LayerGround = 6;
	private const int c_LayerObstacle = 9;

	[MenuItem("Polygone/Vehicles/Build NAVIGATION Test Track")]
	public static void Build()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

		// Clean up previous build
		GameObject old = GameObject.Find(c_RootName);
		if (old != null) Object.DestroyImmediate(old);

		var root = new GameObject(c_RootName);
		root.transform.position = TrackOrigin;
		SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

		// --- GROUND ---
		// Massive flat ground slab — entire track sits on this
		CreateBox(root.transform, "Ground", new Vector3(0f, -0.5f, 90f), new Vector3(60f, 1f, 190f), new Color(0.28f, 0.30f, 0.24f), c_LayerGround);

		float x = 0f;
		float z = 0f;

		// --- START ---
		CreatePillar(root.transform, "START", x, z, Color.green);
		CreateLabel(root.transform, "L_Start", x, 3f, z, "СТАРТ");
		z += 8f;

		// ===== SECTION 1: Slalom =====
		CreateLabel(root.transform, "L_S1", x, 3f, z, "1. Змейка");
		float[] cones = { 0f, 4f, -4f, 4f, -4f, 4f };
		for (int i = 0; i < cones.Length; i++)
			Cone(root.transform, $"C{i}", x + cones[i], z + i * 5f);
		z += cones.Length * 5f + 3f;

		// ===== SECTION 2: Narrow passage =====
		CreateLabel(root.transform, "L_S2", x, 3f, z, "2. Узкий проезд");
		float nHalf = c_NarrowWidth * 0.5f;
		float nLen = 18f;
		Wall(root.transform, "S2_L", x - nHalf, z + nLen * 0.5f, c_WallThickness, c_BarrierHeight, nLen, Color.yellow);
		Wall(root.transform, "S2_R", x + nHalf, z + nLen * 0.5f, c_WallThickness, c_BarrierHeight, nLen, Color.yellow);
		z += nLen + 3f;

		// ===== SECTION 3: Sharp right turn =====
		CreateLabel(root.transform, "L_S3", x, 3f, z, "3. Крутой поворот 90°");
		Wall(root.transform, "S3_Block", x, z + 5f, 10f, c_BarrierHeight, c_WallThickness, Color.red);
		x += 8f;
		z += 10f;

		// ===== SECTION 4: Obstacle — wall blocks path =====
		CreateLabel(root.transform, "L_S4", x, 3f, z, "4. Преграда (объезд справа)");
		Wall(root.transform, "S4_Block", x - 2f, z + 4f, 8f, c_BarrierHeight, 0.6f, Color.red);
		Wall(root.transform, "S4_Wall", x + 5f, z + 4f, c_WallThickness, c_BarrierHeight, 10f, Color.yellow);
		x += 5f;
		z += 12f;

		// ===== SECTION 5: Ramp up + plateau + down =====
		CreateLabel(root.transform, "L_S5a", x, 3f, z, "5a. Подъём 10°");
		Ramp(root.transform, "S5_Up", x, z, 14f, 2.4f);
		z += 14f;

		CreateBox(root.transform, "S5_Top", new Vector3(x, 1.2f, z + 3f), new Vector3(c_LaneWidth, 2.4f, 6f), new Color(0.40f, 0.48f, 0.36f), c_LayerGround);
		z += 6f;

		CreateLabel(root.transform, "L_S5b", x, 3f, z, "5b. Спуск");
		Ramp(root.transform, "S5_Down", x, z + 2f, 10f, -2.4f);
		z += 12f;

		// ===== SECTION 6: Side slope =====
		CreateLabel(root.transform, "L_S6", x, 3f, z, "6. Боковой уклон");
		SideSlope(root.transform, "S6_Slope", x, z, 14f, 0.7f);
		z += 16f;

		// ===== SECTION 7: Drop edge =====
		CreateLabel(root.transform, "L_S7", x, 3f, z, "7. Край / сброс");
		CreateBox(root.transform, "S7_Platform", new Vector3(x, 0.5f, z + 3f), new Vector3(c_LaneWidth, 1f, 6f), new Color(0.50f, 0.42f, 0.32f), c_LayerGround);
		Wall(root.transform, "S7_Edge", x, z + 6.5f, c_LaneWidth, 0.1f, 0.3f, Color.red);
		z += 8f;

		// ===== SECTION 8: Reverse target =====
		CreateLabel(root.transform, "L_S8", x, 3f, z, "8. Цель сзади (reverse)");
		CreateMarker(root.transform, "S8_Target", x, z + 3f, Color.magenta, 1.5f);
		Wall(root.transform, "S8_L", x - 3f, z + 2f, c_WallThickness, c_BarrierHeight, 5f, Color.yellow);
		Wall(root.transform, "S8_R", x + 3f, z + 2f, c_WallThickness, c_BarrierHeight, 5f, Color.yellow);
		z += 7f;

		// ===== SECTION 9: Heading arrival =====
		CreateLabel(root.transform, "L_S9", x, 3f, z, "9. Прибытие по стрелке (heading)");
		CreateMarker(root.transform, "S9_Target", x, z + 3f, Color.cyan, 2f);
		Arrow(root.transform, "S9_Arrow", x, 0.15f, z + 3f, Vector3.forward, 3f, Color.cyan);
		z += 7f;

		// ===== SECTION 10: Waypoint chain =====
		CreateLabel(root.transform, "L_S10", x, 3f, z, "10. Цепочка точек (Shift+клик)");
		for (int i = 0; i < 4; i++)
		{
			float ox = (i % 2 == 0) ? -4f : 4f;
			CreateMarker(root.transform, $"S10_{i}", x + ox, z + 3f + i * 5f, new Color(1f, 0.7f, 0.2f), 1.2f);
			CreateLabel(root.transform, $"S10_L{i}", x + ox, 2f, z + 3f + i * 5f, $"{i + 1}");
		}
		z += 3f + 4 * 5f + 4f;

		// ===== FINISH =====
		CreatePillar(root.transform, "FINISH", x, z, Color.red);
		CreateLabel(root.transform, "L_Finish", x, 3f, z, "ФИНИШ");

		// --- CAMERA ---
		Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
		if (cam != null)
		{
			cam.transform.position = new Vector3(TrackOrigin.x, 22f, TrackOrigin.z - 5f);
			cam.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
		}

		// --- VEHICLE ---
		PlaceVehicleAtStart();

		// --- NAVMESH ---
		UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

		// --- SAVE ---
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);

		Debug.Log("[Polygon] Трек готов. X=80 Z=5, длина ~180м, 10 секций.");
	}

	private static void PlaceVehicleAtStart()
	{
		VehicleController v = Object.FindFirstObjectByType<VehicleController>();
		if (v != null)
		{
			v.transform.position = new Vector3(TrackOrigin.x, 1f, TrackOrigin.z + 3f);
			v.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
			return;
		}

		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Vehicles/Light_Armored_Car.prefab");
		if (prefab != null)
		{
			GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
			inst.transform.position = new Vector3(TrackOrigin.x, 1f, TrackOrigin.z + 3f);
			inst.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
			SceneManager.MoveGameObjectToScene(inst, SceneManager.GetActiveScene());
		}
	}

	// ===== PRIMITIVE BUILDERS =====

	private static void CreateBox(Transform p, string n, Vector3 pos, Vector3 scale, Color c, int layer)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n; go.transform.SetParent(p, false);
		go.transform.localPosition = pos; go.transform.localScale = scale; go.layer = layer;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Wall(Transform p, string n, float x, float z, float sx, float sy, float sz, Color c)
	{
		CreateBox(p, n, new Vector3(x, sy * 0.5f, z), new Vector3(sx, sy, sz), c, c_LayerObstacle);
	}

	private static void Cone(Transform p, string n, float x, float z)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		go.name = n; go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, 0.7f, z);
		go.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);
		go.layer = c_LayerObstacle;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(1f, 0.5f, 0f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Ramp(Transform p, string n, float x, float z, float len, float rise)
	{
		float abs = Mathf.Abs(rise);
		float hyp = Mathf.Sqrt(len * len + abs * abs);
		float ang = Mathf.Atan2(abs, len) * Mathf.Rad2Deg;
		float pitch = rise >= 0f ? -ang : ang;

		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n; go.transform.SetParent(p, false); go.layer = c_LayerGround;
		go.transform.localPosition = new Vector3(x, abs * 0.5f, z + len * 0.5f);
		go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
		go.transform.localScale = new Vector3(c_LaneWidth, 0.3f, hyp);
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.38f, 0.52f, 0.72f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void SideSlope(Transform p, string n, float x, float z, float len, float bank)
	{
		float half = c_LaneWidth * 0.5f;
		float hyp = Mathf.Sqrt(half * half + bank * bank);
		float ang = Mathf.Atan2(bank, half) * Mathf.Rad2Deg;

		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n; go.transform.SetParent(p, false); go.layer = c_LayerGround;
		go.transform.localPosition = new Vector3(x + half * 0.5f, bank * 0.5f, z + len * 0.5f);
		go.transform.localRotation = Quaternion.Euler(0f, 0f, -ang);
		go.transform.localScale = new Vector3(hyp, 0.25f, len);
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.55f, 0.42f, 0.58f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void CreateMarker(Transform p, string n, float x, float z, Color c, float s)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n; go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, 0.06f, z);
		go.transform.localScale = new Vector3(s, 0.12f, s);
		go.layer = c_LayerGround;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void CreatePillar(Transform p, string n, float x, float z, Color c)
	{
		CreateBox(p, n, new Vector3(x, 0.75f, z), new Vector3(1f, 1.5f, 1f), c, c_LayerGround);
	}

	private static void Arrow(Transform p, string n, float x, float y, float z, Vector3 dir, float len, Color c)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n; go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, y, z) + dir * (len * 0.5f);
		go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
		go.transform.localScale = new Vector3(0.25f, 0.12f, len);
		go.layer = c_LayerGround;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
	}

	private static void CreateLabel(Transform p, string n, float x, float y, float z, string text)
	{
		var go = new GameObject(n);
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, y, z);
		go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		var tmp = go.AddComponent<TMPro.TextMeshPro>();
		tmp.text = text;
		tmp.fontSize = 4f;
		tmp.alignment = TMPro.TextAlignmentOptions.Center;
		tmp.color = Color.white;
		tmp.enableWordWrapping = false;
	}

	private static Material Mat(Color c)
	{
		Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
		if (sh == null) return null;
		var m = new Material(sh);
		if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
		else if (m.HasProperty("_Color")) m.color = c;
		return m;
	}
}
#endif
