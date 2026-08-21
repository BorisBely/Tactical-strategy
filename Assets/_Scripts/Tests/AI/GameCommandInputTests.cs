using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class GameCommandInputTests
	{
		[Test]
		public void IssueMany_EmptyList_ReturnsZero()
		{
			TacticalCommand command = TacticalCommand.Attack(new Vector3(3f, 0f, 0f), null, TacticalCommandSource.Game);
			Assert.AreEqual(0, GameCommandService.IssueMany(null, in command));
			Assert.AreEqual(0, GameCommandService.IssueMany(new List<Component>(), in command));
		}

		[Test]
		public void ConfirmPoint_OneRecipient_IssuesOnceWithSamePoint()
		{
			GameCommandInput input = CreateInput();
			StubCommandReceiver stub = CreateStub("AI63_One");
			Vector3 point = new Vector3(6f, 0f, 2f);
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				int accepted = input.ConfirmPoint(point, new Component[] { stub });
				Assert.AreEqual(1, accepted);
				Assert.AreEqual(1, stub.IssueCount);
				Assert.AreEqual(TacticalCommandType.Attack, stub.LastType);
				Assert.AreEqual(point, stub.LastPosition);
				Assert.AreEqual(TacticalCommandSource.Game, stub.LastSource);
				Assert.AreEqual(GameCommandInputMode.Normal, input.Mode);
			}
			finally
			{
				Destroy(stub);
				Destroy(input);
			}
		}

		[Test]
		public void ConfirmPoint_ThreeRecipients_IssuesThreeTimesSamePoint()
		{
			GameCommandInput input = CreateInput();
			StubCommandReceiver a = CreateStub("AI63_A");
			StubCommandReceiver b = CreateStub("AI63_B");
			StubCommandReceiver c = CreateStub("AI63_C");
			Vector3 point = new Vector3(9f, 0f, 1f);
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				int accepted = input.ConfirmPoint(point, new Component[] { a, b, c });
				Assert.AreEqual(3, accepted);
				Assert.AreEqual(1, a.IssueCount);
				Assert.AreEqual(1, b.IssueCount);
				Assert.AreEqual(1, c.IssueCount);
				Assert.AreEqual(point, a.LastPosition);
				Assert.AreEqual(point, b.LastPosition);
				Assert.AreEqual(point, c.LastPosition);
			}
			finally
			{
				Destroy(a);
				Destroy(b);
				Destroy(c);
				Destroy(input);
			}
		}

		[Test]
		public void ConfirmPoint_PlayerEmptySelection_SkipsNoRecipients()
		{
			GameCommandInput input = CreateInput();
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				int accepted = input.ConfirmPoint(new Vector3(4f, 0f, 0f));
				Assert.AreEqual(0, accepted);
				Assert.AreEqual("NoRecipients", input.LastSkipReason);
				Assert.AreEqual(GameCommandInputMode.Normal, input.Mode);
			}
			finally
			{
				Destroy(input);
			}
		}

		[Test]
		public void EnemyDebug_SkipsDead_KeepsThreeLiving()
		{
			UnitTeam live0 = CreateEnemy("AI63_E_Live0", false);
			UnitTeam live1 = CreateEnemy("AI63_E_Live1", false);
			UnitTeam live2 = CreateEnemy("AI63_E_Live2", false);
			UnitTeam dead0 = CreateEnemy("AI63_E_Dead0", true);
			UnitTeam dead1 = CreateEnemy("AI63_E_Dead1", true);
			var collected = new List<Component>(8);
			try
			{
				GameCommandRecipientQuery.Collect(GameCommandAudience.EnemyDebug, collected);
				Assert.AreEqual(3, CountNamed(collected, "AI63_E_Live"));
				Assert.AreEqual(0, CountNamed(collected, "AI63_E_Dead"));
			}
			finally
			{
				DestroyGo(live0);
				DestroyGo(live1);
				DestroyGo(live2);
				DestroyGo(dead0);
				DestroyGo(dead1);
			}
		}

		[Test]
		public void EnemyDebug_WithoutAi_AttachesAndAccepts()
		{
			GameCommandInput input = CreateInput();
			UnitTeam enemy = CreateEnemy("AI63_E_Attach", false);
			Vector3 point = new Vector3(5f, 0f, 3f);
			try
			{
				Assert.IsNull(enemy.GetComponent<UnitAIController>());
				input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
				int accepted = input.ConfirmPoint(point, new Component[] { enemy });
				Assert.AreEqual(1, accepted);
				UnitAIController ai = enemy.GetComponent<UnitAIController>();
				Assert.IsNotNull(ai);
				Assert.AreEqual(UnitAIState.Defense, ai.CurrentState);
				Assert.AreEqual(point, ai.CurrentContext.AnchorPosition);
				Assert.IsFalse(ai.DrawSearchHud);
			}
			finally
			{
				DestroyGo(enemy);
				Destroy(input);
			}
		}

		[Test]
		public void PlayerAttack_DoesNotAttachAi()
		{
			GameCommandInput input = CreateInput();
			var go = new GameObject("AI63_P_NoAi");
			go.AddComponent<UnitTeam>().SetTeam(UnitTeamId.Player);
			go.AddComponent<UnitHealth>();
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				int accepted = input.ConfirmPoint(new Vector3(2f, 0f, 0f), new Component[] { go.transform });
				Assert.AreEqual(0, accepted);
				Assert.IsNull(go.GetComponent<UnitAIController>());
			}
			finally
			{
				Object.DestroyImmediate(go);
				Destroy(input);
			}
		}

		[Test]
		public void PlayerAttack_DoesNotTouchEnemy_EnemyDebug_DoesNotTouchPlayerOrNeutral()
		{
			GameCommandInput input = CreateInput();
			UnitAIController player = CreateAi("AI63_P_Iso", UnitTeamId.Player);
			UnitAIController enemy = CreateAi("AI63_E_Iso", UnitTeamId.Enemy);
			UnitAIController neutral = CreateAi("AI63_N_Iso", UnitTeamId.Neutral);
			Vector3 attack = new Vector3(11f, 0f, 0f);
			Vector3 defense = new Vector3(0f, 0f, 8f);
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				Assert.AreEqual(1, input.ConfirmPoint(attack, new Component[] { player }));
				Assert.AreEqual(UnitAIState.Attack, player.CurrentState);
				Assert.AreEqual(UnitAIState.Idle, enemy.CurrentState);
				Assert.AreEqual(UnitAIState.Idle, neutral.CurrentState);

				input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
				Assert.GreaterOrEqual(input.ConfirmPoint(defense), 1);
				Assert.AreEqual(UnitAIState.Attack, player.CurrentState);
				Assert.AreEqual(attack, player.CurrentContext.Destination);
				Assert.AreEqual(UnitAIState.Defense, enemy.CurrentState);
				Assert.AreEqual(defense, enemy.CurrentContext.AnchorPosition);
				Assert.AreEqual(UnitAIState.Idle, neutral.CurrentState);
			}
			finally
			{
				Destroy(player);
				Destroy(enemy);
				Destroy(neutral);
				Destroy(input);
			}
		}

		[Test]
		public void PendingWithoutPoint_IssuesNothing()
		{
			GameCommandInput input = CreateInput();
			StubCommandReceiver stub = CreateStub("AI63_Pending");
			try
			{
				input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
				Assert.IsTrue(TacticalDebugOrderSession.IsCommandPointPending);
				Assert.IsFalse(TacticalDebugOrderSession.IsPicking);
				Assert.AreEqual(0, stub.IssueCount);
				Assert.AreEqual(0, input.CancelPending());
				Assert.AreEqual(0, stub.IssueCount);
				Assert.IsFalse(TacticalDebugOrderSession.IsCommandPointPending);
				Assert.IsFalse(TacticalDebugOrderSession.IsPicking);
				Assert.AreEqual(GameCommandInputMode.Normal, input.Mode);
			}
			finally
			{
				Destroy(stub);
				Destroy(input);
			}
		}

		[Test]
		public void IssueMany_DoesNotFireOrChangeRoe()
		{
			UnitAIController a = CreateAi("AI63_RoeA", UnitTeamId.Player);
			UnitAIController b = CreateAi("AI63_RoeB", UnitTeamId.Player);
			UseOfForceLevel roeA = a.CurrentUseOfForceLevel;
			UseOfForceLevel roeB = b.CurrentUseOfForceLevel;
			TacticalCommand command = TacticalCommand.Attack(new Vector3(8f, 0f, 0f), null, TacticalCommandSource.Game);
			try
			{
				int accepted = GameCommandService.IssueMany(new Component[] { a, b }, in command);
				Assert.AreEqual(2, accepted);
				Assert.AreEqual(roeA, a.CurrentUseOfForceLevel);
				Assert.AreEqual(roeB, b.CurrentUseOfForceLevel);
				Assert.AreEqual(CombatIntent.Hold, a.CurrentCombatIntent);
				Assert.AreEqual(CombatIntent.Hold, b.CurrentCombatIntent);
				Assert.IsFalse(a.HasHostileVisible);
				Assert.IsNull(a.CurrentEngageTarget);
				Assert.IsNull(a.GetComponent<TargetSelector>());
				Assert.AreEqual(UnitAIState.Attack, a.CurrentState);
				Assert.AreEqual(UnitAIState.Attack, b.CurrentState);
			}
			finally
			{
				Destroy(a);
				Destroy(b);
			}
		}

		private static GameCommandInput CreateInput()
		{
			var go = new GameObject("AI63_GameCommandInput");
			return go.AddComponent<GameCommandInput>();
		}

		private static StubCommandReceiver CreateStub(string _name)
		{
			var go = new GameObject(_name);
			return go.AddComponent<StubCommandReceiver>();
		}

		private static UnitTeam CreateEnemy(string _name, bool _dead)
		{
			var go = new GameObject(_name);
			UnitTeam team = go.AddComponent<UnitTeam>();
			team.SetTeam(UnitTeamId.Enemy);
			UnitHealth health = go.AddComponent<UnitHealth>();
			if (_dead)
				health.EnterDead();
			return team;
		}

		private static UnitAIController CreateAi(string _name, UnitTeamId _team)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(_team);
			go.AddComponent<UnitHealth>();
			return go.AddComponent<UnitAIController>();
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

		private static void Destroy(GameCommandInput _input)
		{
			if (_input != null)
				Object.DestroyImmediate(_input.gameObject);
		}

		private static void Destroy(StubCommandReceiver _stub)
		{
			if (_stub != null)
				Object.DestroyImmediate(_stub.gameObject);
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				Object.DestroyImmediate(_controller.gameObject);
		}

		private static void DestroyGo(Component _component)
		{
			if (_component != null)
				Object.DestroyImmediate(_component.gameObject);
		}

		private sealed class StubCommandReceiver : MonoBehaviour, ITacticalCommandReceiver
		{
			public int IssueCount;
			public TacticalCommandType LastType;
			public Vector3 LastPosition;
			public TacticalCommandSource LastSource;

			public TacticalCommandResult IssueCommand(in TacticalCommand _command)
			{
				IssueCount++;
				LastType = _command.Type;
				LastPosition = _command.Position;
				LastSource = _command.Source;
				return TacticalCommandResult.Ok();
			}
		}
	}
}
