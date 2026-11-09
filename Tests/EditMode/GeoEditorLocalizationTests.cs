using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SceneGeography.Editor;
using UnityEditor;

namespace SceneGeography.Tests
{
    public sealed class GeoEditorLocalizationTests
    {
        [Test]
        public void MissingAndUnknownPreferencesDefaultToEnglish()
        {
            using (new GeoEditorLanguageScope(GeoEditorLanguage.English))
            {
                EditorPrefs.DeleteKey(GeoEditorLocalization.PreferenceKey);
                Assert.That(GeoEditorLocalization.ReadPreference(), Is.EqualTo(GeoEditorLanguage.English));
                EditorPrefs.SetInt(GeoEditorLocalization.PreferenceKey, 99);
                Assert.That(GeoEditorLocalization.ReadPreference(), Is.EqualTo(GeoEditorLanguage.English));
                GeoEditorLocalization.SetLanguage(GeoEditorLanguage.SimplifiedChinese);
                Assert.That(GeoEditorLocalization.ReadPreference(), Is.EqualTo(GeoEditorLanguage.SimplifiedChinese));
            }
        }

        [Test]
        public void InvalidLanguageDoesNotChangePreferenceOrActiveLanguage()
        {
            using (new GeoEditorLanguageScope(GeoEditorLanguage.English))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => GeoEditorLocalization.SetLanguage((GeoEditorLanguage)99));
                Assert.That(GeoEditorLocalization.Language, Is.EqualTo(GeoEditorLanguage.English));
                Assert.That(GeoEditorLocalization.ReadPreference(), Is.EqualTo(GeoEditorLanguage.English));
            }
        }

        [Test]
        public void EveryTranslationIsNonemptyAndPreservesFormatArguments()
        {
            Assert.That(GeoEditorLocalization.Chinese.Count, Is.GreaterThan(300));
            foreach (var entry in GeoEditorLocalization.Chinese)
            {
                Assert.That(entry.Key, Is.Not.Null.And.Not.Empty);
                Assert.That(entry.Value, Is.Not.Null.And.Not.Empty, entry.Key);
                var english = Regex.Matches(entry.Key, @"\{\d+(?:[^{}]*)\}").Cast<Match>().Select(x => x.Value).OrderBy(x => x).ToArray();
                var chinese = Regex.Matches(entry.Value, @"\{\d+(?:[^{}]*)\}").Cast<Match>().Select(x => x.Value).OrderBy(x => x).ToArray();
                Assert.That(chinese, Is.EqualTo(english), entry.Key);
            }
        }

        [Test]
        public void CachedLabelsAndFormattingFollowLanguageWithoutTranslatingValues()
        {
            using (new GeoEditorLanguageScope(GeoEditorLanguage.English))
            {
                var english = GeoEditorLocalization.Content("Language");
                Assert.That(english.text, Is.EqualTo("Language"));
                Assert.That(GeoEditorLocalization.Content("Language"), Is.SameAs(english));
                GeoEditorLocalization.SetLanguage(GeoEditorLanguage.SimplifiedChinese);
                Assert.That(GeoEditorLocalization.Content("Language").text, Is.EqualTo("语言"));
                Assert.That(GeoEditorLocalization.Content("Language"), Is.Not.SameAs(english));
                const string id = "User marker: Apply candidate";
                Assert.That(GeoEditorLocalization.Tr(id), Is.EqualTo(id));
                const string format = "Root-local position: {0:G17}, {1:G17}, {2:G17}";
                Assert.That(GeoEditorLocalization.Format(format, 1.0000000000000002, 2, 3), Does.Contain("1.0000000000000002"));
            }
        }
    }
}
