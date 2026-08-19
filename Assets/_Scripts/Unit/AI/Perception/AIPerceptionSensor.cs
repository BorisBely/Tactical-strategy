using UnityEngine;

/// <summary>
/// Caches observer-local <see cref="AIPerceptionFrame"/> after DetectionProcessor.
/// Not baked onto Unit.prefab in AI-0. Does not own TargetSelector or combat.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(15)]
[RequireComponent(typeof(DetectionProcessor))]
public sealed class AIPerceptionSensor : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private DetectionProcessor m_Registry;
	private AIPerceptionFrame m_CurrentFrame;
	#endregion

	#region Public Properties
	public AIPerceptionFrame CurrentFrame => m_CurrentFrame;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Registry == null)
			m_Registry = GetComponent<DetectionProcessor>();
		m_CurrentFrame = AIPerceptionFrame.Empty;
	}

	private void LateUpdate()
	{
		Rebuild();
	}
	#endregion

	#region Public Methods
	public void Rebuild()
	{
		if (m_Registry == null)
			TryGetComponent(out m_Registry);
		m_CurrentFrame = AIPerceptionFrameBuilder.Build(m_Registry);
	}
	#endregion
}
