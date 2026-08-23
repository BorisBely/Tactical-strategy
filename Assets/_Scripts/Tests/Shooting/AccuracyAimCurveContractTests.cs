using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	/// <summary>
	/// Vision Stage 10: Accuracy / AimTime keys live inside the 150/300 vision envelope.
	/// Does not retune Q, ScopeVisionRange, E, recoil, or burst-by-shot.
	/// </summary>
	public sealed class AccuracyAimCurveContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const string c_OpticCatalogRelative = "../Tools/optic_vision_catalog.csv";
		private const string c_WeaponCatalogRelative = "../Tools/weapon_range_catalog.csv";
		private const float c_KeyTolerance = 0.011f;
		private const float c_StepMeters = 5f;
		#endregion

		#region Tests
		[Test]
		public void CombatOptics_SweetSpotInsideHighVision()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			Assert.GreaterOrEqual(optics.Count, 18);

			Dictionary<string, float> highVision = LoadHighVisionByOptic();
			foreach (KeyValuePair<string, WeaponAttachmentDefinition> pair in optics)
			{
				Assert.IsTrue(highVision.TryGetValue(pair.Key, out float vision), pair.Key);
				WeaponDistanceAimProfile profile = pair.Value.DistanceAimProfile;
				Assert.IsNotNull(profile, pair.Key);
				Assert.LessOrEqual(
					profile.GetDispersionMaxKeyedDistanceMeters(),
					vision + 0.01f,
					pair.Key + " disp key beyond vision");
				Assert.LessOrEqual(
					profile.GetAimTimeMaxKeyedDistanceMeters(),
					vision + 0.01f,
					pair.Key + " aim key beyond vision");

				float sweet = FindDispersionSweetSpotMeters(pair.Value, vision);
				Assert.LessOrEqual(sweet, vision + 0.01f, pair.Key + " sweet " + sweet);
			}
		}

		[Test]
		public void SniperOptics_PeakNearOwnEnvelope()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			AssertNearVisionEdge(optics["Attachment_M4_Scope4"], 260f, 240f);
			AssertNearVisionEdge(optics["Attachment_M4_Scope5"], 280f, 260f);
			AssertNearVisionEdge(optics["Attachment_M4_Scope9"], 300f, 260f);
		}

		[Test]
		public void Collimators_DoNotGainLongRangeSweet()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			string[] names =
			{
				"Attachment_M4_Reddot1",
				"Attachment_M4_Reddot2",
				"Attachment_M4_Reddot3",
				"Attachment_M4_RDC",
				"Attachment_AK_Reddot4_Rail"
			};
			for (int i = 0; i < names.Length; i++)
			{
				float sweet = FindDispersionSweetSpotMeters(optics[names[i]], 150f);
				Assert.LessOrEqual(sweet, 100.01f, names[i]);
			}
		}

		[Test]
		public void FrozenAimTimeModifier_Unchanged()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			Assert.AreEqual(0.98f, optics["Attachment_M4_Reddot1"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.00f, optics["Attachment_M4_Aimpoint"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.14f, optics["Attachment_M4_Scope1_3x"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.20f, optics["Attachment_M4_ACOG"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.34f, optics["Attachment_M4_Vortex_Razor"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.46f, optics["Attachment_M4_Scope4"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.56f, optics["Attachment_M4_Scope5"].AimTimeModifier, c_KeyTolerance);
			Assert.AreEqual(1.55f, optics["Attachment_M4_Scope9"].AimTimeModifier, c_KeyTolerance);
			Assert.Greater(
				optics["Attachment_M4_Scope9"].AimTimeModifier,
				optics["Attachment_M4_Reddot1"].AimTimeModifier);
		}

		[Test]
		public void FrozenScopeVisionRange_AndWeaponE_Unchanged()
		{
			Dictionary<string, float> highVision = LoadHighVisionByOptic();
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			foreach (KeyValuePair<string, float> row in highVision)
			{
				WeaponAttachmentDefinition clone = Object.Instantiate(optics[row.Key]);
				try
				{
					if (clone.HasVariableMagnification)
						clone.SetVariableMagnificationActive(true);
					Assert.AreEqual(row.Value, clone.ResolvedScopeVisionRangeMeters, 0.01f, row.Key);
				}
				finally
				{
					Object.DestroyImmediate(clone);
				}
			}

			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			foreach (KeyValuePair<string, float> row in LoadWeaponE())
				Assert.AreEqual(row.Value, weapons[row.Key].EffectiveRangeMeters, 0.01f, row.Key);
		}

		[Test]
		public void CombatWeapons_KeysInside300_AndClassCharacter()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			Assert.AreEqual(26, weapons.Count);

			foreach (KeyValuePair<string, WeaponDefinition> pair in weapons)
			{
				WeaponDistanceAimProfile profile = pair.Value.DistanceAimProfile;
				Assert.IsNotNull(profile, pair.Key);
				Assert.LessOrEqual(
					profile.GetDispersionMaxKeyedDistanceMeters(),
					WeaponDistanceAimProfile.MaxDistanceMeters + 0.01f,
					pair.Key);
				Assert.LessOrEqual(
					profile.GetAimTimeMaxKeyedDistanceMeters(),
					WeaponDistanceAimProfile.MaxDistanceMeters + 0.01f,
					pair.Key);

				float cap = Mathf.Min(
					WeaponDistanceAimProfile.MaxDistanceMeters,
					profile.GetDispersionMaxKeyedDistanceMeters());
				float sweet = FindWeaponDispersionSweetSpotMeters(pair.Value, cap);
				WeaponDistanceCurveLibrary.WeaponBalanceKind kind =
					WeaponDistanceCurveLibrary.ResolveKind(pair.Value);
				AssertClassSweet(pair.Key, kind, sweet);
			}
		}

		[Test]
		public void LibraryFallback_MatchesBakedScope9AndM4()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			WeaponAttachmentDefinition scope9 = optics["Attachment_M4_Scope9"];
			Assert.AreEqual(
				scope9.GetDistanceDispersionMultiplier(300f),
				OpticDistanceCurveLibrary.EvaluateDispersionMultiplier(scope9, 300f),
				c_KeyTolerance);

			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponDefinition m4 = weapons["Weapon_M4_ModA_1"];
			WeaponDistanceCurveLibrary.WeaponBalanceCurves curves =
				WeaponDistanceCurveLibrary.GetCurves(WeaponDistanceCurveLibrary.ResolveKind(m4));
			AnimationCurve baked = OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes);
			Assert.AreEqual(
				m4.GetDistanceDispersionMultiplier(150f),
				baked.Evaluate(150f),
				c_KeyTolerance);
		}

		[Test]
		public void DistanceBeyondVision_IsNotARequiredWorkingState()
		{
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			WeaponAttachmentDefinition reddot = optics["Attachment_M4_Reddot1"];
			float at150 = reddot.GetDistanceDispersionMultiplier(150f);
			float at300 = reddot.GetDistanceDispersionMultiplier(300f);
			Assert.AreEqual(at150, at300, c_KeyTolerance, "1x must clamp, not invent a far sweet");
		}
		#endregion

		#region Private Methods
		private static void AssertNearVisionEdge(
			WeaponAttachmentDefinition _optic,
			float _vision,
			float _minSweet)
		{
			float sweet = FindDispersionSweetSpotMeters(_optic, _vision);
			Assert.GreaterOrEqual(sweet, _minSweet - 0.01f, _optic.name);
			Assert.LessOrEqual(sweet, _vision + 0.01f, _optic.name);
		}

		private static void AssertClassSweet(
			string _weapon,
			WeaponDistanceCurveLibrary.WeaponBalanceKind _kind,
			float _sweet)
		{
			switch (_kind)
			{
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbShort:
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbControlled:
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.ShotgunCqb:
					Assert.LessOrEqual(_sweet, 25.01f, _weapon);
					break;
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.MidRifle:
					Assert.GreaterOrEqual(_sweet, 100f, _weapon);
					Assert.LessOrEqual(_sweet, 160.01f, _weapon);
					break;
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.Marksman:
					Assert.GreaterOrEqual(_sweet, 150f, _weapon);
					Assert.LessOrEqual(_sweet, 250.01f, _weapon);
					break;
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr:
					Assert.GreaterOrEqual(_sweet, 220f, _weapon);
					Assert.LessOrEqual(_sweet, 300.01f, _weapon);
					break;
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.Support762:
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.Support545:
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.HeavySupport:
				case WeaponDistanceCurveLibrary.WeaponBalanceKind.GrenadeSupport:
					Assert.GreaterOrEqual(_sweet, 100f, _weapon);
					Assert.LessOrEqual(_sweet, 220.01f, _weapon);
					break;
				default:
					Assert.LessOrEqual(_sweet, 80.01f, _weapon);
					break;
			}
		}

		private static float FindDispersionSweetSpotMeters(
			WeaponAttachmentDefinition _optic,
			float _maxDistance)
		{
			float best = float.MaxValue;
			float at = 0f;
			for (float d = 0f; d <= _maxDistance + 0.01f; d += c_StepMeters)
			{
				float value = _optic.GetDistanceDispersionMultiplier(d);
				if (value + 0.0005f < best)
				{
					best = value;
					at = d;
				}
			}

			return at;
		}

		private static float FindWeaponDispersionSweetSpotMeters(WeaponDefinition _weapon, float _maxDistance)
		{
			float best = float.MaxValue;
			float at = 0f;
			for (float d = 0f; d <= _maxDistance + 0.01f; d += c_StepMeters)
			{
				float value = _weapon.GetDistanceDispersionMultiplier(d);
				if (value + 0.0005f < best)
				{
					best = value;
					at = d;
				}
			}

			return at;
		}

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

		private static Dictionary<string, float> LoadHighVisionByOptic()
		{
			string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, c_OpticCatalogRelative));
			Assert.IsTrue(File.Exists(csvPath), csvPath);
			var map = new Dictionary<string, float>();
			string[] lines = File.ReadAllLines(csvPath);
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line[0] == '#')
					continue;
				string[] parts = SplitCsv(line);
				if (parts.Length < 6)
					continue;
				string optic = parts[0];
				float range = float.Parse(parts[5], CultureInfo.InvariantCulture);
				if (!map.TryGetValue(optic, out float current) || range > current)
					map[optic] = range;
			}

			return map;
		}

		private static Dictionary<string, float> LoadWeaponE()
		{
			string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, c_WeaponCatalogRelative));
			Assert.IsTrue(File.Exists(csvPath), csvPath);
			var map = new Dictionary<string, float>();
			string[] lines = File.ReadAllLines(csvPath);
			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line[0] == '#')
					continue;
				string[] parts = SplitCsv(line);
				if (parts.Length < 10)
					continue;
				map[parts[0]] = float.Parse(parts[9], CultureInfo.InvariantCulture);
			}

			return map;
		}

		private static string[] SplitCsv(string _line)
		{
			var parts = new List<string>();
			bool inQuotes = false;
			int start = 0;
			for (int i = 0; i < _line.Length; i++)
			{
				char c = _line[i];
				if (c == '"')
					inQuotes = !inQuotes;
				if (c != ',' || inQuotes)
					continue;
				parts.Add(_line.Substring(start, i - start).Trim('"'));
				start = i + 1;
			}

			parts.Add(_line.Substring(start).Trim('"'));
			return parts.ToArray();
		}
		#endregion
	}
}
