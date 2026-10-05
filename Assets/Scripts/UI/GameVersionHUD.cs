using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows the build version (Player Settings &gt; Version) faintly in the lower-left corner.
/// </summary>
[RequireComponent(typeof(UIDocument)), DisallowMultipleComponent]
public sealed class GameVersionHUD : MonoBehaviour
{
    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        Label label = root.Q<Label>("GameVersionLabel");
        if (label == null)
        {
            Debug.LogError("Game version HUD needs GameVersionHUD.uxml on its UIDocument.", this);
            return;
        }
        label.text = "v" + Application.version;
    }
}
