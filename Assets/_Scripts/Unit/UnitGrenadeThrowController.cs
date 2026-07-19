using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Управляет броском гранаты: анимация, визуал в руке, создание снаряда.
/// Живёт на юните вместе с UnitEquipment, CharacterInventory, UnitBusyState.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(63)]
public sealed class UnitGrenadeThrowController : MonoBehaviour
{
	#region Constants
	public const string ParamGrenadeThrow = "GrenadeThrow";
	private static readonly int s_GrenadeThrow = Animator.StringToHash(ParamGrenadeThrow);
	private const string AimLayerName = "Aim_Point_U90-D90";
	private const string GrenadeThrowStateName = "GrenadeThrowStart";
	#endregion

	#region Events
	public event Action ThrowStarted;
	public event Action ThrowCompleted;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private CharacterInventory m_Inventory;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private GrenadeThrowData m_Data;
	[SerializeField] private Transform m_RightHandAnchor;
	[SerializeField] private Transform m_LeftHandAnchor;

	[Header("Route Integration")]
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private CharacterInventory m_InventoryRefForUi;
	#endregion

	#region Private Fields
	private GrenadeThrowPhase m_CurrentPhase;
	private GrenadeType m_SelectedType = GrenadeType.Fragmentation;
	private ItemDefinition m_ActiveGrenadeDefinition;
	private GameObject m_HandGrenadeInstance;
	private GameObject m_PinInstance;
	private GameObject m_LeverInstance;
	private Vector3 m_TargetWorldPosition;
	private Vector3 m_ThrowDirection;
	private bool m_IsThrowOnRoute;
	private Coroutine m_ThrowCompletionCoroutine;
	private Coroutine m_ThrowTurnCoroutine;
	private bool m_PendingThrowOnRoute;
	private int m_AimLayerIndex = -1;
	private bool m_WeaponShownThisThrow;
	#endregion

	#region Public Properties
	public GrenadeThrowPhase CurrentPhase => m_CurrentPhase;
	public bool IsThrowAnimPlaying => m_CurrentPhase == GrenadeThrowPhase.Animating;
	public bool IsAiming => m_CurrentPhase == GrenadeThrowPhase.Aiming;
	public GrenadeType SelectedType => m_SelectedType;
	public GrenadeThrowData Data => m_Data;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		ResolveAimLayerIndex();
	}

	private void OnDisable()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.None)
			CancelThrow();
	}
	#endregion

	#region Public Methods
	public bool CanStartThrow()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.None)
			return false;

		if (m_BusyState != null && m_BusyState.IsBusy)
			return false;

		if (m_Inventory == null)
			return false;

		if (!HasAnyGrenade())
			return false;

		return true;
	}

	public bool HasAnyGrenade()
	{
		if (m_Inventory == null)
			return false;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			if (m_Inventory.BagItems[i].Definition != null && m_Inventory.BagItems[i].Definition.IsGrenade)
				return true;
		}

		return false;
	}

	public void BeginAiming()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.None)
			return;

		m_CurrentPhase = GrenadeThrowPhase.Aiming;
		ThrowStarted?.Invoke();
	}

	public void CancelAiming()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Aiming)
			return;

		m_CurrentPhase = GrenadeThrowPhase.None;
	}

	public bool CycleSelectedType()
	{
		GrenadeType[] order = { GrenadeType.Fragmentation, GrenadeType.Smoke, GrenadeType.Flash };
		int startIndex = Array.IndexOf(order, m_SelectedType);
		if (startIndex < 0)
			startIndex = 0;

		for (int offset = 1; offset <= order.Length; offset++)
		{
			int idx = (startIndex + offset) % order.Length;
			if (IsGrenadeTypeAvailable(order[idx]))
			{
				m_SelectedType = order[idx];
				return true;
			}
		}

		return false;
	}

	public bool SetSelectedType(GrenadeType _type)
	{
		if (!IsGrenadeTypeAvailable(_type))
			return false;

		m_SelectedType = _type;
		return true;
	}

	public bool IsGrenadeTypeAvailable(GrenadeType _type)
	{
		if (m_Inventory == null)
			return false;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			ItemDefinition def = m_Inventory.BagItems[i].Definition;
			if (def != null && def.IsGrenade && def.GrenadeType == _type)
				return true;
		}

		return false;
	}

	public int GetGrenadeCount(GrenadeType _type)
	{
		if (m_Inventory == null)
			return 0;

		int count = 0;
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			ItemDefinition def = m_Inventory.BagItems[i].Definition;
			if (def != null && def.IsGrenade && def.GrenadeType == _type)
				count++;
		}

		return count;
	}

	/// <summary>
	/// Возвращает доступные гранаты с учётом уже заказанных на маршруте.
	/// </summary>
	public List<ItemDefinition> GetAvailableGrenadesFiltered(int _alreadyOrderedFrag, int _alreadyOrderedFlash, int _alreadyOrderedSmoke)
	{
		List<ItemDefinition> result = new List<ItemDefinition>();
		if (m_Inventory == null)
			return result;

		int fragUsed = 0, flashUsed = 0, smokeUsed = 0;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			ItemDefinition def = m_Inventory.BagItems[i].Definition;
			if (def == null || !def.IsGrenade)
				continue;

			switch (def.GrenadeType)
			{
				case GrenadeType.Fragmentation:
					if (fragUsed < _alreadyOrderedFrag) { fragUsed++; continue; }
					break;
				case GrenadeType.Flash:
					if (flashUsed < _alreadyOrderedFlash) { flashUsed++; continue; }
					break;
				case GrenadeType.Smoke:
					if (smokeUsed < _alreadyOrderedSmoke) { smokeUsed++; continue; }
					break;
			}

			result.Add(def);
		}

		result.Sort((a, b) => GetSortOrder(a).CompareTo(GetSortOrder(b)));
		return result;
	}

	public void SetTargetPosition(Vector3 _worldPos)
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Aiming)
			return;

		Vector3 origin = transform.position + Vector3.up * (m_Data != null ? m_Data.ReleaseHeight : 1.5f);
		float dist = Vector3.Distance(origin, _worldPos);
		float maxRange = m_Data != null ? m_Data.MaxRange : 35f;

		if (dist > maxRange)
			_worldPos = origin + (_worldPos - origin).normalized * maxRange;

		m_TargetWorldPosition = _worldPos;
	}

	private Vector3 ApplyLandingSpread(Vector3 _aimTarget)
	{
		float dist = Vector3.Distance(transform.position, _aimTarget);
		float baseSpread = 0.5f;
		float distanceCoeff = 0.08f;
		float rankFactor = ResolveThrowRankFactor();
		float spreadRadius = (baseSpread + dist * distanceCoeff) * rankFactor;

		float reserveFactor = m_Data != null ? m_Data.RollReserveFactor : 0.4f;
		float reserveAbsolute = m_Data != null ? m_Data.RollReserveAbsolute : 0.3f;
		float rollReserve = Mathf.Max(spreadRadius * reserveFactor, reserveAbsolute);
		float effectiveSpread = Mathf.Max(spreadRadius - rollReserve, 0.05f);

		Vector3 origin = transform.position;
		Vector3 toTarget = _aimTarget - origin;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude > 0.001f)
		{
			float pullBack = Mathf.Min(rollReserve, toTarget.magnitude * 0.5f);
			_aimTarget = origin + toTarget.normalized * (toTarget.magnitude - pullBack);
		}

		float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
		Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * effectiveSpread;
		return _aimTarget + offset;
	}

	private float ResolveThrowRankFactor()
	{
		UnitCombatStats stats = GetComponent<UnitCombatStats>();
		if (stats == null)
			return 1f;

		float skill01 = (stats.Marksmanship + stats.WeaponHandling) / 200f;
		return Mathf.Lerp(1.4f, 0.4f, skill01);
	}

	public bool ConfirmThrow(bool _isOnRoute)
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Aiming)
			return false;

		Vector3 origin = transform.position + Vector3.up * (m_Data != null ? m_Data.ReleaseHeight : 1.5f);
		float dist = Vector3.Distance(origin, m_TargetWorldPosition);
		float minRange = m_Data != null ? m_Data.MinRange : 5f;

		if (dist < minRange)
			m_TargetWorldPosition = origin + (m_TargetWorldPosition - origin).normalized * minRange;

		ItemDefinition grenadeDef = FindFirstGrenadeByType(m_SelectedType);
		if (grenadeDef == null)
			return false;

		m_ActiveGrenadeDefinition = grenadeDef;
		m_IsThrowOnRoute = _isOnRoute;
		m_PendingThrowOnRoute = _isOnRoute;
		m_WeaponShownThisThrow = false;
		m_ThrowDirection = m_TargetWorldPosition;
		m_TargetWorldPosition = ApplyLandingSpread(m_TargetWorldPosition);

		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.Throw, true);

		if (!_isOnRoute)
			PauseNavAgent();

		StopThrowTurnCoroutine();
		m_ThrowTurnCoroutine = StartCoroutine(WaitTurnThenThrow());
		return true;
	}

	private float ResolveRotateSpeed()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.RotateSpeed;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.RotateSpeed;
		return 6f;
	}

	private UnityEngine.AI.NavMeshAgent m_CachedNavAgent;
	private Coroutine m_HoldStopCoroutine;

	private void PauseNavAgent()
	{
		if (m_CachedNavAgent == null)
			m_CachedNavAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
		if (m_CachedNavAgent != null && m_CachedNavAgent.isOnNavMesh)
			m_CachedNavAgent.isStopped = true;

		if (m_HoldStopCoroutine != null)
			StopCoroutine(m_HoldStopCoroutine);
		m_HoldStopCoroutine = StartCoroutine(HoldAgentStopped());
	}

	private void ResumeNavAgent()
	{
		if (m_HoldStopCoroutine != null)
		{
			StopCoroutine(m_HoldStopCoroutine);
			m_HoldStopCoroutine = null;
		}

		if (m_CachedNavAgent != null && m_CachedNavAgent.isOnNavMesh)
			m_CachedNavAgent.isStopped = false;
	}

	private IEnumerator HoldAgentStopped()
	{
		while (m_CurrentPhase == GrenadeThrowPhase.Aiming || m_CurrentPhase == GrenadeThrowPhase.Animating)
		{
			if (m_CachedNavAgent != null && m_CachedNavAgent.isOnNavMesh)
			{
				m_CachedNavAgent.isStopped = true;
				m_CachedNavAgent.velocity = Vector3.zero;
			}
			yield return null;
		}
	}

	private IEnumerator WaitTurnThenThrow()
	{
		Vector3 directionPoint = m_ThrowDirection;
		float rotateSpeed = ResolveRotateSpeed();

		while (m_CurrentPhase == GrenadeThrowPhase.Aiming)
		{
			Vector3 dir = directionPoint - transform.position;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.001f)
				break;

			Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);

			if (Quaternion.Angle(transform.rotation, targetRot) < 3f)
			{
				transform.rotation = targetRot;
				break;
			}

			yield return null;
		}

		m_ThrowTurnCoroutine = null;

		if (m_CurrentPhase != GrenadeThrowPhase.Aiming)
			yield break;

		ResolveAimLayerIndex();
		if (m_Animator != null && m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 1f);

		if (m_Animator != null)
			m_Animator.SetTrigger(s_GrenadeThrow);

		m_CurrentPhase = GrenadeThrowPhase.Animating;
		StartThrowCompletionWatch();
	}

	private void StopThrowTurnCoroutine()
	{
		if (m_ThrowTurnCoroutine != null)
		{
			StopCoroutine(m_ThrowTurnCoroutine);
			m_ThrowTurnCoroutine = null;
		}
	}

	public void CancelThrow()
	{
		if (m_CurrentPhase == GrenadeThrowPhase.None)
			return;

		StopThrowTurnCoroutine();
		StopThrowCompletionWatch();
		ClearHandGrenadeVisual();

		if (m_Animator != null && m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);

		if (m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(true);

		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.Throw, false);

		ResumeNavAgent();

		if (m_CurrentPhase == GrenadeThrowPhase.Aiming)
			ThrowCompleted?.Invoke();

		m_CurrentPhase = GrenadeThrowPhase.None;
		m_ActiveGrenadeDefinition = null;
	}
	#endregion

	#region Animation Events
	public void AnimationEvent_GrenadeHideWeapon()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Animating)
			return;

		if (m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(false);
	}

	public void AnimationEvent_GrenadeShowInHand()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Animating)
			return;

		SpawnHandGrenadeVisual();
	}

	public void AnimationEvent_GrenadePinPullSound()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Animating)
			return;

		PlayPinPullSound();
	}

	public void AnimationEvent_GrenadePinPull()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Animating)
			return;

		DetachPinAndDrop();
	}

	public void AnimationEvent_GrenadeRelease()
	{
		if (m_CurrentPhase != GrenadeThrowPhase.Animating)
			return;

		PlayLeverReleaseSound();
		LaunchProjectile();
	}

	public void AnimationEvent_GrenadeShowWeapon()
	{
		m_WeaponShownThisThrow = true;
		if (m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(true);
		ResumeNavAgent();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();

		if (m_RightHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_RightHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);

		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
	}

	private void ResolveAimLayerIndex()
	{
		m_AimLayerIndex = m_Animator != null ? m_Animator.GetLayerIndex(AimLayerName) : -1;
	}

	private ItemDefinition FindFirstGrenadeByType(GrenadeType _type)
	{
		if (m_Inventory == null)
			return null;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			ItemDefinition def = m_Inventory.BagItems[i].Definition;
			if (def != null && def.IsGrenade && def.GrenadeType == _type)
				return def;
		}

		return null;
	}

	private int FindFirstGrenadeBagIndex(GrenadeType _type)
	{
		if (m_Inventory == null)
			return -1;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			ItemDefinition def = m_Inventory.BagItems[i].Definition;
			if (def != null && def.IsGrenade && def.GrenadeType == _type)
				return i;
		}

		return -1;
	}

	private void SpawnHandGrenadeVisual()
	{
		ClearHandGrenadeVisual();

		Transform anchor = m_RightHandAnchor;
		if (m_Data == null || anchor == null || m_ActiveGrenadeDefinition == null)
			return;

		Vector3 localPos = m_ActiveGrenadeDefinition.RightHandLocalPosition;
		Quaternion localRot = m_ActiveGrenadeDefinition.RightHandLocalRotation;

		GameObject lootPrefab = m_ActiveGrenadeDefinition.DropWorldPrefab;
		if (lootPrefab == null)
		{
			Debug.LogWarning($"[GrenadeThrow] No DropWorldPrefab for {m_ActiveGrenadeDefinition.name}, falling back to hand prefab.");
			GameObject fallback = m_Data.GetHandPrefab(m_ActiveGrenadeDefinition);
			if (fallback != null)
			{
				m_HandGrenadeInstance = Instantiate(fallback, anchor);
				m_HandGrenadeInstance.transform.localPosition = localPos;
				m_HandGrenadeInstance.transform.localRotation = localRot;
			}
			return;
		}

		m_HandGrenadeInstance = new GameObject("HandGrenadeVisual");
		m_HandGrenadeInstance.transform.SetParent(anchor, false);
		m_HandGrenadeInstance.transform.localPosition = localPos;
		m_HandGrenadeInstance.transform.localRotation = localRot;

		GameObject visualSource = FindVisualChild(lootPrefab);
		if (visualSource != null)
		{
			GameObject visual = Instantiate(visualSource, m_HandGrenadeInstance.transform);
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
		}
		else
		{
			Debug.LogWarning($"[GrenadeThrow] No 'Visual' child in {lootPrefab.name}. Instantiating full prefab.");
			GameObject full = Instantiate(lootPrefab, m_HandGrenadeInstance.transform);
			full.transform.localPosition = Vector3.zero;
			full.transform.localRotation = Quaternion.identity;
			full.transform.localScale = Vector3.one;
			StripPhysicsComponents(full);
		}

		FindAndSeparateGrenadeParts(m_HandGrenadeInstance);

		DisablePhysicsOnVisual(m_HandGrenadeInstance);
	}

	private void FindAndSeparateGrenadeParts(GameObject _root)
	{
		m_PinInstance = null;
		m_LeverInstance = null;

		List<Transform> all = new List<Transform>();
		CollectDetachableFromVisual(_root.transform, all);

		for (int i = 0; i < all.Count; i++)
		{
			Transform child = all[i];
			string nameLower = child.name.ToLowerInvariant();
			if (nameLower.Contains("pin") || nameLower.Contains("пин"))
				m_PinInstance = child.gameObject;
			else if (nameLower.Contains("clip") || nameLower.Contains("куб"))
				m_LeverInstance = child.gameObject;
		}
	}

	private static GameObject FindVisualChild(GameObject _prefab)
	{
		Transform t = _prefab.transform.Find("Visual");
		if (t != null)
			return t.gameObject;

		for (int i = 0; i < _prefab.transform.childCount; i++)
		{
			Transform child = _prefab.transform.GetChild(i);
			Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
			if (renderers.Length > 0 && !child.TryGetComponent<Rigidbody>(out _))
				return child.gameObject;
		}

		return null;
	}

	private static void StripPhysicsComponents(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
			Destroy(bodies[i]);

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			Destroy(colliders[i]);

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			Destroy(pickups[i]);
	}

	private void ClearHandGrenadeVisual()
	{
		if (m_HandGrenadeInstance != null)
		{
			Destroy(m_HandGrenadeInstance);
			m_HandGrenadeInstance = null;
		}

		m_PinInstance = null;
		m_LeverInstance = null;
	}

	private bool IsNearCameraForDetail()
	{
		float threshold = m_Data != null ? m_Data.GrenadeDetailCullDistance : 12f;
		return WeaponVfxUtility.IsWithinDistance(transform.position, threshold);
	}

	private void DetachPinAndDrop()
	{
		if (m_PinInstance == null)
			return;

		GameObject pin = m_PinInstance;
		m_PinInstance = null;

		if (!IsNearCameraForDetail())
		{
			Destroy(pin);
			return;
		}

		pin.transform.SetParent(null, true);

		Vector3 dropPos = transform.position + transform.forward * 0.3f + Vector3.down * 0.2f;
		pin.transform.position = dropPos;

		Rigidbody rb = pin.GetComponent<Rigidbody>();
		if (rb == null)
			rb = pin.AddComponent<Rigidbody>();
		rb.mass = 0.02f;
		rb.isKinematic = false;
		rb.useGravity = true;
		rb.linearDamping = 0.5f;
		rb.angularDamping = 0.8f;

		CapsuleCollider cc = pin.GetComponent<Collider>() as CapsuleCollider;
		if (cc == null)
		{
			cc = pin.AddComponent<CapsuleCollider>();
			cc.radius = 0.005f;
			cc.height = 0.03f;
			cc.center = new Vector3(0f, 0.015f, 0f);
			cc.direction = 1;
		}

		Destroy(pin, 3f);
	}

	private void DetachLeverAndFly(Vector3 _grenadeVelocity)
	{
		if (m_LeverInstance == null)
			return;

		GameObject lever = m_LeverInstance;
		m_LeverInstance = null;

		if (!IsNearCameraForDetail())
		{
			Destroy(lever);
			return;
		}

		lever.transform.SetParent(null, true);

		Vector3 offset = new Vector3(
			UnityEngine.Random.Range(-0.3f, 0.3f),
			UnityEngine.Random.Range(-0.1f, 0.1f),
			UnityEngine.Random.Range(-0.3f, 0.3f));
		Vector3 leverVelocity = _grenadeVelocity * 0.9f + offset * 2f;

		Rigidbody rb = lever.GetComponent<Rigidbody>();
		if (rb == null)
			rb = lever.AddComponent<Rigidbody>();
		rb.mass = 0.03f;
		rb.isKinematic = false;
		rb.useGravity = true;
		rb.linearDamping = 0.2f;
		rb.angularDamping = 0.5f;
		rb.linearVelocity = leverVelocity;

		GrenadeDetachedPart detached = lever.AddComponent<GrenadeDetachedPart>();
		detached.Lifetime = 3f;
		detached.SinkSpeed = 0.5f;
	}

	private static void DetachGrenadeChildren(GameObject _grenade)
	{
		List<Transform> detachable = new List<Transform>();
		CollectDetachableFromVisual(_grenade.transform, detachable);

		for (int i = 0; i < detachable.Count; i++)
		{
			Transform child = detachable[i];
			child.SetParent(null, true);
			Destroy(child.gameObject, 3f);
		}
	}

	private static void CollectDetachableFromVisual(Transform _parent, List<Transform> _results)
	{
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform child = _parent.GetChild(i);
			if (child.name == "Visual")
				CollectDetachableFromVisual(child, _results);
			else
				_results.Add(child);
		}
	}

	private void LaunchProjectile()
	{
		if (m_Data == null || m_ActiveGrenadeDefinition == null)
			return;

		GrenadeType type = m_ActiveGrenadeDefinition.GrenadeType;

		int bagIndex = FindFirstGrenadeBagIndex(type);
		if (bagIndex >= 0)
			m_Inventory.TryRemoveBagAt(bagIndex, out _);

		RefreshInventoryUiIfActive();

		LaunchHandGrenadeAsProjectile();
	}

	private void LaunchHandGrenadeAsProjectile()
	{
		if (m_HandGrenadeInstance == null)
			return;

		GameObject grenade = m_HandGrenadeInstance;
		m_HandGrenadeInstance = null;

		grenade.transform.SetParent(null, true);
		Vector3 launchPos = grenade.transform.position;
		float arcHeight = m_Data != null ? m_Data.ArcHeight : 3f;
		Vector3 velocity = GrenadeProjectile.CalculateLaunchVelocity(launchPos, m_TargetWorldPosition, arcHeight);

		DetachLeverAndFly(velocity);
		DetachGrenadeChildren(grenade);

		Collider[] cols = grenade.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < cols.Length; i++)
		{
			cols[i].enabled = true;
			cols[i].isTrigger = false;
		}

		if (cols.Length == 0)
		{
			CapsuleCollider cc = grenade.AddComponent<CapsuleCollider>();
			cc.radius = 0.03f;
			cc.height = 0.1f;
			cc.center = new Vector3(0f, 0.05f, 0f);
			cc.direction = 1;

			PhysicsMaterial pm = new PhysicsMaterial("GrenadePhysic")
			{
				bounciness = 0.22f,
				bounceCombine = PhysicsMaterialCombine.Average,
				dynamicFriction = 0.45f,
				staticFriction = 0.5f,
				frictionCombine = PhysicsMaterialCombine.Average
			};
			cc.material = pm;
		}

		Rigidbody rb = grenade.GetComponent<Rigidbody>();
		if (rb == null)
			rb = grenade.AddComponent<Rigidbody>();

		rb.mass = 0.35f;
		rb.isKinematic = false;
		rb.useGravity = true;
		rb.linearDamping = 0.1f;
		rb.angularDamping = 0.3f;
		rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		rb.interpolation = RigidbodyInterpolation.Interpolate;

		GrenadeProjectile gp = grenade.GetComponent<GrenadeProjectile>();
		if (gp == null)
			gp = grenade.AddComponent<GrenadeProjectile>();

		rb.linearVelocity = velocity;
		gp.Initialize(m_TargetWorldPosition, m_Data, gameObject, m_ActiveGrenadeDefinition);
	}

	private void RefreshInventoryUiIfActive()
	{
		if (m_InventoryRefForUi == null)
			m_InventoryRefForUi = m_Inventory;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return;
		if (bindings.ActiveCharacterInventory != m_InventoryRefForUi)
			return;

		bindings.RefreshActiveCharacterPanel();
	}

	private Vector3 GetProjectileSpawnPosition()
	{
		float height = m_Data != null ? m_Data.ReleaseHeight : 1.5f;
		Vector3 forward = transform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.001f)
			forward = Vector3.forward;
		else
			forward.Normalize();
		return transform.position + forward * 0.6f + Vector3.up * height;
	}

	private void FinishThrow()
	{
		m_CurrentPhase = GrenadeThrowPhase.None;

		if (m_Animator != null && m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);

		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.Throw, false);

		m_ActiveGrenadeDefinition = null;
		ResumeNavAgent();
		ThrowCompleted?.Invoke();
	}

	private void TurnTowardTarget(Vector3 _target)
	{
		Vector3 dir = _target - transform.position;
		dir.y = 0f;
		if (dir.sqrMagnitude > 0.001f)
			transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
	}

	private void PlayPinPullSound()
	{
		if (m_Data == null)
			return;

		if (m_Data.TryPickPinPullSound(out AudioClip clip))
		{
			Vector3 pos = m_RightHandAnchor != null ? m_RightHandAnchor.position : transform.position + Vector3.up * 1.2f;
			UnitNonFireAudioUtility.PlayAtPoint(clip, pos, m_Data.PinPullVolume, m_Data.PinPullMaxDistance);
		}
	}

	private void PlayLeverReleaseSound()
	{
		if (m_Data == null)
			return;

		if (m_Data.TryPickLeverReleaseSound(out AudioClip clip))
		{
			Vector3 pos = m_RightHandAnchor != null ? m_RightHandAnchor.position : transform.position + Vector3.up * 1.2f;
			UnitNonFireAudioUtility.PlayAtPoint(clip, pos, m_Data.LeverReleaseVolume, m_Data.LeverReleaseMaxDistance);
		}
	}

	private void PlayThrowSound()
	{
		if (m_Data == null)
			return;

		if (m_Data.TryPickThrowSound(out AudioClip clip))
		{
			Vector3 pos = transform.position + Vector3.up * 1.2f;
			UnitNonFireAudioUtility.PlayAtPoint(clip, pos, m_Data.ThrowVolume);
		}
	}

	private static void DisablePhysicsOnVisual(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;
	}

	private static int GetSortOrder(ItemDefinition _def)
	{
		if (_def == null)
			return 99;

		return _def.GrenadeType switch
		{
			GrenadeType.Fragmentation => 0,
			GrenadeType.Flash => 1,
			GrenadeType.Smoke => 2,
			_ => 3
		};
	}

	private void StartThrowCompletionWatch()
	{
		StopThrowCompletionWatch();
		m_ThrowCompletionCoroutine = StartCoroutine(WatchThrowAnimationCompletion());
	}

	private void StopThrowCompletionWatch()
	{
		if (m_ThrowCompletionCoroutine != null)
		{
			StopCoroutine(m_ThrowCompletionCoroutine);
			m_ThrowCompletionCoroutine = null;
		}
	}

	private IEnumerator WatchThrowAnimationCompletion()
	{
		float timeout = 5f;
		float elapsed = 0f;
		bool hasSeenThrowState = false;

		ResolveAimLayerIndex();

		while (m_CurrentPhase == GrenadeThrowPhase.Animating && elapsed < timeout)
		{
			if (m_Animator != null && m_AimLayerIndex >= 0)
			{
				AnimatorStateInfo stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_AimLayerIndex);
				bool isInThrowState = stateInfo.IsName(GrenadeThrowStateName);

				if (isInThrowState)
				{
					hasSeenThrowState = true;

					if (stateInfo.normalizedTime >= 1f)
						break;
				}
				else if (hasSeenThrowState)
				{
					break;
				}
			}

			elapsed += Time.deltaTime;
			yield return null;
		}

		if (m_CurrentPhase == GrenadeThrowPhase.Animating)
		{
			if (!m_WeaponShownThisThrow && m_Equipment != null)
				m_Equipment.SetMainWeaponVisualActive(true);

			FinishThrow();
		}

		m_ThrowCompletionCoroutine = null;
	}
	#endregion
}
