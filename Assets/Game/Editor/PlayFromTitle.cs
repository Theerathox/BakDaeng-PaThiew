using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Pet Game > Play From Title Screen (tick on / off, ON by default)
// On: the Play button always starts the game from the title screen, whatever scene is open
//     (like the built game). Off: Play starts the scene you have open (to test one level).
[InitializeOnLoad]
static class PlayFromTitle
{
    const string Menu = "Tools/Pet Game/Play From Title Screen";
    const string Key = "PetGame.PlayFromTitle";
    const string TitlePath = "Assets/Scenes/Title.unity";

    static PlayFromTitle()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += st => { if (st == PlayModeStateChange.EnteredEditMode) Apply(); };
    }

    static bool On { get => EditorPrefs.GetBool(Key, true); set => EditorPrefs.SetBool(Key, value); }

    [MenuItem(Menu, false, 0)]
    static void Toggle() { On = !On; Apply(); }

    [MenuItem(Menu, true)]
    static bool ToggleCheck() { UnityEditor.Menu.SetChecked(Menu, On); return true; }

    static void Apply()
    {
        EditorSceneManager.playModeStartScene = On ? AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath) : null;
    }
}
