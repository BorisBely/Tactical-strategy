using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a comprehensive navigation test track for the vehicle navigation system v2.0.
/// Places the track to the RIGHT of the existing targets/demo area.
/// Includes: slalom, narrow passage, sharp turns, obstacles, ramps, drops,
/// heading-arrival test, reverse test, and multi-waypoint queue test.
/// </summary>
public static class VehicleNavigationTestArea
{
	public const string RootName = "VehicleNavTestTrack";

	private const float c_LaneWidth = 10f;
	private const float c_TrackOffsetX = 60f; // offset to the right from car position
	private const float c_BarrierHeight = 1.2f;
	private const float c_BarrierThickness = 0.3f;

	#region Public API
	public static GameObject Build(Vector3 _origin)
	{
		GameObject existing = GameObject.Find(RootName);
		if (existing != null)
			DestroyGo(existing);

		var root = new GameObject(RootName);
		root.transform.position = _origin;
		SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

		float x = 0f;
		float z = 0f;

		// --- Start area ---
		CreateMarker(root.transform, "START", new Vector3(x, 0.05f, z), Color.green, 2f);
		CreateLabel(root.transform, "START", new Vector3(x, 1.5f, z), "СТАРТ");
		z += 4f;

		// --- Section 1: Slalom (cones every 4m, offset ±3m) ---
		CreateLabel(root.transform, "S1_Slalom", new Vector3(x, 1.5f, z), "1. Змейка (слалом)");
		float[] slalomOffsets = { 0f, 3f, -3f, 3f, -3f, 3f };
		for (int i = 0; i < slalomOffsets.Length; i++)
		{
			CreateCone(root.transform, $"Cone_S1_{i}", new Vector3(x + slalomOffsets[i], 0f, z + 2f + i * 4f));
		}
		z += 2f + slalomOffsets.Length * 4f;

		// --- Section 2: Narrow passage (walls on both sides, 3m gap) ---
		CreateLabel(root.transform, "S2_Narrow", new Vector3(x, 1.5f, z), "2. Узкий проезд (3м)");
		float narrowWidth = 1.5f;
		float narrowLen = 12f;
		CreateWall(root.transform, "Wall_L_S2", new Vector3(x - narrowWidth, 0.6f, z + narrowLen * 0.5f), new Vector3(c_BarrierThickness, c_BarrierHeight, narrowLen), Color.yellow);
		CreateWall(root.transform, "Wall_R_S2", new Vector3(x + narrowWidth, 0.6f, z + narrowLen * 0.5f), new Vector3(c_BarrierThickness, c_BarrierHeight, narrowLen), Color.yellow);
		z += narrowLen + 2f;

		// --- Section 3: Tight right turn ---
		CreateLabel(root.transform, "S3_TightTurn", new Vector3(x, 1.5f, z), "3. Крутой поворот (вправо 90°)");
		// Outer wall to force the turn
		CreateWall(root.transform, "Wall_L_S3", new Vector3(x - 3f, 0.6f, z + 4f), new Vector3(c_BarrierThickness, c_BarrierHeight, 14f), Color.red);
		z += 8f;
		// Path turns right
		float turnX = x + 6f;
		float turnZ = z;
		CreateWall(root.transform, "Wall_Back_S3", new Vector3(turnX + 3f, 0.6f, turnZ + 2f), new Vector3(c_BarrierThickness, c_BarrierHeight, 8f), Color.red);
		x = turnX;
		z = turnZ + 6f;

		// --- Section 4: Obstacle ahead (wall blocks path, must detour) ---
		CreateLabel(root.transform, "S4_Obstacle", new Vector3(x, 1.5f, z), "4. Препятствие (объезд)");
		CreateWall(root.transform, "Block_S4", new Vector3(x, 0.6f, z + 4f), new Vector3(3f, c_BarrierHeight, 1f), Color.red);
		// Opening on the right
		CreateWall(root.transform, "Wall_R_S4", new Vector3(x + 4f, 0.6f, z + 4f), new Vector3(c_BarrierThickness, c_BarrierHeight, 8f), Color.yellow);
		z += 10f;
		// Path shifted right
		x += 4f;

		// --- Section 5: Ramp up ---
		CreateLabel(root.transform, "S5_Ramp", new Vector3(x, 1.5f, z), "5. Подъём (10°)");
		float rampLen = 10f;
		float rampRise = 1.7f;
		CreateRamp(root.transform, "Ramp_S5", new Vector3(x, 0f, z), rampLen, rampRise, c_LaneWidth);
		z += rampLen;
		// Plateau
		CreateBox(root.transform, "Plateau_S5", new Vector3(x, rampRise * 0.5f, z + 3f), new Vector3(c_LaneWidth, rampRise, 6f), new Color(0.4f, 0.5f, 0.35f));
		z += 6f;
		// Descent
		CreateRamp(root.transform, "RampDown_S5", new Vector3(x, rampRise, z), rampLen * 0.7f, -rampRise, c_LaneWidth);
		z += rampLen * 0.7f;

		// --- Section 6: Side slope ---
		CreateLabel(root.transform, "S6_SideSlope", new Vector3(x, 1.5f, z), "6. Боковой уклон (15°)");
		CreateSideBank(root.transform, "Bank_S6", new Vector3(x, 0f, z), 10f, 0.5f, c_LaneWidth);
		z += 12f;

		// --- Section 7: Drop edge test ---
		CreateLabel(root.transform, "S7_DropEdge", new Vector3(x, 1.5f, z), "7. Обрыв (стоп-линия)");
		// Platform that ends abruptly
		CreateBox(root.transform, "DropPlateau_S7", new Vector3(x, 0.25f, z + 2f), new Vector3(c_LaneWidth, 0.5f, 4f), new Color(0.5f, 0.4f, 0.3f));
		CreateWall(root.transform, "DropStop_S7", new Vector3(x, 0.05f, z + 4.5f), new Vector3(c_LaneWidth, 0.05f, 0.3f), Color.red);
		z += 6f;

		// --- Section 8: Reverse target ---
		CreateLabel(root.transform, "S8_Reverse", new Vector3(x, 1.5f, z), "8. Цель сзади (задний ход)");
		CreateMarker(root.transform, "RevTarget_S8", new Vector3(x, 0.05f, z + 2f), Color.magenta, 1f);
		CreateLabel(root.transform, "RevLbl_S8", new Vector3(x, 1f, z + 2f), "Reverse");
		// walls to force reverse approach
		CreateWall(root.transform, "Wall_L_S8", new Vector3(x - 2.5f, 0.6f, z + 1f), new Vector3(c_BarrierThickness, c_BarrierHeight, 3f), Color.yellow);
		CreateWall(root.transform, "Wall_R_S8", new Vector3(x + 2.5f, 0.6f, z + 1f), new Vector3(c_BarrierThickness, c_BarrierHeight, 3f), Color.yellow);
		z += 5f;

		// --- Section 9: Heading arrival ---
		CreateLabel(root.transform, "S9_Heading", new Vector3(x, 1.5f, z), "9. Прибытие с направлением (капот на СЕВЕР)");
		CreateMarker(root.transform, "Heading_S9", new Vector3(x, 0.05f, z + 2f), Color.cyan, 1.5f);
		// Arrow pointing north (Z+)
		CreateArrow(root.transform, "Arrow_S9", new Vector3(x, 0.1f, z + 2f), Vector3.forward, 2f, Color.cyan);
		z += 5f;

		// --- Section 10: Multi-waypoint queue ---
		CreateLabel(root.transform, "S10_Queue", new Vector3(x, 1.5f, z), "10. Очередь точек (Shift+клик)");
		for (int i = 0; i < 4; i++)
		{
			float ox = (i % 2 == 0) ? -3f : 3f;
			CreateMarker(root.transform, $"Q{i}_S10", new Vector3(x + ox, 0.05f, z + 2f + i * 5f), new Color(1f, 0.7f, 0.2f), 0.8f);
			CreateLabel(root.transform, $"Q{i}Lbl_S10", new Vector3(x + ox, 0.8f, z + 2f + i * 5f), $"{i + 1}");
		}
		z += 2f + 4 * 5f;

		// --- Finish ---
		CreateMarker(root.transform, "FINISH", new Vector3(x, 0.05f, z), Color.red, 2f);
		CreateLabel(root.transform, "FINISH_Lbl", new Vector3(x, 1.5f, z), "ФИНИШ");

		return root;
	}
	#endregion

	#region Builders
	private static void CreateCone(Transform _parent, string _name, Vector3 _localPos)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = new Vector3(0.4f, 0.8f, 0.4f);
		go.layer = ResolveGroundLayer();
		ApplyColor(go, new Color(1f, 0.55f, 0f));
		ConfigureCollider(go);
	}

	private static void CreateWall(Transform _parent, string _name, Vector3 _localPos, Vector3 _scale, Color _color)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = _scale;
		go.layer = ResolveGroundLayer();
		ApplyColor(go, _color);
		ConfigureCollider(go);
	}

	private static void CreateRamp(Transform _parent, string _name, Vector3 _localPos, float _length, float _rise, float _width)
	{
		float absRise = Mathf.Abs(_rise);
		float hyp = Mathf.Sqrt(_length * _length + absRise * absRise);
		float angleDeg = Mathf.Atan2(absRise, _length) * Mathf.Rad2Deg;
		float pitch = _rise >= 0f ? -angleDeg : angleDeg;

		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.layer = ResolveGroundLayer();

		float centerY = _localPos.y + absRise * 0.5f;
		float centerZ = _localPos.z + _length * 0.5f;
		go.transform.localPosition = new Vector3(_localPos.x, centerY, centerZ);
		go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
		go.transform.localScale = new Vector3(_width, 0.3f, hyp);

		ApplyColor(go, _rise >= 0f ? new Color(0.35f, 0.5f, 0.7f) : new Color(0.4f, 0.55f, 0.65f));
		ConfigureCollider(go);
	}

	private static void CreateSideBank(Transform _parent, string _name, Vector3 _localPos, float _length, float _bankHeight, float _width)
	{
		float halfWidth = _width * 0.5f;
		float hyp = Mathf.Sqrt(halfWidth * halfWidth + _bankHeight * _bankHeight);
		float angle = Mathf.Atan2(_bankHeight, halfWidth) * Mathf.Rad2Deg;

		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.layer = ResolveGroundLayer();
		go.transform.localPosition = new Vector3(_localPos.x + halfWidth * 0.5f, _localPos.y + _bankHeight * 0.5f, _localPos.z + _length * 0.5f);
		go.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
		go.transform.localScale = new Vector3(hyp, 0.25f, _length);
		ApplyColor(go, new Color(0.55f, 0.4f, 0.55f));
		ConfigureCollider(go);
	}

	private static void CreateBox(Transform _parent, string _name, Vector3 _localPos, Vector3 _scale, Color _color)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = _scale;
		go.layer = ResolveGroundLayer();
		ApplyColor(go, _color);
		ConfigureCollider(go);
	}

	private static void CreateMarker(Transform _parent, string _name, Vector3 _localPos, Color _color, float _size = 1f)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = new Vector3(_size, 0.1f, _size);
		go.layer = ResolveGroundLayer();
		ApplyColor(go, _color);
		ConfigureCollider(go);
	}

	private static void CreateArrow(Transform _parent, string _name, Vector3 _localPos, Vector3 _dir, float _length, Color _color)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos + _dir * (_length * 0.5f);
		go.transform.localRotation = Quaternion.LookRotation(_dir, Vector3.up);
		go.transform.localScale = new Vector3(0.15f, 0.08f, _length);
		go.layer = ResolveGroundLayer();
		ApplyColor(go, _color);
		ConfigureCollider(go);
	}

	private static void CreateLabel(Transform _parent, string _name, Vector3 _localPos, string _text)
	{
		GameObject go = new GameObject(_name);
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		var tmp = go.AddComponent<TMPro.TextMeshPro>();
		tmp.text = _text;
		tmp.fontSize = 2f;
		tmp.alignment = TMPro.TextAlignmentOptions.Center;
		tmp.color = Color.white;
		var rect = go.GetComponent<RectTransform>();
		if (rect != null) rect.sizeDelta = new Vector2(8f, 1.5f);
	}

	private static void ApplyColor(GameObject _go, Color _color)
	{
		if (!_go.TryGetComponent(out MeshRenderer renderer))
			return;
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		if (shader == null) return;
		var mat = new Material(shader);
		if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _color);
		if (mat.HasProperty("_Color")) mat.color = _color;
		renderer.sharedMaterial = mat;
	}

	private static void ConfigureCollider(GameObject _go)
	{
		if (_go.TryGetComponent(out Collider col))
		{
			col.isTrigger = false;
			col.enabled = true;
		}
	}

	private static int ResolveGroundLayer()
	{
		int layer = LayerMask.NameToLayer("Ground");
		return layer >= 0 ? layer : 6;
	}

	private static void DestroyGo(GameObject _go)
	{
		if (_go == null) return;
		if (Application.isPlaying)
			Object.Destroy(_go);
		else
			Object.DestroyImmediate(_go);
	}
	#endregion
}
