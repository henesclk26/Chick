using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parts of the farm the bird has opened up. A gate given a region (FarmGate.Region) stays shut until that region is
/// open, then stands open for good. The woods open when the chick grows into a chicken, the barn with the chicken's
/// first egg (QuestHUD); the developer panel can open or shut them all. Kept in the farm save (FarmSaveData.openRegions,
/// through QuestHUD).
/// </summary>
public static class FarmRegions
{
    public const string Barn = "barn";
    public const string Woods = "woods";

    /// <summary>Every region there is, with the name shown when it opens.</summary>
    public static readonly (string id, string name, string hint)[] All =
    {
        (Barn, "Ahır", "Ahırın kapısı artık açık. Yumurta sepeti ahırın yanında!"),
        (Woods, "Ağaçlık", "Kümesin güneyindeki kapı artık açık. Ağaçların dibinde mantarlar seni bekliyor!"),
    };

    private static readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>A region was opened while playing (not when a save is read).</summary>
    public static event Action<string> Opened;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() { open.Clear(); Opened = null; }

    public static bool IsOpen(string id) => !string.IsNullOrEmpty(id) && open.Contains(id);

    /// <summary>Opens a region; true if it was shut until now.</summary>
    public static bool Open(string id)
    {
        if (string.IsNullOrEmpty(id) || !open.Add(id)) return false;
        Opened?.Invoke(id);
        return true;
    }

    /// <summary>Opens a region without the "opened" message (one a save already had).</summary>
    public static void OpenQuietly(string id)
    {
        if (!string.IsNullOrEmpty(id)) open.Add(id);
    }

    public static void OpenAll()
    {
        foreach (var region in All) Open(region.id);
    }

    /// <summary>Shuts every region again (the developer panel): their gates close until they are opened.</summary>
    public static void CloseAll() => open.Clear();

    public static string NameOf(string id)
    {
        foreach (var region in All) if (region.id == id) return region.name;
        return id;
    }

    public static string HintOf(string id)
    {
        foreach (var region in All) if (region.id == id) return region.hint;
        return "";
    }

    public static string[] Capture()
    {
        var ids = new List<string>(open);
        ids.Sort(StringComparer.Ordinal);
        return ids.ToArray();
    }

    /// <summary>The regions a save had open (quietly: no "opened" message).</summary>
    public static void Restore(string[] ids)
    {
        open.Clear();
        if (ids != null) foreach (var id in ids) if (!string.IsNullOrEmpty(id)) open.Add(id);
    }
}
