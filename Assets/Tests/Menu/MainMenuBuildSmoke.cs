#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Only runs in a development verification executable with this explicit command-line flag.
public static class MainMenuBuildSmoke
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-menu-quit") < 0) return;
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        var gate = UnityEngine.Object.FindFirstObjectByType<MainMenuGameplayGate>();
        if (SceneManager.GetActiveScene().name != "SampleScene" || menu == null || gate == null || menu.GetComponent<UIDocument>().rootVisualElement.Q<Button>("quit") == null)
        { Debug.LogError("MENU_BUILD_SMOKE_FAIL"); Application.Quit(2); return; }
        Debug.Log("MENU_BUILD_SMOKE_PASS: SampleScene booted with embedded UIDocument and gameplay gate; calling the production quit handler.");
        menu.Quit();
    }
}
#endif
