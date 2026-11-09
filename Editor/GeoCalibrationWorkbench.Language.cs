using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneGeography.Editor
{
    public sealed partial class GeoCalibrationWorkbench
    {
        void AddLanguageSetting(Toolbar toolbar)
        {
            var menu = new ToolbarMenu
            {
                name = "editor-language",
                text = GeoEditorLocalization.Tr("Language") + ": " +
                    (GeoEditorLocalization.Language == GeoEditorLanguage.English ? "English" : "简体中文")
            };
            menu.menu.AppendAction("English", _ => GeoEditorLocalization.SetLanguage(GeoEditorLanguage.English),
                _ => GeoEditorLocalization.Language == GeoEditorLanguage.English ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.menu.AppendAction("简体中文", _ => GeoEditorLocalization.SetLanguage(GeoEditorLanguage.SimplifiedChinese),
                _ => GeoEditorLocalization.Language == GeoEditorLanguage.SimplifiedChinese ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            toolbar.Add(menu);
        }

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(GeoEditorLocalization.Tr("Calibration Workbench"));
            if (model != null && rootVisualElement.childCount > 0)
            {
                BuildUI();
            }

            Repaint();
        }
    }
}
