using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The inside of a building (a box in this object's local space). Outdoor presentation asks <see cref="Contains"/>
/// about the camera and stays quiet indoors: no wind wisps (WindGusts) and no leaves shed (FallingLeaves).
/// Add one per interior.
/// </summary>
public sealed class IndoorArea : MonoBehaviour
{
    [SerializeField] private Vector3 areaCenter;
    [SerializeField] private Vector3 areaSize = new Vector3(10f, 8f, 10f);

    private static readonly List<IndoorArea> Active = new List<IndoorArea>();

    /// <summary>True if the point lies inside any interior.</summary>
    public static bool Contains(Vector3 point)
    {
        foreach (var area in Active)
        {
            Vector3 local = area.transform.InverseTransformPoint(point) - area.areaCenter;
            Vector3 half = area.areaSize * .5f;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) return true;
        }
        return false;
    }

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.3f, .7f, 1f, .8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(areaCenter, areaSize);
    }
}
