#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Setup/tune grenade detonation VFX by grenade meaning:
/// frag = HE debris/fire, flash = bright short pulse, smoke = dense lingering cloud.
/// </summary>
[InitializeOnLoad]
internal static class GrenadeExplosionVfxBootstrap
{
	#region Constants
	private const string c_MarkerPath = "Assets/.grenade_explosion_vfx_setup_done";
	private const string c_RetuneMarkerPath = "Assets/.grenade_explosion_vfx_retune_v4";
	private const string c_ThrowDataPath = "Assets/GameData/Combat/GrenadeThrowData.asset";
	private const string c_Explosion01Path = "Assets/Prefabs/FX/Grenades/FX_Grenade_Explosion_01.prefab";
	private const string c_Explosion02Path = "Assets/Prefabs/FX/Grenades/FX_Grenade_Explosion_02.prefab";
	private const string c_SmokePath = "Assets/Prefabs/FX/Grenades/FX_Grenade_Smoke_Burst_01.prefab";
	private const string c_SoftMaterialPath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Soft.mat";
	private const string c_DarkMaterialPath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Dark.mat";
	#endregion

	#region Bootstrap
	static GrenadeExplosionVfxBootstrap()
	{
		if (!File.Exists(c_MarkerPath) || !File.Exists(c_RetuneMarkerPath))
			EditorApplication.delayCall += RunSetup;
	}

	[MenuItem("Polygone/Combat/Retune Smoke Grenade VFX")]
	public static void RetuneSmokeOnly()
	{
		GrenadeSmokeVfxComparisonBootstrap.RunFullSetup();
	}

	[MenuItem("Polygone/Combat/Setup Grenade Explosion VFX")]
	public static void RunSetup()
	{
		TuneFragExplosionPrefab();
		TuneFlashExplosionPrefab();
		RebuildSmokeCloudPrefab();
		WireThrowData();
		WireFragItemVfxDifferences();

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		File.WriteAllText(c_MarkerPath, "done");
		File.WriteAllText(c_RetuneMarkerPath, "v4");
		Debug.Log("[GrenadeExplosionVfxBootstrap] Grenade VFX tuned by type.");
	}
	#endregion

	#region Prefab Tuning
	private static void TuneFragExplosionPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_Explosion01Path);
		try
		{
			root.name = "FX_Grenade_Explosion_01";
			root.transform.localScale = Vector3.one;

			SetChildActive(root.transform, "FX_Grenade_Explosive_Light_01", true);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Debris_01", true);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Fire_01", true);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Ember_01", true);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Smoke_01", true);

			Transform lightTf = FindChildRecursive(root.transform, "FX_Grenade_Explosive_Light_01");
			if (lightTf != null)
			{
				if (lightTf.TryGetComponent(out Light pointLight))
				{
					pointLight.type = LightType.Point;
					pointLight.color = new Color(1f, 0.82f, 0.55f, 1f);
					pointLight.range = 22f;
					pointLight.intensity = 0f;
					pointLight.enabled = false;
				}

				TimedPointLightPulse pulse = lightTf.GetComponent<TimedPointLightPulse>();
				if (pulse == null)
					pulse = lightTf.gameObject.AddComponent<TimedPointLightPulse>();

				SerializedObject pulseSo = new SerializedObject(pulse);
				pulseSo.FindProperty("m_Light").objectReferenceValue = lightTf.GetComponent<Light>();
				pulseSo.FindProperty("m_PeakIntensity").floatValue = 5.5f;
				pulseSo.FindProperty("m_DurationSeconds").floatValue = 0.14f;
				pulseSo.ApplyModifiedPropertiesWithoutUndo();
			}

			PrefabUtility.SaveAsPrefabAsset(root, c_Explosion01Path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void TuneFlashExplosionPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_Explosion02Path);
		try
		{
			root.name = "FX_Grenade_Explosion_02";
			root.transform.localScale = Vector3.one;

			// Flashbang: bright pulse, almost no HE debris/fire.
			SetChildActive(root.transform, "FX_Grenade_Explosive_Debris_01", false);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Fire_01", false);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Ember_01", true);
			SetChildActive(root.transform, "FX_Grenade_Explosive_Smoke_01", true);

			Transform lightTf = FindChildRecursive(root.transform, "FX_Grenade_Explosive_Light_01");
			if (lightTf != null)
			{
				lightTf.gameObject.SetActive(true);
				if (lightTf.TryGetComponent(out Light pointLight))
				{
					pointLight.type = LightType.Point;
					pointLight.color = new Color(1f, 0.97f, 0.88f, 1f);
					pointLight.range = 18f;
					pointLight.intensity = 0f;
					pointLight.enabled = false;
				}

				TimedPointLightPulse pulse = lightTf.GetComponent<TimedPointLightPulse>();
				if (pulse == null)
					pulse = lightTf.gameObject.AddComponent<TimedPointLightPulse>();

				SerializedObject pulseSo = new SerializedObject(pulse);
				pulseSo.FindProperty("m_Light").objectReferenceValue = lightTf.GetComponent<Light>();
				pulseSo.FindProperty("m_PeakIntensity").floatValue = 10f;
				pulseSo.FindProperty("m_DurationSeconds").floatValue = 0.16f;
				pulseSo.ApplyModifiedPropertiesWithoutUndo();
			}

			TintChildParticles(root.transform, "FX_Grenade_Explosive_Ember_01", new Color(1f, 0.98f, 0.85f, 1f));
			TintChildParticles(root.transform, "FX_Grenade_Explosion_02", new Color(1f, 0.96f, 0.8f, 1f));
			TintChildParticles(root.transform, "FX_Grenade_Explosive_Smoke_01", new Color(0.85f, 0.85f, 0.82f, 0.45f));
			SetChildParticleLifetime(root.transform, "FX_Grenade_Explosive_Ember_01", 0.08f, 0.18f);
			SetChildParticleLifetime(root.transform, "FX_Grenade_Explosive_Smoke_01", 0.35f, 0.8f);

			PrefabUtility.SaveAsPrefabAsset(root, c_Explosion02Path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void RebuildSmokeCloudPrefab()
	{
		Material softMat = AssetDatabase.LoadAssetAtPath<Material>(c_SoftMaterialPath);
		Material darkMat = AssetDatabase.LoadAssetAtPath<Material>(c_DarkMaterialPath);
		GameObject root = new GameObject("FX_Grenade_Smoke_Burst_01");
		try
		{
			// Dense base cloud — long emission like a real smoke grenade.
			ParticleSystem basePs = root.AddComponent<ParticleSystem>();
			ConfigureSmokeLayer(
				basePs,
				root.GetComponent<ParticleSystemRenderer>(),
				softMat != null ? softMat : darkMat,
				durationSeconds: 24f,
				startLifetimeMin: 14f,
				startLifetimeMax: 20f,
				startSizeMin: 1.6f,
				startSizeMax: 3f,
				startSpeedMin: 0.35f,
				startSpeedMax: 1.1f,
				rateOverTime: 18f,
				maxParticles: 280,
				shapeRadius: 1.45f,
				startAlpha: 0.82f,
				holdOpaqueUntil: 0.72f);

			GameObject denseGo = new GameObject("FX_Grenade_Smoke_Dense_01");
			denseGo.transform.SetParent(root.transform, false);
			ParticleSystem densePs = denseGo.AddComponent<ParticleSystem>();
			ConfigureSmokeLayer(
				densePs,
				denseGo.GetComponent<ParticleSystemRenderer>(),
				darkMat != null ? darkMat : softMat,
				durationSeconds: 20f,
				startLifetimeMin: 12f,
				startLifetimeMax: 18f,
				startSizeMin: 2f,
				startSizeMax: 3.8f,
				startSpeedMin: 0.2f,
				startSpeedMax: 0.75f,
				rateOverTime: 12f,
				maxParticles: 160,
				shapeRadius: 1.2f,
				startAlpha: 0.88f,
				holdOpaqueUntil: 0.78f);

			PrefabUtility.SaveAsPrefabAsset(root, c_SmokePath);
		}
		finally
		{
			Object.DestroyImmediate(root);
		}
	}

	private static void ConfigureSmokeLayer(
		ParticleSystem _ps,
		ParticleSystemRenderer _renderer,
		Material _material,
		float durationSeconds,
		float startLifetimeMin,
		float startLifetimeMax,
		float startSizeMin,
		float startSizeMax,
		float startSpeedMin,
		float startSpeedMax,
		float rateOverTime,
		int maxParticles,
		float shapeRadius,
		float startAlpha,
		float holdOpaqueUntil)
	{
		ParticleSystem.MainModule main = _ps.main;
		main.duration = durationSeconds;
		main.loop = false;
		main.prewarm = false;
		main.playOnAwake = true;
		main.startDelay = 0f;
		main.startLifetime = new ParticleSystem.MinMaxCurve(startLifetimeMin, startLifetimeMax);
		main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);
		main.startSize = new ParticleSystem.MinMaxCurve(startSizeMin, startSizeMax);
		main.startColor = new Color(0.82f, 0.82f, 0.8f, startAlpha);
		main.gravityModifier = -0.008f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.maxParticles = maxParticles;
		main.scalingMode = ParticleSystemScalingMode.Hierarchy;

		ParticleSystem.EmissionModule emission = _ps.emission;
		emission.rateOverTime = rateOverTime;
		emission.SetBursts(new[]
		{
			new ParticleSystem.Burst(0f, 24, 36),
			new ParticleSystem.Burst(0.5f, 12, 18),
			new ParticleSystem.Burst(1.4f, 8, 12)
		});

		ParticleSystem.ShapeModule shape = _ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Circle;
		shape.arc = 360f;
		shape.radius = shapeRadius;
		shape.radiusThickness = 1f;
		shape.rotation = new Vector3(-90f, 0f, 0f);
		shape.randomDirectionAmount = 0.45f;
		shape.sphericalDirectionAmount = 0.12f;

		ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _ps.colorOverLifetime;
		colorOverLifetime.enabled = true;
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey(new Color(0.88f, 0.88f, 0.86f), 0f),
				new GradientColorKey(new Color(0.72f, 0.72f, 0.7f), 0.55f),
				new GradientColorKey(new Color(0.55f, 0.55f, 0.53f), 1f)
			},
			new[]
			{
				new GradientAlphaKey(0f, 0f),
				new GradientAlphaKey(startAlpha, 0.05f),
				new GradientAlphaKey(startAlpha * 0.95f, holdOpaqueUntil),
				new GradientAlphaKey(startAlpha * 0.45f, 0.9f),
				new GradientAlphaKey(0f, 1f)
			});
		colorOverLifetime.color = gradient;

		ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _ps.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
			1f,
			new AnimationCurve(
				new Keyframe(0f, 0.4f),
				new Keyframe(0.25f, 1f),
				new Keyframe(1f, 1.35f)));

		ParticleSystem.VelocityOverLifetimeModule velocity = _ps.velocityOverLifetime;
		velocity.enabled = true;
		velocity.space = ParticleSystemSimulationSpace.World;
		velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
		velocity.x = new ParticleSystem.MinMaxCurve(-0.75f, 0.75f);
		velocity.z = new ParticleSystem.MinMaxCurve(-0.75f, 0.75f);

		ParticleSystem.NoiseModule noise = _ps.noise;
		noise.enabled = true;
		noise.strength = 0.35f;
		noise.frequency = 0.14f;
		noise.scrollSpeed = 0.1f;
		noise.damping = true;

		ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = _ps.limitVelocityOverLifetime;
		limitVelocity.enabled = true;
		limitVelocity.limit = 1.4f;
		limitVelocity.dampen = 0.35f;

		_renderer.renderMode = ParticleSystemRenderMode.Billboard;
		_renderer.sharedMaterial = _material;
		_renderer.maxParticleSize = 4.5f;
		_renderer.minParticleSize = 0.05f;
		_renderer.sortingFudge = 12f;
		_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		_renderer.receiveShadows = false;
	}

	private static void WireSmokeThrowData()
	{
		GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
		if (data == null)
			return;

		SerializedObject so = new SerializedObject(data);
		SetFloat(so, "m_SmokeScale", 1.75f);
		SetFloat(so, "m_SmokeLifetimeSeconds", 40f);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);
	}

	private static void WireThrowData()
	{
		GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
		if (data == null)
		{
			Debug.LogError($"[GrenadeExplosionVfxBootstrap] Missing throw data at {c_ThrowDataPath}");
			return;
		}

		GameObject explosion01 = AssetDatabase.LoadAssetAtPath<GameObject>(c_Explosion01Path);
		GameObject explosion02 = AssetDatabase.LoadAssetAtPath<GameObject>(c_Explosion02Path);
		GameObject smoke = AssetDatabase.LoadAssetAtPath<GameObject>(c_SmokePath);

		SerializedObject so = new SerializedObject(data);
		SetObject(so, "m_FragExplosionPrefab", explosion01);
		SetObject(so, "m_FlashExplosionPrefab", explosion02);
		SetObject(so, "m_SmokePrefab", smoke);
		SetFloat(so, "m_ExplosionFuseSeconds", 3.5f);
		SetFloat(so, "m_ExplosionMaxDistanceMeters", 200f);
		SetFloat(so, "m_ExplosionAudioMaxDistance", 220f);
		SetFloat(so, "m_FragExplosionScale", 1.25f);
		SetFloat(so, "m_FragExplosionLifetimeSeconds", 5f);
		SetFloat(so, "m_FlashExplosionScale", 0.9f);
		SetFloat(so, "m_FlashExplosionLifetimeSeconds", 2.2f);
		SetFloat(so, "m_SmokeScale", 1.75f);
		SetFloat(so, "m_SmokeLifetimeSeconds", 40f);

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);
	}

	private static void WireFragItemVfxDifferences()
	{
		GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
		if (data == null)
			return;

		SerializedObject so = new SerializedObject(data);
		SerializedProperty mappings = so.FindProperty("m_ItemMappings");
		if (mappings == null)
			return;

		for (int i = 0; i < mappings.arraySize; i++)
		{
			SerializedProperty mapping = mappings.GetArrayElementAtIndex(i);
			ItemDefinition item = mapping.FindPropertyRelative("Item").objectReferenceValue as ItemDefinition;
			if (item == null || item.GrenadeType != GrenadeType.Fragmentation)
				continue;

			string key = item.LocalizationKey != null ? item.LocalizationKey.ToLowerInvariant() : item.name.ToLowerInvariant();
			if (key.Contains("f1"))
			{
				// F-1: heavier defensive blast, more residual plume.
				mapping.FindPropertyRelative("ExplosionVfxScaleMultiplier").floatValue = 1.18f;
				mapping.FindPropertyRelative("ExplosionVfxLifetimeMultiplier").floatValue = 1.15f;
				mapping.FindPropertyRelative("ExplosionVfxYawOffsetDegrees").floatValue = 12f;
			}
			else if (key.Contains("rgd"))
			{
				// RGD-5: tighter/sharper offensive blast.
				mapping.FindPropertyRelative("ExplosionVfxScaleMultiplier").floatValue = 0.9f;
				mapping.FindPropertyRelative("ExplosionVfxLifetimeMultiplier").floatValue = 0.88f;
				mapping.FindPropertyRelative("ExplosionVfxYawOffsetDegrees").floatValue = 35f;
			}
			else
			{
				// Generic frag: middle ground, different silhouette yaw.
				mapping.FindPropertyRelative("ExplosionVfxScaleMultiplier").floatValue = 1.05f;
				mapping.FindPropertyRelative("ExplosionVfxLifetimeMultiplier").floatValue = 1f;
				mapping.FindPropertyRelative("ExplosionVfxYawOffsetDegrees").floatValue = -18f;
			}
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);
	}
	#endregion

	#region Helpers
	private static void SetObject(SerializedObject _so, string _name, Object _value)
	{
		SerializedProperty prop = _so.FindProperty(_name);
		if (prop != null)
			prop.objectReferenceValue = _value;
	}

	private static void SetFloat(SerializedObject _so, string _name, float _value)
	{
		SerializedProperty prop = _so.FindProperty(_name);
		if (prop != null)
			prop.floatValue = _value;
	}

	private static void SetChildActive(Transform _root, string _name, bool _active)
	{
		Transform child = FindChildRecursive(_root, _name);
		if (child != null)
			child.gameObject.SetActive(_active);
	}

	private static void TintChildParticles(Transform _root, string _name, Color _color)
	{
		Transform child = FindChildRecursive(_root, _name);
		if (child == null || !child.TryGetComponent(out ParticleSystem ps))
			return;

		ParticleSystem.MainModule main = ps.main;
		main.startColor = _color;
	}

	private static void SetChildParticleLifetime(Transform _root, string _name, float _min, float _max)
	{
		Transform child = FindChildRecursive(_root, _name);
		if (child == null || !child.TryGetComponent(out ParticleSystem ps))
			return;

		ParticleSystem.MainModule main = ps.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve(_min, _max);
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}
#endif
