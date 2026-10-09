using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A place where the Tab menu opens: inside the barn (chicken upgrades and abilities) or at the coop
/// (helper chicks). The area is a box in this object's local space.
/// </summary>
public sealed class UpgradeStation : MonoBehaviour
{
    public enum Kind { Barn, Coop }

    [SerializeField] private Kind kind;
    [SerializeField] private Vector3 areaCenter;
    [SerializeField] private Vector3 areaSize = new Vector3(4f, 3f, 4f);

    private static readonly List<UpgradeStation> Active = new List<UpgradeStation>();

    public Kind StationKind => kind;
    public string PromptText => kind == Kind.Barn ? "Yükseltmeler" : "Civciv Yükseltmeleri";

    /// <summary>The station whose area contains <paramref name="position"/>, or null.</summary>
    public static UpgradeStation At(Vector3 position)
    {
        foreach (var station in Active)
        {
            Vector3 local = station.transform.InverseTransformPoint(position) - station.areaCenter;
            Vector3 half = station.areaSize * .5f;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) return station;
        }
        return null;
    }

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .8f, .2f, .8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(areaCenter, areaSize);
    }
}
