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
	[SerializeField] private string m_TargetNamePattern = @"^Cube(10|20|30|40|50|60|70|80|90|100)$";
	[SerializeField, Min(1)] private int m_HitsToDefeat = 10;
	[SerializeField, Min(10f)] private float m_PlayerVisionRange = 120f;
	[SerializeField] private bool m_AutoDiscoverTargetsOnAwake = true;
	[SerializeField] private int m_TargetLayer = 8;
	#endregion

	#region Private Fields
	private readonly List<ShootingRangeTarget> m_Targets = new List<ShootingRangeTarget>(16);
	private Regex m_NameRegex;
	#endregion

	#region Public Properties
	public IReadOnlyList<ShootingRangeTarget> Targets => m_Targets;
	public int HitsToDefeat => m_HitsToDefeat;
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
		Transform[] transforms = FindObjectsByType<Transform>();
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
		TargetsChanged?.Invoke();
	}

	public void SetAllTargetsEnabled(bool _enabled)
	{
		for (int i = 0; i < m_Targets.Count; i++)
		{
			if (m_Targets[i] != null)
				m_Targets[i].SetUserEnabled(_enabled);
		}

		RequestVisionRescanForPlayers();
		TargetsChanged?.Invoke();
	}

	public void ResetTarget(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

		_target.ResetTarget();
		RequestVisionRescanForPlayers();
		TargetsChanged?.Invoke();
	}

	public void SetTargetEnabled(ShootingRangeTarget _target, bool _enabled)
	{
		if (_target == null)
			return;

		_target.SetUserEnabled(_enabled);
		RequestVisionRescanForPlayers();
		TargetsChanged?.Invoke();
	}

	public void ApplyPlayerVisionRange()
	{
#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>();
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

		if (_go.GetComponent<BoxCollider>() == null)
			_go.AddComponent<BoxCollider>();

		ShootingRangeTarget target = _go.GetComponent<ShootingRangeTarget>();
		if (target == null)
			target = _go.AddComponent<ShootingRangeTarget>();

		target.ResetTarget();
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
		return string.CompareOrdinal(_a.DisplayName, _b.DisplayName);
	}

	private void RequestVisionRescanForPlayers()
	{
#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>();
#else
		UnitVision[] visions = FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
			visions[i]?.RequestImmediateScan();
	}
	#endregion
}
