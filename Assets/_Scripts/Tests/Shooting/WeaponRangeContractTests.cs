using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	public sealed class WeaponRangeContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const string c_WeaponCatalogRelative = "../Tools/weapon_range_catalog.csv";
		private const string c_AmmoCatalogRelative = "../Tools/ammo_range_catalog.csv";
		private const float c_RangeTolerance = 0.01f;
		#endregion

		#region Tests
		[Test]
		public void Catalog_ListsEveryCombatWeaponAndAmmo()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			Dictionary<string, AmmoDefinition> ammo = LoadCombatAmmo();
			List<WeaponCatalogRow> weaponRows = LoadWeaponCatalog();
			List<AmmoCatalogRow> ammoRows = LoadAmmoCatalog();

			Assert.AreEqual(26, weapons.Count, "combat weapon count");
			Assert.AreEqual(8, ammo.Count, "combat ammo count");
			Assert.AreEqual(weapons.Count, weaponRows.Count, "weapon catalog coverage");
			Assert.AreEqual(ammo.Count, ammoRows.Count, "ammo catalog coverage");
		}

		[Test]
		public void LiveAssets_MatchApprovedCatalog()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			Dictionary<string, AmmoDefinition> ammo = LoadCombatAmmo();
			Dictionary<string, AmmoCatalogRow> ammoRows = IndexAmmo(LoadAmmoCatalog());

			foreach (AmmoCatalogRow row in ammoRows.Values)
			{
				Assert.AreEqual(row.ProposedRange, ammo[row.Ammo].EffectiveRangeMeters, c_RangeTolerance, row.Ammo);
			}

			foreach (WeaponCatalogRow row in LoadWeaponCatalog())
			{
				Assert.AreEqual(
					row.ProposedWeaponRange,
					weapons[row.Weapon].EffectiveRangeMeters,
					c_RangeTolerance,
					row.Weapon);

				float e = WeaponDamageRangeMath.ResolveEffectiveRangeMeters(
					row.ProposedWeaponRange,
					WeaponDamageRangeMath.ProposedOpticEffectiveRangeModifier,
					ammoRows[row.Ammo].ProposedRange);

				if (row.Category == "RegularHitscan" || row.Category == "HeavyHitscan")
				{
					Assert.Greater(e, 0.1f, row.Weapon);
					Assert.LessOrEqual(e, WeaponDamageRangeMath.MaxHitscanEnvelopeMeters, row.Weapon);
				}

				if (row.Category == "ProjectileSupport")
				{
					Assert.AreEqual("Weapon_MK19", row.Weapon);
					Assert.IsTrue(float.IsNaN(row.MinMultiplierAtEdge));
				}

				if (row.Category == "HeavyHitscan")
					Assert.AreEqual("Weapon_M2Browning_127", row.Weapon);
			}
		}

		[Test]
		public void CombatOptics_RangeModifierIsOne()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			Assert.GreaterOrEqual(optics.Count, 18);
			foreach (KeyValuePair<string, WeaponAttachmentDefinition> pair in optics)
			{
				Assert.AreEqual(
					WeaponDamageRangeMath.ProposedOpticEffectiveRangeModifier,
					pair.Value.EffectiveRangeModifier,
					c_RangeTolerance,
					pair.Key);
			}
		}

		[Test]
		public void Silencers_KeepPhysicalRangeModifier()
		{
			bool sawSilencer = false;
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (ShouldSkipTestAsset(path))
					continue;
				WeaponAttachmentDefinition asset =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (asset == null || asset.AttachmentType != WeaponAttachmentType.Suppressor)
					continue;
				if (asset.name.IndexOf("Silencer", System.StringComparison.OrdinalIgnoreCase) < 0)
					continue;
				sawSilencer = true;
				Assert.AreEqual(1.1f, asset.EffectiveRangeModifier, c_RangeTolerance, asset.name);
			}

			Assert.IsTrue(sawSilencer, "expected combat silencers");
		}

		[Test]
		public void FalloffMath_AtEffective_OnePointFive_AndZero()
		{
			const float effective = 140f;
			Assert.AreEqual(1f, WeaponDamageRangeMath.ComputeFalloffMultiplier(0f, effective), 0.0001f);
			Assert.AreEqual(1f, WeaponDamageRangeMath.ComputeFalloffMultiplier(effective, effective), 0.0001f);
			Assert.AreEqual(
				0.5f,
				WeaponDamageRangeMath.ComputeFalloffMultiplier(effective * 1.5f, effective),
				0.0001f);
			Assert.AreEqual(0f, WeaponDamageRangeMath.ComputeFalloffMultiplier(effective * 2f, effective), 0.0001f);
		}

		[Test]
		public void ResolveEffectiveRange_TakesMinOfWeaponAndAmmo()
		{
			Assert.AreEqual(
				140f,
				WeaponDamageRangeMath.ResolveEffectiveRangeMeters(140f, 1f, 300f),
				0.0001f);
			Assert.AreEqual(
				100f,
				WeaponDamageRangeMath.ResolveEffectiveRangeMeters(140f, 1f, 100f),
				0.0001f);
			Assert.AreEqual(
				154f,
				WeaponDamageRangeMath.ResolveEffectiveRangeMeters(140f, 1.1f, 300f),
				0.0001f);
		}

		[Test]
		public void RoleEdges_MeetMinimumMultiplier()
		{
			Dictionary<string, AmmoCatalogRow> ammoRows = IndexAmmo(LoadAmmoCatalog());
			foreach (WeaponCatalogRow row in LoadWeaponCatalog())
			{
				float proposedE = WeaponDamageRangeMath.ResolveEffectiveRangeMeters(
					row.ProposedWeaponRange,
					WeaponDamageRangeMath.ProposedOpticEffectiveRangeModifier,
					ammoRows[row.Ammo].ProposedRange);

				if (row.Category == "ProjectileSupport")
					continue;
				if (row.Category == "ShotgunCurve")
				{
					Assert.AreEqual(40f, proposedE, c_RangeTolerance, row.Weapon);
					continue;
				}

				float actual = WeaponDamageRangeMath.ComputeFalloffMultiplier(row.EngagementEdge, proposedE);
				Assert.GreaterOrEqual(actual + 0.002f, row.MinMultiplierAtEdge, row.Weapon);
			}
		}

		[Test]
		public void ShotgunPelletCurve_BypassesLinearHitscanFalloff()
		{
			AmmoDefinition ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(
				"Assets/GameData/Shooting/Ammo_12Gauge.asset");
			Assert.IsNotNull(ammo);
			Assert.IsTrue(ammo.UsesShotgunPelletPattern);
			Assert.IsTrue(ammo.TryGetShotgunPelletDamageFalloff(40f, out float edge));
			Assert.AreEqual(0.35f, edge, 0.001f);
			Assert.AreEqual(1f, WeaponDamageRangeMath.ComputeFalloffMultiplier(40f, 40f), 0.0001f);
			Assert.AreNotEqual(1f, edge);
		}

		[Test]
		public void ScopeVisionRange_IndependentOfEffectiveRangeModifier()
		{
			var a = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			var b = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			try
			{
				a.SetScopeVisionRangeMeters(250f);
				a.SetEffectiveRangeModifier(1f);
				b.SetScopeVisionRangeMeters(250f);
				b.SetEffectiveRangeModifier(1.5f);
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

		[Test]
		public void PoseDiagnosticScan_UsesEnvelopeNotDamageModifier()
		{
			var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
			try
			{
				weapon.SetEffectiveRangeMeters(140f);
				Assert.AreEqual(
					WeaponDamageRangeMath.MaxHitscanEnvelopeMeters,
					WeaponPoseAutoCapabilityBaker.ResolveMaxScanMeters(weapon),
					0.001f);
			}
			finally
			{
				Object.DestroyImmediate(weapon);
			}
		}

		[Test]
		public void Mk19_IsProjectileSupportNotLinearFalloff()
		{
			WeaponDefinition mk19 = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
				"Assets/GameData/Shooting/Turret/Weapon_MK19.asset");
			Assert.IsNotNull(mk19);
			Assert.AreEqual(WeaponClassType.AutomaticGrenadeLauncher, mk19.WeaponClass);
			Assert.AreEqual(300f, mk19.EffectiveRangeMeters, c_RangeTolerance);
		}
		#endregion

		#region Private Methods
		private static Dictionary<string, WeaponDefinition> LoadCombatWeapons()
		{
			var map = new Dictionary<string, WeaponDefinition>();
			string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (ShouldSkipTestAsset(path))
					continue;
				WeaponDefinition asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
				if (asset != null)
					map[asset.name] = asset;
			}

			return map;
		}

		private static Dictionary<string, AmmoDefinition> LoadCombatAmmo()
		{
			var map = new Dictionary<string, AmmoDefinition>();
			string[] guids = AssetDatabase.FindAssets("t:AmmoDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (ShouldSkipTestAsset(path))
					continue;
				AmmoDefinition asset = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(path);
				if (asset != null)
					map[asset.name] = asset;
			}

			return map;
		}

		private static Dictionary<string, WeaponAttachmentDefinition> LoadCombatOptics()
		{
			var map = new Dictionary<string, WeaponAttachmentDefinition>();
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (ShouldSkipTestAsset(path))
					continue;
				WeaponAttachmentDefinition asset =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (asset == null || asset.AttachmentType != WeaponAttachmentType.Optic)
					continue;
				map[asset.name] = asset;
			}

			return map;
		}

		private static bool ShouldSkipTestAsset(string _path)
		{
			if (string.IsNullOrEmpty(_path))
				return true;
			return _path.Replace('\\', '/').IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static List<WeaponCatalogRow> LoadWeaponCatalog()
		{
			string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, c_WeaponCatalogRelative));
			Assert.IsTrue(File.Exists(csvPath), csvPath);
			var rows = new List<WeaponCatalogRow>();
			string[] lines = File.ReadAllLines(csvPath);
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line[0] == '#')
					continue;
				string[] parts = SplitCsv(line);
				rows.Add(new WeaponCatalogRow
				{
					Weapon = parts[0],
					Ammo = parts[2],
					Category = parts[6],
					ProposedWeaponRange = float.Parse(parts[9], CultureInfo.InvariantCulture),
					EngagementEdge = float.Parse(parts[10], CultureInfo.InvariantCulture),
					MinMultiplierAtEdge = ParseOptionalFloat(parts[11])
				});
			}

			return rows;
		}

		private static List<AmmoCatalogRow> LoadAmmoCatalog()
		{
			string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, c_AmmoCatalogRelative));
			Assert.IsTrue(File.Exists(csvPath), csvPath);
			var rows = new List<AmmoCatalogRow>();
			string[] lines = File.ReadAllLines(csvPath);
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line[0] == '#')
					continue;
				string[] parts = SplitCsv(line);
				rows.Add(new AmmoCatalogRow
				{
					Ammo = parts[0],
					ProposedRange = float.Parse(parts[5], CultureInfo.InvariantCulture)
				});
			}

			return rows;
		}

		private static Dictionary<string, AmmoCatalogRow> IndexAmmo(List<AmmoCatalogRow> _rows)
		{
			var map = new Dictionary<string, AmmoCatalogRow>();
			for (int i = 0; i < _rows.Count; i++)
				map[_rows[i].Ammo] = _rows[i];
			return map;
		}

		private static float ParseOptionalFloat(string _raw)
		{
			if (string.IsNullOrWhiteSpace(_raw))
				return float.NaN;
			return float.Parse(_raw, CultureInfo.InvariantCulture);
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
		private struct WeaponCatalogRow
		{
			public string Weapon;
			public string Ammo;
			public string Category;
			public float ProposedWeaponRange;
			public float EngagementEdge;
			public float MinMultiplierAtEdge;
		}

		private struct AmmoCatalogRow
		{
			public string Ammo;
			public float ProposedRange;
		}
		#endregion
	}
}
