using UnityEngine;

/// <summary>
/// Включение/выключение групп орудия и щитов на уже выставленных координатах префаба.
/// Старт: всё выключено (источник истины — инвентарь машины).
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleTurretVisualMount : MonoBehaviour
{
	#region Nested
	[System.Serializable]
	private struct LocalPoseSnapshot
	{
		public Vector3 LocalPosition;
		public Vector3 LocalEulerAngles;
		public bool Captured;

		public void CaptureFrom(Transform _t)
		{
			if (_t == null)
			{
				Captured = false;
				return;
			}

			LocalPosition = _t.localPosition;
			LocalEulerAngles = _t.localEulerAngles;
			Captured = true;
		}

		public void ApplyTo(Transform _t)
		{
			if (_t == null || !Captured)
				return;
			_t.localPosition = LocalPosition;
			_t.localEulerAngles = LocalEulerAngles;
		}
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private TurretWeaponVariant m_CurrentVariant = TurretWeaponVariant.None;
	[SerializeField] private bool m_FrontalShieldVisible;
	[SerializeField] private bool m_SurroundShieldVisible;
	[SerializeField] private bool m_GunnerHatchVisible;

	[Header("Pose snapshots (from prefab)")]
	[SerializeField] private LocalPoseSnapshot m_GunBase127Pose;
	[SerializeField] private LocalPoseSnapshot m_Gun127Pose;
	[SerializeField] private LocalPoseSnapshot m_Mag127Pose;
	[SerializeField] private LocalPoseSnapshot m_Mk19BasePose;
	[SerializeField] private LocalPoseSnapshot m_Mk19Pose;
	[SerializeField] private LocalPoseSnapshot m_MagMk19Pose;
	[SerializeField] private LocalPoseSnapshot m_ArmorFrontal127Pose;
	[SerializeField] private LocalPoseSnapshot m_ArmorFrontalMk19Pose;
	[SerializeField] private LocalPoseSnapshot m_ArmorSurroundPose;
	#endregion

	#region Public Properties
	public TurretWeaponVariant CurrentVariant => m_CurrentVariant;
	public bool FrontalShieldVisible => m_FrontalShieldVisible;
	public bool SurroundShieldVisible => m_SurroundShieldVisible;
	public bool GunnerHatchVisible => m_GunnerHatchVisible;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);
		m_Hierarchy?.EnsureBound();
		CaptureSnapshotsIfNeeded();
		ApplyVisualState();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);
		m_Hierarchy?.EnsureBound();
		if (!Application.isPlaying)
			CaptureSnapshotsIfNeeded(_force: true);
	}

	[ContextMenu("Show Browning 12.7")]
	private void CtxShow127() => ShowWeaponVariant(TurretWeaponVariant.Browning127);

	[ContextMenu("Show MK19")]
	private void CtxShowMk19() => ShowWeaponVariant(TurretWeaponVariant.Mk19);

	[ContextMenu("Show None")]
	private void CtxShowNone() => ShowWeaponVariant(TurretWeaponVariant.None);
#endif
	#endregion

	#region Public Methods
	public void Configure(VehicleTurretHierarchyBinder _hierarchy)
	{
		m_Hierarchy = _hierarchy;
		m_Hierarchy?.EnsureBound();
		CaptureSnapshotsIfNeeded(_force: true);
		ApplyVisualState();
	}

	public void ShowWeaponVariant(TurretWeaponVariant _variant)
	{
		m_CurrentVariant = _variant;
		ApplyVisualState();
	}

	public void SetFrontalShieldVisible(bool _visible)
	{
		m_FrontalShieldVisible = _visible;
		ApplyVisualState();
	}

	public void SetSurroundShieldVisible(bool _visible)
	{
		m_SurroundShieldVisible = _visible;
		ApplyVisualState();
	}

	public void SetGunnerHatchVisible(bool _visible)
	{
		m_GunnerHatchVisible = _visible;
		ApplyVisualState();
	}

	public void CaptureSnapshotsIfNeeded(bool _force = false)
	{
		if (m_Hierarchy == null)
			return;

		Capture(ref m_GunBase127Pose, m_Hierarchy.GunBase127, _force);
		Capture(ref m_Gun127Pose, m_Hierarchy.Gun127, _force);
		Capture(ref m_Mag127Pose, m_Hierarchy.Mag127, _force);
		Capture(ref m_Mk19BasePose, m_Hierarchy.Mk19Base, _force);
		Capture(ref m_Mk19Pose, m_Hierarchy.Mk19, _force);
		Capture(ref m_MagMk19Pose, m_Hierarchy.MagMk19, _force);
		Capture(ref m_ArmorFrontal127Pose, m_Hierarchy.ArmorFrontal127, _force);
		Capture(ref m_ArmorFrontalMk19Pose, m_Hierarchy.ArmorFrontalMk19, _force);
		Capture(ref m_ArmorSurroundPose, m_Hierarchy.ArmorSurround, _force);
	}
	#endregion

	#region Private Methods
	private static void Capture(ref LocalPoseSnapshot _snap, Transform _t, bool _force)
	{
		if (_t == null)
			return;
		if (_snap.Captured && !_force)
			return;
		_snap.CaptureFrom(_t);
	}

	private void ApplyVisualState()
	{
		if (m_Hierarchy == null)
			return;

		bool show127 = m_CurrentVariant == TurretWeaponVariant.Browning127;
		bool showMk19 = m_CurrentVariant == TurretWeaponVariant.Mk19;
		bool showNone = m_CurrentVariant == TurretWeaponVariant.None;

		SetActiveRestore(m_Hierarchy.GunBase127, m_GunBase127Pose, show127);
		SetActiveRestore(m_Hierarchy.Gun127, m_Gun127Pose, show127);
		SetActiveRestore(m_Hierarchy.Mag127, m_Mag127Pose, show127);

		SetActiveRestore(m_Hierarchy.Mk19Base, m_Mk19BasePose, showMk19);
		SetActiveRestore(m_Hierarchy.Mk19, m_Mk19Pose, showMk19);
		SetActiveRestore(m_Hierarchy.MagMk19, m_MagMk19Pose, showMk19);

		bool showArmor127 = m_FrontalShieldVisible && show127;
		bool showArmorMk19 = m_FrontalShieldVisible && showMk19;
		bool showArmorDefault = m_FrontalShieldVisible && showNone;
		SetActiveRestore(m_Hierarchy.ArmorFrontal127, m_ArmorFrontal127Pose, showArmor127);
		SetActiveRestore(m_Hierarchy.ArmorFrontalMk19, m_ArmorFrontalMk19Pose, showArmorMk19);
		if (m_Hierarchy.ArmorFrontalDefault != null)
			m_Hierarchy.ArmorFrontalDefault.gameObject.SetActive(showArmorDefault);

		SetActiveRestore(m_Hierarchy.ArmorSurround, m_ArmorSurroundPose, m_SurroundShieldVisible);

		if (m_Hierarchy.GunnerHatchMesh != null)
			m_Hierarchy.GunnerHatchMesh.SetActive(m_GunnerHatchVisible);
	}

	private static void SetActiveRestore(Transform _t, LocalPoseSnapshot _pose, bool _active)
	{
		if (_t == null)
			return;
		if (_active)
			_pose.ApplyTo(_t);
		_t.gameObject.SetActive(_active);
	}
	#endregion
}
