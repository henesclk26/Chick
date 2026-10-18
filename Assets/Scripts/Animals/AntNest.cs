using System.Collections.Generic;
using UnityEngine;

// Marks an ant nest (its pivot is the centre of the entrance hole). Added to the nest models on import
// (AntModelImporter), so every nest placed in the scene registers itself; AntColony watches the register and sends its
// ants to whatever nests exist, adapting when nests are added, removed or moved, also during play.
[DisallowMultipleComponent]
public sealed class AntNest : MonoBehaviour
{
    private static readonly List<AntNest> active = new List<AntNest>();

    /// <summary>Nests currently enabled in loaded scenes.</summary>
    public static IReadOnlyList<AntNest> Active => active;

    /// <summary>Goes up whenever a nest is enabled or disabled, so watchers know to look again.</summary>
    public static int Version { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegister()
    {
        active.Clear();
        Version++;
    }

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
        Version++;
    }

    private void OnDisable()
    {
        active.Remove(this);
        Version++;
    }
}
