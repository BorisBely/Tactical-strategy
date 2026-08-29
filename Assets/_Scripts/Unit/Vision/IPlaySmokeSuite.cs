using System.Collections;

/// <summary>
/// Play smoke that can run standalone or inside <see cref="FrozenLayersPlayCoordinator"/>.
/// </summary>
public interface IPlaySmokeSuite
{
	IEnumerator RunAndWait();
	int LastPassCount { get; }
	int LastFailCount { get; }
}
