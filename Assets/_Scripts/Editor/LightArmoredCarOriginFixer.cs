#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fixes the Light_Armored_Car prefab origin/pivot after a hull mesh replacement.
/// - Resets the root transform to (0,0,0) so the Rigidbody/WheelColliders have a clean origin.
/// - Moves the root hull MeshFilter/Renderer under BodyVisualRoot/BodyHullMesh.
/// - Offsets BodyVisualRoot so the hull bottom sits at a sane ground clearance.
/// - Re-aligns WheelColliders to the visual wheel hubs.
/// - Forces COM to the safe Humvee value.
/// </summary>
public static class LightArmoredCarOriginFixer
{
	private const string c_PrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_VisualRootName = "BodyVisualRoot";
	private const string c_HullMeshName = "BodyHullMesh";
	private const float c_GroundClearance = 0.35f;
	private const float c_WheelRadius = 0.45f;
	private const float c_TargetHubHeight = 0.526f;

	[MenuItem("Tools/Combat Vehicle System/Fix Light Armored Car Origin")]
	public static void Fix()
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_PrefabPath);
		if (root == null)
		{
			Debug.LogError($"[LightArmoredCarOriginFixer] Failed to load prefab: {c_PrefabPath}");
			return;
		}

		try
		{
			Transform rootT = root.transform;
			Vector3 oldRootPos = rootT.localPosition;
			Quaternion oldRootRot = rootT.localRotation;
			Vector3 oldRootScale = rootT.localScale;

			Debug.Log(
				$"[LightArmoredCarOriginFixer] Old root localPosition={oldRootPos} " +
				$"rotation={oldRootRot.eulerAngles} scale={oldRootScale}");

			// 1. Root must be the physical origin, not the hull pivot.
			rootT.localPosition = Vector3.zero;
			rootT.localRotation = Quaternion.identity;
			rootT.localScale = Vector3.one;

			// 2. Ensure visual root (this is what VehicleBodyTilt rotates/offsets).
			Transform visualRoot = rootT.Find(c_VisualRootName);
			if (visualRoot == null)
			{
				GameObject vrGo = new GameObject(c_VisualRootName);
				visualRoot = vrGo.transform;
				visualRoot.SetParent(rootT, false);
			}
			visualRoot.localRotation = Quaternion.identity;
			visualRoot.localScale = Vector3.one;
			// Position will be set after bounds are known.

			// 3. Ensure hull mesh holder.
			Transform hullMesh = visualRoot.Find(c_HullMeshName);
			if (hullMesh == null)
			{
				GameObject hmGo = new GameObject(c_HullMeshName);
				hullMesh = hmGo.transform;
				hullMesh.SetParent(visualRoot, false);
			}
			hullMesh.localPosition = Vector3.zero;
			hullMesh.localRotation = Quaternion.identity;
			hullMesh.localScale = Vector3.one;
			hullMesh.gameObject.layer = root.layer;

			// 4. Move the root hull MeshFilter/Renderer to BodyHullMesh.
			MeshFilter rootFilter = root.GetComponent<MeshFilter>();
			MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();

			if (rootFilter != null)
			{
				if (!hullMesh.TryGetComponent(out MeshFilter hullFilter))
					hullFilter = hullMesh.gameObject.AddComponent<MeshFilter>();
				hullFilter.sharedMesh = rootFilter.sharedMesh;
				Object.DestroyImmediate(rootFilter);
			}

			if (rootRenderer != null)
			{
				if (!hullMesh.TryGetComponent(out MeshRenderer hullRenderer))
					hullRenderer = hullMesh.gameObject.AddComponent<MeshRenderer>();
				hullRenderer.sharedMaterials = rootRenderer.sharedMaterials;
				hullRenderer.shadowCastingMode = rootRenderer.shadowCastingMode;
				hullRenderer.receiveShadows = rootRenderer.receiveShadows;
				hullRenderer.lightProbeUsage = rootRenderer.lightProbeUsage;
				hullRenderer.reflectionProbeUsage = rootRenderer.reflectionProbeUsage;
				hullRenderer.motionVectorGenerationMode = rootRenderer.motionVectorGenerationMode;
				Object.DestroyImmediate(rootRenderer);
			}

			// 5. Normalize visual wheel hub heights (they were shifted below ground).
			NormalizeVisualWheelHeights(rootT);

			// 6. Offset the visual body so the hull bottom is above the wheel bottom by ground clearance.
			MeshFilter finalHullFilter = hullMesh.GetComponent<MeshFilter>();
			if (finalHullFilter != null && finalHullFilter.sharedMesh != null)
			{
				Bounds b = finalHullFilter.sharedMesh.bounds;
				float wheelBottom = c_TargetHubHeight - c_WheelRadius;
				float hullBottom = wheelBottom + c_GroundClearance;
				Vector3 visualOffset = new Vector3(
					-b.center.x,
					hullBottom - b.min.y,
					-b.center.z);
				visualRoot.localPosition = visualOffset;

				Debug.Log(
					$"[LightArmoredCarOriginFixer] Hull bounds={b}; " +
					$"BodyVisualRoot.localPosition={visualOffset} " +
					$"(clearance={c_GroundClearance}, wheelBottom={wheelBottom})");
			}
			else
			{
				Debug.LogWarning("[LightArmoredCarOriginFixer] No hull mesh found; cannot auto-align.");
				visualRoot.localPosition = Vector3.zero;
			}

			// 7. Re-align WheelColliders to visual wheel hubs.
			RealignWheelColliders(rootT);

			// 8. Fix Rigidbody COM/mass.
			Rigidbody body = root.GetComponent<Rigidbody>();
			if (body != null)
			{
				Undo.RecordObject(body, "Fix COM");
				body.centerOfMass = new Vector3(0f, 0.55f, 0.15f);
				body.mass = 2400f;
			}

			PrefabUtility.SaveAsPrefabAsset(root, c_PrefabPath);
			Debug.Log("[LightArmoredCarOriginFixer] Prefab fixed and saved.");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void NormalizeVisualWheelHeights(Transform _root)
	{
		string[] visualNames = new[]
		{
			"SM_Veh_Light_Armored_Car_01_Wheel_fl",
			"SM_Veh_Light_Armored_Car_01_Wheel_fr",
			"SM_Veh_Light_Armored_Car_01_Wheel_rl",
			"SM_Veh_Light_Armored_Car_01_Wheel_rr",
		};

		foreach (string name in visualNames)
		{
			Transform visual = FindDeep(_root, name);
			if (visual == null)
			{
				Debug.LogWarning($"[LightArmoredCarOriginFixer] Visual wheel not found: {name}");
				continue;
			}

			Undo.RecordObject(visual, "Normalize wheel height");
			Vector3 lp = visual.localPosition;
			lp.y = c_TargetHubHeight;
			visual.localPosition = lp;
			Debug.Log($"[LightArmoredCarOriginFixer] {name} hub normalized to local y={c_TargetHubHeight}");
		}
	}

	private static void RealignWheelColliders(Transform _root)
	{
		(string visualName, string colliderName)[] pairs = new[]
		{
			("SM_Veh_Light_Armored_Car_01_Wheel_fl", "WheelCollider_FL"),
			("SM_Veh_Light_Armored_Car_01_Wheel_fr", "WheelCollider_FR"),
			("SM_Veh_Light_Armored_Car_01_Wheel_rl", "WheelCollider_RL"),
			("SM_Veh_Light_Armored_Car_01_Wheel_rr", "WheelCollider_RR"),
		};

		foreach (var pair in pairs)
		{
			Transform visual = FindDeep(_root, pair.visualName);
			Transform colT = _root.Find(pair.colliderName);
			if (visual == null)
			{
				Debug.LogWarning($"[LightArmoredCarOriginFixer] Visual wheel not found: {pair.visualName}");
				continue;
			}
			if (colT == null)
			{
				Debug.LogWarning($"[LightArmoredCarOriginFixer] WheelCollider not found: {pair.colliderName}");
				continue;
			}

			Vector3 localPos = _root.InverseTransformPoint(visual.position);
			colT.localPosition = localPos;

			WheelCollider wc = colT.GetComponent<WheelCollider>();
			if (wc != null)
			{
				wc.center = Vector3.zero;
				wc.radius = 0.45f;
			}

			Debug.Log($"[LightArmoredCarOriginFixer] {pair.colliderName} aligned to {localPos}");
		}
	}

	private static Transform FindDeep(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindDeep(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif
