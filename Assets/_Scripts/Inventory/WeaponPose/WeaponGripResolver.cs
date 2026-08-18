using System;
using UnityEngine;

/// <summary>
/// Equip-time cache + event-driven IK target selection.
/// Writes dummy IK target transforms only — never <c>Equipped_*</c> weapon local TRS.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(69)]
public sealed class WeaponGripResolver : MonoBehaviour
{
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitEquippedWeaponPose m_WeaponPose;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[SerializeField, Range(0f, 1f)] private float m_DefaultRightWeight = 0.35f;
	[SerializeField, Range(0f, 1f)] private float m_DefaultLeftWeight = 0.9f;
	[Tooltip("Длительность blend правой IK-цели при смене Standing/Crouch/Vehicle.")]
	[SerializeField, Min(0.05f)] private float m_StanceIkBlendSeconds = 0.2f;
	[Tooltip("Макс. скорость dummy правой IK в local оружия (м/с).")]
	[SerializeField, Min(0.01f)] private float m_RightTargetMaxMetersPerSecond = 2.5f;
	[Tooltip("Макс. угловая скорость dummy правой IK (град/с).")]
	[SerializeField, Min(1f)] private float m_RightTargetMaxDegreesPerSecond = 540f;

	private WeaponGripRig m_GripRig;
	private Transform m_WeaponLeftHandIk;
	private HandIkState m_CurrentState;
	private Transform m_DerivedPreAimTarget;
	private Transform m_BlendIkTarget;

	private WeaponStance m_LastStance = (WeaponStance)(-1);
	private WeaponPoseState m_LastPose = (WeaponPoseState)(-1);
	private bool m_HasLastSelection;
	private LocomotionStance m_LastLocoStance = (LocomotionStance)(-1);
	private bool m_LastVehicleFireCapable;
	private WeaponHoldContext m_HoldContext;
	private WeaponStance m_StanceBlendFrom;
	private WeaponStance m_StanceBlendTo;
	private float m_StanceBlend01 = 1f;
	private bool m_IsStanceBlending;
	private bool m_HasDummyLocalPose;
	private float m_LastRightTargetJumpMeters;

	public HandIkState CurrentState => m_CurrentState;
	public bool HasGripRig => m_GripRig != null && m_GripRig.HasRightHandIkTargets;
	public WeaponHoldContext HoldContext => m_HoldContext;
	public float LastRightTargetJumpMeters => m_LastRightTargetJumpMeters;
	public float DefaultLeftWeight => m_DefaultLeftWeight;
	public float DefaultRightWeight => m_DefaultRightWeight;

	public event Action TargetsChanged;

	private void Awake() => ResolveRefs();

	private void OnEnable()
	{
		Subscribe();
		RebuildCache();
		RefreshTargets(force: true);
	}

	private void OnDisable()
	{
		Unsubscribe();
		DestroyDerivedPreAim();
		DestroyBlendIkTarget();
	}

	private void Update()
	{
		PollExternalState();
		TickStanceBlend();
		UpdateRightIkTarget();
	}

	/// <summary>Call after equip or prefab save — wires Transform refs once.</summary>
	public void RebuildCache()
	{
		ResolveRefs();
		m_GripRig = null;
		m_WeaponLeftHandIk = null;

		if (m_UnitEquipment == null)
			return;

		m_UnitEquipment.ResolveGripTargets();

		Transform weapon = m_UnitEquipment.MainWeaponRoot;
		if (weapon != null)
		{
			m_GripRig = weapon.GetComponentInChildren<WeaponGripRig>(true);
			if (m_GripRig != null)
				m_GripRig.BuildCache();
		}

		m_WeaponLeftHandIk = m_UnitEquipment.GripLeftHandTarget;
		m_HasLastSelection = false;
		m_HasDummyLocalPose = false;
		m_IsStanceBlending = false;
		m_StanceBlend01 = 1f;
	}

	/// <summary>Left only — foregrip attach/detach.</summary>
	public void RefreshLeftTarget()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.ResolveGripTargets();
		m_WeaponLeftHandIk = m_UnitEquipment != null ? m_UnitEquipment.GripLeftHandTarget : null;
		m_CurrentState.LeftTarget = m_WeaponLeftHandIk;
		m_CurrentState.LeftWeight = m_DefaultLeftWeight;
		TargetsChanged?.Invoke();
	}

	public void RefreshTargets(bool force = false)
	{
		WeaponStance stance = ResolveStance();
		WeaponPoseState pose = ResolvePoseState();

		if (!force && m_HasLastSelection && stance == m_LastStance && pose == m_LastPose)
			return;

		m_LastStance = stance;
		m_LastPose = pose;
		m_HasLastSelection = true;

		m_CurrentState.RightWeight = m_DefaultRightWeight;
		m_CurrentState.LeftWeight = m_DefaultLeftWeight;
		m_CurrentState.LeftTarget = m_WeaponLeftHandIk;

		if (m_GripRig != null && m_GripRig.HasRightHandIkTargets)
			m_CurrentState.RightTarget = ResolveRightTarget(stance, pose);
		else
			m_CurrentState.RightTarget = null;

		TargetsChanged?.Invoke();
	}

	private void HandleEquipmentChanged()
	{
		RebuildCache();
		RefreshTargets(force: true);
	}

	private void HandleReadyPoseChanged()
	{
		if (IsWeaponPoseBlending())
			return;
		WeaponPoseState pose = ResolvePoseState();
		if (pose == m_LastPose && m_HasLastSelection)
			return;
		RefreshTargets();
	}

	private void HandlePoseChanged()
	{
		if (IsWeaponPoseBlending())
		{
			UpdateBlendedRightTarget();
			return;
		}

		RefreshTargets();
	}

	private void HandleStanceChanged()
	{
		WeaponStance stance = ResolveStance();
		if (stance == m_LastStance && m_HasLastSelection)
			return;

		WeaponStance from = m_HasLastSelection ? m_LastStance : stance;
		BeginStanceBlend(from, stance);
		m_LastStance = stance;
		m_LastPose = ResolvePoseState();
		m_HasLastSelection = true;
		m_CurrentState.RightWeight = m_DefaultRightWeight;
		m_CurrentState.LeftWeight = m_DefaultLeftWeight;
		m_CurrentState.LeftTarget = m_WeaponLeftHandIk;
	}

	private void HandleTunerModeChanged() => RefreshTargets(force: true);

	private Transform ResolveDerivedPreAimTarget(WeaponStance _stance)
	{
		if (m_GripRig == null)
			return null;

		Transform low = m_GripRig.GetRightHandTarget(_stance, WeaponPoseState.LowReady);
		Transform aim = m_GripRig.GetRightHandTarget(_stance, WeaponPoseState.Aiming);
		if (low == null && aim == null)
			return null;
		if (low == null)
			return aim;
		if (aim == null)
			return low;

		EnsureDerivedPreAim();
		Transform parent = low.parent != null ? low.parent : aim.parent;
		if (parent != null && m_DerivedPreAimTarget.parent != parent)
			m_DerivedPreAimTarget.SetParent(parent, false);

		PreAimPoseUtility.BlendLocal(
			low.localPosition,
			low.localRotation,
			aim.localPosition,
			aim.localRotation,
			PreAimPoseUtility.RightHandBlend,
			out Vector3 pos,
			out Quaternion rot);
		m_DerivedPreAimTarget.localPosition = pos;
		m_DerivedPreAimTarget.localRotation = rot;
		return m_DerivedPreAimTarget;
	}

	private void EnsureDerivedPreAim()
	{
		if (m_DerivedPreAimTarget != null)
			return;
		var go = new GameObject("PreAimDerivedIK");
		go.hideFlags = HideFlags.HideAndDontSave;
		m_DerivedPreAimTarget = go.transform;
	}

	private void DestroyDerivedPreAim()
	{
		if (m_DerivedPreAimTarget == null)
			return;
		if (Application.isPlaying)
			UnityEngine.Object.Destroy(m_DerivedPreAimTarget.gameObject);
		else
			UnityEngine.Object.DestroyImmediate(m_DerivedPreAimTarget.gameObject);
		m_DerivedPreAimTarget = null;
	}

	private Transform ResolveRightTarget(WeaponStance _stance, WeaponPoseState _pose)
	{
		if (m_GripRig == null || !m_GripRig.HasRightHandIkTargets)
			return null;
		if (_pose == WeaponPoseState.PreAim)
			return ResolveDerivedPreAimTarget(_stance);
		return m_GripRig.GetRightHandTarget(_stance, _pose);
	}

	private bool IsWeaponPoseBlending() =>
		m_WeaponPose != null && m_WeaponPose.IsPoseBlendAnimating && m_WeaponPose.PoseBlend01 < 0.999f;

	private void TickStanceBlend()
	{
		if (!m_IsStanceBlending)
			return;

		float duration = Mathf.Max(0.05f, m_StanceIkBlendSeconds);
		m_StanceBlend01 = Mathf.Min(1f, m_StanceBlend01 + Time.deltaTime / duration);
		if (m_StanceBlend01 >= 0.999f)
		{
			m_StanceBlend01 = 1f;
			m_IsStanceBlending = false;
			m_StanceBlendFrom = m_StanceBlendTo;
		}
	}

	private void BeginStanceBlend(WeaponStance _from, WeaponStance _to)
	{
		if (_from == _to)
		{
			m_IsStanceBlending = false;
			m_StanceBlend01 = 1f;
			m_StanceBlendFrom = _to;
			m_StanceBlendTo = _to;
			return;
		}

		m_IsStanceBlending = true;
		m_StanceBlendFrom = _from;
		m_StanceBlendTo = _to;
		m_StanceBlend01 = 0f;
	}

	private void RebuildHoldContext()
	{
		WeaponStance stance = ResolveStance();
		WeaponPoseState pose = ResolvePoseState();
		bool poseBlend = IsWeaponPoseBlending();
		m_HoldContext = new WeaponHoldContext
		{
			StanceFrom = m_IsStanceBlending ? m_StanceBlendFrom : stance,
			StanceTo = m_IsStanceBlending ? m_StanceBlendTo : stance,
			StanceBlend01 = m_IsStanceBlending ? m_StanceBlend01 : 1f,
			PoseFrom = poseBlend && m_WeaponPose != null ? m_WeaponPose.CurrentPose : pose,
			PoseTo = poseBlend && m_WeaponPose != null ? m_WeaponPose.TargetPose : pose,
			PoseBlend01 = poseBlend && m_WeaponPose != null ? m_WeaponPose.PoseBlend01 : 1f,
			IsPoseBlending = poseBlend,
			IsStanceBlending = m_IsStanceBlending
		};
	}

	private void UpdateRightIkTarget()
	{
		RebuildHoldContext();
		m_LastRightTargetJumpMeters = 0f;
		m_CurrentState.LeftTarget = m_WeaponLeftHandIk;
		m_CurrentState.LeftWeight = m_DefaultLeftWeight;
		m_CurrentState.RightWeight = m_DefaultRightWeight;

		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			RefreshTargets(force: true);
			return;
		}

		Transform weapon = GetWeaponRoot();
		if (weapon == null || m_GripRig == null || !m_GripRig.HasRightHandIkTargets)
		{
			m_HasDummyLocalPose = false;
			return;
		}

		WeaponHoldContext ctx = m_HoldContext;
		Transform fromEmpty = ResolveRightTarget(ctx.StanceFrom, ctx.PoseFrom);
		Transform toEmpty = ResolveRightTarget(ctx.StanceTo, ctx.PoseTo);
		if (toEmpty == null)
			toEmpty = fromEmpty;
		if (fromEmpty == null)
			fromEmpty = toEmpty;
		if (toEmpty == null)
		{
			m_CurrentState.RightTarget = null;
			return;
		}

		if (!TryGetWeaponLocal(weapon, toEmpty, out Vector3 toPos, out Quaternion toRot))
			return;

		Vector3 fromPos = toPos;
		Quaternion fromRot = toRot;
		if (fromEmpty != toEmpty)
			TryGetWeaponLocal(weapon, fromEmpty, out fromPos, out fromRot);

		float t = 1f;
		if (ctx.IsPoseBlending && ctx.IsStanceBlending)
			t = Mathf.Max(ctx.PoseBlend01, ctx.StanceBlend01);
		else if (ctx.IsPoseBlending)
			t = ctx.PoseBlend01;
		else if (ctx.IsStanceBlending)
			t = ctx.StanceBlend01;

		PreAimPoseUtility.BlendLocal(
			fromPos,
			fromRot,
			toPos,
			toRot,
			Mathf.Clamp01(t),
			out Vector3 desiredPos,
			out Quaternion desiredRot);

		EnsureBlendIkTarget();
		if (m_BlendIkTarget.parent != weapon)
			m_BlendIkTarget.SetParent(weapon, worldPositionStays: false);

		if (!m_HasDummyLocalPose)
		{
			m_BlendIkTarget.localPosition = desiredPos;
			m_BlendIkTarget.localRotation = desiredRot;
			m_HasDummyLocalPose = true;
		}
		else
		{
			m_LastRightTargetJumpMeters = Vector3.Distance(m_BlendIkTarget.localPosition, desiredPos);
			float dt = Time.deltaTime;
			m_BlendIkTarget.localPosition = Vector3.MoveTowards(
				m_BlendIkTarget.localPosition,
				desiredPos,
				m_RightTargetMaxMetersPerSecond * dt);
			m_BlendIkTarget.localRotation = Quaternion.RotateTowards(
				m_BlendIkTarget.localRotation,
				desiredRot,
				m_RightTargetMaxDegreesPerSecond * dt);
		}

		bool caughtUp = m_LastRightTargetJumpMeters < 0.001f &&
		                Quaternion.Angle(m_BlendIkTarget.localRotation, desiredRot) < 0.5f;
		bool blending = ctx.IsBlending;
		if (blending || !caughtUp)
			m_CurrentState.RightTarget = m_BlendIkTarget;
		else
			m_CurrentState.RightTarget = toEmpty;

		m_LastStance = ctx.StanceTo;
		m_LastPose = ctx.PoseTo;
		m_HasLastSelection = true;
	}

	private static bool TryGetWeaponLocal(
		Transform _weapon,
		Transform _empty,
		out Vector3 _localPos,
		out Quaternion _localRot)
	{
		_localPos = Vector3.zero;
		_localRot = Quaternion.identity;
		if (_weapon == null || _empty == null)
			return false;

		_localPos = _weapon.InverseTransformPoint(_empty.position);
		_localRot = Quaternion.Inverse(_weapon.rotation) * _empty.rotation;
		return true;
	}

	private void UpdateBlendedRightTarget()
	{
		UpdateRightIkTarget();
	}

	private Transform GetWeaponRoot()
	{
		if (m_UnitEquipment == null)
			return null;
		if (m_UnitEquipment.IsOperatingVehicleTurret)
			return m_UnitEquipment.EffectiveWeaponRoot;
		return m_UnitEquipment.MainWeaponRoot;
	}

	private void EnsureBlendIkTarget()
	{
		if (m_BlendIkTarget != null)
			return;
		var go = new GameObject("PoseBlendIK");
		go.hideFlags = HideFlags.HideAndDontSave;
		m_BlendIkTarget = go.transform;
	}

	private void DestroyBlendIkTarget()
	{
		m_HasDummyLocalPose = false;
		if (m_BlendIkTarget == null)
			return;
		if (Application.isPlaying)
			UnityEngine.Object.Destroy(m_BlendIkTarget.gameObject);
		else
			UnityEngine.Object.DestroyImmediate(m_BlendIkTarget.gameObject);
		m_BlendIkTarget = null;
	}

	private void PollExternalState()
	{
		if (m_Stance != null)
		{
			LocomotionStance loco = m_Stance.CurrentStance;
			if (loco != m_LastLocoStance)
			{
				m_LastLocoStance = loco;
				HandleStanceChanged();
			}
		}

		if (m_VehiclePassengerState != null)
		{
			bool fireCapable = m_VehiclePassengerState.IsFireCapable;
			if (fireCapable != m_LastVehicleFireCapable)
			{
				m_LastVehicleFireCapable = fireCapable;
				HandleStanceChanged();
			}
		}
	}

	private WeaponPoseState ResolvePoseState()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			return m_RuntimeTuner.ActiveWeaponPoseState;

		if (m_WeaponPose != null)
			return m_WeaponPose.GetEffectivePoseForIk();

		return WeaponPoseState.LowReady;
	}

	private WeaponStance ResolveStance()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			return m_RuntimeTuner.ActivePosture switch
			{
				UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch => WeaponStance.Crouching,
				UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle => WeaponStance.Vehicle,
				_ => WeaponStance.Standing,
			};
		}

		if (m_VehiclePassengerState != null && m_VehiclePassengerState.IsFireCapable)
			return WeaponStance.Vehicle;

		if (m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Crouch)
			return WeaponStance.Crouching;

		return WeaponStance.Standing;
	}

	private void Subscribe()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_WeaponPose != null)
		{
			m_WeaponPose.ReadyPoseBlendChanged += HandleReadyPoseChanged;
			m_WeaponPose.PoseChanged += HandlePoseChanged;
		}
		if (m_RuntimeTuner != null)
			m_RuntimeTuner.TuningModeChanged += HandleTunerModeChanged;
	}

	private void Unsubscribe()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_WeaponPose != null)
		{
			m_WeaponPose.ReadyPoseBlendChanged -= HandleReadyPoseChanged;
			m_WeaponPose.PoseChanged -= HandlePoseChanged;
		}
		if (m_RuntimeTuner != null)
			m_RuntimeTuner.TuningModeChanged -= HandleTunerModeChanged;
	}

	private void ResolveRefs()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_WeaponPose == null)
			m_WeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_WeaponPose == null)
			m_WeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_Stance == null)
			m_Stance = GetComponentInParent<UnitAnimatorStance>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponentInParent<VehiclePassengerState>();
	}
}
