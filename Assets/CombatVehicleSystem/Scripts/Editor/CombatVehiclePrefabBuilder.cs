#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CombatVehicleSystem.Editor
{
	/// <summary>
	/// Builds CombatVehicleSystem prefabs from Low_Poly_Vehicles_Controller sources via reflection
	/// so this assembly does not depend on broken / legacy scripts compiling.
	/// </summary>
	public static class CombatVehiclePrefabBuilder
	{
		#region Constants
		private const string c_SourceRoot = "Assets/Low_Poly_Vehicles_Controller";
		private const string c_DestRoot = "Assets/CombatVehicleSystem";
		#endregion

		#region Menu
		[MenuItem("Tools/Combat Vehicle System/Build Full Package Prefabs")]
		public static void BuildFullPackage()
		{
			EnsureFolders();
			CopyCatalogContent();
			CreateAllTunings();
			BuildFxPrefabs();
			BuildVehiclePrefabs("Desert");
			BuildVehiclePrefabs("Forest");
			BuildMinePrefab();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[CombatVehicleSystem] Prefab build complete.");
		}

		[MenuItem("Tools/Combat Vehicle System/Create Tuning Assets Only")]
		public static void CreateTuningsMenu()
		{
			EnsureFolders();
			CreateAllTunings();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		#endregion

		#region Folders / Content
		private static void EnsureFolders()
		{
			string[] folders =
			{
				"Assets/CombatVehicleSystem/Data/Tunings",
				"Assets/CombatVehicleSystem/Prefabs/Vehicles/Desert",
				"Assets/CombatVehicleSystem/Prefabs/Vehicles/Forest",
				"Assets/CombatVehicleSystem/Prefabs/Combat/Shells",
				"Assets/CombatVehicleSystem/Prefabs/Combat/Muzzle",
				"Assets/CombatVehicleSystem/Prefabs/Combat/Impacts",
				"Assets/CombatVehicleSystem/Prefabs/Combat",
				"Assets/CombatVehicleSystem/Effects/Prefabs",
				"Assets/CombatVehicleSystem/Content/Models",
				"Assets/CombatVehicleSystem/Content/Materials/Vehicles",
				"Assets/CombatVehicleSystem/Content/Textures/Vehicles",
				"Assets/CombatVehicleSystem/Effects/Materials",
				"Assets/CombatVehicleSystem/Effects/Textures",
				"Assets/CombatVehicleSystem/Audio/Engines",
				"Assets/CombatVehicleSystem/Audio/Shots"
			};

			foreach (string folder in folders)
				EnsureFolderRecursive(folder);
		}

		private static void EnsureFolderRecursive(string _folder)
		{
			_folder = _folder.Replace("\\", "/");
			if (AssetDatabase.IsValidFolder(_folder))
				return;

			string[] parts = _folder.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
					AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}

		private static void CopyCatalogContent()
		{
			CopyAssetFolder($"{c_SourceRoot}/Models", $"{c_DestRoot}/Content/Models");
			CopyAssetFolder($"{c_SourceRoot}/Materials/MAT_Vehicles", $"{c_DestRoot}/Content/Materials/Vehicles");
			CopyAssetFolder($"{c_SourceRoot}/Materials/MAT_FX", $"{c_DestRoot}/Effects/Materials");
			CopyAssetFolder($"{c_SourceRoot}/Textures/TEX_VHL", $"{c_DestRoot}/Content/Textures/Vehicles");
			CopyAssetFolder($"{c_SourceRoot}/Textures/TEX_FX", $"{c_DestRoot}/Effects/Textures");
			CopyAssetFolder($"{c_SourceRoot}/Sound_Effects/S_Engines", $"{c_DestRoot}/Audio/Engines");
			CopyAssetFolder($"{c_SourceRoot}/Sound_Effects/S_Shots", $"{c_DestRoot}/Audio/Shots");
		}

		private static void CopyAssetFolder(string _sourceFolder, string _destFolder)
		{
			if (!AssetDatabase.IsValidFolder(_sourceFolder))
			{
				Debug.LogWarning($"Missing source folder: {_sourceFolder}");
				return;
			}

			EnsureFolderRecursive(_destFolder);
			string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { _sourceFolder });
			foreach (string guid in guids)
			{
				string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(sourcePath) || AssetDatabase.IsValidFolder(sourcePath))
					continue;
				if (!sourcePath.StartsWith(_sourceFolder))
					continue;

				string relative = sourcePath.Substring(_sourceFolder.Length).TrimStart('/', '\\');
				string destPath = $"{_destFolder}/{relative}".Replace("\\", "/");
				string destDir = Path.GetDirectoryName(destPath)?.Replace("\\", "/");
				if (!string.IsNullOrEmpty(destDir))
					EnsureFolderRecursive(destDir);

				if (AssetDatabase.LoadMainAssetAtPath(destPath) != null)
					continue;

				AssetDatabase.CopyAsset(sourcePath, destPath);
			}
		}
		#endregion

		#region Tunings
		private static void CreateAllTunings()
		{
			CreateTuning("Tuning_Stryker", 2500f, 90f, 5000f, 1f, 120f, 20f, 0.17f, 150f, 200f, 300, new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, 8f, 18f);
			CreateTuning("Tuning_BTR80A", 2500f, 90f, 2500f, 1f, 120f, 12f, 0.2f, 500f, 1200f, 300, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0f, 0f, 0.15f), 40f, 10f);
			CreateTuning("Tuning_BRDM2", 1500f, 90f, 5000f, 1f, 100f, 4f, 0.12f, 250f, 10f, 300, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0f, 0f, 0.08f), 40f, 10f);
			CreateTuning("Tuning_MRAP", 1500f, 90f, 5000f, 1f, 60f, 20f, 0.17f, 200f, 100f, 100, new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, 8f, 18f);
			CreateTuning("Tuning_AMX_10", 1500f, 70f, 5000f, 1f, 50f, 4f, 1f, 3500f, 5000f, 100, Vector3.zero, new Vector3(0f, 0f, 1.5f), 40f, 3f);
			CreateTuning("Tuning_Bradley_M2", 1500f, 75f, 1000f, 50f, 120f, 12f, 0.3f, 500f, 1200f, 300, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0f, 0f, 0.15f), 40f, 10f);
			CreateTuning("Tuning_T72", 1500f, 55f, 2000f, 2f, 120f, 12f, 2f, 3500f, 6000f, 300, Vector3.zero, new Vector3(0f, 0f, 1f), 40f, 10f);
			CreateTuning("Tuning_M1A2", 1500f, 65f, 2000f, 2f, 120f, 12f, 2f, 3500f, 6000f, 300, Vector3.zero, new Vector3(0f, 0f, 1f), 40f, 10f);
		}

		private static VehicleTuning CreateTuning(
			string _name, float _motor, float _topSpeed, float _brake, float _trackScroll,
			float _turnRate, float _downLimit, float _fireInterval, float _shellSpeed, float _recoil,
			int _mag, Vector3 _spread, Vector3 _barrelKick, float _kickSpeed, float _returnSpeed)
		{
			string path = $"{c_DestRoot}/Data/Tunings/{_name}.asset";
			VehicleTuning asset = AssetDatabase.LoadAssetAtPath<VehicleTuning>(path);
			if (asset == null)
			{
				asset = ScriptableObject.CreateInstance<VehicleTuning>();
				AssetDatabase.CreateAsset(asset, path);
			}

			SerializedObject so = new SerializedObject(asset);
			so.FindProperty("m_CenterOfMass").vector3Value = new Vector3(0f, -1f, 0f);
			so.FindProperty("m_MotorForce").floatValue = _motor;
			so.FindProperty("m_TopSpeedKmh").floatValue = _topSpeed;
			so.FindProperty("m_MaxBrakeTorque").floatValue = _brake;
			so.FindProperty("m_TrackScrollScale").floatValue = _trackScroll;
			so.FindProperty("m_TurnRate").floatValue = _turnRate;
			so.FindProperty("m_LimitYaw").boolValue = false;
			so.FindProperty("m_LeftYawLimit").floatValue = 60f;
			so.FindProperty("m_RightYawLimit").floatValue = 60f;
			so.FindProperty("m_UpPitchLimit").floatValue = 60f;
			so.FindProperty("m_DownPitchLimit").floatValue = _downLimit;
			so.FindProperty("m_DefaultAimDistance").floatValue = 200f;
			so.FindProperty("m_FireInterval").floatValue = _fireInterval;
			so.FindProperty("m_ShellSpeed").floatValue = _shellSpeed;
			so.FindProperty("m_HullRecoilForce").floatValue = _recoil;
			so.FindProperty("m_MagazineSize").intValue = _mag;
			so.FindProperty("m_InfiniteAmmo").boolValue = false;
			so.FindProperty("m_ShotSpread").vector3Value = _spread;
			so.FindProperty("m_BarrelKick").vector3Value = _barrelKick;
			so.FindProperty("m_BarrelKickSpeed").floatValue = _kickSpeed;
			so.FindProperty("m_BarrelReturnSpeed").floatValue = _returnSpeed;
			so.FindProperty("m_HitFxLifetime").floatValue = 10f;
			so.FindProperty("m_ShellLifetime").floatValue = 25f;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(asset);
			return asset;
		}

		private static VehicleTuning LoadTuning(string _vehicleName)
		{
			string key = _vehicleName switch
			{
				"Stryker" => "Tuning_Stryker",
				"BTR80A" => "Tuning_BTR80A",
				"BRDM2" => "Tuning_BRDM2",
				"MRAP" => "Tuning_MRAP",
				"AMX_10" => "Tuning_AMX_10",
				"Bradley M2" => "Tuning_Bradley_M2",
				"T72" => "Tuning_T72",
				"M1A2_Abrams" => "Tuning_M1A2",
				_ => null
			};
			return key == null ? null : AssetDatabase.LoadAssetAtPath<VehicleTuning>($"{c_DestRoot}/Data/Tunings/{key}.asset");
		}
		#endregion

		#region FX
		private static void BuildFxPrefabs()
		{
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Muzzle_Flash/12.7mm_Muzzle_Flash.prefab", $"{c_DestRoot}/Prefabs/Combat/Muzzle/Muzzle_12_7.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Muzzle_Flash/14mm_Muzzle_Flash.prefab", $"{c_DestRoot}/Prefabs/Combat/Muzzle/Muzzle_14_5.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Muzzle_Flash/30mm_Muzzle_Flash.prefab", $"{c_DestRoot}/Prefabs/Combat/Muzzle/Muzzle_30.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Muzzle_Flash/Tank_Muzzle_Flash.prefab", $"{c_DestRoot}/Prefabs/Combat/Muzzle/Muzzle_Tank.prefab");

			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Impacts/12.7mm_Impact.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_12_7.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Impacts/14.5mm_Impact.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_14_5.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Impacts/30mm_Impact.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_30.prefab");
			CopyPrefab($"{c_SourceRoot}/Prefabs/FX/FX_Impacts/Tank_Impact.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_Tank.prefab");

			BuildShell($"{c_SourceRoot}/Prefabs/FX/FX_Shells/12.7mm_Shell.prefab", $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_12_7.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_12_7.prefab");
			BuildShell($"{c_SourceRoot}/Prefabs/FX/FX_Shells/14.5mm_Shell.prefab", $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_14_5.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_14_5.prefab");
			BuildShell($"{c_SourceRoot}/Prefabs/FX/FX_Shells/30mm_Shell.prefab", $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_30.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_30.prefab");
			BuildShell($"{c_SourceRoot}/Prefabs/FX/FX_Shells/Tank_Shell.prefab", $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_Tank.prefab", $"{c_DestRoot}/Prefabs/Combat/Impacts/Impact_Tank.prefab");

			CopyPrefab($"{c_SourceRoot}/Prefabs/Vehicles/Destroy_Track.prefab", $"{c_DestRoot}/Prefabs/Combat/Destroy_Track.prefab");

			CopyPrefab($"{c_DestRoot}/Prefabs/Combat/Shells/Shell_12_7.prefab", $"{c_DestRoot}/Effects/Prefabs/Shell_12_7.prefab");
			CopyPrefab($"{c_DestRoot}/Prefabs/Combat/Shells/Shell_14_5.prefab", $"{c_DestRoot}/Effects/Prefabs/Shell_14_5.prefab");
			CopyPrefab($"{c_DestRoot}/Prefabs/Combat/Shells/Shell_30.prefab", $"{c_DestRoot}/Effects/Prefabs/Shell_30.prefab");
			CopyPrefab($"{c_DestRoot}/Prefabs/Combat/Shells/Shell_Tank.prefab", $"{c_DestRoot}/Effects/Prefabs/Shell_Tank.prefab");
		}

		private static void BuildShell(string _source, string _dest, string _impactPath)
		{
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(_source);
			if (source == null)
				return;

			GameObject instance = UnityEngine.Object.Instantiate(source);
			instance.name = Path.GetFileNameWithoutExtension(_dest);

			DestroyComponentsNamed(instance, "Projectile");

			ShellProjectile shell = instance.GetComponent<ShellProjectile>() ?? instance.AddComponent<ShellProjectile>();
			GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>(_impactPath);
			SerializedObject shellSo = new SerializedObject(shell);
			shellSo.FindProperty("m_HitPrefab").objectReferenceValue = impact;
			shellSo.FindProperty("m_HitFxLifetime").floatValue = 10f;
			shellSo.FindProperty("m_Lifetime").floatValue = 25f;
			shellSo.ApplyModifiedPropertiesWithoutUndo();

			SavePrefab(instance, _dest);
			UnityEngine.Object.DestroyImmediate(instance);
		}
		#endregion

		#region Vehicles
		private static readonly string[] s_VehicleNames =
		{
			"AMX_10", "Bradley M2", "BRDM2", "BTR80A", "M1A2_Abrams", "MRAP", "Stryker", "T72"
		};

		private static void BuildVehiclePrefabs(string _biome)
		{
			foreach (string vehicleName in s_VehicleNames)
			{
				string sourcePath = $"{c_SourceRoot}/Prefabs/Vehicles/{_biome}/{vehicleName}.prefab";
				string destPath = $"{c_DestRoot}/Prefabs/Vehicles/{_biome}/{vehicleName}.prefab";
				BuildSingleVehicle(sourcePath, destPath, vehicleName);
			}
		}

		private static void BuildSingleVehicle(string _sourcePath, string _destPath, string _vehicleName)
		{
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(_sourcePath);
			if (source == null)
			{
				Debug.LogWarning($"Missing vehicle: {_sourcePath}");
				return;
			}

			GameObject instance = UnityEngine.Object.Instantiate(source);
			instance.name = _vehicleName;

			Component oldWheeled = FindComponent(instance, "VehicleController");
			Component oldTracked = FindComponent(instance, "TanksController");
			Component oldTurret = FindComponent(instance, "TurretController");
			Component oldGuns = FindComponent(instance, "GunsController");
			Component oldDamage = FindComponent(instance, "TanksDamageController");

			WheeledMotor wheeled = null;
			TrackedMotor tracked = null;

			if (oldWheeled != null)
			{
				wheeled = instance.GetComponent<WheeledMotor>() ?? instance.AddComponent<WheeledMotor>();
				List<WheelAxle> axles = new List<WheelAxle>();
				IEnumerable wheels = GetFieldValue(oldWheeled, "wheels") as IEnumerable;
				if (wheels != null)
				{
					foreach (object w in wheels)
					{
						if (w == null)
							continue;

						WheelCollider col = GetFieldValue(w, "wheelC") as WheelCollider;
						Transform visual = GetFieldValue(w, "wheelT") as Transform;
						bool motor = GetFieldValue(w, "motorTorque") is bool bMotor && bMotor;
						bool steer = GetFieldValue(w, "steering") is bool bSteer && bSteer;
						float steerAngle = GetFieldValue(w, "steeringAngle") is float ang ? ang : 20f;

						WheelAxle axle = new WheelAxle
						{
							Collider = col,
							Visual = visual,
							ApplyMotor = motor,
							ApplySteer = steer,
							SteerAngle = steerAngle
						};

						if (col != null)
						{
							Component oldStuck = col.GetComponent("VehiclesAntiStuckSystem");
							WheelAntiStuck stuck = col.GetComponent<WheelAntiStuck>() ?? col.gameObject.AddComponent<WheelAntiStuck>();
							Transform stuckVisual = visual;
							if (oldStuck != null)
							{
								object enableObj = GetFieldValue(oldStuck, "enable");
								stuck.IsEnabled = enableObj is bool en && en;
								Transform model = GetFieldValue(oldStuck, "wheelModel") as Transform;
								if (model != null)
									stuckVisual = model;
								UnityEngine.Object.DestroyImmediate(oldStuck, true);
							}
							stuck.BindVisual(stuckVisual);
							axle.AntiStuck = stuck;
						}

						axles.Add(axle);
					}
				}
				wheeled.SetAxles(axles.ToArray());
			}

			if (oldTracked != null)
			{
				tracked = instance.GetComponent<TrackedMotor>() ?? instance.AddComponent<TrackedMotor>();
				TrackSide left = ConvertTrack(GetFieldValue(oldTracked, "leftTrack"));
				TrackSide right = ConvertTrack(GetFieldValue(oldTracked, "rightTrack"));
				left.ScrollMaterial = GetFieldValue(oldTracked, "leftTracksMat") as Material;
				right.ScrollMaterial = GetFieldValue(oldTracked, "rightTracksMat") as Material;
				tracked.SetTracks(left, right);
				DestroyComponentsNamed(instance, "TanksAntiStuckSystem");
			}

			TurretAim turret = instance.GetComponent<TurretAim>() ?? instance.AddComponent<TurretAim>();
			if (oldTurret != null)
			{
				turret.Configure(
					GetFieldValue(oldTurret, "baseTurret") as Transform,
					GetFieldValue(oldTurret, "barrelTurret") as Transform,
					GetFieldValue(oldTurret, "cameraTarget") as Transform,
					GetFieldValue(oldTurret, "initialBaseRotation") is Vector3 yaw ? yaw : Vector3.zero,
					GetFieldValue(oldTurret, "initialBarrelRotation") is Vector3 pitch ? pitch : Vector3.zero);
			}

			WeaponMount weapon = instance.GetComponent<WeaponMount>() ?? instance.AddComponent<WeaponMount>();
			if (oldGuns != null)
			{
				GameObject oldShell = GetFieldValue(oldGuns, "ShellPrefab") as GameObject;
				weapon.Configure(
					GetFieldValue(oldGuns, "ejectPoint") as Transform,
					GetFieldValue(oldGuns, "recoilPosition") as Transform,
					GetFieldValue(oldGuns, "recoilBarrelTransform") as Transform,
					RemapShellPrefab(oldShell),
					GetFieldValue(oldGuns, "hitPrefab") as GameObject,
					GetFieldValue(oldGuns, "muzzleFlash") as ParticleSystem,
					GetFieldValue(oldGuns, "barrelSource") as AudioSource,
					GetFieldValue(oldGuns, "shotSound") as AudioClip);
			}

			if (oldDamage != null)
			{
				TrackBreakHandler handler = instance.GetComponent<TrackBreakHandler>() ?? instance.AddComponent<TrackBreakHandler>();
				GameObject broken = AssetDatabase.LoadAssetAtPath<GameObject>($"{c_DestRoot}/Prefabs/Combat/Destroy_Track.prefab");
				if (broken == null)
					broken = GetFieldValue(oldDamage, "destroyTrack") as GameObject;
				handler.Configure(
					broken,
					GetFieldValue(oldDamage, "leftTrack") as GameObject,
					GetFieldValue(oldDamage, "rightTrack") as GameObject);

				Component[] oldTriggers = FindComponentsInChildren(instance, "TrackDamage");
				foreach (Component oldTrigger in oldTriggers)
				{
					TrackBreakTrigger trigger = oldTrigger.GetComponent<TrackBreakTrigger>() ?? oldTrigger.gameObject.AddComponent<TrackBreakTrigger>();
					object trackEnum = GetFieldValue(oldTrigger, "track");
					TrackBreakSide side = TrackBreakSide.Left;
					if (trackEnum != null && trackEnum.ToString().IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0)
						side = TrackBreakSide.Right;
					trigger.Configure(GetFieldValue(oldTrigger, "spawnPoint") as Transform, side);
					UnityEngine.Object.DestroyImmediate(oldTrigger, true);
				}

				UnityEngine.Object.DestroyImmediate(oldDamage, true);
			}

			VehicleBrain brain = instance.GetComponent<VehicleBrain>() ?? instance.AddComponent<VehicleBrain>();
			SerializedObject brainSo = new SerializedObject(brain);
			brainSo.FindProperty("m_Tuning").objectReferenceValue = LoadTuning(_vehicleName);
			brainSo.FindProperty("m_WheeledMotor").objectReferenceValue = wheeled;
			brainSo.FindProperty("m_TrackedMotor").objectReferenceValue = tracked;
			brainSo.FindProperty("m_TurretAim").objectReferenceValue = turret;
			brainSo.FindProperty("m_WeaponMount").objectReferenceValue = weapon;
			AudioSource[] sources = instance.GetComponents<AudioSource>();
			if (sources != null && sources.Length > 0)
				brainSo.FindProperty("m_EngineAudio").objectReferenceValue = sources[0];
			brainSo.ApplyModifiedPropertiesWithoutUndo();
			brain.AutoWire();

			if (oldWheeled != null)
				UnityEngine.Object.DestroyImmediate(oldWheeled, true);
			if (oldTracked != null)
				UnityEngine.Object.DestroyImmediate(oldTracked, true);
			if (oldTurret != null)
				UnityEngine.Object.DestroyImmediate(oldTurret, true);
			if (oldGuns != null)
				UnityEngine.Object.DestroyImmediate(oldGuns, true);

			DestroyComponentsNamed(instance, "VehiclesManager");
			DestroyComponentsNamed(instance, "CameraOrbit");
			DestroyComponentsNamed(instance, "BillboardFX");
			DestroyComponentsNamed(instance, "LPVCInput");

			SavePrefab(instance, _destPath);
			UnityEngine.Object.DestroyImmediate(instance);
		}

		private static TrackSide ConvertTrack(object _source)
		{
			TrackSide side = new TrackSide { Enabled = true };
			if (_source == null)
				return side;

			side.Colliders = GetFieldValue(_source, "wheelsCollider") as WheelCollider[];
			side.Visuals = GetFieldValue(_source, "wheelsTransform") as Transform[];
			side.Bones = GetFieldValue(_source, "wheelsBones") as Transform[];

			if (side.Colliders == null)
				return side;

			for (int i = 0; i < side.Colliders.Length; i++)
			{
				WheelCollider col = side.Colliders[i];
				if (col == null)
					continue;

				Component oldStuck = col.GetComponent("TanksAntiStuckSystem");
				WheelAntiStuck stuck = col.GetComponent<WheelAntiStuck>() ?? col.gameObject.AddComponent<WheelAntiStuck>();
				Transform visual = side.Visuals != null && i < side.Visuals.Length ? side.Visuals[i] : null;
				if (oldStuck != null)
				{
					object enableObj = GetFieldValue(oldStuck, "enable");
					stuck.IsEnabled = enableObj is bool en && en;
					Transform model = GetFieldValue(oldStuck, "wheelModel") as Transform;
					stuck.BindVisual(model != null ? model : visual);
					UnityEngine.Object.DestroyImmediate(oldStuck, true);
				}
				else
				{
					stuck.BindVisual(visual);
				}
			}

			return side;
		}

		private static GameObject RemapShellPrefab(GameObject _oldShell)
		{
			if (_oldShell == null)
				return null;

			string mapped = _oldShell.name switch
			{
				"12.7mm_Shell" => $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_12_7.prefab",
				"14.5mm_Shell" => $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_14_5.prefab",
				"30mm_Shell" => $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_30.prefab",
				"Tank_Shell" => $"{c_DestRoot}/Prefabs/Combat/Shells/Shell_Tank.prefab",
				_ => null
			};

			if (mapped == null)
				return _oldShell;
			GameObject remapped = AssetDatabase.LoadAssetAtPath<GameObject>(mapped);
			return remapped != null ? remapped : _oldShell;
		}
		#endregion

		#region Mine
		private static void BuildMinePrefab()
		{
			string sourcePath = $"{c_SourceRoot}/Prefabs/TM-62_Mine.prefab";
			string destPath = $"{c_DestRoot}/Prefabs/Combat/TM62_Mine.prefab";
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
			if (source == null)
				return;

			GameObject instance = UnityEngine.Object.Instantiate(source);
			instance.name = "TM62_Mine";

			Component oldMine = FindComponentInChildren(instance, "Mine");
			GameObject host = oldMine != null ? oldMine.gameObject : instance;
			if (host.GetComponent<ExplosiveMine>() == null)
				host.AddComponent<ExplosiveMine>();

			if (oldMine != null)
				UnityEngine.Object.DestroyImmediate(oldMine, true);
			DestroyComponentsNamed(instance, "BillboardFX");

			SavePrefab(instance, destPath);
			UnityEngine.Object.DestroyImmediate(instance);
		}
		#endregion

		#region Reflection Helpers
		private static object GetFieldValue(object _target, string _fieldName)
		{
			if (_target == null)
				return null;

			Type type = _target.GetType();
			while (type != null)
			{
				FieldInfo field = type.GetField(_fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
					return field.GetValue(_target);
				type = type.BaseType;
			}
			return null;
		}

		private static Component FindComponent(GameObject _root, string _typeName)
		{
			foreach (Component component in _root.GetComponents<Component>())
			{
				if (component != null && component.GetType().Name == _typeName)
					return component;
			}
			return null;
		}

		private static Component FindComponentInChildren(GameObject _root, string _typeName)
		{
			Component[] all = FindComponentsInChildren(_root, _typeName);
			return all.Length > 0 ? all[0] : null;
		}

		private static Component[] FindComponentsInChildren(GameObject _root, string _typeName)
		{
			List<Component> list = new List<Component>();
			foreach (Component component in _root.GetComponentsInChildren<Component>(true))
			{
				if (component != null && component.GetType().Name == _typeName)
					list.Add(component);
			}
			return list.ToArray();
		}

		private static void DestroyComponentsNamed(GameObject _root, string _typeName)
		{
			foreach (Component component in FindComponentsInChildren(_root, _typeName))
				UnityEngine.Object.DestroyImmediate(component, true);
		}
		#endregion

		#region Prefab IO
		private static void CopyPrefab(string _source, string _dest)
		{
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(_source);
			if (source == null)
			{
				Debug.LogWarning($"Missing prefab: {_source}");
				return;
			}

			GameObject instance = UnityEngine.Object.Instantiate(source);
			instance.name = Path.GetFileNameWithoutExtension(_dest);
			SavePrefab(instance, _dest);
			UnityEngine.Object.DestroyImmediate(instance);
		}

		private static void SavePrefab(GameObject _instance, string _path)
		{
			string dir = Path.GetDirectoryName(_path)?.Replace("\\", "/");
			if (!string.IsNullOrEmpty(dir))
				EnsureFolderRecursive(dir);
			PrefabUtility.SaveAsPrefabAsset(_instance, _path);
		}
		#endregion
	}
}
#endif
