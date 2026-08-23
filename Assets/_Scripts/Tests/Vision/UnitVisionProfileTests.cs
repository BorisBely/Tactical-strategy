using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class UnitVisionProfileTests
	{
		private WeaponAttachmentDefinition m_A;
		private WeaponAttachmentDefinition m_B;
		private WeaponAttachmentDefinition m_Variable;

		[SetUp]
		public void SetUp()
		{
			m_A = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			m_B = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			m_Variable = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_A != null)
				Object.DestroyImmediate(m_A);
			if (m_B != null)
				Object.DestroyImmediate(m_B);
			if (m_Variable != null)
				Object.DestroyImmediate(m_Variable);
		}

		[Test]
		public void ClampScopeRange_Pins150And300()
		{
			Assert.AreEqual(150f, UnitVisionProfile.ClampScopeRange(0f), 0.001f);
			Assert.AreEqual(150f, UnitVisionProfile.ClampScopeRange(149f), 0.001f);
			Assert.AreEqual(150f, UnitVisionProfile.ClampScopeRange(150f), 0.001f);
			Assert.AreEqual(200f, UnitVisionProfile.ClampScopeRange(200f), 0.001f);
			Assert.AreEqual(300f, UnitVisionProfile.ClampScopeRange(300f), 0.001f);
			Assert.AreEqual(300f, UnitVisionProfile.ClampScopeRange(350f), 0.001f);
			Assert.AreEqual(300f, UnitVisionProfile.ClampScopeRange(500f), 0.001f);
		}

		[Test]
		public void HasMagnifiedScopeBonus_ZeroAnd150AreInactive()
		{
			Assert.IsFalse(UnitVisionProfile.HasMagnifiedScopeBonus(0f));
			Assert.IsFalse(UnitVisionProfile.HasMagnifiedScopeBonus(150f));
			Assert.IsTrue(UnitVisionProfile.HasMagnifiedScopeBonus(200f));
			Assert.IsTrue(UnitVisionProfile.HasMagnifiedScopeBonus(300f));
		}

		[Test]
		public void ReadRawScopeRange_IgnoresEffectiveRangeModifier()
		{
			m_A.SetScopeVisionRangeMeters(250f);
			m_A.SetEffectiveRangeModifier(1f);
			m_B.SetScopeVisionRangeMeters(250f);
			m_B.SetEffectiveRangeModifier(1.6f);

			Assert.AreEqual(250f, UnitVisionProfile.ReadRawScopeRange(new[] { m_A }), 0.001f);
			Assert.AreEqual(250f, UnitVisionProfile.ReadRawScopeRange(new[] { m_B }), 0.001f);
			Assert.AreNotEqual(m_A.EffectiveRangeModifier, m_B.EffectiveRangeModifier);
		}

		[Test]
		public void VariableMagnification_1xInactive_6xUsesHighRange()
		{
			m_Variable.ConfigureVariableMagnification(150f, 250f);
			m_Variable.SetVariableMagnificationActive(false);
			Assert.AreEqual(150f, m_Variable.ResolvedScopeVisionRangeMeters, 0.001f);
			Assert.IsFalse(UnitVisionProfile.HasMagnifiedScopeBonus(m_Variable.ResolvedScopeVisionRangeMeters));

			ResolvedVisionProfile low = UnitVisionProfile.Resolve(
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.Aiming,
				new[] { m_Variable },
				false);
			Assert.IsFalse(low.IsScopeActive);
			Assert.AreEqual(UnitVisionProfile.BaseRangeMeters, low.MaxRangeMeters, 0.001f);

			m_Variable.SetVariableMagnificationActive(true);
			Assert.AreEqual(250f, m_Variable.ResolvedScopeVisionRangeMeters, 0.001f);
			ResolvedVisionProfile high = UnitVisionProfile.Resolve(
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.Aiming,
				new[] { m_Variable },
				false);
			Assert.IsTrue(high.IsScopeActive);
			Assert.AreEqual(250f, high.MaxRangeMeters, 0.001f);
		}
	}
}
