using UnityEditor;

namespace SceneGeography.Editor
{
    internal static class GeoInspectorGUI
    {
        internal static bool IsReporter(UnityEngine.Object value) =>
            value is GeoPositionReporter || value is GeoOrientationReporter;

        internal static UnityEngine.Transform ReporterTarget(UnityEngine.Object value) =>
            value is GeoPositionReporter position ? position.target : (value as GeoOrientationReporter)?.target;

        internal static bool DrawFields(SerializedObject serialized)
        {
            serialized.Update();
            var property = serialized.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                using (new EditorGUI.DisabledScope(property.name == "m_Script" || !GeoInspectorRules.IsEditable(serialized.targetObject, property.name)))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property, GeoEditorLocalization.PropertyContent(property), true);
                    if (EditorGUI.EndChangeCheck() && IsReporter(serialized.targetObject) &&
                        property.name == "target")
                    {
                        serialized.FindProperty("hasExplicitTarget").boolValue = property.objectReferenceValue != null;
                    }
                }
            }

            return serialized.ApplyModifiedProperties();
        }
    }
}
