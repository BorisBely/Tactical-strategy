#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт боевые ragdoll-коллайдеры на костях юнита с прежними логическими зонами тела.
/// </summary>
public static class UnitRagdollPrefabBuilder
{
	private enum JointProfile
	{
		Hips,
		Spine,
		Neck,
		Head,
		Shoulder,
		Elbow,
		Hand,
		HipLeg,
		Knee
	}

	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_RagdollPhysicsMaterialPath = "Assets/Settings/UnitRagdoll.physicsMaterial";
	private const int c_UnitLayer = 7;

	private static PhysicsMaterial s_CachedRagdollPhysicsMaterial;

	[MenuItem("Polygone/Combat/Build Unit Ragdoll Hitboxes")]
	public static void BuildFromMenu()
	{
		Build();
	}

	public static void Build()
	{
		s_CachedRagdollPhysicsMaterial = null;
		GameObject root = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		if (root == null)
		{
			Debug.LogError($"Missing prefab: {c_UnitPrefabPath}");
			return;
		}

		RemoveExistingHitboxRig(root);
		RemoveExistingRagdollZones(root);

		Rigidbody hips = CreateRagdollZone(root, "Hips", BodyPartType.Abdomen, JointProfile.Hips, 8f, 0.14f, 0.22f, new Vector3(0f, 0.02f, 0f), null, true);
		Rigidbody spine01 = CreateRagdollZone(root, "Spine_01", BodyPartType.Abdomen, JointProfile.Spine, 6f, 0.14f, 0.22f, Vector3.zero, hips, true);
		Rigidbody spine02 = CreateRagdollZone(root, "Spine_02", BodyPartType.Chest, JointProfile.Spine, 5f, 0.17f, 0.24f, Vector3.zero, spine01, true);
		Rigidbody spine03 = CreateRagdollZone(root, "Spine_03", BodyPartType.Chest, JointProfile.Spine, 5f, 0.18f, 0.27f, Vector3.zero, spine02, true);
		Rigidbody neck = CreateRagdollZone(root, "Neck", BodyPartType.Neck, JointProfile.Neck, 1.2f, 0.055f, 0.12f, Vector3.zero, spine03, true);
		CreateRagdollZone(root, "Head", BodyPartType.Head, JointProfile.Head, 3f, 0.11f, 0.22f, new Vector3(0f, 0f, 0.02f), neck, true);

		Rigidbody shoulderL = CreateRagdollZone(root, "Shoulder_L", BodyPartType.LeftArm, JointProfile.Shoulder, 2f, 0.07f, 0.28f, Vector3.zero, spine03, true);
		Rigidbody elbowL = CreateRagdollZone(root, "Elbow_L", BodyPartType.LeftArm, JointProfile.Elbow, 1.5f, 0.06f, 0.26f, Vector3.zero, shoulderL, true);
		CreateRagdollZone(root, "Hand_L", BodyPartType.LeftArm, JointProfile.Hand, 0.7f, 0.045f, 0.13f, Vector3.zero, elbowL, false);

		Rigidbody shoulderR = CreateRagdollZone(root, "Shoulder_R", BodyPartType.RightArm, JointProfile.Shoulder, 2f, 0.07f, 0.28f, Vector3.zero, spine03, true);
		Rigidbody elbowR = CreateRagdollZone(root, "Elbow_R", BodyPartType.RightArm, JointProfile.Elbow, 1.5f, 0.06f, 0.26f, Vector3.zero, shoulderR, true);
		CreateRagdollZone(root, "Hand_R", BodyPartType.RightArm, JointProfile.Hand, 0.7f, 0.045f, 0.13f, Vector3.zero, elbowR, false);

		Rigidbody upperLegL = CreateRagdollZone(root, "UpperLeg_L", BodyPartType.LeftLeg, JointProfile.HipLeg, 4f, 0.09f, 0.42f, Vector3.zero, hips, true);
		CreateRagdollZone(root, "LowerLeg_L", BodyPartType.LeftLeg, JointProfile.Knee, 3f, 0.08f, 0.42f, Vector3.zero, upperLegL, true);
		Rigidbody upperLegR = CreateRagdollZone(root, "UpperLeg_R", BodyPartType.RightLeg, JointProfile.HipLeg, 4f, 0.09f, 0.42f, Vector3.zero, hips, true);
		CreateRagdollZone(root, "LowerLeg_R", BodyPartType.RightLeg, JointProfile.Knee, 3f, 0.08f, 0.42f, Vector3.zero, upperLegR, true);

		Collider selectionBounds = CreateSelectionBounds(root);
		DisableLegacyColliders(root);
		WireComponentReferences(root, selectionBounds);
		EnsureCoreComponents(root);
		ConfigureRagdollController(root);

		PrefabUtility.SaveAsPrefabAsset(root, c_UnitPrefabPath);
		PrefabUtility.UnloadPrefabContents(root);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("Unit prefab updated with ragdoll body hitboxes and SelectionBounds.");
	}

	private static void RemoveExistingHitboxRig(GameObject _root)
	{
		string[] names =
		{
			"Hitbox_Head", "Hitbox_Neck", "Hitbox_Chest", "Hitbox_Abdomen",
			"Hitbox_LeftArm", "Hitbox_RightArm", "Hitbox_LeftLeg", "Hitbox_RightLeg",
			"AimProxy", "SelectionBounds"
		};

		for (int i = 0; i < names.Length; i++)
		{
			Transform existing = FindDeepChild(_root.transform, names[i]);
			if (existing != null)
				Object.DestroyImmediate(existing.gameObject);
		}
	}

	private static void RemoveExistingRagdollZones(GameObject _root)
	{
		UnitBodyHitZone[] zones = _root.GetComponentsInChildren<UnitBodyHitZone>(true);
		for (int i = 0; i < zones.Length; i++)
		{
			UnitBodyHitZone zone = zones[i];
			if (zone == null)
				continue;

			GameObject go = zone.gameObject;
			Object.DestroyImmediate(zone);

			CharacterJoint joint = go.GetComponent<CharacterJoint>();
			if (joint != null)
				Object.DestroyImmediate(joint);

			Rigidbody body = go.GetComponent<Rigidbody>();
			if (body != null)
				Object.DestroyImmediate(body);

			Collider col = go.GetComponent<Collider>();
			if (col != null && go.name != "SelectionBounds")
				Object.DestroyImmediate(col);
		}
	}

	private static Rigidbody CreateRagdollZone(
		GameObject _root,
		string _boneName,
		BodyPartType _bodyPart,
		JointProfile _jointProfile,
		float _mass,
		float _radius,
		float _height,
		Vector3 _extraCenter,
		Rigidbody _connectedBody,
		bool _includeInVision)
	{
		Transform bone = FindDeepChild(_root.transform, _boneName);
		if (bone == null)
		{
			Debug.LogWarning($"Bone '{_boneName}' not found on Unit prefab. Skipping ragdoll zone {_bodyPart}.");
			return null;
		}

		bone.gameObject.layer = c_UnitLayer;

		CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
		capsule.isTrigger = false;
		capsule.material = GetRagdollPhysicsMaterial();
		ConfigureCapsuleForBone(bone, capsule, _jointProfile, _radius, _height, _extraCenter);

		Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
		body.mass = Mathf.Max(0.01f, _mass);
		body.isKinematic = true;
		body.useGravity = false;
		body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		body.linearDamping = 0.35f;
		body.angularDamping = 4.5f;
		body.maxAngularVelocity = 2.5f;
		body.maxDepenetrationVelocity = 0.8f;
		body.sleepThreshold = 0.08f;
		body.interpolation = RigidbodyInterpolation.Interpolate;

		if (_connectedBody != null)
			ConfigureJoint(bone.gameObject.AddComponent<CharacterJoint>(), _connectedBody, _jointProfile);

		UnitBodyHitZone zone = bone.gameObject.AddComponent<UnitBodyHitZone>();
		SerializedObject zoneSo = new SerializedObject(zone);
		zoneSo.FindProperty("m_BodyPart").enumValueIndex = (int)_bodyPart;
		zoneSo.FindProperty("m_IncludeInVision").boolValue = _includeInVision;
		zoneSo.ApplyModifiedPropertiesWithoutUndo();

		return body;
	}

	private static void ConfigureCapsuleForBone(
		Transform _bone,
		CapsuleCollider _capsule,
		JointProfile _profile,
		float _radius,
		float _height,
		Vector3 _extraCenter)
	{
		Vector3 boneVector = ResolveBoneAxis(_bone, _profile, _height, _radius);
		float boneLength = boneVector.magnitude;
		int direction = ResolveCapsuleDirection(boneVector);

		_capsule.direction = direction;
		_capsule.radius = _radius;
		_capsule.height = Mathf.Max(_height, boneLength + _radius * 2f);
		_capsule.center = boneVector * 0.5f + _extraCenter;
	}

	private static Vector3 ResolveBoneAxis(Transform _bone, JointProfile _profile, float _height, float _radius)
	{
		Vector3 axis = Vector3.zero;
		Transform child = FindLengthReferenceChild(_bone, _profile);
		if (child != null)
		{
			Vector3 childLocal = child.localPosition;
			if (childLocal.sqrMagnitude > 0.0001f)
				axis = childLocal;
		}

		if (axis.sqrMagnitude < 0.0001f && _bone.parent != null)
		{
			Vector3 incoming = _bone.localPosition;
			if (incoming.sqrMagnitude > 0.0001f)
				axis = incoming.normalized * Mathf.Max(0.01f, _height - _radius * 2f);
		}

		if (axis.sqrMagnitude < 0.0001f)
			axis = Vector3.up * Mathf.Max(0.01f, _height - _radius * 2f);

		return axis;
	}

	private static int ResolveCapsuleDirection(Vector3 _localAxis)
	{
		Vector3 abs = new Vector3(Mathf.Abs(_localAxis.x), Mathf.Abs(_localAxis.y), Mathf.Abs(_localAxis.z));
		if (abs.x >= abs.y && abs.x >= abs.z)
			return 0;

		if (abs.y >= abs.x && abs.y >= abs.z)
			return 1;

		return 2;
	}

	private static Transform FindLengthReferenceChild(Transform _bone, JointProfile _profile)
	{
		if (_profile == JointProfile.Hips)
		{
			Transform spine = FindChildByNameContains(_bone, "Spine");
			if (spine != null)
				return spine;
		}

		if (_profile == JointProfile.Spine)
		{
			Transform neck = FindChildByNameContains(_bone, "Neck");
			if (neck != null)
				return neck;
		}

		Transform best = null;
		float bestDist = 0f;
		for (int i = 0; i < _bone.childCount; i++)
		{
			Transform child = _bone.GetChild(i);
			if (!IsBoneLikeChild(child))
				continue;

			float dist = child.localPosition.sqrMagnitude;
			if (dist > bestDist)
			{
				bestDist = dist;
				best = child;
			}
		}

		return best;
	}

	private static Transform FindChildByNameContains(Transform _bone, string _namePart)
	{
		for (int i = 0; i < _bone.childCount; i++)
		{
			Transform child = _bone.GetChild(i);
			if (child.name.Contains(_namePart))
				return child;
		}

		return null;
	}

	private static bool IsBoneLikeChild(Transform _child)
	{
		string name = _child.name;
		if (name.Contains("Hitbox") || name.Contains("SelectionBounds") || name == "Sphere")
			return false;

		if (name is "Eyebrows" or "Eyes" or "Jaw" or "Hair" or "Teeth")
			return false;

		if (_child.localPosition.sqrMagnitude < 0.0001f &&
		    (_child.GetComponent<SkinnedMeshRenderer>() != null || _child.GetComponent<MeshRenderer>() != null))
			return false;

		return true;
	}

	private static PhysicsMaterial GetRagdollPhysicsMaterial()
	{
		if (s_CachedRagdollPhysicsMaterial != null)
			return s_CachedRagdollPhysicsMaterial;

		PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(c_RagdollPhysicsMaterialPath);
		if (material == null)
		{
			Debug.LogWarning(
				$"Ragdoll physics material not found at '{c_RagdollPhysicsMaterialPath}'. " +
				"Using in-memory fallback for this build.");
			material = new PhysicsMaterial("UnitRagdoll_Runtime")
			{
				dynamicFriction = 0.7f,
				staticFriction = 0.7f,
				bounciness = 0f,
				frictionCombine = PhysicsMaterialCombine.Average,
				bounceCombine = PhysicsMaterialCombine.Minimum
			};
		}

		s_CachedRagdollPhysicsMaterial = material;
		return material;
	}

	private static void ConfigureJoint(CharacterJoint _joint, Rigidbody _connectedBody, JointProfile _jointProfile)
	{
		_joint.connectedBody = _connectedBody;
		_joint.enablePreprocessing = true;
		_joint.enableProjection = true;
		_joint.projectionDistance = 0.04f;
		_joint.projectionAngle = 8f;
		_joint.massScale = 1f;
		Rigidbody ownBody = _joint.GetComponent<Rigidbody>();
		float ownMass = ownBody != null ? ownBody.mass : 1f;
		_joint.connectedMassScale = Mathf.Max(0.5f, ownMass / Mathf.Max(0.01f, _connectedBody.mass));
		_joint.swingAxis = ResolveSwingAxis(_jointProfile);

		ResolveJointLimits(_jointProfile, out float lowTwist, out float highTwist, out float swing1, out float swing2);
		_joint.lowTwistLimit = CreateLimit(lowTwist);
		_joint.highTwistLimit = CreateLimit(highTwist);
		_joint.swing1Limit = CreateLimit(swing1);
		_joint.swing2Limit = CreateLimit(swing2);
	}

	private static SoftJointLimit CreateLimit(float _limit)
	{
		return new SoftJointLimit { limit = _limit };
	}

	private static Vector3 ResolveSwingAxis(JointProfile _jointProfile)
	{
		switch (_jointProfile)
		{
			case JointProfile.Elbow:
			case JointProfile.Knee:
				return Vector3.right;
			default:
				return Vector3.forward;
		}
	}

	private static void ResolveJointLimits(
		JointProfile _jointProfile,
		out float _lowTwist,
		out float _highTwist,
		out float _swing1,
		out float _swing2)
	{
		switch (_jointProfile)
		{
			case JointProfile.Spine:
				_lowTwist = -12f;
				_highTwist = 12f;
				_swing1 = 16f;
				_swing2 = 10f;
				return;

			case JointProfile.Neck:
				_lowTwist = -8f;
				_highTwist = 8f;
				_swing1 = 12f;
				_swing2 = 8f;
				return;

			case JointProfile.Head:
				_lowTwist = -12f;
				_highTwist = 12f;
				_swing1 = 16f;
				_swing2 = 10f;
				return;

			case JointProfile.Shoulder:
				_lowTwist = -45f;
				_highTwist = 45f;
				_swing1 = 70f;
				_swing2 = 45f;
				return;

			case JointProfile.Elbow:
				_lowTwist = -8f;
				_highTwist = 8f;
				_swing1 = 55f;
				_swing2 = 8f;
				return;

			case JointProfile.Hand:
				_lowTwist = -20f;
				_highTwist = 20f;
				_swing1 = 35f;
				_swing2 = 20f;
				return;

			case JointProfile.HipLeg:
				_lowTwist = -25f;
				_highTwist = 25f;
				_swing1 = 45f;
				_swing2 = 30f;
				return;

			case JointProfile.Knee:
				_lowTwist = -6f;
				_highTwist = 6f;
				_swing1 = 55f;
				_swing2 = 6f;
				return;

			default:
				_lowTwist = -25f;
				_highTwist = 25f;
				_swing1 = 35f;
				_swing2 = 20f;
				return;
		}
	}

	private static Collider CreateSelectionBounds(GameObject _root)
	{
		var boundsGo = new GameObject("SelectionBounds");
		boundsGo.layer = c_UnitLayer;
		Transform boundsTransform = boundsGo.transform;
		boundsTransform.SetParent(_root.transform, false);
		boundsTransform.localPosition = Vector3.zero;
		boundsTransform.localRotation = Quaternion.identity;
		boundsTransform.localScale = Vector3.one;

		var capsule = boundsGo.AddComponent<CapsuleCollider>();
		capsule.isTrigger = true;
		capsule.direction = 1;
		capsule.center = new Vector3(0f, 0.91f, 0f);
		capsule.radius = 0.22f;
		capsule.height = 1.82f;
		return capsule;
	}

	private static void DisableLegacyColliders(GameObject _root)
	{
		Collider rootCapsule = _root.GetComponent<CapsuleCollider>();
		if (rootCapsule != null)
			rootCapsule.enabled = false;

		Transform sphere = FindDeepChild(_root.transform, "Sphere");
		if (sphere != null)
		{
			Collider sphereCollider = sphere.GetComponent<Collider>();
			if (sphereCollider != null)
				sphereCollider.enabled = false;
		}
	}

	private static void WireComponentReferences(GameObject _root, Collider _selectionBounds)
	{
		if (_root.TryGetComponent(out UnitVision vision))
		{
			SerializedObject so = new SerializedObject(vision);
			so.FindProperty("m_BodyCollider").objectReferenceValue = null;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		if (_root.TryGetComponent(out RtsUnitMember member) && _selectionBounds != null)
		{
			SerializedObject so = new SerializedObject(member);
			so.FindProperty("m_SelectionCollider").objectReferenceValue = _selectionBounds;
			so.ApplyModifiedPropertiesWithoutUndo();
		}
	}

	private static void EnsureCoreComponents(GameObject _root)
	{
		EnsureComponent<InjuryResolver>(_root);
		EnsureComponent<UnitRagdollController>(_root);
		EnsureComponent<UnitConsciousness>(_root);
		EnsureComponent<UnitConsciousnessRules>(_root);
	}

	private static void ConfigureRagdollController(GameObject _root)
	{
		if (!_root.TryGetComponent(out UnitRagdollController controller))
			return;

		SerializedObject so = new SerializedObject(controller);
		so.FindProperty("m_DefaultImpulse").floatValue = 1.6f;
		so.FindProperty("m_DefaultUpImpulse").floatValue = 0f;
		so.FindProperty("m_HitBoneImpulseMultiplier").floatValue = 0.8f;
		so.FindProperty("m_RootFollowThroughMultiplier").floatValue = 0.25f;
		so.FindProperty("m_RandomSideImpulse").floatValue = 0.15f;
		so.FindProperty("m_IgnoreSelfCollision").boolValue = true;
		so.FindProperty("m_RagdollLinearDamping").floatValue = 0.35f;
		so.FindProperty("m_RagdollAngularDamping").floatValue = 4.5f;
		so.FindProperty("m_MaxRagdollAngularSpeed").floatValue = 2.5f;
		so.FindProperty("m_AngularDecayPerSecond").floatValue = 10f;
		so.FindProperty("m_SettleDelay").floatValue = 0.7f;
		so.FindProperty("m_SettleRequiredSeconds").floatValue = 0.35f;
		so.FindProperty("m_SleepLinearSpeed").floatValue = 0.12f;
		so.FindProperty("m_SleepAngularSpeed").floatValue = 0.25f;
		so.FindProperty("m_MakeKinematicWhenSettled").boolValue = true;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	private static T EnsureComponent<T>(GameObject _root) where T : Component
	{
		if (_root.TryGetComponent(out T component))
			return component;

		return _root.AddComponent<T>();
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindDeepChild(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif
