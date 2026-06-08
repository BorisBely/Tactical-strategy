using UnityEngine;

/// <summary>
/// Переключает подпись графика точности между «Точность» и «Контроль отдачи» при hover на рукоятки, дульные модули и приклады.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LocalizedTextMeshProUGUI))]
[DefaultExecutionOrder(100)]
public sealed class MissionPrepModificationGraphCaption : MonoBehaviour
{
	#region Constants
	private const string c_AccuracyLocalizationKey = "mission_prep.stats.accuracy";
	private const string c_RecoilControlLocalizationKey = "mission_prep.stats.recoil_control";
	#endregion

	#region Private Fields
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	[SerializeField] private LocalizedTextMeshProUGUI m_LocalizedText;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_LocalizedText == null)
			m_LocalizedText = GetComponent<LocalizedTextMeshProUGUI>();
	}

	private void OnEnable()
	{
		ResolveCoordinator();
		SubscribeCoordinator();
		RefreshCaption();
	}

	private void OnDisable()
	{
		UnsubscribeCoordinator();
	}

	private void Update()
	{
		if (m_Coordinator != null)
			return;

		ResolveCoordinator();
		if (m_Coordinator == null)
			return;

		SubscribeCoordinator();
		RefreshCaption();
	}
	#endregion

	#region Private Methods
	private void ResolveCoordinator()
	{
		if (m_Coordinator != null)
			return;

		m_Coordinator = GetComponentInParent<MissionPrepLoadoutCoordinator>();
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void SubscribeCoordinator()
	{
		if (m_Coordinator == null)
			return;

		m_Coordinator.ModificationGraphDataChanged -= HandleCoordinatorGraphDataChanged;
		m_Coordinator.ModificationGraphDataChanged += HandleCoordinatorGraphDataChanged;
	}

	private void UnsubscribeCoordinator()
	{
		if (m_Coordinator == null)
			return;

		m_Coordinator.ModificationGraphDataChanged -= HandleCoordinatorGraphDataChanged;
	}

	private void HandleCoordinatorGraphDataChanged()
	{
		RefreshCaption();
	}

	private void RefreshCaption()
	{
		if (m_LocalizedText == null)
			m_LocalizedText = GetComponent<LocalizedTextMeshProUGUI>();
		if (m_LocalizedText == null)
			return;

		ResolveCoordinator();
		bool useRecoilCaption = m_Coordinator != null && m_Coordinator.IsAccuracyGraphRecoilPreviewActive;
		m_LocalizedText.SetLocalizationKey(useRecoilCaption ? c_RecoilControlLocalizationKey : c_AccuracyLocalizationKey);
	}
	#endregion
}
