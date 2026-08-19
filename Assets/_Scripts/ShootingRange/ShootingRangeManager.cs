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
	[SerializeField] private string m_TargetNamePattern = @"^Sphere(50|100|150|200|250|300|350|400|450|500)$";
	[SerializeField, Min(10f), Tooltip("Must match Unit.prefab VisionRange (500 m). Padding past 500 (e.g. 550) leaks perception beyond the contract cap.")]
	private float m_PlayerVisionRange = 500f;
	[SerializeField] private bool m_AutoDiscoverTargetsOnAwake = true;
	[SerializeField] private bool m_StartWithTargetsEnabled = true;
	[SerializeField] private int m_TargetLayer = 8;

	[Header("Player Unit Rank")]
	[Tooltip("Ранги от худшего к лучшему для кнопки смены ранга на полигоне.")]
	[SerializeField] private UnitCombatRankDefinition[] m_RankCycleOrder;

	[Header("Target Hit Counter")]
	[Tooltip("Режим счётчика попаданий для всех мишеней: Off, 1, 2, 3, 5 или 10 попаданий до авто-выключения.")]
	[SerializeField] private ShootingRangeTargetHitCounterMode m_HitCounterMode = ShootingRangeTargetHitCounterMode.None;

	[Header("Impact Surface Test")]
	[Tooltip("Чередует Concrete / Metal / Wood / Glass на мишенях для теста звуков и декалей попадания.")]
	[SerializeField] private bool m_AssignImpactTestSurfaces = true;
	[SerializeField] private PhysicsMaterial m_SurfaceConcrete;
	[SerializeField] private PhysicsMaterial m_SurfaceMetal;
	[SerializeField] private PhysicsMaterial m_SurfaceWood;
	[SerializeField] private PhysicsMaterial m_SurfaceGlass;
	[SerializeField] private Material m_VisualConcrete;
	[SerializeField] private Material m_VisualMetal;
	[SerializeField] private Material m_VisualWood;
	[SerializeField] private Material m_VisualGlass;
	#endregion

	#region Private Fields
	private readonly List<ShootingRangeTarget> m_Targets = new List<ShootingRangeTarget>(16);
	private readonly List<UnitCombatStats> m_SelectedCombatStatsBuffer = new List<UnitCombatStats>(16);
	private Regex m_NameRegex;

	private static readonly Color s_ColorConcrete = new Color(0.55f, 0.55f, 0.55f, 1f);
	private static readonly Color s_ColorMetal = new Color(0.70f, 0.72f, 0.76f, 1f);
	private static readonly Color s_ColorWood = new Color(0.58f, 0.36f, 0.18f, 1f);
	private static readonly Color s_ColorGlass = new Color(0.55f, 0.78f, 0.92f, 1f);
	private static readonly Color s_DisabledSurfaceColor = new Color(0.35f, 0.35f, 0.35f, 0.25f);
	#endregion

	#region Public Properties
	public IReadOnlyList<ShootingRangeTarget> Targets => m_Targets;
	public ShootingRangeTargetHitCounterMode HitCounterMode => m_HitCounterMode;
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
		ApplyHitCounterModeToAllTargets();
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
		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
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

		if (!TryCollectSelectedPlayerCombatStats(out IReadOnlyList<UnitCombatStats> combatStatsList))
			return false;

		UnitCombatRankDefinition[] rankOrder = ResolveRankCycleOrder();
		if (rankOrder == null || rankOrder.Length == 0)
			return false;

		bool changedAny = false;
		for (int i = 0; i < combatStatsList.Count; i++)
		{
			UnitCombatStats combatStats = combatStatsList[i];
			if (combatStats == null)
				continue;

			UnitCombatRankDefinition nextRank = UnitCombatRankCycle.GetNextRank(combatStats.RankPreset, rankOrder);
			if (nextRank == null)
				continue;

			combatStats.ApplyRankPreset(nextRank);
			changedAny = true;
			Debug.Log(
				$"[Полигон] Ранг юнита: {UnitCombatRankCycle.ResolveRankLabel(nextRank)} | меткость {combatStats.Marksmanship:F0} | handling {combatStats.WeaponHandling:F0} | отдача {combatStats.RecoilControl:F0} | юнит: {ResolveUnitDisplayName(combatStats)}",
				this);
		}

		if (!changedAny)
			return false;

		_newRankLabel = BuildSelectedUnitRankLabel(combatStatsList);
		return true;
	}

	public bool CanCyclePlayerUnitRank()
	{
		if (!TryCollectSelectedPlayerCombatStats(out _))
			return false;

		UnitCombatRankDefinition[] rankOrder = ResolveRankCycleOrder();
		return rankOrder != null && rankOrder.Length > 0;
	}

	public string GetPlayerUnitRankLabel()
	{
		if (!TryCollectSelectedPlayerCombatStats(out IReadOnlyList<UnitCombatStats> combatStatsList))
			return "— (select unit)";

		return BuildSelectedUnitRankLabel(combatStatsList);
	}

	public string GetHitCounterModeLabel()
	{
		return ShootingRangeTargetHitCounterModeUtility.GetDisplayLabel(m_HitCounterMode);
	}

	public bool TryCycleHitCounterMode(out string _newModeLabel)
	{
		m_HitCounterMode = ShootingRangeTargetHitCounterModeUtility.GetNextMode(m_HitCounterMode);
		ApplyHitCounterModeToAllTargets();
		_newModeLabel = GetHitCounterModeLabel();
		Debug.Log($"[Полигон] Счётчик попаданий: {_newModeLabel}", this);
		return true;
	}

	public void ApplyHitCounterModeToAllTargets()
	{
		for (int i = 0; i < m_Targets.Count; i++)
		{
			if (m_Targets[i] != null)
				m_Targets[i].SetHitCounterMode(m_HitCounterMode);
		}
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
			case PlayerDebugInjuryType.RandomWound:
				health.AddDebugRandomWoundAndKnockout();
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
		if (DetectionHarnessPlayMode.IsCalibrationPlay)
			return;

#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude);
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

		if (m_AssignImpactTestSurfaces)
			ApplyImpactTestSurface(_go, sphereCollider, target);

		target.ResetTargetHealth();
		target.SetHitCounterMode(m_HitCounterMode);
		target.SetUserEnabled(m_StartWithTargetsEnabled);
	}

	private void ApplyImpactTestSurface(GameObject _go, Collider _collider, ShootingRangeTarget _target)
	{
		if (!TryResolveImpactTestSurface(_go.name, out _, out PhysicsMaterial physicsMaterial, out Material visualMaterial, out Color intactColor))
			return;

		if (_collider != null && physicsMaterial != null)
			_collider.sharedMaterial = physicsMaterial;

		if (_go.TryGetComponent(out Renderer renderer) && visualMaterial != null)
			renderer.sharedMaterial = visualMaterial;

		_target?.ConfigureSurfaceVisual(intactColor, s_DisabledSurfaceColor);
	}

	private bool TryResolveImpactTestSurface(
		string _objectName,
		out string _surfaceName,
		out PhysicsMaterial _physicsMaterial,
		out Material _visualMaterial,
		out Color _intactColor)
	{
		_surfaceName = null;
		_physicsMaterial = null;
		_visualMaterial = null;
		_intactColor = s_ColorConcrete;

		if (!TryParseDistanceMeters(_objectName, out int distanceMeters))
			return false;

		int surfaceIndex = (distanceMeters / 50 - 1) % 4;
		if (surfaceIndex < 0)
			surfaceIndex = 0;

		switch (surfaceIndex)
		{
			case 0:
				_surfaceName = "Concrete";
				_physicsMaterial = m_SurfaceConcrete;
				_visualMaterial = m_VisualConcrete;
				_intactColor = s_ColorConcrete;
				return true;
			case 1:
				_surfaceName = "Metal";
				_physicsMaterial = m_SurfaceMetal;
				_visualMaterial = m_VisualMetal;
				_intactColor = s_ColorMetal;
				return true;
			case 2:
				_surfaceName = "Wood";
				_physicsMaterial = m_SurfaceWood;
				_visualMaterial = m_VisualWood;
				_intactColor = s_ColorWood;
				return true;
			default:
				_surfaceName = "Glass";
				_physicsMaterial = m_SurfaceGlass;
				_visualMaterial = m_VisualGlass;
				_intactColor = s_ColorGlass;
				return true;
		}
	}

	private static bool TryParseDistanceMeters(string _objectName, out int _distanceMeters)
	{
		_distanceMeters = 0;
		if (string.IsNullOrEmpty(_objectName) || !_objectName.StartsWith("Sphere", StringComparison.Ordinal))
			return false;

		return int.TryParse(_objectName.Substring("Sphere".Length), out _distanceMeters) && _distanceMeters > 0;
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

	private bool TryCollectSelectedPlayerCombatStats(out IReadOnlyList<UnitCombatStats> _combatStatsList)
	{
		m_SelectedCombatStatsBuffer.Clear();

		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection == null || selection.CollectSelectedPlayerCombatStats(m_SelectedCombatStatsBuffer) == 0)
		{
			_combatStatsList = null;
			return false;
		}

		_combatStatsList = m_SelectedCombatStatsBuffer;
		return true;
	}

	private static string BuildSelectedUnitRankLabel(IReadOnlyList<UnitCombatStats> _combatStatsList)
	{
		if (_combatStatsList == null || _combatStatsList.Count == 0)
			return "— (select unit)";

		if (_combatStatsList.Count == 1)
		{
			UnitCombatStats combatStats = _combatStatsList[0];
			string rankLabel = UnitCombatRankCycle.ResolveRankLabel(combatStats.RankPreset);
			return $"{rankLabel} · {ResolveUnitDisplayName(combatStats)}";
		}

		UnitCombatRankDefinition sharedRank = _combatStatsList[0]?.RankPreset;
		for (int i = 1; i < _combatStatsList.Count; i++)
		{
			if (_combatStatsList[i]?.RankPreset != sharedRank)
			{
				return $"mixed · ×{_combatStatsList.Count}";
			}
		}

		return $"{UnitCombatRankCycle.ResolveRankLabel(sharedRank)} · ×{_combatStatsList.Count}";
	}

	private static string ResolveUnitDisplayName(UnitCombatStats _combatStats)
	{
		if (_combatStats == null)
			return "—";

		RtsUnitMember member = _combatStats.GetComponent<RtsUnitMember>();
		if (member == null)
			member = _combatStats.GetComponentInParent<RtsUnitMember>();

		if (member != null)
		{
			UnitRosterDisplayState roster = UnitRosterDisplayState.GetOrCreate(member.gameObject);
			if (roster != null && !string.IsNullOrWhiteSpace(roster.FullName))
				return roster.FullName;

			return member.gameObject.name;
		}

		return _combatStats.gameObject.name;
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
		UnitVision[] visions = FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude);
#else
		UnitVision[] visions = FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
			visions[i]?.RequestImmediateScan();
	}
	#endregion
}
