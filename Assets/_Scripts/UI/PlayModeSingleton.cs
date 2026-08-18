using UnityEngine;

/// <summary>
/// Blocks lazy GameObject spawn during play-mode teardown / domain reload.
/// Menu singletons must not call <c>new GameObject</c> from OnDestroy.
/// </summary>
internal static class PlayModeSingleton
{
	private static bool s_Quitting;

	public static bool CanSpawn => Application.isPlaying && !s_Quitting;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		s_Quitting = false;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void BindQuit()
	{
		Application.quitting -= HandleQuitting;
		Application.quitting += HandleQuitting;
	}

	private static void HandleQuitting()
	{
		s_Quitting = true;
		Application.quitting -= HandleQuitting;
	}
}
