using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class GameCommandLayerTests
	{
		#region Matrix
		[Test]
		public void Matrix_Idle_AcceptsDefenseAttackSearchFlee_RejectsRetreat()
		{
			UnitAIController controller = CreateAi("AI64_MxIdle");
			try
			{
				AssertAccept(controller, Cmd.Defense(P(1f)), UnitAIState.Defense);
				Enter(controller, UnitAIState.Idle);
				AssertAccept(controller, Cmd.Attack(P(2f)), UnitAIState.Attack);
				Enter(controller, UnitAIState.Idle);
				AssertAccept(controller, Cmd.Search(P(3f)), UnitAIState.Search);
				Enter(controller, UnitAIState.Idle);
				AssertAccept(controller, Cmd.Flee(P(4f)), UnitAIState.Flee);
				Enter(controller, UnitAIState.Idle);
				AssertReject(controller, Cmd.Retreat(P(5f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Idle);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Matrix_Defense_AcceptsAttackRetreatSearch()
		{
			UnitAIController controller = CreateAi("AI64_MxDef");
			try
			{
				Enter(controller, UnitAIState.Defense);
				AssertAccept(controller, Cmd.Attack(P(6f)), UnitAIState.Attack);
				Enter(controller, UnitAIState.Defense);
				AssertAccept(controller, Cmd.Retreat(P(7f)), UnitAIState.Retreat);
				Enter(controller, UnitAIState.Defense);
				AssertAccept(controller, Cmd.Search(P(8f)), UnitAIState.Search);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Matrix_Attack_AcceptsDefenseRetreatSearch()
		{
			UnitAIController controller = CreateAi("AI64_MxAtk");
			try
			{
				Enter(controller, UnitAIState.Attack);
				AssertAccept(controller, Cmd.Defense(P(9f)), UnitAIState.Defense);
				Enter(controller, UnitAIState.Attack);
				AssertAccept(controller, Cmd.Retreat(P(10f)), UnitAIState.Retreat);
				Enter(controller, UnitAIState.Attack);
				AssertAccept(controller, Cmd.Search(P(11f)), UnitAIState.Search);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Matrix_Search_AcceptsAttackDefenseRetreat()
		{
			UnitAIController controller = CreateAi("AI64_MxSrch");
			try
			{
				Enter(controller, UnitAIState.Search);
				AssertAccept(controller, Cmd.Attack(P(12f)), UnitAIState.Attack);
				Enter(controller, UnitAIState.Search);
				AssertAccept(controller, Cmd.Defense(P(13f)), UnitAIState.Defense);
				Enter(controller, UnitAIState.Search);
				AssertAccept(controller, Cmd.Retreat(P(14f)), UnitAIState.Retreat);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Matrix_Retreat_AcceptsDefense_RejectsAttackSearch()
		{
			UnitAIController controller = CreateAi("AI64_MxRet");
			try
			{
				Enter(controller, UnitAIState.Retreat);
				AssertAccept(controller, Cmd.Defense(P(15f)), UnitAIState.Defense);
				Enter(controller, UnitAIState.Retreat);
				AssertReject(controller, Cmd.Attack(P(16f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Retreat);
				AssertReject(controller, Cmd.Search(P(17f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Retreat);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Matrix_Flee_AcceptsCancel_RejectsAttackDefenseSearchRetreat()
		{
			UnitAIController controller = CreateAi("AI64_MxFlee");
			try
			{
				Enter(controller, UnitAIState.Flee);
				AssertReject(controller, Cmd.Attack(P(18f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Flee);
				AssertReject(controller, Cmd.Defense(P(19f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Flee);
				AssertReject(controller, Cmd.Search(P(20f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Flee);
				AssertReject(controller, Cmd.Retreat(P(21f)), GameCommandRejectReason.InvalidStateTransition,
					UnitAIState.Flee);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region Lifecycle
		[Test]
		public void Replace_AttackWalk_ThenDefense_WalksToAnchor()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_Replace");
			Vector3 attack = P(20f);
			Vector3 defense = P(3f, 8f);
			try
			{
				AssertAccept(controller, Cmd.Attack(attack), UnitAIState.Attack);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.IsTrue(recorder.HasMoveIntent);
				Assert.AreEqual(UnitNavigationReason.Attack, recorder.Reason);

				AssertAccept(controller, Cmd.Defense(defense), UnitAIState.Defense);
				Assert.AreEqual(defense, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(defense, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsTrue(recorder.HasMoveIntent);
				Assert.AreEqual(defense, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Defense, recorder.Reason);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void RapidSeries_LastTaskIsDefense_WalksToAnchor()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_Rapid");
			Vector3 a = P(4f);
			Vector3 b = P(8f);
			Vector3 c = P(12f);
			Vector3 d = P(1f, 9f);
			try
			{
				AssertAccept(controller, Cmd.Attack(a), UnitAIState.Attack);
				AssertAccept(controller, Cmd.Retreat(b), UnitAIState.Retreat);
				AssertAccept(controller, Cmd.Attack(c), UnitAIState.Attack);
				AssertAccept(controller, Cmd.Defense(d), UnitAIState.Defense);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(d, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(d, recorder.LastDestination);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsTrue(recorder.HasMoveIntent);
				Assert.AreEqual(UnitNavigationReason.Defense, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SameState_AttackDefenseRetreat_ReplaceWithoutIdle()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_Same");
			try
			{
				Vector3 a1 = P(4f);
				Vector3 a2 = P(9f, 2f);
				AssertAccept(controller, Cmd.Attack(a1), UnitAIState.Attack);
				controller.ClearTrace();
				AssertAccept(controller, Cmd.Attack(a2), UnitAIState.Attack);
				Assert.AreEqual(a2, controller.CurrentContext.Destination);
				CollectionAssert.DoesNotContain(controller.Trace, "Enter:Idle");
				CollectionAssert.Contains(controller.Trace, "Exit:Attack");
				CollectionAssert.Contains(controller.Trace, "Enter:Attack");
				Assert.AreEqual(a2, recorder.LastDestination);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsTrue(recorder.HasMoveIntent);

				Vector3 d1 = P(1f);
				Vector3 d2 = P(2f, 5f);
				AssertAccept(controller, Cmd.Defense(d1), UnitAIState.Defense);
				controller.ClearTrace();
				AssertAccept(controller, Cmd.Defense(d2), UnitAIState.Defense);
				Assert.AreEqual(d2, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(d2, controller.CurrentContext.Destination);
				CollectionAssert.DoesNotContain(controller.Trace, "Enter:Idle");
				Assert.AreEqual(d2, recorder.LastDestination);
				Assert.IsTrue(recorder.HasMoveIntent);

				Vector3 r1 = P(-4f);
				Vector3 r2 = P(-9f, 1f);
				AssertAccept(controller, Cmd.Retreat(r1), UnitAIState.Retreat);
				int stops = recorder.StopCount;
				controller.ClearTrace();
				AssertAccept(controller, Cmd.Retreat(r2), UnitAIState.Retreat);
				Assert.AreEqual(r2, controller.CurrentContext.Destination);
				CollectionAssert.DoesNotContain(controller.Trace, "Enter:Idle");
				Assert.AreEqual(r2, recorder.LastDestination);
				Assert.Greater(recorder.StopCount, stops);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Cancel_FromAttackDefenseRetreatSearch_GoesIdle_ArrivalDoesNotResurrect()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_Cancel");
			try
			{
				Vector3 attack = P(18f);
				AssertAccept(controller, Cmd.Attack(attack), UnitAIState.Attack);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);
				Assert.IsFalse(controller.CurrentContext.HasDestination);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);

				controller.transform.position = attack;
				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);

				AssertAccept(controller, Cmd.Defense(P(2f)), UnitAIState.Defense);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);
				Assert.IsFalse(controller.CurrentContext.HasDestination);

				AssertAccept(controller, Cmd.Defense(P(1f)), UnitAIState.Defense);
				AssertAccept(controller, Cmd.Retreat(P(-6f)), UnitAIState.Retreat);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);

				AssertAccept(controller, Cmd.Search(P(7f, 3f)), UnitAIState.Search);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);
				Assert.AreEqual(Vector3.zero, controller.CurrentContext.SearchPosition);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void ReplaceViaNewCommand_AttackRetreat_SearchAttack_RetreatDefense()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_ViaNew");
			try
			{
				Vector3 a = P(10f);
				Vector3 r = P(-8f);
				AssertAccept(controller, Cmd.Attack(a), UnitAIState.Attack);
				AssertAccept(controller, Cmd.Retreat(r), UnitAIState.Retreat);
				Assert.AreEqual(r, controller.CurrentContext.Destination);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);

				Vector3 s = P(4f, 6f);
				Vector3 a2 = P(11f, 1f);
				AssertAccept(controller, Cmd.Cancel(), UnitAIState.Idle);
				AssertAccept(controller, Cmd.Search(s), UnitAIState.Search);
				AssertAccept(controller, Cmd.Attack(a2), UnitAIState.Attack);
				Assert.AreEqual(a2, controller.CurrentContext.Destination);

				AssertAccept(controller, Cmd.Defense(P(0f, 2f)), UnitAIState.Defense);
				AssertAccept(controller, Cmd.Retreat(P(-3f)), UnitAIState.Retreat);
				AssertAccept(controller, Cmd.Defense(P(5f, 5f)), UnitAIState.Defense);
				Assert.AreEqual(P(5f, 5f), controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(P(5f, 5f), recorder.LastDestination);
				Assert.IsTrue(recorder.HasMoveIntent);
				Assert.AreEqual(UnitNavigationReason.Defense, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IndependentControllers_KeepOwnStates()
		{
			UnitAIController p1 = CreateAi("AI64_P01");
			UnitAIController p2 = CreateAi("AI64_P02");
			UnitAIController p3 = CreateAi("AI64_P03");
			try
			{
				AssertAccept(p1, Cmd.Attack(P(1f)), UnitAIState.Attack);
				AssertAccept(p2, Cmd.Defense(P(2f)), UnitAIState.Defense);
				AssertAccept(p3, Cmd.Defense(P(1f)), UnitAIState.Defense);
				AssertAccept(p3, Cmd.Retreat(P(-3f)), UnitAIState.Retreat);
				Assert.AreEqual(UnitAIState.Attack, p1.CurrentState);
				Assert.AreEqual(UnitAIState.Defense, p2.CurrentState);
				Assert.AreEqual(UnitAIState.Retreat, p3.CurrentState);
				Assert.AreEqual(P(1f), p1.CurrentContext.Destination);
				Assert.AreEqual(P(2f), p2.CurrentContext.AnchorPosition);
				Assert.AreEqual(P(2f), p2.CurrentContext.Destination);
				Assert.AreEqual(P(-3f), p3.CurrentContext.Destination);
			}
			finally
			{
				Destroy(p1);
				Destroy(p2);
				Destroy(p3);
			}
		}
		#endregion

		#region Mass And Input
		[Test]
		public void MassPlayer_ThreeAttack_FourthAndOthersUntouched()
		{
			GameCommandInput input = CreateInput();
			UnitAIController p1 = CreateAi("AI64_MassP1", UnitTeamId.Player);
			UnitAIController p2 = CreateAi("AI64_MassP2", UnitTeamId.Player);
			UnitAIController p3 = CreateAi("AI64_MassP3", UnitTeamId.Player);
			UnitAIController p4 = CreateAi("AI64_MassP4", UnitTeamId.Player);
			UnitAIController enemy = CreateAi("AI64_MassE", UnitTeamId.Enemy);
			UnitAIController neutral = CreateAi("AI64_MassN", UnitTeamId.Neutral);
			Vector3 point = P(6f, 1f);
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				int accepted = input.ConfirmPoint(point, new Component[] { p1, p2, p3 });
				Assert.AreEqual(3, accepted);
				Assert.AreEqual(UnitAIState.Attack, p1.CurrentState);
				Assert.AreEqual(UnitAIState.Attack, p2.CurrentState);
				Assert.AreEqual(UnitAIState.Attack, p3.CurrentState);
				Assert.AreEqual(point, p1.CurrentContext.Destination);
				Assert.AreEqual(point, p2.CurrentContext.Destination);
				Assert.AreEqual(UnitAIState.Idle, p4.CurrentState);
				Assert.AreEqual(UnitAIState.Idle, enemy.CurrentState);
				Assert.AreEqual(UnitAIState.Idle, neutral.CurrentState);
			}
			finally
			{
				Destroy(p1);
				Destroy(p2);
				Destroy(p3);
				Destroy(p4);
				Destroy(enemy);
				Destroy(neutral);
				Destroy(input);
			}
		}

		[Test]
		public void EnemyDebug_LiveCollect_SpawnDeathAndNoAiAttach()
		{
			GameCommandInput input = CreateInput();
			var living = new List<UnitTeam>(16);
			try
			{
				for (int i = 0; i < 10; i++)
					living.Add(CreateEnemy("AI64_LiveE_" + i, false, false));

				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.EnemyDebug);
				Assert.AreEqual(10, input.ConfirmPoint(P(4f), ToComponents(living)));
				AssertNamedState("AI64_LiveE_", UnitAIState.Attack, 10);

				UnitTeam extra = CreateEnemy("AI64_LiveE_10", false, false);
				living.Add(extra);
				var collected = new List<Component>(16);
				GameCommandRecipientQuery.Collect(GameCommandAudience.EnemyDebug, collected);
				Assert.AreEqual(11, CountNamed(collected, "AI64_LiveE_"));

				input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
				Assert.GreaterOrEqual(input.ConfirmPoint(P(8f, 2f)), 11);
				AssertNamedState("AI64_LiveE_", UnitAIState.Defense, 11);

				living[0].GetComponent<UnitHealth>().EnterDead();
				living[1].GetComponent<UnitHealth>().EnterDead();
				living[2].GetComponent<UnitHealth>().EnterDead();
				UnitTeam noAi0 = CreateEnemy("AI64_LiveE_N0", false, false);
				CreateEnemy("AI64_LiveE_N1", false, false);

				collected.Clear();
				GameCommandRecipientQuery.Collect(GameCommandAudience.EnemyDebug, collected);
				Assert.AreEqual(10, CountNamed(collected, "AI64_LiveE_"));
				Assert.IsFalse(ContainsName(collected, living[0].name));
				Assert.IsFalse(ContainsName(collected, living[1].name));
				Assert.IsFalse(ContainsName(collected, living[2].name));
				Assert.AreEqual(0, CountDeadNamed(collected, "AI64_LiveE_"));

				input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
				input.ConfirmPoint(P(1f, 3f));
				Assert.AreEqual(UnitAIState.Defense, extra.GetComponent<UnitAIController>().CurrentState);
				Assert.IsNotNull(noAi0.GetComponent<UnitAIController>());
				Assert.AreEqual(UnitAIState.Defense, living[0].GetComponent<UnitAIController>().CurrentState);
			}
			finally
			{
				DestroyNamed("AI64_LiveE_");
				Destroy(input);
			}
		}

		[Test]
		public void Issue_InactiveAndDestroyed_RejectsInvalidUnit()
		{
			UnitAIController controller = CreateAi("AI64_Inactive");
			TacticalCommand attack = Cmd.Attack(P(5f));
			try
			{
				AssertAccept(controller, attack, UnitAIState.Attack);
				controller.gameObject.SetActive(false);
				GameCommandResult inactive = GameCommandService.Issue(controller, in attack);
				Assert.IsFalse(inactive.Accepted);
				Assert.AreEqual(GameCommandRejectReason.InvalidUnit, inactive.Reason);

				Object.DestroyImmediate(controller.gameObject);
				GameCommandResult destroyed = GameCommandService.Issue(controller, in attack);
				Assert.IsFalse(destroyed.Accepted);
				Assert.AreEqual(GameCommandRejectReason.InvalidUnit, destroyed.Reason);
				controller = null;
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Input_SearchFleeImmediateCancel_AndNormalConfirmIsZero()
		{
			GameCommandInput input = CreateInput();
			UnitAIController player = CreateAi("AI64_ModesP", UnitTeamId.Player);
			UnitTeam enemy = CreateEnemy("AI64_ModesE", false, true);
			try
			{
				Assert.AreEqual(0, input.ConfirmPoint(P(1f), new Component[] { player }));
				Assert.AreEqual("NotPending", input.LastSkipReason);

				input.BeginPending(GameCommandInputMode.SearchPending, GameCommandAudience.PlayerSelected);
				Assert.AreEqual(1, input.ConfirmPoint(P(7f, 4f), new Component[] { player }));
				Assert.AreEqual(UnitAIState.Search, player.CurrentState);
				Assert.AreEqual(P(7f, 4f), player.CurrentContext.SearchPosition);

				input.BeginPending(GameCommandInputMode.FleePending, GameCommandAudience.EnemyDebug);
				Assert.AreEqual(1, input.ConfirmPoint(P(-5f), new Component[] { enemy }));
				Assert.AreEqual(UnitAIState.Flee, enemy.GetComponent<UnitAIController>().CurrentState);

				int cancelled = input.IssueImmediateCancel(GameCommandAudience.EnemyDebug);
				Assert.GreaterOrEqual(cancelled, 1);
				Assert.AreEqual(UnitAIState.Idle, enemy.GetComponent<UnitAIController>().CurrentState);
			}
			finally
			{
				Destroy(player);
				DestroyGo(enemy);
				Destroy(input);
			}
		}

		[Test]
		public void PlayerAndEnemyDebug_BuildSameCommandPayload()
		{
			GameCommandInput input = CreateInput();
			StubCommandReceiver player = CreateStub("AI64_SameP");
			StubCommandReceiver enemy = CreateStub("AI64_SameE");
			Vector3 point = P(3f, 4f);
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				input.ConfirmPoint(point, new Component[] { player });
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.EnemyDebug);
				input.ConfirmPoint(point, new Component[] { enemy });
				Assert.AreEqual(TacticalCommandType.Attack, player.LastType);
				Assert.AreEqual(player.LastType, enemy.LastType);
				Assert.AreEqual(point, player.LastPosition);
				Assert.AreEqual(player.LastPosition, enemy.LastPosition);
				Assert.AreEqual(TacticalCommandSource.Game, player.LastSource);
				Assert.AreEqual(player.LastSource, enemy.LastSource);
			}
			finally
			{
				Destroy(player);
				Destroy(enemy);
				Destroy(input);
			}
		}

		[Test]
		public void Command_DoesNotFireChangeRoeOrAssignCombatTarget()
		{
			UnitAIController controller = CreateAi("AI64_CombatIso");
			UseOfForceLevel roe = controller.CurrentUseOfForceLevel;
			try
			{
				AssertAccept(controller, Cmd.Attack(P(8f)), UnitAIState.Attack);
				AssertAccept(controller, Cmd.Defense(P(1f)), UnitAIState.Defense);
				AssertAccept(controller, Cmd.Search(P(4f)), UnitAIState.Search);
				AssertAccept(controller, Cmd.Flee(P(-4f)), UnitAIState.Flee);
				Assert.AreEqual(roe, controller.CurrentUseOfForceLevel);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
				Assert.IsNull(controller.CurrentEngageTarget);
				Assert.IsNull(controller.CurrentContext.TargetEntity);
				Assert.IsNull(controller.GetComponent<TargetSelector>());
				Assert.IsFalse(controller.HasHostileVisible);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Command_WalksOnlyThroughMoveRecorder()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder("AI64_NavIso");
			try
			{
				AssertAccept(controller, Cmd.Attack(P(14f)), UnitAIState.Attack);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitNavigationReason.Attack, recorder.Reason);
				AssertAccept(controller, Cmd.Defense(P(0f)), UnitAIState.Defense);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.AreEqual(UnitNavigationReason.None, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region Helpers
		private static class Cmd
		{
			public static TacticalCommand Attack(Vector3 _p)
			{
				return TacticalCommand.Attack(_p, null, TacticalCommandSource.Game);
			}

			public static TacticalCommand Defense(Vector3 _p)
			{
				return TacticalCommand.Defense(_p, TacticalCommandSource.Game);
			}

			public static TacticalCommand Search(Vector3 _p)
			{
				return TacticalCommand.Search(_p, TacticalCommandSource.Game);
			}

			public static TacticalCommand Retreat(Vector3 _p)
			{
				return TacticalCommand.Retreat(_p, TacticalCommandSource.Game);
			}

			public static TacticalCommand Flee(Vector3 _p)
			{
				return TacticalCommand.Flee(_p, TacticalCommandSource.Game);
			}

			public static TacticalCommand Cancel()
			{
				return TacticalCommand.Cancel(TacticalCommandSource.Game);
			}
		}

		private static Vector3 P(float _x, float _z = 0f)
		{
			return new Vector3(_x, 0f, _z);
		}

		private static void Enter(UnitAIController _controller, UnitAIState _state)
		{
			GameCommandService.Issue(_controller, Cmd.Cancel());
			switch (_state)
			{
				case UnitAIState.Defense:
					GameCommandService.Issue(_controller, Cmd.Defense(P(1f)));
					break;
				case UnitAIState.Attack:
					GameCommandService.Issue(_controller, Cmd.Attack(P(2f)));
					break;
				case UnitAIState.Search:
					GameCommandService.Issue(_controller, Cmd.Search(P(3f)));
					break;
				case UnitAIState.Retreat:
					GameCommandService.Issue(_controller, Cmd.Defense(P(1f)));
					GameCommandService.Issue(_controller, Cmd.Retreat(P(-2f)));
					break;
				case UnitAIState.Flee:
					GameCommandService.Issue(_controller, Cmd.Flee(P(-4f)));
					break;
			}

			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static void AssertAccept(UnitAIController _controller, TacticalCommand _command, UnitAIState _state)
		{
			GameCommandResult result = GameCommandService.Issue(_controller, in _command);
			Assert.IsTrue(result.Accepted, _command.Type + " -> " + _state);
			Assert.AreEqual(GameCommandRejectReason.None, result.Reason);
			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static void AssertReject(
			UnitAIController _controller,
			TacticalCommand _command,
			GameCommandRejectReason _reason,
			UnitAIState _stay)
		{
			GameCommandResult result = GameCommandService.Issue(_controller, in _command);
			Assert.IsFalse(result.Accepted, _command.Type.ToString());
			Assert.AreEqual(_reason, result.Reason);
			Assert.AreEqual(_stay, _controller.CurrentState);
		}

		private static GameCommandInput CreateInput()
		{
			var go = new GameObject("AI64_GameCommandInput");
			return go.AddComponent<GameCommandInput>();
		}

		private static UnitAIController CreateAi(string _name, UnitTeamId _team = UnitTeamId.Player)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(_team);
			go.AddComponent<UnitHealth>();
			return go.AddComponent<UnitAIController>();
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder) CreateWithRecorder(string _name)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(UnitTeamId.Player);
			go.AddComponent<UnitHealth>();
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			return (controller, recorder);
		}

		private static UnitTeam CreateEnemy(string _name, bool _dead, bool _withAi)
		{
			var go = new GameObject(_name);
			UnitTeam team = go.AddComponent<UnitTeam>();
			team.SetTeam(UnitTeamId.Enemy);
			UnitHealth health = go.AddComponent<UnitHealth>();
			if (_dead)
				health.EnterDead();
			if (_withAi)
				go.AddComponent<UnitAIController>();
			return team;
		}

		private static StubCommandReceiver CreateStub(string _name)
		{
			var go = new GameObject(_name);
			return go.AddComponent<StubCommandReceiver>();
		}

		private static Component[] ToComponents(List<UnitTeam> _teams)
		{
			var list = new Component[_teams.Count];
			for (int i = 0; i < _teams.Count; i++)
				list[i] = _teams[i];
			return list;
		}

		private static int CountNamed(List<Component> _list, string _prefix)
		{
			int n = 0;
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && _list[i].gameObject.name.StartsWith(_prefix))
					n++;
			}

			return n;
		}

		private static bool ContainsName(List<Component> _list, string _name)
		{
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && _list[i].gameObject.name == _name)
					return true;
			}

			return false;
		}

		private static int CountDeadNamed(List<Component> _list, string _prefix)
		{
			int n = 0;
			for (int i = 0; i < _list.Count; i++)
			{
				Component c = _list[i];
				if (c == null || !c.gameObject.name.StartsWith(_prefix))
					continue;
				if (c.TryGetComponent(out UnitHealth health) && health.IsDead)
					n++;
			}

			return n;
		}

		private static void AssertNamedState(string _prefix, UnitAIState _state, int _expected)
		{
			UnitAIController[] all = Object.FindObjectsByType<UnitAIController>(FindObjectsInactive.Exclude);
			int n = 0;
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] == null || !all[i].gameObject.name.StartsWith(_prefix))
					continue;
				Assert.AreEqual(_state, all[i].CurrentState, all[i].name);
				n++;
			}

			Assert.AreEqual(_expected, n);
		}

		private static void DestroyNamed(string _prefix)
		{
			UnitTeam[] teams = Object.FindObjectsByType<UnitTeam>(FindObjectsInactive.Include);
			for (int i = 0; i < teams.Length; i++)
			{
				if (teams[i] != null && teams[i].gameObject.name.StartsWith(_prefix))
					Object.DestroyImmediate(teams[i].gameObject);
			}
		}

		private static void Destroy(GameCommandInput _input)
		{
			if (_input != null)
				Object.DestroyImmediate(_input.gameObject);
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				Object.DestroyImmediate(_controller.gameObject);
		}

		private static void Destroy(StubCommandReceiver _stub)
		{
			if (_stub != null)
				Object.DestroyImmediate(_stub.gameObject);
		}

		private static void DestroyGo(Component _component)
		{
			if (_component != null)
				Object.DestroyImmediate(_component.gameObject);
		}

		private sealed class StubCommandReceiver : MonoBehaviour, ITacticalCommandReceiver
		{
			public TacticalCommandType LastType;
			public Vector3 LastPosition;
			public TacticalCommandSource LastSource;

			public TacticalCommandResult IssueCommand(in TacticalCommand _command)
			{
				LastType = _command.Type;
				LastPosition = _command.Position;
				LastSource = _command.Source;
				return TacticalCommandResult.Ok();
			}
		}
		#endregion
	}
}
