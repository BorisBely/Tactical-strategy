#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Открывает окно Render Pipeline Converter для конвертации материалов из Built-in в URP.
/// Используйте: Tools → Polygone → Open Render Pipeline Converter
/// </summary>
public static class OpenURPConverter
{
    private const string c_MenuPath = "Tools/Polygone/Open Render Pipeline Converter";

    [MenuItem(c_MenuPath)]
    public static void OpenConverter()
    {
        EditorApplication.ExecuteMenuItem("Window/Rendering/Render Pipeline Converter");
    }
}
#endif
