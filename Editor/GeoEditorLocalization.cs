using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneGeography.Editor
{
    internal enum GeoEditorLanguage
    {
        English,
        SimplifiedChinese
    }

    // Editor-only preference. Never serialized into a Profile, scene, or runtime assembly.
    internal static partial class GeoEditorLocalization
    {
        internal const string PreferenceKey = "io.github.ruyeruye.scene-geography.editor.language";
        static GeoEditorLanguage language = ReadPreference();
        static readonly Dictionary<string, GUIContent> contents = new Dictionary<string, GUIContent>();

        internal static GeoEditorLanguage Language => language;
        internal static event Action Changed;

        internal static GeoEditorLanguage ReadPreference() =>
            EditorPrefs.GetInt(PreferenceKey, 0) == 1 ? GeoEditorLanguage.SimplifiedChinese : GeoEditorLanguage.English;

        internal static void SetLanguage(GeoEditorLanguage value)
        {
            if (value != GeoEditorLanguage.English && value != GeoEditorLanguage.SimplifiedChinese)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            EditorPrefs.SetInt(PreferenceKey, (int)value);
            if (language == value) return;
            language = value;
            contents.Clear();
            Changed?.Invoke();
            foreach (var inspector in Resources.FindObjectsOfTypeAll<UnityEditor.Editor>())
            {
                if (inspector is GeoReferenceEditor || inspector is GeoCalibrationProfileEditor ||
                    inspector is GeoControlPointMarkerEditor || inspector is GeoComponentEditor ||
                    inspector is GeoReferenceConsumerEditor)
                    inspector.Repaint();
            }
        }

        internal static string Tr(string english) => language == GeoEditorLanguage.SimplifiedChinese &&
            english != null && Chinese.TryGetValue(english, out var chinese) ? chinese : english;

        internal static string Format(string english, params object[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, Tr(english), arguments);

        internal static GUIContent Content(string english, string tooltip = "")
        {
            string key = english + "\u001f" + tooltip;
            if (!contents.TryGetValue(key, out var content))
            {
                content = new GUIContent(Tr(english), Tr(tooltip));
                contents.Add(key, content);
            }

            return content;
        }

        internal static GUIContent PropertyContent(SerializedProperty property) =>
            Content(property.displayName, property.tooltip);

        internal static void LocalizeProperty(PropertyField field, SerializedProperty property)
        {
            field.label = Tr(property.displayName);
            field.tooltip = Tr(property.tooltip);
            // Binding generates nested fields after attachment. Translate their metadata, never their values.
            field.schedule.Execute(() => field.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.tooltip)) element.tooltip = Tr(element.tooltip);
            }));
        }
    }
}
