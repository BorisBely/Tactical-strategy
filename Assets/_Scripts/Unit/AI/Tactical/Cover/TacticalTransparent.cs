using UnityEngine;

/// <summary>
/// Bake-time marker: this collider is glass / tactical passthrough, not a cover wall.
/// Does not change Vision, Fire, or movement. #13.2B.3
/// </summary>
[DisallowMultipleComponent]
public sealed class TacticalTransparent : MonoBehaviour
{
}

/// <summary>
/// Semantic query for <see cref="TacticalTransparent"/>. Not a material-name table.
/// </summary>
public static class TacticalTransparency
{
	#region Public Methods
	public static bool IsMarked(Component _component)
	{
		return _component != null && _component.GetComponentInParent<TacticalTransparent>() != null;
	}
	#endregion
}
