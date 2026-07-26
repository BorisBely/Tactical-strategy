using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a WheelCollider physics demo: solid ground slab + relief scaled for Light_Armored_Car
/// (≈5.2 m long × 2.8 m wide, wheel radius 0.45 m). Idempotent.
/// </summary>
public static class VehiclePhysicsDemoArea
{
	#region Constants
	public const string RootName = "VehiclePhysicsDemo";
	private const string c_GroundName = "PhysicsGroundCollider";

	// Humvee-scale references (metres)
	private const float c_LaneWidth = 9f;
	private const float c_WheelRadius = 0.45f;
	#endregion

	#region Public API
	public static GameObject EnsureInActiveScene()
	{
		GameObject existing = GameObject.Find(RootName);
		if (existing != null)
			DestroyGo(existing);

		Vector3 origin = ResolveOrigin();
		var root = new GameObject(RootName);
		root.transform.position = origin;
		SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

		// Solid drive surface for WheelColliders under car + course (top face at y=0).
		// Origin is ~12 m ahead of the car, so extend backward to cover the spawn.
		CreateGroundSlab(root.transform, new Vector3(0f, -0.5f, 24f), new Vector3(30f, 1f, 80f));

		float z = 0f;

		CreateMarker(root.transform, "01_Approach", new Vector3(0f, 0.05f, z + 4f), new Color(0.35f, 0.55f, 0.35f));
		z += 8f;

		// Speed bumps ~1/3..2/5 of wheel radius — suspension demo, not barriers
		CreateBump(root.transform, "02_Bump_A", new Vector3(0f, 0f, z + 2f), c_WheelRadius * 0.33f, 1.6f);
		CreateBump(root.transform, "02_Bump_B", new Vector3(0f, 0f, z + 5.5f), c_WheelRadius * 0.4f, 1.8f);
		CreateBump(root.transform, "02_Bump_C", new Vector3(0f, 0f, z + 9f), c_WheelRadius * 0.28f, 1.4f);
		z += 12f;

		// Gentle climb ≈4° (1 m / 14 m)
		CreateRamp(root.transform, "03_RampUp_Gentle", z, 14f, 1.0f, c_LaneWidth);
		z += 14f;

		CreateBox(root.transform, "04_Plateau", new Vector3(0f, 0.5f, z + 3f), new Vector3(c_LaneWidth, 1.0f, 6f),
			new Color(0.42f, 0.48f, 0.38f));
		z += 6f;

		CreateRamp(root.transform, "05_RampDown", z, 12f, -1.0f, c_LaneWidth);
		z += 12f;

		for (int i = 0; i < 6; i++)
		{
			CreateBump(root.transform, $"06_Washboard_{i}", new Vector3(0f, 0f, z + 1.2f + i * 1.5f),
				0.08f, 0.9f);
		}

		z += 10f;

		CreateSideBank(root.transform, "07_SideBank", z, 10f, 0.35f);
		z += 12f;

		// Steeper ≈9° (1.4 m / 9 m) — still within Humvee climb
		CreateRamp(root.transform, "08_RampUp_Steeper", z, 9f, 1.4f, c_LaneWidth);
		z += 9f;
		CreateBox(root.transform, "09_HighPlateau", new Vector3(0f, 0.7f, z + 2.5f), new Vector3(c_LaneWidth, 1.4f, 5f),
			new Color(0.5f, 0.45f, 0.35f));
		z += 5f;
		CreateRamp(root.transform, "10_RampDown_Steeper", z, 9f, -1.4f, c_LaneWidth);

		CreateMarker(root.transform, "END_Demo", new Vector3(0f, 0.05f, z + 12f), new Color(0.7f, 0.25f, 0.2f));

		EnsurePlaneMeshCollider();
		return root;
	}
	#endregion

	#region Private Builders
	private static Vector3 ResolveOrigin()
	{
		VehicleController vehicle = Object.FindFirstObjectByType<VehicleController>();
		if (vehicle != null)
		{
			Vector3 p = vehicle.transform.position;
			return new Vector3(p.x, 0f, p.z + 12f);
		}

		GameObject named = GameObject.Find("Light_Armored_Car");
		if (named != null)
		{
			Vector3 p = named.transform.position;
			return new Vector3(p.x, 0f, p.z + 12f);
		}

		return new Vector3(13.24f, 0f, 18f);
	}

	private static void EnsurePlaneMeshCollider()
	{
		GameObject plane = GameObject.Find("Plane");
		if (plane == null)
			return;

		// If we already built a dedicated ground slab, the Plane mesh collider
		// overlaps it at y=0 and causes WheelCollider jitter. Disable it.
		if (GameObject.Find(c_GroundName) != null)
		{
			if (plane.TryGetComponent(out MeshCollider existingCol))
				existingCol.enabled = false;
			return;
		}

		if (!plane.TryGetComponent(out MeshCollider meshCol))
			meshCol = plane.AddComponent<MeshCollider>();
		meshCol.convex = false;
		meshCol.enabled = true;

		int ground = LayerMask.NameToLayer("Ground");
		if (ground >= 0)
			plane.layer = ground;
	}

	private static void CreateGroundSlab(Transform _parent, Vector3 _localPos, Vector3 _size)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = c_GroundName;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = _size;
		go.layer = ResolveGroundLayer();

		if (go.TryGetComponent(out MeshRenderer renderer))
			renderer.enabled = false;

		if (go.TryGetComponent(out Collider col))
		{
			col.isTrigger = false;
			col.enabled = true;
		}
	}

	private static void CreateBump(Transform _parent, string _name, Vector3 _localPos, float _height, float _length)
	{
		CreateBox(_parent, _name,
			_localPos + Vector3.up * (_height * 0.5f),
			new Vector3(c_LaneWidth * 0.92f, _height, _length),
			new Color(0.72f, 0.45f, 0.22f));
	}

	private static void CreateRamp(
		Transform _parent,
		string _name,
		float _startZ,
		float _length,
		float _rise,
		float _width)
	{
		float absRise = Mathf.Abs(_rise);
		float hyp = Mathf.Sqrt(_length * _length + absRise * absRise);
		float angleDeg = Mathf.Atan2(absRise, _length) * Mathf.Rad2Deg;

		// Ascending toward +Z uses negative X pitch; descending uses positive.
		float pitch = _rise >= 0f ? -angleDeg : angleDeg;

		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.layer = ResolveGroundLayer();

		float centerY = absRise * 0.5f;
		float centerZ = _startZ + _length * 0.5f;
		go.transform.localPosition = new Vector3(0f, centerY, centerZ);
		go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
		go.transform.localScale = new Vector3(_width, 0.28f, hyp);

		ApplyColor(go, _rise >= 0f ? new Color(0.38f, 0.55f, 0.72f) : new Color(0.45f, 0.58f, 0.68f));
		ConfigureCollider(go);
	}

	private static void CreateSideBank(Transform _parent, string _name, float _startZ, float _length, float _bankHeight)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.layer = ResolveGroundLayer();

		float halfLane = c_LaneWidth * 0.5f;
		float hyp = Mathf.Sqrt(halfLane * halfLane + _bankHeight * _bankHeight);
		float angle = Mathf.Atan2(_bankHeight, halfLane) * Mathf.Rad2Deg;

		go.transform.localPosition = new Vector3(halfLane * 0.5f, _bankHeight * 0.5f, _startZ + _length * 0.5f);
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

	private static void CreateMarker(Transform _parent, string _name, Vector3 _localPos, Color _color)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(_parent, false);
		go.transform.localPosition = _localPos;
		go.transform.localScale = new Vector3(1.2f, 0.1f, 1.2f);
		go.layer = ResolveGroundLayer();
		ApplyColor(go, _color);
		ConfigureCollider(go);
	}

	private static void ApplyColor(GameObject _go, Color _color)
	{
		if (!_go.TryGetComponent(out MeshRenderer renderer))
			return;

		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null)
			shader = Shader.Find("Standard");
		if (shader == null)
			return;

		var mat = new Material(shader);
		if (mat.HasProperty("_BaseColor"))
			mat.SetColor("_BaseColor", _color);
		if (mat.HasProperty("_Color"))
			mat.color = _color;
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
		if (_go == null)
			return;
		if (Application.isPlaying)
			Object.Destroy(_go);
		else
			Object.DestroyImmediate(_go);
	}
	#endregion
}
