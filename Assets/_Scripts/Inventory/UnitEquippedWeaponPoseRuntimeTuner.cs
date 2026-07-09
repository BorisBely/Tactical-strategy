using System;
using UnityEngine;

/// <summary>
/// EDITOR TOOL — weapon pose / hand IK tuning in Play Mode.
/// Not required on Unit for gameplay. Runtime pose/IK comes from ItemDefinition +
/// LeftHandIkTarget* empties (weapon body or foregrip prefab).
///
/// How to tune again:
/// 1. Menu: Polygone → Weapons → Add Weapon Pose Runtime Tuner To Unit
///    (or Add Component → UnitEquippedWeaponPoseRuntimeTuner on Unit)
/// 2. Play Mode → Enable Runtime Tuning
/// 3. Hands Frozen → place Equipped_*
/// 4. Not Ready / Ready → move RightHandIkTarget* and LeftHandIkTarget*
/// 5. Save To Asset (weapon). For foregrip left IK: Save Left IK To Foregrip Prefab
/// 6. Disable tuning / remove this component from Unit when done
///
/// Modes: Hands Frozen (no IK) → Not Ready → Ready.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44)]
public sealed class UnitEquippedWeaponPoseRuntimeTuner : MonoBehaviour
{
	#region Nested Types
	public enum TuningTarget
	{
		/// <summary>Hands follow animation only (IK off). Place weapon — first / base coordinates.</summary>
		HandsFrozen = 0,
		/// <summary>Not ready: weapon pose + right-hand IK target.</summary>
		NotReady = 1,
		/// <summary>Ready: weapon pose + right-hand IK target.</summary>
		Ready = 2
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Runtime tuning")]
	[Tooltip("When on: weapon/IK transforms are free to move in Hierarchy.")]
	[SerializeField] private bool m_EnableRuntimeTuning;
	[Tooltip("Hands Frozen = place weapon with IK off (base coords). Then Not Ready / Ready for poses + hand IK.")]
	[SerializeField] private TuningTarget m_ActiveTarget = TuningTarget.HandsFrozen;

	[Header("Captured — weapon pose (Hand_R local)")]
	[SerializeField] private Vector3 m_NotReadyLocalPosition;
	[SerializeField] private Vector3 m_NotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ReadyLocalPosition;
	[SerializeField] private Vector3 m_ReadyLocalEulerAngles;

	[Header("Captured — right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_NotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_NotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_ReadyIkLocalPosition;
	[SerializeField] private Vector3 m_ReadyIkLocalEulerAngles;

	[Header("Captured — left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_LeftNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_LeftNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_LeftReadyIkLocalPosition;
	[SerializeField] private Vector3 m_LeftReadyIkLocalEulerAngles;
	#endregion

	#region Private Fields
	private TuningTarget m_LastAppliedTarget = (TuningTarget)(-1);
	private bool m_WasTuningActive;
	#endregion

	#region Public Properties
	public bool IsTuningActive => m_EnableRuntimeTuning && Application.isPlaying;

	/// <summary>Hands Frozen: both hands IK off so weapon can be placed freely.</summary>
	public bool ShouldDisableAllHandIk => IsTuningActive && m_ActiveTarget == TuningTarget.HandsFrozen;

	/// <summary>Not Ready / Ready: force right-hand IK on for tuning targets.</summary>
	public bool ForcesRightHandIk => IsTuningActive && m_ActiveTarget != TuningTarget.HandsFrozen;

	public TuningTarget ActiveTarget => m_ActiveTarget;

	public float ForcedReadyBlend01 => m_ActiveTarget == TuningTarget.Ready ? 1f : 0f;

	public Vector3 NotReadyLocalPosition => m_NotReadyLocalPosition;
	public Vector3 NotReadyLocalEulerAngles => m_NotReadyLocalEulerAngles;
	public Vector3 ReadyLocalPosition => m_ReadyLocalPosition;
	public Vector3 ReadyLocalEulerAngles => m_ReadyLocalEulerAngles;
	public Vector3 NotReadyIkLocalPosition => m_NotReadyIkLocalPosition;
	public Vector3 NotReadyIkLocalEulerAngles => m_NotReadyIkLocalEulerAngles;
	public Vector3 ReadyIkLocalPosition => m_ReadyIkLocalPosition;
	public Vector3 ReadyIkLocalEulerAngles => m_ReadyIkLocalEulerAngles;
	public Vector3 LeftNotReadyIkLocalPosition => m_LeftNotReadyIkLocalPosition;
	public Vector3 LeftNotReadyIkLocalEulerAngles => m_LeftNotReadyIkLocalEulerAngles;
	public Vector3 LeftReadyIkLocalPosition => m_LeftReadyIkLocalPosition;
	public Vector3 LeftReadyIkLocalEulerAngles => m_LeftReadyIkLocalEulerAngles;
	public UnitEquipment UnitEquipment => m_UnitEquipment;

	/// <summary>
	/// Left-hand IK empties live on the installed foregrip visual (not the weapon body).
	/// In that case ItemDefinition left IK coords must not override / be overwritten by Save.
	/// </summary>
	public bool IsLeftHandIkDrivenByForegrip
	{
		get
		{
			Transform foregripRoot = GetForegripVisualRoot();
			if (foregripRoot == null)
				return false;

			return FindChildRecursive(foregripRoot, "LeftHandIkTarget") != null
			       || FindChildRecursive(foregripRoot, "LeftHandIkTarget_NotReady") != null;
		}
	}

	/// <summary>Installed under-barrel foregrip visual root, if any.</summary>
	public Transform GetForegripVisualRoot()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		return equippedWeapon != null ? equippedWeapon.UnderBarrelForegripVisualRoot : null;
	}

	/// <summary>When tuning: do not overwrite MainWeaponRoot — user moves it in Hierarchy.</summary>
	public bool ShouldSkipWeaponPoseWrite => IsTuningActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		ResolveReferences();
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
	}

	private void LateUpdate()
	{
		if (!Application.isPlaying)
			return;

		bool tuning = IsTuningActive;
		if (tuning && !m_WasTuningActive)
			BeginTuningSession();
		else if (!tuning && m_WasTuningActive)
			EndTuningSession();

		m_WasTuningActive = tuning;
		if (!tuning)
			return;

		if (m_ActiveTarget != m_LastAppliedTarget)
		{
			ApplyActiveTargetPoseToWeapon();
			m_LastAppliedTarget = m_ActiveTarget;
		}

		CaptureLiveWeaponPoseFromScene();
		// Always capture all four IK targets (L/R × ready/not-ready), even in Hands Frozen.
		CaptureLiveIkFromScene();
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Ensure Left/Right HandIkTarget and *_NotReady empties exist, then refresh caches.
	/// Right IK → weapon body. Left IK → foregrip visual when present, else weapon body.
	/// </summary>
	public void EnsureAllHandIkTargetsExist()
	{
		if (m_UnitEquipment == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
			return;

		EnsureChildEmpty(weaponRoot, def.RightHandIkTargetChildName, m_ReadyIkLocalPosition, m_ReadyIkLocalEulerAngles);
		EnsureChildEmpty(weaponRoot, def.RightHandIkTargetNotReadyChildName, m_NotReadyIkLocalPosition, m_NotReadyIkLocalEulerAngles);

		EquippedWeapon equippedWeapon = m_UnitEquipment.EquippedWeapon;
		Transform leftParent = equippedWeapon != null && equippedWeapon.UnderBarrelForegripVisualRoot != null
			? equippedWeapon.UnderBarrelForegripVisualRoot
			: weaponRoot;

		EnsureChildEmpty(leftParent, def.LeftHandIkTargetChildName, m_LeftReadyIkLocalPosition, m_LeftReadyIkLocalEulerAngles);

		string leftNotReadyName = def.LeftHandIkTargetNotReadyChildName;
		if (string.IsNullOrWhiteSpace(leftNotReadyName))
			leftNotReadyName = "LeftHandIkTarget_NotReady";
		EnsureChildEmpty(leftParent, leftNotReadyName, m_LeftNotReadyIkLocalPosition, m_LeftNotReadyIkLocalEulerAngles);

		m_UnitEquipment.RefreshHandIkTargets();
	}

	/// <summary>Capture weapon pose + all four hand IK targets from Hierarchy.</summary>
	public void CaptureAllForSave()
	{
		EnsureAllHandIkTargetsExist();
		CaptureLiveWeaponPoseFromScene();
		CaptureLiveIkFromScene();
	}

	public void GetOverridePoses(
		out Vector3 _relaxedPosition,
		out Quaternion _relaxedRotation,
		out Vector3 _readyPosition,
		out Quaternion _readyRotation,
		out float _forcedBlend01)
	{
		_relaxedPosition = m_NotReadyLocalPosition;
		_relaxedRotation = Quaternion.Euler(m_NotReadyLocalEulerAngles);
		_readyPosition = m_ReadyLocalPosition;
		_readyRotation = Quaternion.Euler(m_ReadyLocalEulerAngles);
		_forcedBlend01 = ForcedReadyBlend01;
	}

	public void LoadFromEquippedDefinition()
	{
		ItemDefinition def = m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
		if (def == null)
			return;

		m_NotReadyLocalPosition = def.RightHandLocalPosition;
		m_NotReadyLocalEulerAngles = def.RightHandLocalRotation.eulerAngles;

		m_ReadyLocalPosition = def.RightHandReadyLocalPosition;
		m_ReadyLocalEulerAngles = def.RightHandReadyLocalEulerAngles;
		if (m_ReadyLocalPosition == Vector3.zero && m_ReadyLocalEulerAngles == Vector3.zero)
		{
			m_ReadyLocalPosition = m_NotReadyLocalPosition;
			m_ReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
		}

		m_NotReadyIkLocalPosition = def.RightHandIkNotReadyLocalPosition;
		m_NotReadyIkLocalEulerAngles = def.RightHandIkNotReadyLocalEulerAngles;
		m_ReadyIkLocalPosition = def.RightHandIkReadyLocalPosition;
		m_ReadyIkLocalEulerAngles = def.RightHandIkReadyLocalEulerAngles;

		// Foregrip owns left IK empties — do not push weapon ItemDefinition left coords onto the grip.
		if (!IsLeftHandIkDrivenByForegrip)
		{
			m_LeftNotReadyIkLocalPosition = def.LeftHandIkNotReadyLocalPosition;
			m_LeftNotReadyIkLocalEulerAngles = def.LeftHandIkNotReadyLocalEulerAngles;
			m_LeftReadyIkLocalPosition = def.LeftHandIkReadyLocalPosition;
			m_LeftReadyIkLocalEulerAngles = def.LeftHandIkReadyLocalEulerAngles;
		}

		CaptureIkFromTargetsIfUnset();
		ApplyActiveTargetPoseToWeapon();
		ApplyStoredIkToTargets();
		m_LastAppliedTarget = m_ActiveTarget;
	}

	public void CaptureLiveWeaponPoseFromScene()
	{
		Transform weaponRoot = m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
		if (weaponRoot == null)
			return;

		if (m_ActiveTarget == TuningTarget.Ready)
		{
			m_ReadyLocalPosition = weaponRoot.localPosition;
			m_ReadyLocalEulerAngles = weaponRoot.localEulerAngles;
			return;
		}

		// HandsFrozen + NotReady both write the base / not-ready weapon pose (first coordinates).
		m_NotReadyLocalPosition = weaponRoot.localPosition;
		m_NotReadyLocalEulerAngles = weaponRoot.localEulerAngles;
	}

	public void CaptureLiveIkFromScene()
	{
		Transform notReady = m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetNotReadyTransform : null;
		if (notReady != null)
		{
			m_NotReadyIkLocalPosition = notReady.localPosition;
			m_NotReadyIkLocalEulerAngles = notReady.localEulerAngles;
		}

		Transform ready = m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetTransform : null;
		if (ready != null)
		{
			m_ReadyIkLocalPosition = ready.localPosition;
			m_ReadyIkLocalEulerAngles = ready.localEulerAngles;
		}

		Transform leftNotReady = m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetNotReadyTransform : null;
		if (leftNotReady != null)
		{
			m_LeftNotReadyIkLocalPosition = leftNotReady.localPosition;
			m_LeftNotReadyIkLocalEulerAngles = leftNotReady.localEulerAngles;
		}

		Transform leftReady = m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetTransform : null;
		if (leftReady != null)
		{
			m_LeftReadyIkLocalPosition = leftReady.localPosition;
			m_LeftReadyIkLocalEulerAngles = leftReady.localEulerAngles;
		}
	}

	public void ApplyActiveTargetPoseToWeapon()
	{
		Transform weaponRoot = m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
		if (weaponRoot == null)
			return;

		if (m_ActiveTarget == TuningTarget.Ready)
		{
			weaponRoot.localPosition = m_ReadyLocalPosition;
			weaponRoot.localRotation = Quaternion.Euler(m_ReadyLocalEulerAngles);
			return;
		}

		weaponRoot.localPosition = m_NotReadyLocalPosition;
		weaponRoot.localRotation = Quaternion.Euler(m_NotReadyLocalEulerAngles);
	}

	public void ApplyStoredIkToTargets()
	{
		if (m_UnitEquipment == null)
			return;

		Transform notReady = m_UnitEquipment.RightHandIkTargetNotReadyTransform;
		if (notReady != null)
		{
			notReady.localPosition = m_NotReadyIkLocalPosition;
			notReady.localRotation = Quaternion.Euler(m_NotReadyIkLocalEulerAngles);
		}

		Transform ready = m_UnitEquipment.RightHandIkTargetTransform;
		if (ready != null)
		{
			ready.localPosition = m_ReadyIkLocalPosition;
			ready.localRotation = Quaternion.Euler(m_ReadyIkLocalEulerAngles);
		}

		// Never overwrite foregrip LeftHandIkTarget* from weapon-asset coords.
		if (IsLeftHandIkDrivenByForegrip)
			return;

		Transform leftNotReady = m_UnitEquipment.LeftHandIkTargetNotReadyTransform;
		if (leftNotReady != null)
		{
			leftNotReady.localPosition = m_LeftNotReadyIkLocalPosition;
			leftNotReady.localRotation = Quaternion.Euler(m_LeftNotReadyIkLocalEulerAngles);
		}

		Transform leftReady = m_UnitEquipment.LeftHandIkTargetTransform;
		if (leftReady != null)
		{
			leftReady.localPosition = m_LeftReadyIkLocalPosition;
			leftReady.localRotation = Quaternion.Euler(m_LeftReadyIkLocalEulerAngles);
		}
	}

	public Transform GetActiveIkTargetTransform()
	{
		if (m_UnitEquipment == null || m_ActiveTarget == TuningTarget.HandsFrozen)
			return null;

		return m_ActiveTarget == TuningTarget.NotReady
			? m_UnitEquipment.RightHandIkTargetNotReadyTransform
			: m_UnitEquipment.RightHandIkTargetTransform;
	}

	/// <summary>Copy current base (not-ready) weapon pose into ready pose as a starting point.</summary>
	public void CopyBaseWeaponPoseToReady()
	{
		m_ReadyLocalPosition = m_NotReadyLocalPosition;
		m_ReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
		if (m_ActiveTarget == TuningTarget.Ready)
			ApplyActiveTargetPoseToWeapon();
	}

	public string BuildYamlSnippet()
	{
		return
			$"  m_RightHandLocalPosition: {{x: {Format(m_NotReadyLocalPosition.x)}, y: {Format(m_NotReadyLocalPosition.y)}, z: {Format(m_NotReadyLocalPosition.z)}}}\n" +
			$"  m_RightHandLocalEulerAngles: {{x: {Format(m_NotReadyLocalEulerAngles.x)}, y: {Format(m_NotReadyLocalEulerAngles.y)}, z: {Format(m_NotReadyLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandReadyLocalPosition: {{x: {Format(m_ReadyLocalPosition.x)}, y: {Format(m_ReadyLocalPosition.y)}, z: {Format(m_ReadyLocalPosition.z)}}}\n" +
			$"  m_RightHandReadyLocalEulerAngles: {{x: {Format(m_ReadyLocalEulerAngles.x)}, y: {Format(m_ReadyLocalEulerAngles.y)}, z: {Format(m_ReadyLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandIkNotReadyLocalPosition: {{x: {Format(m_NotReadyIkLocalPosition.x)}, y: {Format(m_NotReadyIkLocalPosition.y)}, z: {Format(m_NotReadyIkLocalPosition.z)}}}\n" +
			$"  m_RightHandIkNotReadyLocalEulerAngles: {{x: {Format(m_NotReadyIkLocalEulerAngles.x)}, y: {Format(m_NotReadyIkLocalEulerAngles.y)}, z: {Format(m_NotReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandIkReadyLocalPosition: {{x: {Format(m_ReadyIkLocalPosition.x)}, y: {Format(m_ReadyIkLocalPosition.y)}, z: {Format(m_ReadyIkLocalPosition.z)}}}\n" +
			$"  m_RightHandIkReadyLocalEulerAngles: {{x: {Format(m_ReadyIkLocalEulerAngles.x)}, y: {Format(m_ReadyIkLocalEulerAngles.y)}, z: {Format(m_ReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_LeftHandIkNotReadyLocalPosition: {{x: {Format(m_LeftNotReadyIkLocalPosition.x)}, y: {Format(m_LeftNotReadyIkLocalPosition.y)}, z: {Format(m_LeftNotReadyIkLocalPosition.z)}}}\n" +
			$"  m_LeftHandIkNotReadyLocalEulerAngles: {{x: {Format(m_LeftNotReadyIkLocalEulerAngles.x)}, y: {Format(m_LeftNotReadyIkLocalEulerAngles.y)}, z: {Format(m_LeftNotReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_LeftHandIkReadyLocalPosition: {{x: {Format(m_LeftReadyIkLocalPosition.x)}, y: {Format(m_LeftReadyIkLocalPosition.y)}, z: {Format(m_LeftReadyIkLocalPosition.z)}}}\n" +
			$"  m_LeftHandIkReadyLocalEulerAngles: {{x: {Format(m_LeftReadyIkLocalEulerAngles.x)}, y: {Format(m_LeftReadyIkLocalEulerAngles.y)}, z: {Format(m_LeftReadyIkLocalEulerAngles.z)}}}";
	}
	#endregion

	#region Private Methods
	private void BeginTuningSession()
	{
		LoadFromEquippedDefinition();
		Debug.Log(
			"[WeaponPoseTuner] ON — start with Hands Frozen: place Equipped_* (base coords). " +
			"Then Not Ready / Ready for poses + hand IK. Save To Asset when done.",
			this);
	}

	private void EndTuningSession()
	{
		m_LastAppliedTarget = (TuningTarget)(-1);
		// Push last captured IK empties back, then restore pose from ItemDefinition
		// (same path as normal gameplay — including not-ready right-hand IK weight).
		ApplyStoredIkToTargets();
		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();
		Debug.Log("[WeaponPoseTuner] OFF — pose driven from ItemDefinition again.", this);
	}

	private void CaptureIkFromTargetsIfUnset()
	{
		if (m_UnitEquipment == null)
			return;

		if (m_NotReadyIkLocalPosition == Vector3.zero && m_NotReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = m_UnitEquipment.RightHandIkTargetNotReadyTransform;
			if (t != null)
			{
				m_NotReadyIkLocalPosition = t.localPosition;
				m_NotReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_ReadyIkLocalPosition == Vector3.zero && m_ReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = m_UnitEquipment.RightHandIkTargetTransform;
			if (t != null)
			{
				m_ReadyIkLocalPosition = t.localPosition;
				m_ReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_LeftNotReadyIkLocalPosition == Vector3.zero && m_LeftNotReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = m_UnitEquipment.LeftHandIkTargetNotReadyTransform;
			if (t == null)
				t = m_UnitEquipment.LeftHandIkTargetTransform;
			if (t != null)
			{
				m_LeftNotReadyIkLocalPosition = t.localPosition;
				m_LeftNotReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_LeftReadyIkLocalPosition == Vector3.zero && m_LeftReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = m_UnitEquipment.LeftHandIkTargetTransform;
			if (t != null)
			{
				m_LeftReadyIkLocalPosition = t.localPosition;
				m_LeftReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}
	}

	private static void EnsureChildEmpty(
		Transform _parent,
		string _name,
		Vector3 _fallbackLocalPosition,
		Vector3 _fallbackLocalEuler)
	{
		if (_parent == null || string.IsNullOrWhiteSpace(_name))
			return;

		// Direct child only — never reparent a foregrip LeftHandIkTarget onto the weapon body.
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return;

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _fallbackLocalPosition;
		t.localRotation = Quaternion.Euler(_fallbackLocalEuler);
		t.localScale = Vector3.one;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrWhiteSpace(_name))
			return null;

		Transform direct = _root.Find(_name);
		if (direct != null)
			return direct;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}

	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
	}

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			return;

		m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			return;

		m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void HandleEquipmentChanged()
	{
		if (IsTuningActive)
			LoadFromEquippedDefinition();
	}

	private static string Format(float _value)
	{
		return Math.Abs(_value) < 0.0001f ? "0" : _value.ToString("0.####");
	}
	#endregion
}
