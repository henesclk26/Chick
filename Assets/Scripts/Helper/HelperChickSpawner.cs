using System.Collections.Generic;
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
    private readonly List<HelperChickController> active = new List<HelperChickController>();

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        growth = GetComponent<PlayerGrowthController>();
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        active.RemoveAll(helper => helper == null);
        int wanted = ShouldExist() ? upgrades.HelperChickCount : 0;
        while (active.Count > wanted) DespawnLast();
        while (active.Count < wanted) Spawn();
    }

    private void OnDisable()
    {
        while (active.Count > 0) DespawnLast();
    }

    private bool ShouldExist() =>
        helperPrefab != null && upgrades != null && player != null && growth != null &&
        player.isActiveAndEnabled && upgrades.HelperChickCount > 0 &&
        growth.CurrentForm == PlayerGrowthController.Form.Chicken && !growth.IsTransforming &&
        (gameplayGate == null || gameplayGate.IsReleased);

    private void Spawn()
    {
        int index = active.Count;
        Vector3 offset = Quaternion.Euler(0f, index * 120f, 0f) * -player.transform.forward * .45f;
        HelperChickController helper = Instantiate(helperPrefab, player.transform.position + offset, player.transform.rotation);
        helper.name = "HelperChick_" + (index + 1);
        active.Add(helper);
        helper.Initialize(upgrades, player, active, index);
    }

    private void DespawnLast()
    {
        int index = active.Count - 1;
        HelperChickController helper = active[index];
        active.RemoveAt(index);
        if (helper == null) return;
        helper.gameObject.SetActive(false);
        Destroy(helper.gameObject);
    }
}
