using UnityEngine;

/// <summary>
/// Keeps the helper chick in step with the "Yardımcı Civciv" upgrade: it exists only while the upgrade is
/// bought, the player is a chicken and gameplay is running. At most <see cref="PlayerUpgrades.MaxHelperChicks"/> exist.
/// </summary>
[DisallowMultipleComponent]
public sealed class HelperChickSpawner : MonoBehaviour
{
    [SerializeField] private HelperChickController helperPrefab;
    [SerializeField] private PlayerUpgrades upgrades;
    [SerializeField] private MainMenuGameplayGate gameplayGate;

    private ChickPlayerController player;
    private PlayerGrowthController growth;
    private HelperChickController active;

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        growth = GetComponent<PlayerGrowthController>();
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        bool wanted = ShouldExist();
        if (wanted && active == null) Spawn();
        else if (!wanted && active != null) Despawn();
    }

    private void OnDisable() => Despawn();

    private bool ShouldExist() =>
        helperPrefab != null && upgrades != null && player != null && growth != null &&
        player.isActiveAndEnabled && upgrades.HelperChickCount > 0 &&
        growth.CurrentForm == PlayerGrowthController.Form.Chicken && !growth.IsTransforming &&
        (gameplayGate == null || gameplayGate.IsReleased);

    private void Spawn()
    {
        active = Instantiate(helperPrefab, player.transform.position, player.transform.rotation);
        active.name = "HelperChick";
        active.Initialize(upgrades, player);
    }

    private void Despawn()
    {
        if (active == null) return;
        Destroy(active.gameObject);
        active = null;
    }
}
