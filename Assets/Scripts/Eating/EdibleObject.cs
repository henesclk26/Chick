using System;
using System.Collections.Generic;
using UnityEngine;

public enum EdibleCategory { Other, Seed }

/// <summary>Passive food contract. No polling, physics simulation or progression logic.</summary>
[DisallowMultipleComponent]
public sealed class EdibleObject : MonoBehaviour
{
    [SerializeField] private EdibleCategory category = EdibleCategory.Other;
    public EdibleCategory Category => category;
    public static event Action<EdibleObject> Consumed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetConsumptionListeners() => Consumed = null;
    [SerializeField, HideInInspector] private string persistentId;
    [SerializeField, Min(0)] private int requiredEatLevel;
    [Header("Statistics")]
    [SerializeField] private string statisticKey;
    [SerializeField] private string statisticDisplayName;
    [SerializeField] private Transform bitePoint;
    [SerializeField] private Transform contactVisual;
    [SerializeField, Range(0.5f, 1.5f)] private float interactionAssistMultiplier = 1f;
    [SerializeField, Min(0.001f)] private float maxVerticalInteractionOffset = 0.04f;
    [SerializeField] private bool requireExposedFace;
    [SerializeField] private Vector3 localExposedFaceNormal = Vector3.up;

    public int RequiredEatLevel => requiredEatLevel;
    public string PersistentId => !string.IsNullOrEmpty(persistentId) ? persistentId : BuildHierarchyId();
    public string StatisticKey => FoodStatisticIdentity.ResolveKey(this, statisticKey);
    public string StatisticDisplayName => FoodStatisticIdentity.ResolveDisplayName(StatisticKey, statisticDisplayName);
    public bool IsConsumed { get; private set; }
    public Vector3 BitePosition => bitePoint != null ? bitePoint.position : transform.position;
    // Only this child moves during contact assist. The logical root/collider remain static.
    public Transform ContactVisual => contactVisual;
    public float InteractionAssistMultiplier => Mathf.Clamp(interactionAssistMultiplier, 0.5f, 1.5f);
    public float MaxVerticalInteractionOffset => Mathf.Max(0.001f, maxVerticalInteractionOffset);
    public bool IsSurfaceAccessibleFrom(Vector3 worldPosition)
    {
        if (!requireExposedFace) return true;
        Vector3 normal = transform.TransformDirection(localExposedFaceNormal.normalized);
        return Vector3.Dot(normal, worldPosition - BitePosition) > 0.001f;
    }

    /// <summary>Sets up food assembled in code (e.g. strawberries a plant drops); call before it can be eaten.</summary>
    public void ConfigureRuntime(string id, EdibleCategory foodCategory, string key, string displayName,
        Transform bite, Transform visual, float assistMultiplier, float maxVerticalOffset)
    {
        persistentId = id;
        category = foodCategory;
        statisticKey = key;
        statisticDisplayName = displayName;
        bitePoint = bite;
        contactVisual = visual;
        interactionAssistMultiplier = assistMultiplier;
        maxVerticalInteractionOffset = maxVerticalOffset;
    }

    /// <summary>Only for food assembled in code: the eater must be on this side (local normal) to reach it.</summary>
    public void ConfigureExposedFace(Vector3 localNormal)
    {
        requireExposedFace = true;
        localExposedFaceNormal = localNormal;
    }

    /// <summary>
    /// Food eaten over several pecks (e.g. a watermelon slice's flesh): each successful peck calls this and the
    /// food is consumed only when it returns true (the peck that finishes it). Unset for ordinary food.
    /// </summary>
    public Func<bool> BiteHandler { get; set; }

    public bool CanBeEaten(int eatLevel = 0) =>
        isActiveAndEnabled && !IsConsumed && eatLevel >= requiredEatLevel;

    public bool Consume(int eatLevel = 0)
    {
        if (!CanBeEaten(eatLevel)) return false;
        // A bite that does not finish multi-peck food still counts as a successful peck.
        if (BiteHandler != null && !BiteHandler()) return true;
        MarkConsumed();
        Consumed?.Invoke(this);
        return true;
    }

    /// <summary>Consumed without raising <see cref="Consumed"/> (restored saves, a finished food's twin proxy).</summary>
    public void MarkConsumed()
    {
        IsConsumed = true;
        // Hides every visual and disables every collider immediately; no Destroy spam.
        gameObject.SetActive(false);
    }

    // Explicit pool reuse: simply re-enabling a consumed object cannot duplicate consumption.
    public void ResetForReuse()
    {
        IsConsumed = false;
        gameObject.SetActive(true);
    }

    public static string[] CaptureConsumedIds()
    {
        var consumed = new List<string>();
        foreach (var edible in FindObjectsByType<EdibleObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (edible.IsConsumed && !string.IsNullOrEmpty(edible.PersistentId))
                consumed.Add(edible.PersistentId);
        }
        consumed.Sort(StringComparer.Ordinal);
        return consumed.ToArray();
    }

    public static void RestoreConsumedIds(string[] consumedIds)
    {
        if (consumedIds == null || consumedIds.Length == 0) return;

        var consumed = new HashSet<string>(consumedIds, StringComparer.Ordinal);
        foreach (var edible in FindObjectsByType<EdibleObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!string.IsNullOrEmpty(edible.PersistentId) && consumed.Contains(edible.PersistentId))
                edible.MarkConsumed();
        }
    }

    private string BuildHierarchyId()
    {
        var hierarchy = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            hierarchy.Push(current.name);
            current = current.parent;
        }
        return gameObject.scene.path + "/" + string.Join("/", hierarchy.ToArray());
    }
}
