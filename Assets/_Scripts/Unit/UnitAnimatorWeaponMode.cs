using UnityEngine;

/// <summary>
/// Синхронизирует int <c>WeaponMode</c> на <see cref="Animator"/> с фактически экипированным предметом.
/// Готовность оружия управляется отдельным bool-параметром <c>WeaponReady</c> в <see cref="UnitWeaponReadyHandsLayer"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorWeaponMode : MonoBehaviour
{
	public const string ParamWeaponMode = "WeaponMode";
	public const string ParamStance = "Stance";
	public const string ParamWeaponReady = "WeaponReady";

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

	/// <summary>Согласовано с порогами переходов в контроллере (idle NavSpeed &lt; 0.05, движение &gt; 0.055).</summary>
	private const float c_MoveNavSpeedAnimatorThreshold = 0.055f;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;

	[Header("Плавность")]
	[SerializeField, Min(0.02f)] private float m_WeaponModeCrossFadeSeconds = 0.22f;

	private int m_LastWeaponModeValue = -1;

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
	}

	private void OnEnable()
	{
		m_LastWeaponModeValue = -1;
		PushWeaponModeIfChanged();
	}

	private void LateUpdate()
	{
		PushWeaponModeIfChanged();
	}

	/// <summary>
	/// Пересобрать базовый слой под текущий <c>WeaponMode</c> (и при «готов» — без обязательного idle).
	/// При активном движении по <c>NavSpeed</c> сразу переходит в locomotion-стейт нужной ветки.
	/// </summary>
	public void ReplayLocomotionIdleCrossfade()
	{
		SnapBaseLayerToWeaponBranch();
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
			SnapBaseLayerToWeaponBranch();
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

	private void SnapBaseLayerToWeaponBranch()
	{
		if (m_Animator == null)
			return;

		int stance = m_Animator.GetInteger(s_Stance);
		if (!LocomotionProneFeature.Enabled && stance == (int)LocomotionStance.Prone)
			stance = (int)LocomotionStance.Standing;

		float navSpeed = m_Animator.GetFloat(s_NavSpeed);
		bool weaponReady = m_Animator.GetBool(s_WeaponReady);
		string qualifiedState = navSpeed >= c_MoveNavSpeedAnimatorThreshold
			? ResolveBaseLayerLocomotionQualified(m_LastWeaponModeValue, stance)
			: ResolveBaseLayerIdleQualified(m_LastWeaponModeValue, stance, weaponReady);

		m_Animator.CrossFadeInFixedTime(qualifiedState, m_WeaponModeCrossFadeSeconds, 0);
	}

	private static string QualifyBaseLayerPath(string _subMachine, string _leaf) =>
		$"{BaseLayerAnimatorName}.{_subMachine}.{_leaf}";

	private static string ResolveBaseLayerIdleQualified(int _weaponMode, int _stance, bool _weaponReady)
	{
		string targetLeaf;

		if (_weaponMode == (int)LocomotionWeaponMode.Rifle ||
		    _weaponMode == (int)LocomotionWeaponMode.Pistol)
		{
			targetLeaf = _stance switch
			{
				(int)LocomotionStance.Crouch => "RifleCrouch_Idle",
				(int)LocomotionStance.Prone => _weaponReady ? "Stand_Aim_Idle" : "Stand_Relaxed_Idle",
				_ => _weaponReady ? "Stand_Aim_Idle" : "Stand_Relaxed_Idle"
			};
		}
		else
		{
			targetLeaf = _stance switch
			{
				(int)LocomotionStance.Crouch => "Crouch_Idle",
				_ => "Stand_Relaxed_Rifle_Idle"
			};
		}

		string subMachine = ResolveBaseLayerSubStateMachine(_weaponMode, targetLeaf);
		return QualifyBaseLayerPath(subMachine, targetLeaf);
	}

	private string ResolveBaseLayerLocomotionQualified(int _weaponMode, int _stance)
	{
		if (_weaponMode == (int)LocomotionWeaponMode.Rifle ||
		    _weaponMode == (int)LocomotionWeaponMode.Pistol)
		{
			if (_stance == (int)LocomotionStance.Crouch || _stance == (int)LocomotionStance.Prone)
				return QualifyBaseLayerPath(SubStateMachineRifleCrouch, "RifleCrouch_Move");

			int tier = m_Animator.GetInteger(s_LocomotionTier);
			bool ready = m_Animator.GetBool(s_WeaponReady);
			string leaf;
			if (ready)
				leaf = tier == (int)UnitClickToMove.MoveTier.Run ? "Jog_Aim_F_Loop" : "Walk_Aim_F_Loop";
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
