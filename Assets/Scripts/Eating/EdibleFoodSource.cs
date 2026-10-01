using UnityEngine;

/// <summary>Identifies a placed food whose independently edible children share a body.</summary>
[DisallowMultipleComponent]
public sealed class EdibleFoodSource : MonoBehaviour
{
    // Assigned per scene instance by EdibleFoodSourceIdentity, never baked into the prefab.
    [SerializeField, HideInInspector] private string sourceId;

    [Header("Future whole-food eating (locked by default)")]
    [SerializeField] private EdibleObject wholeFood;
    [SerializeField] private bool wholeFoodEatingEnabled;

    public bool WholeFoodEatingEnabled => wholeFoodEatingEnabled;
    public int RequiredWholeFoodEatLevel => wholeFood != null ? wholeFood.RequiredEatLevel : int.MaxValue;

    private void Awake()
    {
        if (wholeFood != null) wholeFood.enabled = wholeFoodEatingEnabled;
    }

    /// <summary>Future growth code explicitly unlocks this; eating children never unlocks it.</summary>
    public void SetWholeFoodEatingEnabled(bool enabled)
    {
        wholeFoodEatingEnabled = enabled;
        if (wholeFood != null) wholeFood.enabled = enabled;
    }

    public bool CanEatWholeFood(int eatLevel) => wholeFoodEatingEnabled &&
        wholeFood != null && wholeFood.CanBeEaten(eatLevel);

    /// <summary>
    /// Future large-food interaction calls this at its animation impact.
    /// Consuming the root hides the body and remaining children and uses the existing save contract.
    /// Current chick targeting continues to target only child pieces.
    /// </summary>
    public bool TryConsumeWholeFood(int eatLevel)
    {
        return CanEatWholeFood(eatLevel) && wholeFood.Consume(eatLevel);
    }
}
