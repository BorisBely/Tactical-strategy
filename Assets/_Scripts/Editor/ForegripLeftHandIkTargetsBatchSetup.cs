#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch: ensures every foregrip attachment visual has
/// <c>LeftHandIkTarget</c> and <c>LeftHandIkTarget_NotReady</c>.
/// Missing empties are created; NotReady is seeded from Ready when available.
/// </summary>
public static class ForegripLeftHandIkTargetsBatchSetup
{
	#region Constants
	private const string c_AttachmentsRoot = "Assets/Prefabs/Weapons";
	private const string c_ReadyName = "LeftHandIkTarget";
	private const string c_NotReadyName = "LeftHandIkTarget_NotReady";
	#endregion

	#region Menu
	[MenuItem("Polygone/Weapons/Add LeftHandIkTargets To All Foregrip Prefabs")]
	public static void AddToAllForegripPrefabs()
	{
		string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { c_AttachmentsRoot });
		int scanned = 0;
		int alreadyOk = 0;
		int createdReady = 0;
		int createdNotReady = 0;
		int skipped = 0;
		var report = new StringBuilder();

		try
		{
			// Do NOT wrap PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset in
			// AssetDatabase.StartAssetEditing — that combo can hard-crash Unity.
			for (int i = 0; i < prefabGuids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
					continue;

				string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
				if (!IsForegripAttachmentVisualPrefab(fileName, path))
					continue;

				scanned++;
				EditorUtility.DisplayProgressBar(
					"Foregrip LeftHandIkTargets",
					fileName,
					(float)i / Mathf.Max(1, prefabGuids.Length));

				GameObject root = null;
				try
				{
					root = PrefabUtility.LoadPrefabContents(path);
					if (root == null)
					{
						skipped++;
						report.AppendLine($"SKIP load: {path}");
						continue;
					}

					bool changed = false;
					Transform ready = FindChildRecursive(root.transform, c_ReadyName);
					Transform notReady = FindChildRecursive(root.transform, c_NotReadyName);

					if (ready == null)
					{
						ready = CreateEmpty(root.transform, c_ReadyName, Vector3.zero, Vector3.zero);
						createdReady++;
						changed = true;
						report.AppendLine($"CREATE {c_ReadyName}: {fileName}");
					}

					if (notReady == null)
					{
						Vector3 seedPos = ready.localPosition;
						Vector3 seedEuler = ready.localEulerAngles;
						CreateEmpty(root.transform, c_NotReadyName, seedPos, seedEuler);
						createdNotReady++;
						changed = true;
						report.AppendLine($"CREATE {c_NotReadyName}: {fileName} (seeded from Ready)");
					}

					if (!changed)
					{
						alreadyOk++;
						report.AppendLine($"OK both present: {fileName}");
					}
					else
					{
						PrefabUtility.SaveAsPrefabAsset(root, path);
					}
				}
				catch (System.Exception ex)
				{
					skipped++;
					report.AppendLine($"ERROR {fileName}: {ex.Message}");
					Debug.LogException(ex);
				}
				finally
				{
					if (root != null)
						PrefabUtility.UnloadPrefabContents(root);
				}
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
		}

		string summary =
			$"Foregrip LeftHandIkTargets batch done.\n" +
			$"Scanned foregrip visuals: {scanned}\n" +
			$"Already OK: {alreadyOk}\n" +
			$"Created {c_ReadyName}: {createdReady}\n" +
			$"Created {c_NotReadyName}: {createdNotReady}\n" +
			$"Skipped: {skipped}\n\n" +
			report;

		Debug.Log(summary);

		// Defer dialog — showing it immediately after prefab save/unload can crash some Editor builds.
		string dialogSummary = summary;
		EditorApplication.delayCall += () =>
		{
			if (!EditorUtility.DisplayDialog("Foregrip LeftHandIkTargets", dialogSummary, "OK"))
				return;
		};
	}
	#endregion

	#region Private Methods
	private static bool IsForegripAttachmentVisualPrefab(string _fileName, string _path)
	{
		if (string.IsNullOrEmpty(_fileName) || string.IsNullOrEmpty(_path))
			return false;

		// Only equipped attachment visuals, not world loot.
		if (!_path.Contains("/Attachments/") && !_path.Contains("\\Attachments\\"))
			return false;

		if (!_fileName.StartsWith("Attachment_Visual_", System.StringComparison.Ordinal))
			return false;

		string lower = _fileName.ToLowerInvariant();
		return lower.Contains("foregrip") || lower.Contains("fore_grip") || lower.Contains("bipod");
	}

	private static Transform CreateEmpty(
		Transform _parent,
		string _name,
		Vector3 _localPosition,
		Vector3 _localEuler)
	{
		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _localPosition;
		t.localRotation = Quaternion.Euler(_localEuler);
		t.localScale = Vector3.one;
		return t;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrWhiteSpace(_name))
			return null;

		Transform direct = _root.Find(_name);
		if (direct != null)
			return direct;

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
