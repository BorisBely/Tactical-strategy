using UnityEngine;

/// <summary>
/// Синхронизирует int <c>WeaponMode</c> на <see cref="Animator"/> с фактически экипированным предметом.
/// Weapon readiness is controlled by a separate bool parameter <c>WeaponReady</c> in <see cref="UnitWeaponReadyHandsLayer"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorWeaponMode : MonoBehaviour
{
	public const string ParamWeaponMode = "WeaponMode";
	public const string ParamStance = "Stance";
	public const string ParamWeaponReady = "WeaponReady";
	/// <summary>
	/// Body idle style (independent of <see cref="ParamWeaponReady"/> fire/locomotion).
	/// 0 = Aim: Stand_Aim_Idle / RifleCrouch_Idle_Ready (LowReady, HighReady, PreAim, PointAim, Aiming).
	/// 1 = Relaxed: Stand_Relaxed_Idle / RifleCrouch_Idle (NotReady, NotReadyPatrol, HipFire).
	/// Vehicle seat uses VehicleReady; tuner HipFire in a vehicle uses Seat_Aim.
	/// </summary>
	public const string ParamWeaponStandIdle = "WeaponStandIdle";

	/// <summary>Base-layer idle selection (independent of <see cref="ParamWeaponReady"/> locomotion/reload).</summary>
	public enum WeaponStandIdleStyle
	{
		AimIdle = 0,
		RelaxedIdle = 1,
	}

	/// <summary>Имена под-машин на базовом слое контроллера (<see cref="Animator.CrossFadeInFixedTime"/> требует полный путь: слой.подмашина.стейт).</summary>
	public const string BaseLayerAnimatorName = "Base Layer";
	public const string SubStateMachineUnarmed = "Locomotion_Unarmed";
	public const string SubStateMachineRifleStanding = "Rifle_Standing";
	public const string SubStateMachineRifleCrouch = "Rifle_Crouch";

	private static readonly int s_WeaponMode = Animator.StringToHash(ParamWeaponMode);
	private static readonly int s_Stance = Animator.StringToHash(ParamStance);
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);
	private static readonly int s_WeaponReady = Animator.StringToHash(ParamWeaponReady);
	private static readonly int s_WeaponStandIdle = Animator.StringToHash(ParamWeaponStandIdle);

	/// <summary>Согласовано с порогами переходов в контроллере (idle NavSpeed &lt; 0.05, движение &gt; 0.055).</summary>
	private const float c_MoveNavSpeedAnimatorThreshold = 0.055f;
	private const float c_HipFireWalkCrossFadeMinSeconds = 0.18f;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;

	[Header("Плавность")]
	[SerializeField, Min(0.02f)] private float m_WeaponModeCrossFadeSeconds = 0.22f;
	[SerializeField, Min(0.02f)] private float m_RelaxedReadyWalkCrossFadeSeconds = 0.18f;

	[Header("Debug")]
	[SerializeField] private bool m_LogCrouchTransitions = true;

	private static readonly int s_CrouchWalkFLoop = Animator.StringToHash("CrouchWalk_F_Loop");
	private static readonly int s_RifleCrouchMove = Animator.StringToHash("RifleCrouch_Move");
	private static readonly int s_RifleCrouchIdle = Animator.StringToHash("RifleCrouch_Idle");
	private static readonly int s_RifleCrouchIdleReady = Animator.StringToHash("RifleCrouch_Idle_Ready");

	private int m_LastWeaponModeValue = -1;
	private int m_LastSnappedStance = int.MinValue;
	private RtsUnitMember m_LogMember;
	private bool m_WasInTransition;
	private int m_LoggedCurrentHash;
	private int m_LoggedNextHash;
	private string m_PendingXfadeReason;
	private string m_PendingXfadeTarget;
	private int m_PendingXfadeFrame = -1;

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_StanceSource == null)
			m_StanceSource = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		m_LogMember = GetComponent<RtsUnitMember>();
	}

	private void OnEnable()
	{
		m_LastWeaponModeValue = -1;
		m_LastSnappedStance = int.MinValue;
		m_WasInTransition = false;
		m_LoggedCurrentHash = 0;
		m_LoggedNextHash = 0;
		m_PendingXfadeReason = null;
		PushWeaponModeIfChanged();
	}

	private void Update()
	{
		PushWeaponModeIfChanged();
		TickStanceLocomotionSnap();
		TickRelaxedReadyWalkSnap();
	}

	private void LateUpdate()
	{
		TickCrouchTransitionLog();
	}

	/// <summary>
	/// Rebuild the base layer for the current <c>WeaponMode</c> (and in high ready — without requiring idle).
	/// При активном движении по <c>NavSpeed</c> сразу переходит в locomotion-стейт нужной ветки.
	/// </summary>
	public void ReplayLocomotionIdleCrossfade()
	{
		SnapBaseLayerToWeaponBranch("replay");
	}

	private void PushWeaponModeIfChanged()
	{
		if (m_Animator == null)
			return;

		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		int value = ComputeEffectiveWeaponMode(current);

		if (value != m_LastWeaponModeValue)
		{
			m_LastWeaponModeValue = value;
			m_Animator.SetInteger(s_WeaponMode, value);
			SnapBaseLayerToWeaponBranch("weapon-mode");
		}
	}

	private int ComputeEffectiveWeaponMode(ItemDefinition current)
	{
		if (current == null || !current.IsEquipment)
			return (int)LocomotionWeaponMode.Unarmed;

		if (current.EquipmentKind != EquipmentKind.Weapon)
			return (int)LocomotionWeaponMode.Unarmed;

		return current.WeaponType == WeaponType.Secondary
			? (int)LocomotionWeaponMode.Pistol
			: (int)LocomotionWeaponMode.Rifle;
	}

	private void SnapBaseLayerToWeaponBranch(string _reason)
	{
		if (m_Animator == null || !m_Animator.isActiveAndEnabled)
			return;
		if (ShouldFreezeTunerWalkAnimator())
			return;

		int stance = ResolveLocomotionStance();
		if (!LocomotionProneFeature.Enabled && stance == (int)LocomotionStance.Prone)
			stance = (int)LocomotionStance.Standing;
		m_LastSnappedStance = stance;

		float navSpeed = m_Animator.GetFloat(s_NavSpeed);
		bool weaponReady = m_Animator.GetBool(s_WeaponReady);
		bool useAimStandIdle = m_Animator.GetInteger(s_WeaponStandIdle) == (int)WeaponStandIdleStyle.AimIdle;
		bool useLocomotion = navSpeed >= c_MoveNavSpeedAnimatorThreshold || IsTunerHipFireWalkBody();
		string qualifiedState = useLocomotion
			? ResolveBaseLayerLocomotionQualified(m_LastWeaponModeValue, stance, useAimStandIdle)
			: ResolveBaseLayerIdleQualified(m_LastWeaponModeValue, stance, weaponReady, useAimStandIdle);

		TryCrossFadeLayer0(qualifiedState, m_WeaponModeCrossFadeSeconds, _reason);
	}

	private void TickStanceLocomotionSnap()
	{
		if (m_Animator == null || !m_Animator.isActiveAndEnabled)
			return;

		int stance = ResolveLocomotionStance();
		if (!LocomotionProneFeature.Enabled && stance == (int)LocomotionStance.Prone)
			stance = (int)LocomotionStance.Standing;
		if (stance == m_LastSnappedStance)
			return;

		m_LastSnappedStance = stance;
		SnapBaseLayerToWeaponBranch("stance-change");
	}

	private void TickRelaxedReadyWalkSnap()
	{
		if (m_Animator == null || !m_Animator.isActiveAndEnabled)
			return;
		if (ShouldFreezeTunerWalkAnimator())
			return;
		if (!IsRifleFamilyMode())
			return;
		if (!ShouldSnapHipFireWalkClips())
			return;

		int stance = ResolveLocomotionStance();
		string walk = ResolveBaseLayerLocomotionQualified(m_LastWeaponModeValue, stance, false);
		float duration = Mathf.Max(c_HipFireWalkCrossFadeMinSeconds, m_RelaxedReadyWalkCrossFadeSeconds);
		TryCrossFadeLayer0(walk, duration, "hip-walk-snap");
	}

	private bool IsRifleFamilyMode()
	{
		return m_LastWeaponModeValue == (int)LocomotionWeaponMode.Rifle ||
		       m_LastWeaponModeValue == (int)LocomotionWeaponMode.Pistol;
	}

	private bool ShouldSnapHipFireWalkClips()
	{
		if (IsTunerHipFireWalkBody())
			return true;
		if (m_ReadyHands != null && m_ReadyHands.EffectivePoseState.IsHipFireHold()
		    && m_ReadyHands.EffectivePoseState != WeaponPoseState.HipFire)
			return true;
		return IsHipFireWalkActive();
	}

	private bool IsTunerHipFireWalkBody()
	{
		return m_RuntimeTuner != null
		       && m_RuntimeTuner.IsTuningActive
		       && (m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk
		           || m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk);
	}

	private bool ShouldFreezeTunerWalkAnimator() =>
		m_RuntimeTuner != null && m_RuntimeTuner.ShouldFreezeWalkAnimator;

	private bool IsHipFireWalkActive()
	{
		if (!IsRifleFamilyMode())
			return false;
		if (m_Animator.GetFloat(s_NavSpeed) < c_MoveNavSpeedAnimatorThreshold)
			return false;
		if (!m_Animator.GetBool(s_WeaponReady))
			return false;
		if (m_Animator.GetInteger(s_WeaponStandIdle) != (int)WeaponStandIdleStyle.RelaxedIdle)
			return false;

		int stance = ResolveLocomotionStance();
		if (stance == (int)LocomotionStance.Prone)
			return false;
		if (stance == (int)LocomotionStance.Crouch)
			return true;
		return m_Animator.GetInteger(s_LocomotionTier) == (int)UnitClickToMove.MoveTier.Walk;
	}

	private int ResolveLocomotionStance()
	{
		if (m_StanceSource != null)
			return (int)m_StanceSource.CurrentStance;
		return m_Animator.GetInteger(s_Stance);
	}

	private void TryCrossFadeLayer0(string _qualifiedStateFullPath, float _duration, string _reason)
	{
		int hash = Animator.StringToHash(_qualifiedStateFullPath);
		if (!m_Animator.HasState(0, hash))
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.LogWarning(
				$"{nameof(UnitAnimatorWeaponMode)}: нет стейта «{_qualifiedStateFullPath}» в контроллере «{(m_Animator.runtimeAnimatorController != null ? m_Animator.runtimeAnimatorController.name : "NULL")}» на «{gameObject.name}». Проверьте имена в Animator и параметр Weapon/Stance.",
				this);
#endif
			return;
		}

		int leafHash = Animator.StringToHash(LeafName(_qualifiedStateFullPath));
		if (ShouldSkipLayerCrossFade(0, hash, leafHash))
			return;

		AnimatorStateInfo current = m_Animator.GetCurrentAnimatorStateInfo(0);
		float normalizedTime = 0f;
		if (m_Animator.GetFloat(s_NavSpeed) >= c_MoveNavSpeedAnimatorThreshold
		    && current.shortNameHash == leafHash)
			normalizedTime = current.normalizedTime;
		m_PendingXfadeReason = _reason;
		m_PendingXfadeTarget = LeafName(_qualifiedStateFullPath);
		m_PendingXfadeFrame = Time.frameCount;
		if (IsCrouchWatchLeaf(current.shortNameHash) || IsCrouchWatchLeaf(leafHash))
		{
			LogCrouchXfade(
				"code",
				$"reason={_reason} {LeafLabel(current.shortNameHash)} → {m_PendingXfadeTarget} dur={Mathf.Max(0.02f, _duration):F2} nrm={normalizedTime:F2}");
		}
		m_Animator.CrossFadeInFixedTime(_qualifiedStateFullPath, Mathf.Max(0.02f, _duration), 0, normalizedTime);
	}

	private bool ShouldSkipLayerCrossFade(int _layer, int _qualifiedHash, int _leafHash)
	{
		AnimatorStateInfo current = m_Animator.GetCurrentAnimatorStateInfo(_layer);
		if (IsLayerState(current, _qualifiedHash, _leafHash))
			return true;
		if (!m_Animator.IsInTransition(_layer))
			return false;
		return true;
	}

	private void TickCrouchTransitionLog()
	{
		if (!m_LogCrouchTransitions || m_Animator == null || !m_Animator.isActiveAndEnabled)
			return;
		if (!UnitFacingDebugLog.ShouldLog(m_LogMember))
			return;

		bool inTransition = m_Animator.IsInTransition(0);
		AnimatorStateInfo current = m_Animator.GetCurrentAnimatorStateInfo(0);
		AnimatorStateInfo next = inTransition ? m_Animator.GetNextAnimatorStateInfo(0) : current;
		bool watch = IsCrouchWatchLeaf(current.shortNameHash) || IsCrouchWatchLeaf(next.shortNameHash);
		if (!watch && m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Crouch)
		{
			m_WasInTransition = inTransition;
			m_LoggedCurrentHash = current.shortNameHash;
			m_LoggedNextHash = inTransition ? next.shortNameHash : 0;
			return;
		}

		bool destChanged = next.shortNameHash != m_LoggedNextHash;
		bool srcChanged = current.shortNameHash != m_LoggedCurrentHash;
		if (inTransition && (!m_WasInTransition || destChanged || srcChanged))
		{
			AnimatorTransitionInfo transition = m_Animator.GetAnimatorTransitionInfo(0);
			bool codeSameFrame = m_PendingXfadeFrame == Time.frameCount;
			string kind = transition.anyState
				? (codeSameFrame ? "anyState-or-CrossFade" : "graph-AnyState")
				: "graph-local";
			string transName = transition.IsName("HipFire_CrouchWalk")
				? "HipFire_CrouchWalk"
				: transition.IsName("HipFire_CrouchWalk_Pistol")
					? "HipFire_CrouchWalk_Pistol"
					: transition.IsName("HipFire_MoveToCrouchWalk")
						? "HipFire_MoveToCrouchWalk"
						: "-";
			LogCrouchXfade(
				"graph",
				$"kind={kind} name={transName} anyState={(transition.anyState ? 1 : 0)} dur={transition.duration:F2} " +
				$"{LeafLabel(current.shortNameHash)} → {LeafLabel(next.shortNameHash)} " +
				$"codeSameFrame={(codeSameFrame ? 1 : 0)} pending={m_PendingXfadeReason ?? "-"}→{m_PendingXfadeTarget ?? "-"}");
		}

		if (!inTransition && srcChanged && IsCrouchWatchLeaf(current.shortNameHash))
			LogCrouchXfade("enter", LeafLabel(current.shortNameHash));

		m_WasInTransition = inTransition;
		m_LoggedCurrentHash = current.shortNameHash;
		m_LoggedNextHash = inTransition ? next.shortNameHash : 0;
	}

	private void LogCrouchXfade(string _src, string _detail)
	{
		if (!m_LogCrouchTransitions)
			return;
		if (!UnitFacingDebugLog.ShouldLog(m_LogMember))
			return;

		int stanceAnim = m_Animator.GetInteger(s_Stance);
		int stanceSrc = ResolveLocomotionStance();
		Debug.Log(
			$"[CrouchXfade] unit={name} src={_src} {_detail} " +
			$"ready={(m_Animator.GetBool(s_WeaponReady) ? 1 : 0)} " +
			$"standIdle={m_Animator.GetInteger(s_WeaponStandIdle)} " +
			$"stanceAnim={stanceAnim} stanceSrc={stanceSrc} " +
			$"nav={m_Animator.GetFloat(s_NavSpeed):F3} " +
			$"mode={m_Animator.GetInteger(s_WeaponMode)} " +
			$"tier={m_Animator.GetInteger(s_LocomotionTier)} " +
			$"frame={Time.frameCount}",
			this);
	}

	private static bool IsCrouchWatchLeaf(int _shortNameHash)
	{
		return _shortNameHash == s_CrouchWalkFLoop
		       || _shortNameHash == s_RifleCrouchMove
		       || _shortNameHash == s_RifleCrouchIdle
		       || _shortNameHash == s_RifleCrouchIdleReady;
	}

	private static string LeafLabel(int _shortNameHash)
	{
		if (_shortNameHash == s_CrouchWalkFLoop)
			return "CrouchWalk_F_Loop";
		if (_shortNameHash == s_RifleCrouchMove)
			return "RifleCrouch_Move";
		if (_shortNameHash == s_RifleCrouchIdle)
			return "RifleCrouch_Idle";
		if (_shortNameHash == s_RifleCrouchIdleReady)
			return "RifleCrouch_Idle_Ready";
		return $"hash:{_shortNameHash}";
	}

	private static string LeafName(string _qualified)
	{
		int dot = _qualified.LastIndexOf('.');
		return dot >= 0 ? _qualified.Substring(dot + 1) : _qualified;
	}

	private static bool IsLayerState(AnimatorStateInfo _info, int _qualifiedHash, int _leafHash)
	{
		return _info.fullPathHash == _qualifiedHash
		       || _info.shortNameHash == _leafHash
		       || _info.fullPathHash == _leafHash;
	}

	private static string QualifyBaseLayerPath(string _subMachine, string _leaf) =>
		$"{BaseLayerAnimatorName}.{_subMachine}.{_leaf}";

	private static string ResolveBaseLayerIdleQualified(int _weaponMode, int _stance, bool _weaponReady, bool _useAimStandIdle)
	{
		string targetLeaf;

		if (_weaponMode == (int)LocomotionWeaponMode.Rifle ||
		    _weaponMode == (int)LocomotionWeaponMode.Pistol)
		{
			targetLeaf = _stance switch
			{
				// Same split as standing: HighReady stays on the Aim/Ready clip even though WeaponReady is false.
				(int)LocomotionStance.Crouch => _useAimStandIdle ? "RifleCrouch_Idle_Ready" : "RifleCrouch_Idle",
				(int)LocomotionStance.Prone => _useAimStandIdle ? "Stand_Aim_Idle" : "Stand_Relaxed_Idle",
				_ => _useAimStandIdle ? "Stand_Aim_Idle" : "Stand_Relaxed_Idle"
			};
		}
		else
		{
			targetLeaf = _stance switch
			{
				(int)LocomotionStance.Crouch => "Crouch_Idle",
				_ => "Stand_Relaxed_Idle"
			};
		}

		string subMachine = ResolveBaseLayerSubStateMachine(_weaponMode, targetLeaf);
		return QualifyBaseLayerPath(subMachine, targetLeaf);
	}

	private string ResolveBaseLayerLocomotionQualified(int _weaponMode, int _stance, bool _useAimStandIdle)
	{
		if (_weaponMode == (int)LocomotionWeaponMode.Rifle ||
		    _weaponMode == (int)LocomotionWeaponMode.Pistol)
		{
			bool fireReady = m_Animator.GetBool(s_WeaponReady);
			bool aimHold = _useAimStandIdle || fireReady;
			if (_stance == (int)LocomotionStance.Crouch)
			{
				string crouchLeaf = aimHold
					? "RifleCrouch_Move"
					: "CrouchWalk_F_Loop";
				return QualifyBaseLayerPath(SubStateMachineRifleCrouch, crouchLeaf);
			}

			if (_stance == (int)LocomotionStance.Prone)
				return QualifyBaseLayerPath(SubStateMachineRifleCrouch, "RifleCrouch_Move");

			int tier = m_Animator.GetInteger(s_LocomotionTier);
			string leaf;
			if (tier == (int)UnitClickToMove.MoveTier.Sprint && !fireReady)
				leaf = "Sprint_F_Loop";
			else if (aimHold)
			{
				if (tier == (int)UnitClickToMove.MoveTier.Run)
					leaf = "Jog_Aim_F_Loop";
				else
					leaf = "Walk_Aim_F_Loop";
			}
			else
			{
				leaf = tier switch
				{
					(int)UnitClickToMove.MoveTier.Run => "Run_F_Loop",
					(int)UnitClickToMove.MoveTier.Sprint => "Sprint_F_Loop",
					_ => "Walk_F_Loop"
				};
			}
			return QualifyBaseLayerPath(SubStateMachineRifleStanding, leaf);
		}

		switch ((LocomotionStance)_stance)
		{
			case LocomotionStance.Crouch:
				return QualifyBaseLayerPath(SubStateMachineUnarmed, "Crouch_Locomotion");
			case LocomotionStance.Prone:
				return QualifyBaseLayerPath(SubStateMachineUnarmed, "Stand_Locomotion");
			default:
				return QualifyBaseLayerPath(SubStateMachineUnarmed, "Stand_Locomotion");
		}
	}

	private static string ResolveBaseLayerSubStateMachine(int _weaponMode, string _idleLeafName)
	{
		bool rifleBranch = _weaponMode == (int)LocomotionWeaponMode.Rifle ||
		                   _weaponMode == (int)LocomotionWeaponMode.Pistol;
		if (!rifleBranch)
			return SubStateMachineUnarmed;

		return _idleLeafName.StartsWith("RifleCrouch_", System.StringComparison.Ordinal)
			? SubStateMachineRifleCrouch
			: SubStateMachineRifleStanding;
	}
}
