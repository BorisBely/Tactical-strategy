using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Настраивает мишени полигона в сцене и управляет ими из UI.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShootingRangeManager : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private ShootingRangeTargetRegistry m_TargetRegistry;
	[SerializeField] private string m_TargetNamePattern = @"^Sphere(10|20|30|40|50|60|70|80|90|100)$";
	[SerializeField, Min(10f)] private float m_PlayerVisionRange = 120f;
	[SerializeField] private bool m_AutoDiscoverTargetsOnAwake = true;
	[SerializeField] private bool m_StartWithTargetsEnabled = true;
	[SerializeField] private int m_TargetLayer = 8;

	[Header("Player Unit Rank")]
	[Tooltip("Ранги от худшего к лучшему для кнопки смены ранга на полигоне.")]
	[SerializeField] private UnitCombatRankDefinition[] m_RankCycleOrder;
	#endregion

	#region Private Fields
	private readonly List<ShootingRangeTarget> m_Targets = new List<ShootingRangeTarget>(16);
	private Regex m_NameRegex;
	#endregion

	#region Public Properties
	public IReadOnlyList<ShootingRangeTarget> Targets => m_Targets;
	#endregion

	#region Public Events
	public event Action TargetsChanged;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveRegistry();
		m_NameRegex = new Regex(m_TargetNamePattern);

		if (m_AutoDiscoverTargetsOnAwake)
			DiscoverAndConfigureTargets();
	}

	private void Start()
	{
		StartCoroutine(InitializeAfterUnitsSpawned());
	}

	private IEnumerator InitializeAfterUnitsSpawned()
	{
		for (int i = 0; i < 5; i++)
		{
			ApplyPlayerVisionRange();
			yield return null;
		}

		RefreshTargetList();
		SetAllTargetsEnabled(m_StartWithTargetsEnabled);
		NotifyShootingRangeUi();
	}

	private static void NotifyShootingRangeUi()
	{
#if UNITY_2023_1_OR_NEWER
		ShootingRangeUiController uiController = UnityEngine.Object.FindAnyObjectByType<ShootingRangeUiController>(FindObjectsInactive.Exclude);
#else
		ShootingRangeUiController uiController = UnityEngine.Object.FindObjectOfType<ShootingRangeUiController>();
#endif
		uiController?.RefreshPanelState();
	}

	private void OnEnable()
	{
		RefreshTargetList();
	}
	#endregion

	#region Public Methods
	public void DiscoverAndConfigureTargets()
	{
		ResolveRegistry();

#if UNITY_2023_1_OR_NEWER
		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		Transform[] transforms = FindObjectsOfType<Transform>();
#endif
		for (int i = 0; i < transforms.Length; i++)
		{
			Transform t = transforms[i];
			if (t == null || !m_NameRegex.IsMatch(t.name))
				continue;

			ConfigureTargetObject(t.gameObject);
		}

		RefreshTargetList();
	}

	public void ResetAllTargets()
	{
		for (int i = 0; i < m_Targets.Count; i++)
		{
			if (m_Targets[i] != null)
				m_Targets[i].ResetTarget();
		}

		RequestVisionRescanForPlayers();
	}

	public void ResetAllTargetsHealth()
	{
		for (int i = 0; i < m_Targets.Count; i++)
		{
			if (m_Targets[i] != null)
				m_Targets[i].ResetTargetHealth();
		}
	}

	public void SetAllTargetsEnabled(bool _enabled)
	{
		for (int i = 0; i < m_Targets.Count; i++)
		{
			if (m_Targets[i] != null)
				m_Targets[i].SetUserEnabled(_enabled);
		}

		RequestVisionRescanForPlayers();
	}

	public void ResetTarget(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

		_target.ResetTarget();
		RequestVisionRescanForPlayers();
	}

	public void ResetTargetHealth(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

		_target.ResetTargetHealth();
	}

	public void ToggleTarget(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

		_target.SetUserEnabled(!_target.IsUserEnabled);
		RequestVisionRescanForPlayers();
	}

	public void SetTargetEnabled(ShootingRangeTarget _target, bool _enabled)
	{
		if (_target == null)
			return;

		_target.SetUserEnabled(_enabled);
		RequestVisionRescanForPlayers();
	}

	public bool TryGetTargetByDistanceMeters(int _distanceMeters, out ShootingRangeTarget _target)
	{
		string targetName = $"Sphere{_distanceMeters}";
		for (int i = 0; i < m_Targets.Count; i++)
		{
			ShootingRangeTarget candidate = m_Targets[i];
			if (candidate != null && candidate.DisplayName == targetName)
			{
				_target = candidate;
				return true;
			}
		}

		_target = null;
		return false;
	}

	public void ResetTargetByDistanceMeters(int _distanceMeters)
	{
		if (TryGetTargetByDistanceMeters(_distanceMeters, out ShootingRangeTarget target))
			ResetTarget(target);
	}

	public void ResetTargetHealthByDistanceMeters(int _distanceMeters)
	{
		if (TryGetTargetByDistanceMeters(_distanceMeters, out ShootingRangeTarget target))
			ResetTargetHealth(target);
	}

	public void ToggleTargetByDistanceMeters(int _distanceMeters)
	{
		if (TryGetTargetByDistanceMeters(_distanceMeters, out ShootingRangeTarget target))
			ToggleTarget(target);
	}

	public bool TryCyclePlayerUnitRank(out string _newRankLabel)
	{
		_newRankLabel = "—";

		if (!TryFindPlayerUnitCombatStats(out UnitCombatStats combatStats))
			return false;

		UnitCombatRankDefinition[] rankOrder = ResolveRankCycleOrder();
		if (rankOrder == null || rankOrder.Length == 0)
			return false;

		UnitCombatRankDefinition nextRank = UnitCombatRankCycle.GetNextRank(combatStats.RankPreset, rankOrder);
		if (nextRank == null)
			return false;

		combatStats.ApplyRankPreset(nextRank);
		_newRankLabel = UnitCombatRankCycle.ResolveRankLabel(nextRank);
		Debug.Log(
			$"[Полигон] Ранг юнита: {_newRankLabel} | меткость {combatStats.Marksmanship:F0} | handling {combatStats.WeaponHandling:F0} | отдача {combatStats.RecoilControl:F0} | юнит: {combatStats.gameObject.name}",
			this);
		return true;
	}

	public string GetPlayerUnitRankLabel()
	{
		if (!TryFindPlayerUnitCombatStats(out UnitCombatStats combatStats))
			return "—";

		return UnitCombatRankCycle.ResolveRankLabel(combatStats.RankPreset);
	}

	public bool TryAddPlayerDebugInjury(PlayerDebugInjuryType _injuryType)
	{
		if (!TryFindPlayerUnitHealth(out UnitHealth health))
		{
			Debug.LogWarning("[Полигон] Не удалось добавить травму: не найден UnitHealth у активного игрока.", this);
			return false;
		}

		switch (_injuryType)
		{
			case PlayerDebugInjuryType.ArmBleeding:
				health.AddDebugInjuryArmBleeding();
				break;
			case PlayerDebugInjuryType.LegFracture:
				health.AddDebugInjuryLegFracture();
				break;
			case PlayerDebugInjuryType.LungDamage:
				health.AddDebugInjuryLungDamage();
				break;
			default:
				return false;
		}

		Debug.Log($"[Полигон] Добавлена debug-травма {_injuryType} | юнит: {health.gameObject.name}", this);
		return true;
	}

	public bool TryClearPlayerInjuries()
	{
		if (!TryFindPlayerUnitHealth(out UnitHealth health))
		{
			Debug.LogWarning("[Полигон] Не удалось очистить травмы: не найден UnitHealth у активного игрока.", this);
			return false;
		}

		health.ClearInjuries();
		Debug.Log($"[Полигон] Травмы очищены | юнит: {health.gameObject.name}", this);
		return true;
	}

	public void ApplyPlayerVisionRange()
	{
#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		UnitVision[] visions = FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
		{
			UnitVision vision = visions[i];
			if (vision == null)
				continue;

			UnitTeam team = vision.GetComponent<UnitTeam>();
			if (team != null && team.Team == UnitTeamId.Player)
				vision.SetVisionRange(m_PlayerVisionRange);
		}
	}
	#endregion

	#region Private Methods
	private void ResolveRegistry()
	{
		if (m_TargetRegistry != null)
			return;

		m_TargetRegistry = GetComponent<ShootingRangeTargetRegistry>();
		if (m_TargetRegistry == null)
			m_TargetRegistry = gameObject.AddComponent<ShootingRangeTargetRegistry>();
	}

	private void ConfigureTargetObject(GameObject _go)
	{
		if (_go == null)
			return;

		_go.layer = m_TargetLayer;

		SphereCollider sphereCollider = _go.GetComponent<SphereCollider>();
		if (sphereCollider == null)
		{
			BoxCollider boxCollider = _go.GetComponent<BoxCollider>();
			if (boxCollider != null)
				Destroy(boxCollider);

			sphereCollider = _go.AddComponent<SphereCollider>();
		}

		sphereCollider.radius = 0.5f;
		sphereCollider.center = Vector3.zero;

		ShootingRangeTarget target = _go.GetComponent<ShootingRangeTarget>();
		if (target == null)
			target = _go.AddComponent<ShootingRangeTarget>();

		target.ResetTargetHealth();
		target.SetUserEnabled(m_StartWithTargetsEnabled);
	}

	private void RefreshTargetList()
	{
		m_Targets.Clear();
		if (m_TargetRegistry == null)
			return;

		IReadOnlyList<ShootingRangeTarget> all = m_TargetRegistry.GetAllTargets();
		for (int i = 0; i < all.Count; i++)
		{
			ShootingRangeTarget target = all[i];
			if (target == null)
				continue;

			if (!m_Targets.Contains(target))
				m_Targets.Add(target);
		}

		m_Targets.Sort(CompareTargetsByName);
		TargetsChanged?.Invoke();
	}

	private static int CompareTargetsByName(ShootingRangeTarget _a, ShootingRangeTarget _b)
	{
		if (_a == null && _b == null)
			return 0;
		if (_a == null)
			return 1;
		if (_b == null)
			return -1;

		int distanceCompare = GetTargetDistanceSortKey(_a).CompareTo(GetTargetDistanceSortKey(_b));
		return distanceCompare != 0
			? distanceCompare
			: string.CompareOrdinal(_a.DisplayName, _b.DisplayName);
	}

	private static int GetTargetDistanceSortKey(ShootingRangeTarget _target)
	{
		if (_target == null)
			return int.MaxValue;

		string name = _target.DisplayName;
		if (name != null &&
		    name.StartsWith("Sphere") &&
		    int.TryParse(name.Substring(6), out int distanceMeters))
		{
			return distanceMeters;
		}

		return int.MaxValue - 1;
	}

	private UnitCombatRankDefinition[] ResolveRankCycleOrder()
	{
		if (m_RankCycleOrder == null || m_RankCycleOrder.Length == 0)
			return null;

		int assignedCount = 0;
		for (int i = 0; i < m_RankCycleOrder.Length; i++)
		{
			if (m_RankCycleOrder[i] != null)
				assignedCount++;
		}

		return assignedCount > 0 ? m_RankCycleOrder : null;
	}

	private bool TryFindPlayerUnitCombatStats(out UnitCombatStats _combatStats)
	{
		return UnitCombatStatsLookup.TryGetActivePlayerCombatStats(out _combatStats);
	}

	private bool TryFindPlayerUnitHealth(out UnitHealth _health)
	{
		_health = null;
		if (!TryFindPlayerUnitCombatStats(out UnitCombatStats combatStats))
			return false;

		_health = combatStats.GetComponent<UnitHealth>();
		if (_health == null)
			_health = combatStats.GetComponentInParent<UnitHealth>();

		return _health != null;
	}

	private void RequestVisionRescanForPlayers()
	{
#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		UnitVision[] visions = FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
			visions[i]?.RequestImmediateScan();
	}
	#endregion
}
