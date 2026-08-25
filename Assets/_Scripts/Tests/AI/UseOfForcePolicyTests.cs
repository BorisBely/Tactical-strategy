using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UseOfForcePolicyTests
	{
		[Test]
		public void Matrix_FriendlyDeniedOnAllLevels()
		{
			UseOfForceLevel[] levels =
			{
				UseOfForceLevel.SelfDefense,
				UseOfForceLevel.RestrictedDefense,
				UseOfForceLevel.MissionCombat,
				UseOfForceLevel.FullEngagement,
				UseOfForceLevel.NoFriendlyFire
			};

			for (int i = 0; i < levels.Length; i++)
			{
				ForcePermission p = Eval(levels[i], PerceivedRelationship.Friendly, false);
				Assert.IsFalse(p.Allowed, levels[i].ToString());
				Assert.AreEqual(ForcePermissionReason.FriendlyProtected, p.Reason, levels[i].ToString());
			}
		}

		[Test]
		public void Matrix_SelfDefense_HostileRequiresImmediateThreat()
		{
			ForcePermission noThreat = Eval(UseOfForceLevel.SelfDefense, PerceivedRelationship.Hostile, false);
			Assert.IsFalse(noThreat.Allowed);
			Assert.AreEqual(ForcePermissionReason.SelfDefenseNoImmediateThreat, noThreat.Reason);

			ForcePermission threat = Eval(UseOfForceLevel.SelfDefense, PerceivedRelationship.Hostile, true);
			Assert.IsTrue(threat.Allowed);
			Assert.AreEqual(ForcePermissionReason.SelfDefenseImmediateThreat, threat.Reason);
		}

		[Test]
		public void Matrix_RestrictedDefenseAndMissionCombat_IgnoreImmediateThreat()
		{
			UseOfForceLevel[] levels =
			{
				UseOfForceLevel.RestrictedDefense,
				UseOfForceLevel.MissionCombat
			};

			for (int i = 0; i < levels.Length; i++)
			{
				ForcePermission without = Eval(levels[i], PerceivedRelationship.Hostile, false);
				ForcePermission with = Eval(levels[i], PerceivedRelationship.Hostile, true);
				Assert.IsTrue(without.Allowed, levels[i] + " without threat");
				Assert.IsTrue(with.Allowed, levels[i] + " with threat");
				Assert.AreEqual(without.Reason, with.Reason, levels[i].ToString());
				Assert.AreEqual(ForcePermissionReason.PolicyAllowsHostile, with.Reason, levels[i].ToString());
			}
		}

		[Test]
		public void Matrix_Levels2to4_HostileYes_UnknownNeutralNo()
		{
			UseOfForceLevel[] levels =
			{
				UseOfForceLevel.RestrictedDefense,
				UseOfForceLevel.MissionCombat,
				UseOfForceLevel.FullEngagement
			};

			for (int i = 0; i < levels.Length; i++)
			{
				ForcePermission hostile = Eval(levels[i], PerceivedRelationship.Hostile, false);
				Assert.IsTrue(hostile.Allowed, levels[i] + " hostile");
				Assert.AreEqual(ForcePermissionReason.PolicyAllowsHostile, hostile.Reason);

				ForcePermission unknown = Eval(levels[i], PerceivedRelationship.Unknown, false);
				Assert.IsFalse(unknown.Allowed, levels[i] + " unknown");
				Assert.AreEqual(ForcePermissionReason.UnknownNotAllowed, unknown.Reason);

				ForcePermission neutral = Eval(levels[i], PerceivedRelationship.Neutral, false);
				Assert.IsFalse(neutral.Allowed, levels[i] + " neutral");
				Assert.AreEqual(ForcePermissionReason.NeutralNotAllowed, neutral.Reason);
			}
		}

		[Test]
		public void Matrix_NoFriendlyFire_NonFriendlyAllowed()
		{
			AssertAllowed(UseOfForceLevel.NoFriendlyFire, PerceivedRelationship.Hostile, ForcePermissionReason.NonFriendly);
			AssertAllowed(UseOfForceLevel.NoFriendlyFire, PerceivedRelationship.Neutral, ForcePermissionReason.NonFriendly);
			AssertAllowed(UseOfForceLevel.NoFriendlyFire, PerceivedRelationship.Unknown, ForcePermissionReason.NonFriendly);
		}

		[Test]
		public void Level5_UsesNotEqualFriendly_NotOrChain()
		{
			ForcePermission p = UseOfForceEvaluator.Evaluate(new UseOfForceContext
			{
				Level = UseOfForceLevel.NoFriendlyFire,
				HasContact = true,
				Relationship = PerceivedRelationship.Unknown
			});
			Assert.IsTrue(p.Allowed);
			Assert.AreEqual(ForcePermissionReason.NonFriendly, p.Reason);
			Assert.AreNotEqual(ForcePermissionReason.PolicyAllowsHostile, p.Reason);
		}

		[Test]
		public void NoContact_Denied()
		{
			ForcePermission p = UseOfForceEvaluator.Evaluate(new UseOfForceContext
			{
				Level = UseOfForceLevel.NoFriendlyFire,
				HasContact = false,
				Relationship = PerceivedRelationship.Hostile,
				ImmediateThreat = true
			});
			Assert.IsFalse(p.Allowed);
			Assert.AreEqual(ForcePermissionReason.NoContact, p.Reason);
		}

		[Test]
		public void UsesRelationship_NotIdentity()
		{
			ForcePermission p = Eval(UseOfForceLevel.FullEngagement, PerceivedRelationship.Friendly, false);
			Assert.IsFalse(p.Allowed);
			Assert.AreEqual(ForcePermissionReason.FriendlyProtected, p.Reason);
		}

		[Test]
		public void IgnoresAiState()
		{
			ForcePermission idle = UseOfForceEvaluator.Evaluate(new UseOfForceContext
			{
				Level = UseOfForceLevel.FullEngagement,
				HasContact = true,
				Relationship = PerceivedRelationship.Hostile,
				State = UnitAIState.Idle
			});
			ForcePermission attack = UseOfForceEvaluator.Evaluate(new UseOfForceContext
			{
				Level = UseOfForceLevel.FullEngagement,
				HasContact = true,
				Relationship = PerceivedRelationship.Hostile,
				State = UnitAIState.Attack
			});
			Assert.IsTrue(idle.Allowed);
			Assert.IsTrue(attack.Allowed);
			Assert.AreEqual(idle.Reason, attack.Reason);
		}

		[Test]
		public void Controller_SetPolicyDoesNotChangeState()
		{
			var go = new GameObject("AI1A_Policy");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				controller.EnsureStarted();
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.AreEqual(UseOfForceLevel.SelfDefense, controller.CurrentUseOfForceLevel);
				controller.ClearTrace();
				Assert.IsTrue(controller.TrySetUseOfForcePolicy(UseOfForceLevel.NoFriendlyFire));
				Assert.AreEqual(UseOfForceLevel.NoFriendlyFire, controller.CurrentUseOfForceLevel);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void TwoControllers_IndependentPolicies()
		{
			var goA = new GameObject("AI1A_A");
			var goB = new GameObject("AI1A_B");
			try
			{
				UnitAIController a = goA.AddComponent<UnitAIController>();
				UnitAIController b = goB.AddComponent<UnitAIController>();
				Assert.IsTrue(a.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense));
				Assert.IsTrue(b.TrySetUseOfForcePolicy(UseOfForceLevel.FullEngagement));
				a.ImmediateThreat = false;
				b.ImmediateThreat = false;

				ForcePermission permA = a.EvaluateForce(true, PerceivedRelationship.Hostile, null);
				ForcePermission permB = b.EvaluateForce(true, PerceivedRelationship.Hostile, null);
				Assert.IsFalse(permA.Allowed);
				Assert.IsTrue(permB.Allowed);
				Assert.AreEqual(UnitAIState.Idle, a.CurrentState);
				Assert.AreEqual(UnitAIState.Idle, b.CurrentState);
			}
			finally
			{
				Object.DestroyImmediate(goA);
				Object.DestroyImmediate(goB);
			}
		}

		private static void AssertAllowed(
			UseOfForceLevel _level,
			PerceivedRelationship _relationship,
			ForcePermissionReason _reason)
		{
			ForcePermission p = Eval(_level, _relationship, false);
			Assert.IsTrue(p.Allowed, _level + " " + _relationship);
			Assert.AreEqual(_reason, p.Reason, _level + " " + _relationship);
		}

		private static ForcePermission Eval(
			UseOfForceLevel _level,
			PerceivedRelationship _relationship,
			bool _immediateThreat)
		{
			return UseOfForceEvaluator.Evaluate(new UseOfForceContext
			{
				Level = _level,
				HasContact = true,
				Relationship = _relationship,
				ImmediateThreat = _immediateThreat,
				State = UnitAIState.Defense
			});
		}
	}
}
