using UnityEngine;

namespace QHotUpdateSystem.Editor.Windows.Components
{
    public class CollapsibleGroup
    {
        public bool Expanded;
        public string Title;

        public CollapsibleGroup(string title, bool expanded = true)
        {
            Title = title;
            Expanded = expanded;
        }

        public bool Begin()
        {
            Expanded = UnityEditor.EditorGUILayout.Foldout(Expanded, Title, true);
            if (Expanded)
                UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
            return Expanded;
        }

        public void End()
        {
            if (Expanded)
                UnityEditor.EditorGUILayout.EndVertical();
        }
    }
}