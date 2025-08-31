using UnityEditor;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Windows.Components
{
    public static class SearchBar
    {
        public static string Draw(string text, System.Action onClear = null)
        {
            GUILayout.BeginHorizontal();
            var newText = GUILayout.TextField(text, "SearchTextField");
            if (GUILayout.Button("", string.IsNullOrEmpty(newText) ? "SearchCancelButtonEmpty" : "SearchCancelButton"))
            {
                newText = "";
                onClear?.Invoke();
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();
            return newText;
        }
    }
}