using System.Collections;
#pragma warning disable CS0414
using System.Collections.Generic;
#pragma warning disable CS0414
using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.AI;
#pragma warning disable CS0414

/// <summary>
/// После стабилизации бессознательного юнита включает Laying Sleeping на слое Carried_Pose.
/// Клип имеет RootT.y ~1 м — без ground-snap и с включённым NavMeshAgent тело «левитирует».
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(64)]
public sealed class UnitStabilizedUnconsciousPoseController : MonoBehaviour
{
	#region Constants
	public const string ParamIsStabilizedSleeping = "IsStabilizedSleeping";

	private const string c_CarriedPoseLayerName = UnitFiremanCarryController.CarriedPoseLayerName;
	private const string c_StateLayingSleeping = "LayingSleeping";
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitHealth m_Health;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private NavMeshAgent m_NavMeshAgent;

	[Header("Presentation")]
	[SerializeField, Min(0.05f)] private float m_LayerWeightFadeInSeconds = 0.2f;
	[SerializeField, Min(0.05f)] private float m_LayerWeightFadeOutSeconds = 0.75f;
	[SerializeField, Min(0f)] private float m_DeathLimbImpulse = 0.55f;
	[SerializeField, Min(0f)] private float m_DeathLimbTorque = 0.35f;

	[Header("Ground Snap")]
	[SerializeField] private LayerMask m_GroundLayers = ~0;
	[SerializeField, Min(0f)] private float m_GroundSkinMeters = 0.03f;
	[SerializeField, Min(0f)] private float m_HipsClearanceMeters = 0.07f;
	[SerializeField, Min(0.1f)] private float m_GroundProbeUpMeters = 2f;
	[SerializeField, Min(0.1f)] private float m_GroundProbeDownMeters = 4f;
	[SerializeField, Min(0.01f)] private float m_GroundSnapMaxStepMeters = 0.45f;

	[Header("Debug")]
	[SerializeField] private bool m_IsSleepPoseActive;
	[SerializeField] private bool m_ExternalPoseOverride;
	[SerializeField] private float m_DebugLayerWeight;
	#endregion

	#region Private Fields
	private static readonly int s_IsStabilizedSleeping = Animator.StringToHash(ParamIsStabilizedSleeping);

	private readonly List<PoseSample> m_BlendFromPose = new List<PoseSample>(64);
	private readonly List<float> m_MutedLayerWeights = new List<float>(8);
	private Coroutine m_TransitionCoroutine;
	private int m_CarriedPoseLayerIndex = -1;
	private float m_SmoothedLayerWeight;
	private float m_BaseLayerWeightBeforeSleep = 1f;
	private bool m_OwnsCarriedPoseLayer;
	private bool m_PoseBlendActive;
	private float m_PoseBlendStartedAt;
	private float m_PoseBlendDuration;
	private bool m_DidMuteOtherLayers;
	private bool m_DisabledNavAgentForSleep;
	private int m_HardSnapFramesRemaining;
	#endregion

	#region Private Types
	private struct PoseSample
	{
		public Transform Transform;
		public Quaternion LocalRotation;
	}
	#endregion

	#region Public Properties
	public bool IsSleepPoseActive => m_IsSleepPoseActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		ResolveLayerIndex();
	}

	private void OnEnable()
	{
		if (m_Health != null)
			m_Health.Changed += HandleHealthChanged;
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged += HandleConsciousnessChanged;

		EvaluateDesiredPose(_immediate: true);
	}

	private void OnDisable()
	{
		if (m_Health != null)
			m_Health.Changed -= HandleHealthChanged;
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged -= HandleConsciousnessChanged;

		StopTransitionCoroutine();
		ForceExitImmediate();
	}

	private void Update()
	{
		if (!m_OwnsCarriedPoseLayer || m_Animator == null || m_CarriedPoseLayerIndex < 0)
			return;

		float target = m_IsSleepPoseActive ? 1f : 0f;
		float fadeSeconds = m_IsSleepPoseActive
			? Mathf.Max(0.05f, m_LayerWeightFadeInSeconds)
			: Mathf.Max(0.05f, m_LayerWeightFadeOutSeconds);
		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, target, Time.deltaTime / fadeSeconds);
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, m_SmoothedLayerWeight);
		m_DebugLayerWeight = m_SmoothedLayerWeight;
	}

	private void LateUpdate()
	{
		// Старый ragdoll-blend больше не используем — он оставлял тело «носом в землю / ногами вверх».
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();

		if (!m_IsSleepPoseActive || !m_OwnsCarriedPoseLayer)
			return;

		KeepNavigationDisabledForSleepPose();
		KeepSleepRootUprightYawOnly();

		bool hardSnap = m_HardSnapFramesRemaining > 0;
		if (hardSnap)
			m_HardSnapFramesRemaining--;

		SnapSleepPoseToGround(hardSnap);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Fireman carry / drag временно забирают слой Carried_Pose.
	/// </summary>
	public void NotifyExternalPoseOverride(bool _active)
	{
		m_ExternalPoseOverride = _active;
		if (_active)
		{
			StopTransitionCoroutine();
			ReleaseLayerOwnershipKeepSleepFlag();
		}
		else
		{
			EvaluateDesiredPose(_immediate: false);
		}
	}
	#endregion

	#region Private Methods — Evaluate
	private void HandleHealthChanged()
	{
		EvaluateDesiredPose(_immediate: false);
	}

	private void HandleConsciousnessChanged(bool _)
	{
		EvaluateDesiredPose(_immediate: false);
	}

	private void EvaluateDesiredPose(bool _immediate)
	{
		ResolveReferences();
		if (m_ExternalPoseOverride)
			return;

		bool wantsSleep = ShouldUseSleepPose();
		if (wantsSleep == m_IsSleepPoseActive && (wantsSleep || !m_OwnsCarriedPoseLayer))
			return;

		StopTransitionCoroutine();
		if (wantsSleep)
			m_TransitionCoroutine = StartCoroutine(CoEnterSleepPose(_immediate));
		else if (m_Health != null && m_Health.IsDead && m_IsSleepPoseActive)
			m_TransitionCoroutine = StartCoroutine(CoExitSleepPoseToDead());
		else
			m_TransitionCoroutine = StartCoroutine(CoExitSleepPoseToRagdoll(_immediate));
	}

	private bool ShouldUseSleepPose()
	{
		if (m_Health == null || m_Consciousness == null)
			return false;
		if (m_Health.IsDead || m_Consciousness.IsConscious)
			return false;
		if (!m_Health.HasInjuries || m_Health.HasUnstabilizedInjuries)
			return false;
		if (m_ExternalPoseOverride)
			return false;

		return true;
	}
	#endregion

	#region Private Methods — Enter / Exit
	private IEnumerator CoEnterSleepPose(bool _immediate)
	{
		ResolveReferences();
		ResolveLayerIndex();

		if (m_Animator == null || m_CarriedPoseLayerIndex < 0)
		{
			Debug.LogWarning(
				$"[StabilizedSleep:{name}] CoEnterSleepPose aborted: animator/layer missing " +
				$"(animator={(m_Animator != null)}, layer={m_CarriedPoseLayerIndex}).",
				this);
			m_TransitionCoroutine = null;
			yield break;
		}

		float yawDegrees = ResolveSleepYawDegrees();
		Vector3 hipsWorld = ResolveHipsWorldPosition();

		if (m_RagdollController != null && m_RagdollController.IsRagdollActive)
			m_RagdollController.SetRagdollActive(false, _preserveCurrentPose: true, _restoreWeaponControl: false);
		else
			m_RagdollController?.SetWeaponControlFrozenForAnimatedPose(true);

		// SetRagdollActive(false) снова включает NavMeshAgent — сразу гасим, иначе он поднимает root.
		DisableNavigationForSleepPose();

		m_ReadyHands?.SetReadyWanted(false);

		// Корень строго вертикальный (только yaw): клип Laying Sleeping кладёт тело сам (RootQ).
		transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
		if (hipsWorld.sqrMagnitude > 0.0001f)
		{
			Vector3 pos = transform.position;
			pos.x = hipsWorld.x;
			pos.z = hipsWorld.z;
			transform.position = pos;
		}

		if (!m_Animator.enabled)
			m_Animator.enabled = true;

		m_Animator.applyRootMotion = false;
		MuteOtherAnimatorLayers();
		m_Animator.SetBool(s_IsStabilizedSleeping, true);
		// Принудительно входим в стейт: одних transition-условий иногда не хватает в тот же кадр.
		m_Animator.CrossFadeInFixedTime(c_StateLayingSleeping, 0.05f, m_CarriedPoseLayerIndex, 0f);

		m_OwnsCarriedPoseLayer = true;
		m_IsSleepPoseActive = true;
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();
		m_HardSnapFramesRemaining = 12;

		// Вес сразу высокий: иначе snap считается по стоячей/ragdoll позе, а клип с RootT.y~1м
		// потом поднимает тело в воздух.
		m_SmoothedLayerWeight = _immediate ? 1f : 0.85f;
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, m_SmoothedLayerWeight);
		m_DebugLayerWeight = m_SmoothedLayerWeight;

		m_Animator.Update(0f);
		KeepSleepRootUprightYawOnly();
		SnapSleepPoseToGround(_hardSnap: true);

		yield return null;
		KeepNavigationDisabledForSleepPose();
		m_Animator.Update(0f);
		KeepSleepRootUprightYawOnly();
		SnapSleepPoseToGround(_hardSnap: true);

		yield return null;
		KeepNavigationDisabledForSleepPose();
		m_SmoothedLayerWeight = 1f;
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);
		m_DebugLayerWeight = 1f;
		KeepSleepRootUprightYawOnly();
		SnapSleepPoseToGround(_hardSnap: true);

		m_TransitionCoroutine = null;
	}

	private IEnumerator CoExitSleepPoseToDead()
	{
		ResolveReferences();
		ResolveLayerIndex();

		m_IsSleepPoseActive = false;
		if (m_Animator != null)
			m_Animator.SetBool(s_IsStabilizedSleeping, false);

		float fadeSeconds = Mathf.Max(0.05f, m_LayerWeightFadeOutSeconds);
		float timeout = Time.time + fadeSeconds + 0.25f;
		while (m_OwnsCarriedPoseLayer && m_SmoothedLayerWeight > 0.15f && Time.time < timeout)
			yield return null;

		if (m_Animator != null && m_CarriedPoseLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);

		m_OwnsCarriedPoseLayer = false;
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();
		m_SmoothedLayerWeight = 0f;
		m_DebugLayerWeight = 0f;

		if (m_RagdollController != null)
			m_RagdollController.ReactivateRagdollWithSoftLimbSettle(m_DeathLimbImpulse, m_DeathLimbTorque);

		RestoreBaseLayerWeight();
		ClearSleepNavigationLock();
		m_TransitionCoroutine = null;
	}

	private IEnumerator CoExitSleepPoseToRagdoll(bool _immediate)
	{
		ResolveReferences();
		ResolveLayerIndex();

		m_IsSleepPoseActive = false;
		m_HardSnapFramesRemaining = 0;
		if (m_Animator != null)
			m_Animator.SetBool(s_IsStabilizedSleeping, false);

		if (!_immediate && m_OwnsCarriedPoseLayer)
		{
			float fadeSeconds = Mathf.Max(0.05f, m_LayerWeightFadeOutSeconds);
			float timeout = Time.time + fadeSeconds + 0.25f;
			while (m_SmoothedLayerWeight > 0.02f && Time.time < timeout)
				yield return null;
		}

		if (m_Animator != null && m_CarriedPoseLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);

		m_OwnsCarriedPoseLayer = false;
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();
		m_SmoothedLayerWeight = 0f;
		m_DebugLayerWeight = 0f;

		bool shouldRagdoll = m_Consciousness != null && !m_Consciousness.IsConscious;
		if (shouldRagdoll && m_RagdollController != null && !m_RagdollController.IsRagdollActive)
			m_RagdollController.SetRagdollActive(true);
		else if (!shouldRagdoll)
			m_RagdollController?.SetWeaponControlFrozenForAnimatedPose(false);

		RestoreBaseLayerWeight();
		RestoreNavigationAfterSleepPose();
		m_TransitionCoroutine = null;
	}

	private void ForceExitImmediate()
	{
		m_IsSleepPoseActive = false;
		m_OwnsCarriedPoseLayer = false;
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();
		m_SmoothedLayerWeight = 0f;
		m_HardSnapFramesRemaining = 0;

		if (m_Animator != null)
		{
			m_Animator.SetBool(s_IsStabilizedSleeping, false);
			if (m_CarriedPoseLayerIndex >= 0)
				m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);
			RestoreBaseLayerWeight();
		}

		if (m_RagdollController != null && !m_RagdollController.IsRagdollActive)
			m_RagdollController.SetWeaponControlFrozenForAnimatedPose(false);

		RestoreNavigationAfterSleepPose();
	}

	private void ReleaseLayerOwnershipKeepSleepFlag()
	{
		m_IsSleepPoseActive = false;
		m_OwnsCarriedPoseLayer = false;
		m_PoseBlendActive = false;
		m_BlendFromPose.Clear();
		m_SmoothedLayerWeight = 0f;
		m_HardSnapFramesRemaining = 0;

		if (m_Animator != null)
		{
			m_Animator.SetBool(s_IsStabilizedSleeping, false);
			RestoreBaseLayerWeight();
		}

		// Carry/drag/vehicle сами держат навигацию; флаг сбрасываем без включения агента.
		ClearSleepNavigationLock();
		// Оружие/IK оставляем замороженными — carry/drag/ragdoll сами решат дальнейшее состояние.
	}
	#endregion

	#region Private Methods — Helpers
	private void BeginPoseBlend(float _duration)
	{
		if (m_BlendFromPose.Count == 0)
			return;

		m_PoseBlendActive = true;
		m_PoseBlendStartedAt = Time.time;
		m_PoseBlendDuration = Mathf.Max(0.05f, _duration);
	}

	private float ResolveSleepYawDegrees()
	{
		if (m_Animator != null && m_Animator.isHuman)
		{
			Transform hips = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
			Transform head = m_Animator.GetBoneTransform(HumanBodyBones.Head);
			Transform leftFoot = m_Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
			Transform rightFoot = m_Animator.GetBoneTransform(HumanBodyBones.RightFoot);

			Vector3 bodyAxis = Vector3.zero;
			if (hips != null && head != null)
				bodyAxis = head.position - hips.position;
			else if (hips != null && leftFoot != null && rightFoot != null)
				bodyAxis = hips.position - 0.5f * (leftFoot.position + rightFoot.position);

			bodyAxis.y = 0f;
			if (bodyAxis.sqrMagnitude > 0.01f)
				return Quaternion.LookRotation(bodyAxis.normalized, Vector3.up).eulerAngles.y;
		}

		Vector3 flatForward = transform.forward;
		flatForward.y = 0f;
		if (flatForward.sqrMagnitude > 0.01f)
			return Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;

		return transform.eulerAngles.y;
	}

	private Vector3 ResolveHipsWorldPosition()
	{
		if (m_Animator != null && m_Animator.isHuman)
		{
			Transform hips = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
			if (hips != null)
				return hips.position;
		}

		return transform.position;
	}

	private void KeepSleepRootUprightYawOnly()
	{
		Vector3 euler = transform.eulerAngles;
		transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
	}

	private void MuteOtherAnimatorLayers()
	{
		if (m_Animator == null)
			return;

		m_MutedLayerWeights.Clear();
		int layerCount = m_Animator.layerCount;
		for (int i = 0; i < layerCount; i++)
		{
			m_MutedLayerWeights.Add(m_Animator.GetLayerWeight(i));
			if (i == m_CarriedPoseLayerIndex)
				continue;

			m_Animator.SetLayerWeight(i, 0f);
		}

		m_BaseLayerWeightBeforeSleep = m_MutedLayerWeights.Count > 0 ? m_MutedLayerWeights[0] : 1f;
		m_DidMuteOtherLayers = true;
	}

	private void RestoreMutedAnimatorLayers()
	{
		if (m_Animator == null || !m_DidMuteOtherLayers)
			return;

		int layerCount = Mathf.Min(m_Animator.layerCount, m_MutedLayerWeights.Count);
		for (int i = 0; i < layerCount; i++)
		{
			if (i == m_CarriedPoseLayerIndex)
				continue;

			m_Animator.SetLayerWeight(i, m_MutedLayerWeights[i]);
		}

		m_MutedLayerWeights.Clear();
		m_DidMuteOtherLayers = false;
	}

	private void SnapSleepPoseToGround(bool _hardSnap)
	{
		if (m_Animator == null || !m_Animator.isHuman)
			return;

		Transform hips = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
		Transform spine = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
		Transform chest = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
		Transform head = m_Animator.GetBoneTransform(HumanBodyBones.Head);
		Transform leftUpperLeg = m_Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
		Transform rightUpperLeg = m_Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
		Transform leftFoot = m_Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
		Transform rightFoot = m_Animator.GetBoneTransform(HumanBodyBones.RightFoot);

		float sampleY = float.MaxValue;
		Vector3 sampleOrigin = transform.position;
		int sampleCount = 0;
		Vector3 sampleSum = Vector3.zero;

		void Accumulate(Transform _bone)
		{
			if (_bone == null)
				return;
			sampleSum += _bone.position;
			sampleCount++;
			sampleY = Mathf.Min(sampleY, _bone.position.y);
		}

		// Торс + бёдра: клип Laying Sleeping держит RootT.y ~1 м над root.
		Accumulate(hips);
		Accumulate(spine);
		Accumulate(chest);
		Accumulate(leftUpperLeg);
		Accumulate(rightUpperLeg);
		if (sampleCount == 0)
		{
			Accumulate(head);
			Accumulate(leftFoot);
			Accumulate(rightFoot);
		}

		if (sampleCount == 0 || sampleY >= float.MaxValue * 0.5f)
			return;

		sampleOrigin = sampleSum / sampleCount;

		float probeStartY = Mathf.Max(sampleOrigin.y, transform.position.y) + m_GroundProbeUpMeters;
		Vector3 probeOrigin = new Vector3(sampleOrigin.x, probeStartY, sampleOrigin.z);
		float probeDistance = (probeStartY - sampleOrigin.y) + m_GroundProbeDownMeters;
		RaycastHit[] hits = Physics.RaycastAll(
			probeOrigin,
			Vector3.down,
			probeDistance,
			m_GroundLayers,
			QueryTriggerInteraction.Ignore);
		if (hits == null || hits.Length == 0)
			return;

		float bestGroundY = float.NegativeInfinity;
		for (int i = 0; i < hits.Length; i++)
		{
			Collider col = hits[i].collider;
			if (col == null)
				continue;
			if (col.transform == transform || col.transform.IsChildOf(transform))
				continue;

			bestGroundY = Mathf.Max(bestGroundY, hits[i].point.y);
		}

		if (float.IsNegativeInfinity(bestGroundY))
			return;

		float lowestTargetY = bestGroundY + m_GroundSkinMeters;
		float deltaY = lowestTargetY - sampleY;

		if (hips != null)
		{
			float hipsTargetY = bestGroundY + Mathf.Max(m_GroundSkinMeters, m_HipsClearanceMeters);
			float hipsDelta = hipsTargetY - hips.position.y;
			// Не поднимаем корпус выше, чем нужно для бёдер, если торс уже на земле.
			if (hipsDelta > deltaY)
				deltaY = Mathf.Lerp(deltaY, hipsDelta, 0.2f);
		}

		if (Mathf.Abs(deltaY) < 0.001f)
			return;

		if (!_hardSnap)
			deltaY = Mathf.Clamp(deltaY, -m_GroundSnapMaxStepMeters, m_GroundSnapMaxStepMeters);

		transform.position += Vector3.up * deltaY;
	}

	private void DisableNavigationForSleepPose()
	{
		if (m_NavMeshAgent == null)
			m_NavMeshAgent = GetComponent<NavMeshAgent>();
		if (m_NavMeshAgent == null)
			return;

		if (m_NavMeshAgent.enabled)
		{
			if (m_NavMeshAgent.isOnNavMesh)
			{
				m_NavMeshAgent.isStopped = true;
				m_NavMeshAgent.ResetPath();
			}

			m_NavMeshAgent.updatePosition = false;
			m_NavMeshAgent.updateRotation = false;
			m_NavMeshAgent.enabled = false;
			m_DisabledNavAgentForSleep = true;
		}
		else if (m_DisabledNavAgentForSleep)
		{
			m_NavMeshAgent.updatePosition = false;
			m_NavMeshAgent.updateRotation = false;
		}
	}

	private void KeepNavigationDisabledForSleepPose()
	{
		if (!m_IsSleepPoseActive)
			return;

		if (m_NavMeshAgent == null)
			m_NavMeshAgent = GetComponent<NavMeshAgent>();
		if (m_NavMeshAgent == null)
			return;

		if (m_NavMeshAgent.enabled)
		{
			m_DisabledNavAgentForSleep = true;
			if (m_NavMeshAgent.isOnNavMesh)
			{
				m_NavMeshAgent.isStopped = true;
				m_NavMeshAgent.ResetPath();
			}

			m_NavMeshAgent.updatePosition = false;
			m_NavMeshAgent.updateRotation = false;
			m_NavMeshAgent.enabled = false;
		}
	}

	private void ClearSleepNavigationLock()
	{
		m_DisabledNavAgentForSleep = false;
		m_HardSnapFramesRemaining = 0;
	}

	private void RestoreNavigationAfterSleepPose()
	{
		if (!m_DisabledNavAgentForSleep)
			return;

		m_DisabledNavAgentForSleep = false;
		m_HardSnapFramesRemaining = 0;

		if (m_NavMeshAgent == null)
			m_NavMeshAgent = GetComponent<NavMeshAgent>();
		if (m_NavMeshAgent == null)
			return;

		// Бессознательный / ragdoll — агент должен остаться выключенным.
		if (m_RagdollController != null && m_RagdollController.IsRagdollActive)
			return;
		if (m_Consciousness != null && !m_Consciousness.IsConscious)
			return;

		m_NavMeshAgent.enabled = true;
		m_NavMeshAgent.updatePosition = true;
		m_NavMeshAgent.updateRotation = true;
		if (m_NavMeshAgent.isOnNavMesh)
			m_NavMeshAgent.Warp(transform.position);
	}

	private void CaptureCurrentPose()
	{
		m_BlendFromPose.Clear();
		if (m_Animator == null || !m_Animator.isHuman)
			return;

		for (int i = (int)HumanBodyBones.Hips; i <= (int)HumanBodyBones.RightToes; i++)
		{
			Transform bone = m_Animator.GetBoneTransform((HumanBodyBones)i);
			if (bone == null)
				continue;

			m_BlendFromPose.Add(new PoseSample
			{
				Transform = bone,
				LocalRotation = bone.localRotation
			});
		}
	}

	private void RestoreBaseLayerWeight()
	{
		if (m_DidMuteOtherLayers)
		{
			RestoreMutedAnimatorLayers();
			return;
		}

		if (m_Animator == null)
			return;

		m_Animator.SetLayerWeight(0, m_BaseLayerWeightBeforeSleep > 0f ? m_BaseLayerWeightBeforeSleep : 1f);
	}

	private void StopTransitionCoroutine()
	{
		if (m_TransitionCoroutine == null)
			return;

		StopCoroutine(m_TransitionCoroutine);
		m_TransitionCoroutine = null;
	}

	private void ResolveLayerIndex()
	{
		if (m_Animator == null)
		{
			m_CarriedPoseLayerIndex = -1;
			return;
		}

		m_CarriedPoseLayerIndex = m_Animator.GetLayerIndex(c_CarriedPoseLayerName);
	}

	private void ResolveReferences()
	{
		if (m_Health == null)
			m_Health = GetComponent<UnitHealth>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_NavMeshAgent == null)
			m_NavMeshAgent = GetComponent<NavMeshAgent>();
	}
	#endregion
}
