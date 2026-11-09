using UnityEditor;
using UnityEngine;

namespace SceneGeography.Editor
{
    internal abstract class GeoSettingsDrawer : PropertyDrawer
    {
        protected abstract bool Editable(SerializedProperty parent, SerializedProperty child);
        protected virtual string Warning(SerializedProperty property) => null;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            var child = property.Copy();
            var end = property.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
                }
                while (child.NextVisible(false) && !SerializedProperty.EqualContents(child, end));
            }

            if (Warning(property) != null)
            {
                height += 48 + EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var child = property.Copy();
                var end = property.GetEndProperty();
                if (child.NextVisible(true))
                {
                    do
                    {
                        row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                        row.height = EditorGUI.GetPropertyHeight(child, true);
                        using (new EditorGUI.DisabledScope(!Editable(property, child)))
                        {
                            EditorGUI.PropertyField(row, child, GeoEditorLocalization.PropertyContent(child), true);
                        }
                    }
                    while (child.NextVisible(false) && !SerializedProperty.EqualContents(child, end));
                }

                string warning = Warning(property);
                if (warning != null)
                {
                    row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                    row.height = 48;
                    EditorGUI.HelpBox(row, warning, MessageType.Warning);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(GeoCoordinateSettings))]
    internal sealed class GeoCoordinateSettingsDrawer : GeoSettingsDrawer
    {
        protected override bool Editable(SerializedProperty parent, SerializedProperty child) => GeoInspectorRules.IsCoordinateFieldEditable((GeoHeightKind)parent.FindPropertyRelative("heightKind").enumValueIndex, child.name);
        protected override string Warning(SerializedProperty property)
        {
            var kind = (GeoHeightKind)property.FindPropertyRelative("heightKind").enumValueIndex;
            if (kind == GeoHeightKind.Orthometric)
            {
                return GeoEditorLocalization.Tr("No height model is installed. Orthometric conversion returns MissingHeightModel and must not be treated as ellipsoid height.");
            }

            if (kind == GeoHeightKind.Unspecified ||
                property.FindPropertyRelative("horizontalType").enumValueIndex == 0)
            {
                return GeoEditorLocalization.Tr("Select the horizontal coordinate type and height reference explicitly. The system never guesses them.");
            }

            return null;
        }
    }

    [CustomPropertyDrawer(typeof(GeoCalibrationOptionsSettings))]
    internal sealed class GeoCalibrationOptionsSettingsDrawer : GeoSettingsDrawer
    {
        protected override bool Editable(SerializedProperty parent, SerializedProperty child)
        {
            string toggle = GeoInspectorRules.OptionSwitch(child.name);
            return (toggle == null || parent.FindPropertyRelative(toggle).boolValue) &&
                GeoInspectorRules.IsEditable(parent.serializedObject.targetObject, child.propertyPath);
        }
    }
}
