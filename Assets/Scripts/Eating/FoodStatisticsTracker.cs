using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FoodStatisticsTracker : MonoBehaviour
{
    [SerializeField] private FarmSaveSystem saveSystem;
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    private readonly Dictionary<string, FoodStatSaveEntry> entries = new Dictionary<string, FoodStatSaveEntry>();

    public event Action Changed;
    public int TotalPoints => entries.Values.Sum(entry => Mathf.Max(0, entry.count));

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        EdibleObject.Consumed += HandleConsumed;
        if (saveSystem == null) return;
        saveSystem.Loaded += HandleLoaded;
        saveSystem.Saving += HandleSaving;
        if (saveSystem.LastLoadedData != null) Restore(saveSystem.LastLoadedData);
    }

    private void OnDisable()
    {
        EdibleObject.Consumed -= HandleConsumed;
        if (saveSystem == null) return;
        saveSystem.Loaded -= HandleLoaded;
        saveSystem.Saving -= HandleSaving;
    }

    public IReadOnlyList<FoodStatSaveEntry> GetSnapshot(bool includeKnownFoods = true)
    {
        Dictionary<string, FoodStatSaveEntry> snapshot = entries.ToDictionary(pair => pair.Key, pair => Clone(pair.Value));
        if (includeKnownFoods)
        {
            foreach (string key in FoodStatisticIdentity.KnownKeys)
                if (!snapshot.ContainsKey(key))
                    snapshot[key] = new FoodStatSaveEntry { key = key, displayName = FoodStatisticIdentity.ResolveDisplayName(key), count = 0 };
        }
        return snapshot.Values.OrderByDescending(entry => entry.count)
            .ThenBy(entry => entry.displayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private void HandleConsumed(EdibleObject edible)
    {
        if (edible == null) return;
        string key = edible.StatisticKey;
        if (!entries.TryGetValue(key, out FoodStatSaveEntry entry))
        {
            entry = new FoodStatSaveEntry { key = key, displayName = edible.StatisticDisplayName };
            entries.Add(key, entry);
        }
        entry.count++;
        Changed?.Invoke();
    }

    private void HandleLoaded(FarmSaveData data) => Restore(data);

    private void HandleSaving(FarmSaveData data)
    {
        if (data == null || (gameplayGate != null && !gameplayGate.IsReleased)) return;
        data.foodStatistics = entries.Values.Where(entry => entry.count > 0)
            .OrderBy(entry => entry.key, StringComparer.Ordinal).Select(Clone).ToArray();
    }

    private void Restore(FarmSaveData data)
    {
        entries.Clear();
        if (data?.foodStatistics != null && data.foodStatistics.Length > 0)
        {
            foreach (FoodStatSaveEntry saved in data.foodStatistics)
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.key) || saved.count <= 0) continue;
                string key = saved.key.Trim().ToLowerInvariant();
                entries[key] = new FoodStatSaveEntry
                {
                    key = key,
                    displayName = string.IsNullOrWhiteSpace(saved.displayName) ? FoodStatisticIdentity.ResolveDisplayName(key) : saved.displayName,
                    count = Mathf.Max(0, saved.count)
                };
            }
        }
        else if (data?.consumedEdibleIds != null && data.consumedEdibleIds.Length > 0)
        {
            MigrateConsumedIds(data.consumedEdibleIds);
        }
        Changed?.Invoke();
    }

    private void MigrateConsumedIds(IEnumerable<string> consumedIds)
    {
        HashSet<string> ids = new HashSet<string>(consumedIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        foreach (EdibleObject edible in FindObjectsByType<EdibleObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (edible == null || !ids.Contains(edible.PersistentId)) continue;
            string key = edible.StatisticKey;
            if (!entries.TryGetValue(key, out FoodStatSaveEntry entry))
            {
                entry = new FoodStatSaveEntry { key = key, displayName = edible.StatisticDisplayName };
                entries.Add(key, entry);
            }
            entry.count++;
        }
    }

    private void ResolveReferences()
    {
        if (saveSystem == null) saveSystem = FindFirstObjectByType<FarmSaveSystem>(FindObjectsInactive.Include);
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
    }

    private static FoodStatSaveEntry Clone(FoodStatSaveEntry source) => new FoodStatSaveEntry
    { key = source.key, displayName = source.displayName, count = source.count };
}
