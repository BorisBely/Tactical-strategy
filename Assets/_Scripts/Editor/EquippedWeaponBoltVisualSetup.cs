#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Проводка BoltCarrier / DustCover на Equipped-префабах по известным именам мешей и крайним Z.
/// </summary>
public static class EquippedWeaponBoltVisualSetup
{
	#region Nested Types
	private struct PlatformBoltConfig
	{
		public string SlideExactName;
		public string DustGuardExactName;
		public Vector3 RestLocalPosition;
		public float OpenLocalZ;
		public Vector3 BoltHandleOpenLocalEulerAngles;
		public float BoltHandleRotatePhaseNormalized;
		public float BoltActionCycleSeconds;
		public float DustCoverClosedDegrees;
		public bool HasDustCover;

		public Vector3 OpenLocalOffset =>
			new Vector3(0f, 0f, OpenLocalZ - RestLocalPosition.z);
	}
	#endregion

	#region Constants
	private static readonly PlatformBoltConfig c_Ak = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Mod_B_Body_Slide_01",
		RestLocalPosition = new Vector3(0.02077112f, 0.1182137f, 0.192758f),
		OpenLocalZ = 0.105f,
		HasDustCover = false,
	};

	private static readonly PlatformBoltConfig c_M4 = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Mod_Body_Slide_01",
		DustGuardExactName = "SM_Wep_Mod_Body_DustGuard_01",
		RestLocalPosition = new Vector3(0.006450404f, 0.1069666f, 0.1459795f),
		OpenLocalZ = 0.0669f,
		DustCoverClosedDegrees = -160f,
		HasDustCover = true,
	};

	private static readonly PlatformBoltConfig c_Benelli = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Shotgun_Slide_01",
		RestLocalPosition = new Vector3(0f, 0f, 0.1717751f),
		OpenLocalZ = 0.1011f,
		HasDustCover = false,
	};

	private static readonly PlatformBoltConfig c_Svd = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Preset_B_Sniper_01_Slide",
		RestLocalPosition = new Vector3(0f, 0f, 0.192758f),
		OpenLocalZ = 0.1056f,
		HasDustCover = false,
	};

	private static readonly PlatformBoltConfig c_Mosin = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Rifle_Bolt_01",
		RestLocalPosition = new Vector3(-0.0010720361f, 0.04559894f, 0.07529684f),
		OpenLocalZ = -0.0189f,
		BoltHandleOpenLocalEulerAngles = new Vector3(0f, 0f, 80f),
		BoltHandleRotatePhaseNormalized = 0.25f,
		BoltActionCycleSeconds = 0.55f,
		HasDustCover = false,
	};

	private static readonly PlatformBoltConfig c_Sniper762 = new PlatformBoltConfig
	{
		SlideExactName = "SM_Wep_Sniper_Bolt_01",
		RestLocalPosition = new Vector3(0f, 0.08647374f, 0.15418145f),
		OpenLocalZ = 0.0193f,
		BoltHandleOpenLocalEulerAngles = Vector3.zero,
		BoltHandleRotatePhaseNormalized = 0.25f,
		BoltActionCycleSeconds = 0.45f,
		HasDustCover = false,
	};
	#endregion

	#region Menu
	[MenuItem("Tools/Weapons/Wire Bolt + Dust Cover Visuals (Selected)", false, 50)]
	private static void WireSelected()
	{
		GameObject[] selected = Selection.gameObjects;
		if (selected == null || selected.Length == 0)
		{
			EditorUtility.DisplayDialog("Bolt visuals", "Выбери Equipped-префаб(ы) или объекты с EquippedWeapon.", "OK");
			return;
		}

		int wired = 0;
		for (int i = 0; i < selected.Length; i++)
		{
			if (TryWireRoot(selected[i]))
				wired++;
		}

		AssetDatabase.SaveAssets();
		Debug.Log($"Bolt/Dust visuals wired on {wired}/{selected.Length} selection(s).");
	}

	[MenuItem("Tools/Weapons/Wire Bolt + Dust Cover Visuals (All Equipped Prefabs)", false, 51)]
	private static void WireAllEquippedPrefabsMenu()
	{
		WireAllEquippedPrefabs();
	}

	/// <summary>Batch: -executeMethod EquippedWeaponBoltVisualSetup.WireAllEquippedPrefabs</summary>
	public static void WireAllEquippedPrefabs()
	{
		string[] guids = AssetDatabase.FindAssets("t:Prefab Equipped_", new[] { "Assets/Prefabs/Weapons" });
		int wired = 0;
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				if (TryWireRoot(root))
				{
					PrefabUtility.SaveAsPrefabAsset(root, path);
					wired++;
					Debug.Log($"Wired bolt visuals: {path}");
				}
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		AssetDatabase.SaveAssets();
		Debug.Log($"Bolt/Dust visuals wired on {wired}/{guids.Length} Equipped prefabs.");
	}
	#endregion

	#region Public Methods
	public static bool TryWireRoot(GameObject _root)
	{
		if (_root == null)
			return false;

		EquippedWeapon weapon = _root.GetComponent<EquippedWeapon>();
		if (weapon == null)
			weapon = _root.GetComponentInChildren<EquippedWeapon>(true);
		if (weapon == null)
			return false;

		if (!TryResolvePlatform(weapon.transform, out PlatformBoltConfig config, out Transform slide, out Transform dustGuard))
			return false;

		Undo.RegisterFullObjectHierarchyUndo(weapon.gameObject, "Wire Bolt Dust Visuals");

		Vector3 rest = slide.localPosition;
		// Сохраняем X/Y с префаба, Z rest берём из конфига платформы (если близко) или текущий.
		if (Mathf.Abs(rest.z - config.RestLocalPosition.z) < 0.05f)
			rest = new Vector3(rest.x, rest.y, config.RestLocalPosition.z);
		else if (config.RestLocalPosition.x != 0f || config.RestLocalPosition.y != 0f)
			rest = config.RestLocalPosition;

		slide.localPosition = rest;
		Vector3 openOffset = new Vector3(0f, 0f, config.OpenLocalZ - rest.z);

		SerializedObject so = new SerializedObject(weapon);
		so.FindProperty("m_BoltCarrier").objectReferenceValue = slide;
		so.FindProperty("m_BoltOpenLocalOffset").vector3Value = openOffset;
		so.FindProperty("m_BoltHandleOpenLocalEulerAngles").vector3Value = config.BoltHandleOpenLocalEulerAngles;
		so.FindProperty("m_BoltHandleRotatePhaseNormalized").floatValue =
			config.BoltHandleRotatePhaseNormalized > 0f ? config.BoltHandleRotatePhaseNormalized : 0.25f;
		so.FindProperty("m_BoltCycleSeconds").floatValue = 0.085f;
		so.FindProperty("m_BoltCycleSecondsSingleShot").floatValue = 0.16f;
		so.FindProperty("m_BoltActionCycleSeconds").floatValue = config.BoltActionCycleSeconds;

		if (config.HasDustCover && dustGuard != null)
		{
			// Pivot меша уже верный — крутим сам DustGuard, без лишней пустышки.
			so.FindProperty("m_DustCoverHinge").objectReferenceValue = dustGuard;
			so.FindProperty("m_DustCoverClosedDegrees").floatValue = config.DustCoverClosedDegrees;
			so.FindProperty("m_DustCoverHingeAxis").vector3Value = Vector3.forward;
			dustGuard.localRotation = Quaternion.identity;
		}
		else
		{
			so.FindProperty("m_DustCoverHinge").objectReferenceValue = null;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
		EditorUtility.SetDirty(slide.gameObject);
		return true;
	}
	#endregion

	#region Private Methods
	private static bool TryResolvePlatform(
		Transform _root,
		out PlatformBoltConfig _config,
		out Transform _slide,
		out Transform _dustGuard)
	{
		_config = default;
		_slide = null;
		_dustGuard = null;

		if (TryFindExact(_root, c_M4.SlideExactName, out _slide))
		{
			_config = c_M4;
			TryFindExact(_root, c_M4.DustGuardExactName, out _dustGuard);
			return true;
		}

		if (TryFindExact(_root, c_Ak.SlideExactName, out _slide))
		{
			_config = c_Ak;
			return true;
		}

		if (TryFindExact(_root, c_Benelli.SlideExactName, out _slide))
		{
			_config = c_Benelli;
			// Benelli: X/Y rest берём с префаба, Z из конфига.
			return true;
		}

		if (TryFindExact(_root, c_Svd.SlideExactName, out _slide))
		{
			_config = c_Svd;
			return true;
		}

		if (TryFindExact(_root, c_Mosin.SlideExactName, out _slide))
		{
			_config = c_Mosin;
			return true;
		}

		if (TryFindExact(_root, c_Sniper762.SlideExactName, out _slide))
		{
			_config = c_Sniper762;
			return true;
		}

		return false;
	}

	private static bool TryFindExact(Transform _root, string _exactName, out Transform _found)
	{
		_found = null;
		if (_root == null || string.IsNullOrEmpty(_exactName))
			return false;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _exactName)
			{
				_found = all[i];
				return true;
			}
		}

		return false;
	}
	#endregion
}
#endif
