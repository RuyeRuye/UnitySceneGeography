using System;
using SceneGeography.Editor;
using UnityEditor;

namespace SceneGeography.Tests
{
    internal sealed class GeoEditorLanguageScope : IDisposable
    {
        readonly GeoEditorLanguage previous = GeoEditorLocalization.Language;
        readonly bool hadPreference = EditorPrefs.HasKey(GeoEditorLocalization.PreferenceKey);
        readonly int preference = EditorPrefs.GetInt(GeoEditorLocalization.PreferenceKey, 0);

        internal GeoEditorLanguageScope(GeoEditorLanguage language)
        {
            GeoEditorLocalization.SetLanguage(language);
        }

        public void Dispose()
        {
            GeoEditorLocalization.SetLanguage(previous);
            if (hadPreference)
            {
                EditorPrefs.SetInt(GeoEditorLocalization.PreferenceKey, preference);
            }
            else
            {
                EditorPrefs.DeleteKey(GeoEditorLocalization.PreferenceKey);
            }
        }
    }
}
