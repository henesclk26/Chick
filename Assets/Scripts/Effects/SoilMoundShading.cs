using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The soil mounds under the garden plants (lettuce, tomato, pepper, grape, berry bushes) are low poly with spiky
/// overhangs. Seen from the chick's camera, the undersides of those overhangs face the ground, get neither sun nor sky
/// and read as black holes in the soil. At start every mound mesh is copied once with its normals bent towards up, so
/// the overhangs shade like the soil around them; the shape is unchanged. Presentation only.
/// </summary>
public sealed class SoilMoundShading : MonoBehaviour
{
    [SerializeField, Tooltip("Name of the mound meshes inside the plant models.")]
    private string meshName = "Mound";
    [SerializeField, Range(0f, 1f), Tooltip("How far the normals turn towards up. 0: as modelled.")]
    private float bendUp = .65f;

    private readonly List<Mesh> copies = new List<Mesh>();

    private void Start()
    {
        var bent = new Dictionary<Mesh, Mesh>();
        foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Mesh source = filter.sharedMesh;
            if (source == null || source.name != meshName) continue;
            if (!bent.TryGetValue(source, out Mesh copy))
            {
                if (!source.isReadable) { Debug.LogWarning("SoilMoundShading: " + source.name + " is not readable.", filter); continue; }
                copy = Instantiate(source);
                copy.name = source.name + " (bent normals)";
                var normals = copy.normals;
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.Lerp(normals[i], Vector3.up, bendUp).normalized;
                copy.normals = normals;
                bent[source] = copy;
                copies.Add(copy);
            }
            filter.sharedMesh = copy;
        }
    }

    private void OnDestroy()
    {
        foreach (var copy in copies) if (copy != null) Destroy(copy);
    }
}
