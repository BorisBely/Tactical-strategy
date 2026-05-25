using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepInventoryModificationClick : MonoBehaviour, IPointerClickHandler
{
	#region Constants
	private const float c_SingleClickDelaySeconds = 0.38f;
	#endregion

	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	#endregion

	#region Private Fields
	private Coroutine m_PendingSingleClick;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void OnDisable()
	{
		CancelPendingClick();
	}
	#endregion

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		if (eventData.clickCount >= 2)
		{
			CancelPendingClick();
			return;
		}

		CancelPendingClick();
		m_PendingSingleClick = StartCoroutine(HandleSingleClickAfterDelay());
	}
	#endregion

	#region Private Methods
	private IEnumerator HandleSingleClickAfterDelay()
	{
		yield return new WaitForSecondsRealtime(c_SingleClickDelaySeconds);
		m_PendingSingleClick = null;

		if (!Application.isPlaying)
			yield break;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		m_Coordinator?.TryToggleModificationPanel(m_Slot);
	}

	private void CancelPendingClick()
	{
		if (m_PendingSingleClick == null)
			return;

		StopCoroutine(m_PendingSingleClick);
		m_PendingSingleClick = null;
	}
	#endregion
}
