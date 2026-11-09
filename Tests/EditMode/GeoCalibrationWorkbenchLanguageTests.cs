using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SceneGeography.Editor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SceneGeography.Tests
{
    public sealed partial class GeoCalibrationWorkbenchTests
    {
        [UnityTest]
        public IEnumerator LanguageMenuPreservesDraftPointEditsCandidateAndSelection()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility();
                yield return null;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", flags)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", flags).GetValue(window);
                string originalProfile = GeoCalibrationWorkbenchData.Json(profile);
                string oldId = marker.pointId;
                marker.pointId = "Renamed user ID";
                work.Draft.boundHeadingDegrees = 17;
                Assert.That(work.StageReplacement(oldId, marker), Is.True, work.Error);
                Assert.That(work.StageRemoval(profile.points[1].id), Is.True, work.Error);
                Assert.That(work.Calculate(), Is.True, work.Error);
                var candidate = work.Candidate;
                string draft = GeoCalibrationWorkbenchData.Json(work.Draft);
                string edits = JsonUtility.ToJson(work.PointEdits);
                string markerJson = JsonUtility.ToJson(marker);
                window.rootVisualElement.Q<ToolbarSearchField>("scene-search").value = "not visible";
                ((Toggle)typeof(GeoCalibrationWorkbench).GetField("bind", flags).GetValue(window)).value = true;

                foreach (string language in new[] { "English", "简体中文" })
                {
                    window.rootVisualElement.Q<ToolbarMenu>("editor-language").menu.MenuItems()
                        .OfType<DropdownMenuAction>().Single(x => x.name == language).Execute();
                    yield return null;
                    Assert.That(GeoCalibrationWorkbenchData.Json(work.Draft), Is.EqualTo(draft));
                    Assert.That(JsonUtility.ToJson(work.PointEdits), Is.EqualTo(edits));
                    Assert.That(work.Candidate, Is.SameAs(candidate));
                    Assert.That(work.CandidateIsCurrent(), Is.True);
                    Assert.That(work.Selected, Is.EqualTo(new[] { marker }));
                    Assert.That(window.rootVisualElement.Q<ToolbarSearchField>("scene-search").value, Is.EqualTo("not visible"));
                    Assert.That(((Toggle)typeof(GeoCalibrationWorkbench).GetField("bind", flags).GetValue(window)).value, Is.True);
                    Assert.That(window.hasUnsavedChanges, Is.True);
                    Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(originalProfile));
                    Assert.That(JsonUtility.ToJson(marker), Is.EqualTo(markerJson));
                    Assert.That(reference.IsInitialized, Is.False);
                    Assert.That(window.rootVisualElement.Q<Button>("calculate-candidate").text,
                        Is.EqualTo(language == "English" ? "Calculate candidate" : "计算候选"));
                    Assert.That(GeoCalibrationWorkbenchData.GetMarkerStatus(marker, profile).State, Is.EqualTo(GeoMarkerState.New));
                }
            }
            finally
            {
                window.DiscardChanges();
                window.Close();
            }
        }

        [Test]
        public void MarkerSynchronizationIsIndependentOfDisplayLanguage()
        {
            foreach (var language in new[] { GeoEditorLanguage.English, GeoEditorLanguage.SimplifiedChinese })
            {
                GeoEditorLocalization.SetLanguage(language);
                var status = GeoCalibrationWorkbenchData.GetMarkerStatus(marker, profile);
                Assert.That(status.State, Is.EqualTo(GeoMarkerState.Synchronized));
                Assert.That(status.IsSynchronized, Is.True);
                Assert.That(status.Message, Is.EqualTo(language == GeoEditorLanguage.English ? "Synchronized" : "已同步"));
            }
        }
    }
}
