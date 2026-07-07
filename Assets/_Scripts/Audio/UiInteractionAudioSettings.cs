using UnityEngine;

/// <summary>
/// Общие UI-звуки (hover и click по кнопкам, dropdown и другим Selectable).
/// Загружается из <see cref="Resources"/> по пути Audio/UiInteractionAudioSettings.
/// </summary>
[CreateAssetMenu(fileName = "UiInteractionAudioSettings", menuName = "Game/Audio/UI Interaction Audio Settings")]
public sealed class UiInteractionAudioSettings : ScriptableObject
{
	#region Serialized Fields
	[SerializeField] private AudioClip m_GenericButtonHoverClip;
	[SerializeField, Range(0f, 1f)] private float m_GenericButtonHoverVolume = 0.4f;
	[SerializeField] private AudioClip m_GenericButtonClickClip;
	[SerializeField, Range(0f, 1f)] private float m_GenericButtonClickVolume = 0.85f;
	#endregion

	#region Public Properties
	public AudioClip GenericButtonHoverClip => m_GenericButtonHoverClip;

	public float GenericButtonHoverVolume => m_GenericButtonHoverVolume;

	public AudioClip GenericButtonClickClip => m_GenericButtonClickClip;

	public float GenericButtonClickVolume => m_GenericButtonClickVolume;
	#endregion
}
