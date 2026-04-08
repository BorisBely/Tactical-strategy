using UnityEngine;

/// <summary>
/// Вертикальное наведение: параметр <c>AimPitch</c> и слой <c>UpperBody_AimAdditive</c> на Animator.
/// Горизонталь — корень юнита (<see cref="UnitClickToMove"/>). При «готов» и видимой цели корень оружия только локальный из <see cref="ItemDefinition"/>, вертикаль даёт анимация.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(55)]
public sealed class UnitWeaponAiming : MonoBehaviour
{
	#region Constants
	private const string c_ParamAimPitch = "AimPitch";
	private const string c_AimLayerName = "UpperBody_AimAdditive";
	private const float c_PitchDegreesMax = 90f;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitVision m_Vision;
	[Tooltip("Forward — направление юнита (корень, бёдра).")]
	[SerializeField] private Transform m_UnitForwardSource;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitBusyState m_BusyState;

	[Header("Условия прицела")]
	[Tooltip("Только при «готов» и видимой цели; иначе AimPitch и слой в ноль.")]
	[SerializeField] private bool m_RequireReadyAndTarget = true;
	[Tooltip("Учитывать видимую цель из UnitVision для боевого прицела.")]
	[SerializeField] private bool m_AimAtVisibleTarget = true;

	[Header("Вертикаль (Animator)")]
	[SerializeField, Min(0f)] private float m_PitchSmoothTime = 0.08f;
	[SerializeField, Min(0f)] private float m_LayerWeightSmoothSeconds = 0.08f;

	[Tooltip("Не наводить по вертикали во время смены стойки (UnitBusyState + StanceTransition).")]
	[SerializeField] private bool m_BlockAimDuringStanceTransition = true;

	[Header("Инспектор (только отображение)")]
	[SerializeField] private float m_DebugSmoothedPitch01;
	[SerializeField, Range(0f, 1f)] private float m_DebugAimLayerWeight;

	[Header("Отладка лучей")]
	[SerializeField] private bool m_DrawBarrelForwardRay;
	[SerializeField, Min(0.1f)] private float m_BarrelForwardRayLength = 4f;
	[SerializeField] private Color m_BarrelForwardRayColor = new Color(1f, 0.85f, 0f, 0.95f);
	#endregion

	#region Private Fields
	private static readonly int s_AimPitch = Animator.StringToHash(c_ParamAimPitch);

	private ItemDefinition m_LastEquippedDefinition;
	private Quaternion m_BaseWeaponLocalRotation = Quaternion.identity;
	private Transform m_BarrelTransform;

	private int m_AimLayerIndex = -1;
	private float m_SmoothedPitch01;
	private float m_PitchVelocity;
	private float m_SmoothedLayerWeight;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_UnitForwardSource == null)
			m_UnitForwardSource = transform;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();

		ResolveLayerIndex();
	}

	private void OnEnable()
	{
		ResolveLayerIndex();
		m_SmoothedPitch01 = 0f;
		m_PitchVelocity = 0f;
		m_SmoothedLayerWeight = 0f;
		m_BarrelTransform = null;
		m_LastEquippedDefinition = null;
		if (m_Animator != null)
		{
			m_Animator.SetFloat(s_AimPitch, 0f);
			if (m_AimLayerIndex >= 0)
				m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);
		}
	}

	private void Update()
	{
		if (m_UnitEquipment == null || m_Animator == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
		{
			ResetAimAnimatorParameters();
			return;
		}

		if (!TrySyncWeaponDefinition(weaponRoot, def))
			return;

		ApplyAnimatorAimParameters();
	}

	private void LateUpdate()
	{
		if (m_UnitEquipment == null || m_UnitForwardSource == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
			return;

		if (!TrySyncWeaponDefinition(weaponRoot, def) || m_BarrelTransform == null)
			return;

		if (!ShouldApplyWeaponLocalOnlyForAim())
			return;

		weaponRoot.localRotation = m_BaseWeaponLocalRotation;

		if (m_DrawBarrelForwardRay)
			Debug.DrawRay(m_BarrelTransform.position, m_BarrelTransform.forward * m_BarrelForwardRayLength, m_BarrelForwardRayColor);
	}
	#endregion

	#region Private Methods
	private bool ShouldApplyWeaponLocalOnlyForAim()
	{
		if (!m_RequireReadyAndTarget)
			return false;

		bool ready = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		bool hasTarget = m_Vision != null && m_Vision.VisibleTarget != null;
		if (!ready || !hasTarget || !m_AimAtVisibleTarget)
			return false;

		if (m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return false;

		return true;
	}

	private void ResetAimAnimatorParameters()
	{
		m_LastEquippedDefinition = null;
		m_BarrelTransform = null;
		m_SmoothedLayerWeight = 0f;
		m_SmoothedPitch01 = 0f;
		m_PitchVelocity = 0f;
		if (m_Animator != null)
		{
			m_Animator.SetFloat(s_AimPitch, 0f);
			if (m_AimLayerIndex >= 0)
				m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);
		}
		m_DebugSmoothedPitch01 = 0f;
		m_DebugAimLayerWeight = 0f;
	}

	private void ResolveLayerIndex()
	{
		m_AimLayerIndex = m_Animator != null ? m_Animator.GetLayerIndex(c_AimLayerName) : -1;
	}

	private void ResolveBarrelTransform(Transform _weaponRoot)
	{
		EquippedWeapon w = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (w != null)
		{
			m_BarrelTransform = w.BarrelTransform != null ? w.BarrelTransform : _weaponRoot;
			return;
		}

		m_BarrelTransform = _weaponRoot;
	}

	private bool TrySyncWeaponDefinition(Transform _weaponRoot, ItemDefinition _def)
	{
		if (_def != m_LastEquippedDefinition)
		{
			m_LastEquippedDefinition = _def;
			m_BaseWeaponLocalRotation = _def.RightHandLocalRotation;
			ResolveBarrelTransform(_weaponRoot);
			_weaponRoot.localRotation = m_BaseWeaponLocalRotation;
		}

		return m_BarrelTransform != null;
	}

	private void ApplyAnimatorAimParameters()
	{
		if (m_AimLayerIndex < 0)
			ResolveLayerIndex();

		bool ready = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		Transform target = m_Vision != null ? m_Vision.VisibleTarget : null;
		bool hasTarget = target != null;

		bool stanceBlocks = m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		                    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;

		bool combatAim = m_RequireReadyAndTarget && ready && hasTarget && m_AimAtVisibleTarget && !stanceBlocks;

		float targetLayer = combatAim ? 1f : 0f;
		float wSmooth = Mathf.Max(0.0001f, m_LayerWeightSmoothSeconds);
		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetLayer, Time.deltaTime / wSmooth);

		if (m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, m_SmoothedLayerWeight);

		float targetPitch01 = 0f;
		if (combatAim && m_BarrelTransform != null)
		{
			Vector3 aimPoint = GetTargetAimPointWorld(target);
			Vector3 dir = aimPoint - m_BarrelTransform.position;
			if (dir.sqrMagnitude > 1e-6f)
			{
				dir.Normalize();
				float horiz = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
				float pitchDeg = Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
				pitchDeg = Mathf.Clamp(pitchDeg, -c_PitchDegreesMax, c_PitchDegreesMax);
				targetPitch01 = pitchDeg / c_PitchDegreesMax;
			}
		}

		if (m_PitchSmoothTime <= 0.0001f)
		{
			m_SmoothedPitch01 = targetPitch01;
			m_PitchVelocity = 0f;
		}
		else
		{
			m_SmoothedPitch01 = Mathf.SmoothDamp(m_SmoothedPitch01, targetPitch01, ref m_PitchVelocity, m_PitchSmoothTime,
				Mathf.Infinity, Time.deltaTime);
		}

		m_Animator.SetFloat(s_AimPitch, m_SmoothedPitch01);

		m_DebugSmoothedPitch01 = m_SmoothedPitch01;
		m_DebugAimLayerWeight = m_SmoothedLayerWeight;
	}

	private static Vector3 GetTargetAimPointWorld(Transform _targetRoot)
	{
		if (_targetRoot != null && _targetRoot.TryGetComponent(out UnitVision uv) && uv.BodyCollider != null)
			return uv.BodyCollider.bounds.center;

		return _targetRoot != null ? _targetRoot.position + Vector3.up * 1.2f : Vector3.zero;
	}
	#endregion
}
