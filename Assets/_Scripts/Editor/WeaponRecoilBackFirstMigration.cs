#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Перенос калибровки визуальной отдачи на back-first профиль:
/// ShotPitch 2.5, BackScale 0.035, UpScale 0.008, HandPitch 0.8, HandUp 0.75,
/// MaxShotImpulse 6, PitchCurve 0/0.6/1.7/4.5.
/// Записывает значения в префабы и объекты открытых сцен, у которых уже есть UnitWeaponRecoil.
/// </summary>
public static class WeaponRecoilBackFirstMigration
{
	private const string c_MenuPath = "Polygone/Shooting/Migrate Weapon Recoil Back-First Calibration";

	[MenuItem(c_MenuPath)]
	public static void MigrateMenu()
	{
		MigrateAll(out int prefabs, out int scenes);
		Debug.Log($"[WeaponRecoilMigration] prefabs={prefabs} sceneObjects={scenes}");
		EditorUtility.DisplayDialog(
			"Weapon Recoil Back-First",
			$"Обновлено: префабов — {prefabs}, объектов в сценах — {scenes}.",
			"OK");
	}

	/// <summary>
	/// Точка входа для batch mode: Unity.exe -batchmode -quit -projectPath ... -executeMethod WeaponRecoilBackFirstMigration.MigrateAllBatchMode
	/// </summary>
	public static void MigrateAllBatchMode()
	{
		MigrateAll(out int prefabs, out int scenes);
		Debug.Log($"[WeaponRecoilMigration] BATCH DONE prefabs={prefabs} sceneObjects={scenes}");
	}

	public static void MigrateAll(out int _prefabCount, out int _sceneCount)
	{
		_prefabCount = 0;
		_sceneCount = 0;

		string[] guids = AssetDatabase.FindAssets("t:Prefab");
		var processed = new HashSet<string>();
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(path) || !processed.Add(path))
				continue;

			if (MigratePrefab(path, out bool hasRecoil, out bool changed))
			{
				if (changed)
				{
					_prefabCount++;
					Debug.Log($"[WeaponRecoilMigration] PREFAB SAVED: {path}");
				}
				else if (hasRecoil)
					Debug.Log($"[WeaponRecoilMigration] PREFAB UNCHANGED (already migrated): {path}");
			}
		}

		for (int s = 0; s < SceneManager.sceneCount; s++)
		{
			Scene scene = SceneManager.GetSceneAt(s);
			if (!scene.isLoaded)
				continue;

			bool sceneChanged = false;
			GameObject[] roots = scene.GetRootGameObjects();
			foreach (GameObject root in roots)
			{
				UnitWeaponRecoil[] recoils = root.GetComponentsInChildren<UnitWeaponRecoil>(true);
				foreach (UnitWeaponRecoil recoil in recoils)
				{
					if (ApplyCalibration(recoil))
					{
						sceneChanged = true;
						_sceneCount++;
					}
				}
			}

			if (sceneChanged)
				EditorSceneManager.MarkSceneDirty(scene);
		}

		AssetDatabase.SaveAssets();
	}

	private static bool MigratePrefab(string _path, out bool _hasRecoil, out bool _changed)
	{
		_hasRecoil = false;
		_changed = false;
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		try
		{
			UnitWeaponRecoil[] recoils = root.GetComponentsInChildren<UnitWeaponRecoil>(true);
			if (recoils.Length == 0)
				return true;

			_hasRecoil = true;
			foreach (UnitWeaponRecoil recoil in recoils)
				_changed |= ApplyCalibration(recoil);

			if (!_changed)
				return true;

			PrefabUtility.SavePrefabAsset(root);
			AssetDatabase.SaveAssets();
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static bool ApplyCalibration(UnitWeaponRecoil _recoil)
	{
		SerializedObject so = new SerializedObject(_recoil);
		bool changed = false;
		changed |= SetFloat(so, "m_ShotPitch", 2.5f);
		changed |= SetFloat(so, "m_BackScale", 0.035f);
		changed |= SetFloat(so, "m_UpScale", 0.008f);
		changed |= SetFloat(so, "m_HandPitch", 0.8f);
		changed |= SetFloat(so, "m_HandBack", 1f);
		changed |= SetFloat(so, "m_HandUp", 0.75f);
		changed |= SetFloat(so, "m_ShotYawScale", 0.3f);
		changed |= SetFloat(so, "m_YawBias", 0.45f);
		changed |= SetFloat(so, "m_ShotSmoothTime", 0.08f);
		changed |= SetFloat(so, "m_DecayWhileFiringMultiplier", 1.75f);
		changed |= SetFloat(so, "m_MaxShotImpulse", 6f);
		changed |= SetFloat(so, "m_MaxShotYawDegrees", 6f);
		changed |= SetFloat(so, "m_VisualOffsetScale", 1f);
		changed |= SetCurve(so, "m_PitchCurve", new[]
		{
			new Keyframe(0f, 0f),
			new Keyframe(15f, 0.6f),
			new Keyframe(30f, 1.7f),
			new Keyframe(60f, 4.5f),
		});

		if (changed)
		{
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(_recoil);
		}

		return changed;
	}

	private static bool SetFloat(SerializedObject _so, string _name, float _value)
	{
		SerializedProperty property = _so.FindProperty(_name);
		if (property == null || property.propertyType != SerializedPropertyType.Float)
			return false;

		if (Mathf.Approximately(property.floatValue, _value))
			return false;

		property.floatValue = _value;
		return true;
	}

	private static bool SetCurve(SerializedObject _so, string _name, Keyframe[] _keys)
	{
		SerializedProperty property = _so.FindProperty(_name);
		if (property == null)
			return false;

		AnimationCurve current = property.animationCurveValue;
		if (current != null && current.length == _keys.Length)
		{
			bool same = true;
			for (int i = 0; i < _keys.Length; i++)
			{
				if (Mathf.Abs(current[i].time - _keys[i].time) > 0.0001f ||
				    Mathf.Abs(current[i].value - _keys[i].value) > 0.0001f)
				{
					same = false;
					break;
				}
			}

			if (same)
				return false;
		}

		property.animationCurveValue = new AnimationCurve(_keys);
		return true;
	}
}
#endif
