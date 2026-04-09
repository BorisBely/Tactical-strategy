using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RTS-обёртка над существующими компонентами юнита:
/// регистрация в списке selectable-юнитов, групповые команды и состояние выделения.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsUnitMember : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private GameObject m_SelectionVisualRoot;
	[SerializeField] private bool m_DisableDirectInputForRts = true;
	[Header("RTS Reaction")]
	[SerializeField, Min(0f)] private float m_ReactionDelayMinSeconds = 0.08f;
	[SerializeField, Min(0f)] private float m_ReactionDelayMaxSeconds = 0.28f;
	[SerializeField] private float m_RuntimeReactionDelaySeconds;
	[SerializeField, Min(0f)] private float m_MoveStartDelayMinSeconds = 0f;
	[SerializeField, Min(0f)] private float m_MoveStartDelayMaxSeconds = 0f;
	[SerializeField] private float m_RuntimeMoveStartDelaySeconds;
	[Header("Animator Variation")]
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMin = 0.97f;
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMax = 1.03f;
	[SerializeField] private float m_RuntimeMoveAnimatorSpeed = 1f;
	[SerializeField] private bool m_IsSelected;

	private static readonly List<RtsUnitMember> s_Instances = new List<RtsUnitMember>(128);
	private Coroutine m_PendingCommandCoroutine;
	private int m_PendingCommandVersion;
	#endregion

	#region Public Properties
	public static IReadOnlyList<RtsUnitMember> Instances => s_Instances;
	public CharacterInventory CharacterInventory => m_CharacterInventory;
	public bool IsSelected => m_IsSelected;
	public bool IsPlayerSelectable => m_Team != null && m_Team.Team == UnitTeamId.Player;
	public bool WantsReady => m_ReadyHands != null && m_ReadyHands.WantsReady;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_SelectionCollider == null)
			m_SelectionCollider = GetComponent<Collider>();

		m_RuntimeReactionDelaySeconds = UnityEngine.Random.Range(
			Mathf.Min(m_ReactionDelayMinSeconds, m_ReactionDelayMaxSeconds),
			Mathf.Max(m_ReactionDelayMinSeconds, m_ReactionDelayMaxSeconds));
		m_RuntimeMoveStartDelaySeconds = UnityEngine.Random.Range(
			Mathf.Min(m_MoveStartDelayMinSeconds, m_MoveStartDelayMaxSeconds),
			Mathf.Max(m_MoveStartDelayMinSeconds, m_MoveStartDelayMaxSeconds));
		m_RuntimeMoveAnimatorSpeed = UnityEngine.Random.Range(
			Mathf.Min(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax),
			Mathf.Max(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax));

		if (m_DisableDirectInputForRts)
			ApplyDirectInputState(false);
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		SetSelected(false);
		ApplyAnimatorSpeedVariation();
	}

	private void OnDisable()
	{
		CancelPendingCommand();
		ResetAnimatorSpeed();
		s_Instances.Remove(this);
		SetSelected(false);
	}

	private void Update()
	{
		ApplyAnimatorSpeedVariation();
	}
	#endregion

	#region Public Methods
	public void SetSelected(bool _selected)
	{
		m_IsSelected = _selected;
		if (m_SelectionVisualRoot != null)
			m_SelectionVisualRoot.SetActive(_selected);
	}

	public void IssueMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_ClickToMove != null)
			{
				m_ClickToMove.IssueNavOrder(_worldPosition, _moveTier);
				return;
			}

			if (m_LocomotionDriver != null)
			{
				UnitNavLocomotionDriver.MoveTier navTier = _moveTier switch
				{
					UnitClickToMove.MoveTier.Run => UnitNavLocomotionDriver.MoveTier.Run,
					UnitClickToMove.MoveTier.Sprint => UnitNavLocomotionDriver.MoveTier.Sprint,
					_ => UnitNavLocomotionDriver.MoveTier.Walk
				};
				m_LocomotionDriver.IssueNavOrder(_worldPosition, navTier);
			}
		}, m_RuntimeReactionDelaySeconds + m_RuntimeMoveStartDelaySeconds);
	}

	public void SetReadyWanted(bool _ready)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_ReadyHands != null)
				m_ReadyHands.SetReadyWanted(_ready);
		});
	}

	public void RequestStance(LocomotionStance _stance)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_Stance != null)
				m_Stance.RequestStance(_stance);
		});
	}

	public void HardStop()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_ClickToMove != null)
			{
				m_ClickToMove.HardStop();
				return;
			}

			if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();
		});
	}

	public bool TryGetCurrentStance(out LocomotionStance _stance)
	{
		if (m_Stance == null)
		{
			_stance = LocomotionStance.Standing;
			return false;
		}

		_stance = m_Stance.CurrentStance;
		return true;
	}

	public bool TryGetSelectionBounds(out Bounds _bounds)
	{
		if (m_SelectionCollider != null)
		{
			_bounds = m_SelectionCollider.bounds;
			return true;
		}

		_bounds = new Bounds(transform.position, Vector3.one);
		return true;
	}

	public void ApplyDirectInputState(bool _enabled)
	{
		if (m_ClickToMove != null)
			m_ClickToMove.SetDirectInputEnabled(_enabled);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(_enabled);
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(_enabled);
	}
	#endregion

	#region Private Methods
	private void ScheduleRtsCommand(Action _command)
	{
		ScheduleRtsCommand(_command, m_RuntimeReactionDelaySeconds);
	}

	private void ScheduleRtsCommand(Action _command, float _delaySeconds)
	{
		if (_command == null)
			return;

		m_PendingCommandVersion++;
		int commandVersion = m_PendingCommandVersion;

		if (m_PendingCommandCoroutine != null)
			StopCoroutine(m_PendingCommandCoroutine);

		if (_delaySeconds <= 0f)
		{
			m_PendingCommandCoroutine = null;
			_command();
			return;
		}

		m_PendingCommandCoroutine = StartCoroutine(ExecuteRtsCommandAfterDelay(commandVersion, _delaySeconds, _command));
	}

	private IEnumerator ExecuteRtsCommandAfterDelay(int _commandVersion, float _delaySeconds, Action _command)
	{
		yield return new WaitForSeconds(_delaySeconds);

		if (_commandVersion != m_PendingCommandVersion)
			yield break;

		m_PendingCommandCoroutine = null;
		_command?.Invoke();
	}

	private void CancelPendingCommand()
	{
		m_PendingCommandVersion++;
		if (m_PendingCommandCoroutine == null)
			return;

		StopCoroutine(m_PendingCommandCoroutine);
		m_PendingCommandCoroutine = null;
	}

	private void ApplyAnimatorSpeedVariation()
	{
		if (m_Animator == null)
			return;

		m_Animator.speed = IsExecutingMoveOrder() ? m_RuntimeMoveAnimatorSpeed : 1f;
	}

	private bool IsExecutingMoveOrder()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.HasMoveIntent;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;

		return false;
	}

	private void ResetAnimatorSpeed()
	{
		if (m_Animator != null)
			m_Animator.speed = 1f;
	}
	#endregion
}
