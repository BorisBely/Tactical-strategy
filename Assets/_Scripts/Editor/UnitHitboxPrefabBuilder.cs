#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт хитбоксы частей тела и SelectionBounds на префабе юнита.
/// </summary>
public static class UnitHitboxPrefabBuilder
{
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const int c_UnitLayer = 7;

	[MenuItem("Polygone/Combat/Build Unit Body Hitboxes")]
	[Obsolete("Use UnitRagdollPrefabBuilder.Build instead.")]
	public static void BuildFromMenu()
	{
		Build();
	}

	[Obsolete("Use UnitRagdollPrefabBuilder.Build instead.")]
	public static void Build()
	{
		UnitRagdollPrefabBuilder.Build();
		return;
#pragma warning disable CS0162
		GameObject root = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		if (root == null)
		{
			Debug.LogError($"Missing prefab: {c_UnitPrefabPath}");
			return;
		}

		RemoveExistingHitboxRig(root);

		CreateCombatHitbox(root, "Head", "Hitbox_Head", BodyPartType.Head, HitboxShape.Capsule,
			new Vector3(0f, 0.05f, 0.02f), new Vector3(0.11f, 0.2f, 0.11f));
		CreateCombatHitbox(root, "Neck", "Hitbox_Neck", BodyPartType.Neck, HitboxShape.Capsule,
			Vector3.zero, new Vector3(0.065f, 0.11f, 0.065f));
		CreateCombatHitbox(root, "Spine_03", "Hitbox_Chest", BodyPartType.Chest, HitboxShape.Box,
			Vector3.zero, new Vector3(0.38f, 0.3f, 0.22f));
		CreateCombatHitbox(root, "Spine_01", "Hitbox_Abdomen", BodyPartType.Abdomen, HitboxShape.Box,
			new Vector3(0f, -0.02f, 0f), new Vector3(0.32f, 0.24f, 0.2f));
		CreateCombatHitbox(root, "Shoulder_L", "Hitbox_LeftArm", BodyPartType.LeftArm, HitboxShape.Capsule,
			new Vector3(0f, -0.22f, 0f), new Vector3(0.085f, 0.34f, 0.085f));
		CreateCombatHitbox(root, "Shoulder_R", "Hitbox_RightArm", BodyPartType.RightArm, HitboxShape.Capsule,
			new Vector3(0f, -0.22f, 0f), new Vector3(0.085f, 0.34f, 0.085f));
		CreateCombatHitbox(root, "UpperLeg_L", "Hitbox_LeftLeg", BodyPartType.LeftLeg, HitboxShape.Capsule,
			new Vector3(0f, -0.24f, 0f), new Vector3(0.105f, 0.44f, 0.105f));
		CreateCombatHitbox(root, "UpperLeg_R", "Hitbox_RightLeg", BodyPartType.RightLeg, HitboxShape.Capsule,
			new Vector3(0f, -0.24f, 0f), new Vector3(0.105f, 0.44f, 0.105f));

		Collider selectionBounds = CreateSelectionBounds(root);

		DisableLegacyColliders(root);
		WireComponentReferences(root, selectionBounds);
		EnsureInjuryResolver(root);

		PrefabUtility.SaveAsPrefabAsset(root, c_UnitPrefabPath);
		PrefabUtility.UnloadPrefabContents(root);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("Unit prefab updated with body part hitboxes and SelectionBounds.");
#pragma warning restore CS0162
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
				UnityEngine.Object.DestroyImmediate(existing.gameObject);
		}
	}

	private static void CreateCombatHitbox(
		GameObject _root,
		string _boneName,
		string _hitboxName,
		BodyPartType _bodyPart,
		HitboxShape _shape,
		Vector3 _localPosition,
		Vector3 _size)
	{
		Transform bone = FindDeepChild(_root.transform, _boneName);
		if (bone == null)
		{
			Debug.LogWarning($"Bone '{_boneName}' not found on Unit prefab. Skipping {_hitboxName}.");
			return;
		}

		var hitboxGo = new GameObject(_hitboxName);
		hitboxGo.layer = c_UnitLayer;
		Transform hitboxTransform = hitboxGo.transform;
		hitboxTransform.SetParent(bone, false);
		hitboxTransform.localPosition = _localPosition;
		hitboxTransform.localRotation = Quaternion.identity;
		hitboxTransform.localScale = Vector3.one;

		AddCollider(hitboxGo, _shape, _size, _isTrigger: false);

		var zone = hitboxGo.AddComponent<UnitBodyHitZone>();
		SerializedObject zoneSo = new SerializedObject(zone);
		zoneSo.FindProperty("m_BodyPart").enumValueIndex = (int)_bodyPart;
		zoneSo.ApplyModifiedPropertiesWithoutUndo();
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

	private static void EnsureInjuryResolver(GameObject _root)
	{
		if (!_root.TryGetComponent(out InjuryResolver _))
			_root.AddComponent<InjuryResolver>();
	}

	private static void AddCollider(GameObject _go, HitboxShape _shape, Vector3 _size, bool _isTrigger)
	{
		switch (_shape)
		{
			case HitboxShape.Box:
			{
				var box = _go.AddComponent<BoxCollider>();
				box.isTrigger = _isTrigger;
				box.center = Vector3.zero;
				box.size = _size;
				break;
			}
			case HitboxShape.Capsule:
			{
				var capsule = _go.AddComponent<CapsuleCollider>();
				capsule.isTrigger = _isTrigger;
				capsule.direction = 1;
				capsule.center = Vector3.zero;
				capsule.radius = _size.x;
				capsule.height = _size.y;
				break;
			}
		}
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

	private enum HitboxShape
	{
		Box,
		Capsule
	}
}
#endif
