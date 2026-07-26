using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a navigation test track at a fixed world position.
/// 10 sections of increasing complexity.
/// </summary>
public static class VehicleNavigationTestArea
{
	public const string RootName = "VehicleNavTestTrack";

	// Fixed track origin — far from existing scene content
	public static readonly Vector3 TrackOrigin = new Vector3(80f, 0f, 5f);

	// Ground slab size covering entire track
	private const float c_GroundW = 40f;
	private const float c_GroundL = 180f;
	private const float c_Lane = 10f;
	private const float c_BarrierH = 1.5f;
	private const float c_BarrierT = 0.4f;

	private const int c_LayerGround = 6;   // "Ground"
	private const int c_LayerObstacle = 9;  // "Obstacle"

	public static GameObject Build()
	{
		// Remove old track
		GameObject old = GameObject.Find(RootName);
		if (old != null) DestroyGo(old);

		// Remove old ground slab
		GameObject oldGround = GameObject.Find("NavTestGround");
		if (oldGround != null) DestroyGo(oldGround);

		var root = new GameObject(RootName);
		root.transform.position = TrackOrigin;
		SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

		// Large drivable ground slab (top face at y=0)
		CreateGroundSlab(root.transform);

		float x = 0f;
		float z = 0f;

		// === START ===
		Marker(root.transform, "START", x, z, Color.green, 3f);
		Label(root.transform, "L_Start", x, 2f, z, "СТАРТ");
		z += 6f;

		// === 1. Slalom ===
		Label(root.transform, "L_S1", x, 2f, z, "1. Змейка");
		float[] sl = { 0f, 3.5f, -3.5f, 3.5f, -3.5f, 3.5f };
		for (int i = 0; i < sl.Length; i++)
			Cone(root.transform, $"S1_C{i}", x + sl[i], z + 3f + i * 4f);
		z += 3f + sl.Length * 4f + 2f;

		// === 2. Narrow passage (3m gap) ===
		Label(root.transform, "L_S2", x, 2f, z, "2. Узкий проезд (3м)");
		Wall(root.transform, "S2_WL", x - 1.5f, z + 7f, 0.4f, c_BarrierH, 14f, Color.yellow);
		Wall(root.transform, "S2_WR", x + 1.5f, z + 7f, 0.4f, c_BarrierH, 14f, Color.yellow);
		z += 16f;

		// === 3. Sharp 90° turn right ===
		Label(root.transform, "L_S3", x, 2f, z, "3. Поворот 90°");
		Wall(root.transform, "S3_Block", x, z + 6f, 8f, c_BarrierH, 0.4f, Color.red);
		// Opening to the right
		x += 7f;
		z += 10f;

		// === 4. Obstacle — wall blocks path, detour right ===
		Label(root.transform, "L_S4", x, 2f, z, "4. Препятствие");
		Wall(root.transform, "S4_Block", x - 3f, z + 4f, 6f, c_BarrierH, 0.5f, Color.red);
		Wall(root.transform, "S4_WR", x + 4f, z + 4f, 0.4f, c_BarrierH, 10f, Color.yellow);
		x += 5f;
		z += 12f;

		// === 5. Ramp up + plateau + ramp down ===
		Label(root.transform, "L_S5", x, 2f, z, "5. Подъём 10°");
		Ramp(root.transform, "S5_Up", x, z, 12f, 2f, c_Lane);
		z += 12f;
		Box(root.transform, "S5_Plateau", x, 1f, z + 3.5f, c_Lane, 2f, 7f, new Color(0.4f, 0.5f, 0.35f), c_LayerGround);
		z += 7f;
		Ramp(root.transform, "S5_Dn", x, z + 2f, 8f, -2f, c_Lane);
		z += 10f;

		// === 6. Side slope ===
		Label(root.transform, "L_S6", x, 2f, z, "6. Боковой уклон");
		SideBank(root.transform, "S6_Bank", x, z, 12f, 0.6f, c_Lane);
		z += 14f;

		// === 7. Dead end / drop edge ===
		Label(root.transform, "L_S7", x, 2f, z, "7. Тупик / обрыв");
		Box(root.transform, "S7_Platform", x, 0.5f, z + 3f, c_Lane, 1f, 6f, new Color(0.5f, 0.4f, 0.3f), c_LayerGround);
		Wall(root.transform, "S7_Stop", x, z + 6.5f, c_Lane, 0.1f, 0.3f, Color.red);
		z += 8f;

		// === 8. Reverse target ===
		Label(root.transform, "L_S8", x, 2f, z, "8. Цель сзади (задний ход)");
		Marker(root.transform, "S8_Target", x, z + 2f, Color.magenta, 1.2f);
		Label(root.transform, "S8_Lbl", x, 1.5f, z + 2f, "Reverse");
		Wall(root.transform, "S8_WL", x - 3f, z + 1.5f, 0.4f, c_BarrierH, 4f, Color.yellow);
		Wall(root.transform, "S8_WR", x + 3f, z + 1.5f, 0.4f, c_BarrierH, 4f, Color.yellow);
		z += 6f;

		// === 9. Heading arrival ===
		Label(root.transform, "L_S9", x, 2f, z, "9. Прибытие с курсом");
		Marker(root.transform, "S9_Target", x, z + 2f, Color.cyan, 1.5f);
		Arrow(root.transform, "S9_Arrow", x, 0.1f, z + 2f, Vector3.forward, 2.5f, Color.cyan);
		z += 6f;

		// === 10. Queue / waypoints ===
		Label(root.transform, "L_S10", x, 2f, z, "10. Очередь (Shift+клик)");
		for (int i = 0; i < 4; i++)
		{
			float ox = (i % 2 == 0) ? -3.5f : 3.5f;
			Marker(root.transform, $"S10_Q{i}", x + ox, z + 3f + i * 5f, new Color(1f, 0.7f, 0.2f), 0.9f);
			Label(root.transform, $"S10_L{i}", x + ox, 1f, z + 3f + i * 5f, $"{i + 1}");
		}
		z += 3f + 4 * 5f + 2f;

		// === FINISH ===
		Marker(root.transform, "FINISH", x, z, Color.red, 3f);
		Label(root.transform, "L_Finish", x, 2f, z, "ФИНИШ");

		return root;
	}

	// --- Primitive builders ---

	private static void CreateGroundSlab(Transform _parent)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = "NavTestGround";
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = new Vector3(0f, -0.5f, c_GroundL * 0.5f);
		go.transform.localScale = new Vector3(c_GroundW, 1f, c_GroundL);
		go.layer = c_LayerGround;
		if (go.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = Mat(new Color(0.25f, 0.28f, 0.22f));
		if (go.TryGetComponent(out Collider c)) { c.isTrigger = false; c.enabled = true; }
	}

	private static void Cone(Transform p, string n, float x, float z)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		go.name = n;
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, 0.8f, z);
		go.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
		go.layer = c_LayerObstacle;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(1f, 0.5f, 0f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Wall(Transform p, string n, float x, float z, float sx, float sy, float sz, Color c)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, sy * 0.5f, z);
		go.transform.localScale = new Vector3(sx, sy, sz);
		go.layer = c_LayerObstacle;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Box(Transform p, string n, float x, float y, float z, float sx, float sy, float sz, Color c, int layer)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, y, z);
		go.transform.localScale = new Vector3(sx, sy, sz);
		go.layer = layer;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Ramp(Transform p, string n, float x, float z, float len, float rise, float w)
	{
		float abs = Mathf.Abs(rise);
		float hyp = Mathf.Sqrt(len * len + abs * abs);
		float ang = Mathf.Atan2(abs, len) * Mathf.Rad2Deg;
		float pitch = rise >= 0f ? -ang : ang;

		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.layer = c_LayerGround;
		go.transform.localPosition = new Vector3(x, abs * 0.5f, z + len * 0.5f);
		go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
		go.transform.localScale = new Vector3(w, 0.3f, hyp);
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.35f, 0.5f, 0.7f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void SideBank(Transform p, string n, float x, float z, float len, float bank, float w)
	{
		float half = w * 0.5f;
		float hyp = Mathf.Sqrt(half * half + bank * bank);
		float ang = Mathf.Atan2(bank, half) * Mathf.Rad2Deg;

		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.layer = c_LayerGround;
		go.transform.localPosition = new Vector3(x + half * 0.5f, bank * 0.5f, z + len * 0.5f);
		go.transform.localRotation = Quaternion.Euler(0f, 0f, -ang);
		go.transform.localScale = new Vector3(hyp, 0.25f, len);
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.55f, 0.4f, 0.55f));
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Marker(Transform p, string n, float x, float z, Color c, float s)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, 0.06f, z);
		go.transform.localScale = new Vector3(s, 0.12f, s);
		go.layer = c_LayerGround;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
		go.GetComponent<Collider>().isTrigger = false;
	}

	private static void Arrow(Transform p, string n, float x, float y, float z, Vector3 dir, float len, Color c)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = n;
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, y, z) + dir * (len * 0.5f);
		go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
		go.transform.localScale = new Vector3(0.2f, 0.1f, len);
		go.layer = c_LayerGround;
		go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
	}

	private static void Label(Transform p, string n, float x, float y, float z, string text)
	{
		var go = new GameObject(n);
		go.transform.SetParent(p, false);
		go.transform.localPosition = new Vector3(x, y, z);
		go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		var tmp = go.AddComponent<TMPro.TextMeshPro>();
		tmp.text = text;
		tmp.fontSize = 3f;
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
		if (m.HasProperty("_Color")) m.color = c;
		return m;
	}

	private static void DestroyGo(GameObject go)
	{
		if (go == null) return;
		if (Application.isPlaying) Object.Destroy(go);
		else Object.DestroyImmediate(go);
	}
}