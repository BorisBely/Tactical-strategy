using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class OpticRangeContractTests
	{
		#region Constants
		private const string c_CombatOpticFolder = "Assets/GameData/Shooting";
		private const string c_CatalogRelative = "../Tools/optic_vision_catalog.csv";
		#endregion

		#region Tests
		[Test]
		public void CombatOptics_MatchCatalogAndEnvelope()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			Assert.GreaterOrEqual(optics.Count, 18, "combat optic count");

			List<CatalogRow> catalog = LoadCatalog();
			Assert.Greater(catalog.Count, 0, "catalog rows");

			bool saw300 = false;
			var seenAssets = new HashSet<string>();
			foreach (CatalogRow row in catalog)
			{
				Assert.IsTrue(optics.TryGetValue(row.Optic, out WeaponAttachmentDefinition optic), row.Optic);
				seenAssets.Add(row.Optic);

				WeaponAttachmentDefinition clone = Object.Instantiate(optic);
				try
				{
					if (row.HasVariable)
						clone.SetVariableMagnificationActive(row.Magnification > 1.01f);

					float resolved = clone.ResolvedScopeVisionRangeMeters;
					Assert.AreEqual(row.ScopeVisionRange, resolved, 0.001f, row.Optic + "/" + row.Mode);
					Assert.GreaterOrEqual(resolved, UnitVisionProfile.MinScopeRangeMeters, row.Optic);
					Assert.LessOrEqual(resolved, UnitVisionProfile.MaxScopeRangeMeters, row.Optic);

					if (row.Magnification <= 1.01f)
					{
						Assert.AreEqual(150f, resolved, 0.001f, row.Optic + " 1x");
						Assert.IsFalse(UnitVisionProfile.HasMagnifiedScopeBonus(resolved), row.Optic + " 1x bonus");
					}
					else
					{
						Assert.Greater(resolved, 150.01f, row.Optic + " mag>1");
						Assert.IsTrue(UnitVisionProfile.HasMagnifiedScopeBonus(resolved), row.Optic + " mag>1 bonus");
					}

					if (Mathf.Abs(resolved - 300f) < 0.01f)
						saw300 = true;
				}
				finally
				{
					Object.DestroyImmediate(clone);
				}
			}

			Assert.IsTrue(saw300, "at least one combat optic must be 300 m");
			Assert.AreEqual(seenAssets.Count, optics.Count, "catalog must list every combat optic");
		}

		[Test]
		public void SameVisionRange_DifferentEffectiveRangeModifier_SameResolvedVision()
		{
			var a = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			var b = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			try
			{
				a.SetScopeVisionRangeMeters(250f);
				a.SetEffectiveRangeModifier(1f);
				b.SetScopeVisionRangeMeters(250f);
				b.SetEffectiveRangeModifier(1.5f);

				float rangeA = UnitVisionProfile.ReadRawScopeRange(new[] { a });
				float rangeB = UnitVisionProfile.ReadRawScopeRange(new[] { b });
				Assert.AreEqual(250f, rangeA, 0.001f);
				Assert.AreEqual(250f, rangeB, 0.001f);
				Assert.AreNotEqual(a.EffectiveRangeModifier, b.EffectiveRangeModifier);

				ResolvedVisionProfile profileA = UnitVisionProfile.Resolve(
					UnitVisionProfile.BaseRangeMeters,
					UnitVisionProfile.BaseFovDegrees,
					WeaponPoseState.Aiming,
					new[] { a },
					false);
				ResolvedVisionProfile profileB = UnitVisionProfile.Resolve(
					UnitVisionProfile.BaseRangeMeters,
					UnitVisionProfile.BaseFovDegrees,
					WeaponPoseState.Aiming,
					new[] { b },
					false);
				Assert.AreEqual(profileA.MaxRangeMeters, profileB.MaxRangeMeters, 0.001f);
			}
			finally
			{
				Object.DestroyImmediate(a);
				Object.DestroyImmediate(b);
			}
		}
		#endregion

		#region Private Methods
		private static Dictionary<string, WeaponAttachmentDefinition> LoadCombatOptics()
		{
			var map = new Dictionary<string, WeaponAttachmentDefinition>();
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_CombatOpticFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (string.IsNullOrEmpty(path))
					continue;
				string normalized = path.Replace('\\', '/');
				if (normalized.IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0)
					continue;

				WeaponAttachmentDefinition asset =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (asset == null || asset.AttachmentType != WeaponAttachmentType.Optic)
					continue;
				map[asset.name] = asset;
			}

			return map;
		}

		private static List<CatalogRow> LoadCatalog()
		{
			string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, c_CatalogRelative));
			Assert.IsTrue(File.Exists(csvPath), csvPath);

			var rows = new List<CatalogRow>();
			string[] lines = File.ReadAllLines(csvPath);
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line[0] == '#')
					continue;

				string[] parts = SplitCsv(line);
				Assert.GreaterOrEqual(parts.Length, 7, line);
				rows.Add(new CatalogRow
				{
					Optic = parts[0],
					Mode = parts[3],
					Magnification = float.Parse(parts[4], CultureInfo.InvariantCulture),
					ScopeVisionRange = float.Parse(parts[5], CultureInfo.InvariantCulture),
					HasVariable = parts[6] == "1"
				});
			}

			return rows;
		}

		private static string[] SplitCsv(string _line)
		{
			var parts = new List<string>();
			bool inQuotes = false;
			var current = new System.Text.StringBuilder();
			for (int i = 0; i < _line.Length; i++)
			{
				char c = _line[i];
				if (c == '"')
				{
					inQuotes = !inQuotes;
					continue;
				}

				if (c == ',' && !inQuotes)
				{
					parts.Add(current.ToString());
					current.Length = 0;
					continue;
				}

				current.Append(c);
			}

			parts.Add(current.ToString());
			return parts.ToArray();
		}
		#endregion

		#region Nested Types
		private struct CatalogRow
		{
			public string Optic;
			public string Mode;
			public float Magnification;
			public float ScopeVisionRange;
			public bool HasVariable;
		}
		#endregion
	}
}
