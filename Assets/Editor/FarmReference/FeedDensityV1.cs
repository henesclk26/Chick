using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Triples the feed in the first two parts of the farm (Coop Yard Seeds and Barn Yard Seeds, under Food &amp; Test
/// Areas/On The Map) without adding new feeding spots: every seed group gets twice as many seeds again, spread around
/// its own seeds over a slightly wider patch (<see cref="Widen"/>). Each new seed copies a seed of its own group (the
/// same prefab, so the mix of wheat, corn, rice and sunflower stays), lies on the same surface as that seed (the ground,
/// or a trough's floor) at the same height above it, turned at random, never on top of a sack, a fence or a wall and
/// never inside another seed. New seeds are numbered on from the group's own names, so every save id stays unique.
/// One undo step.
/// </summary>
public static class FeedDensityV1
{
    private const string Root = "Food & Test Areas/On The Map/";
    private static readonly string[] Areas = { "Coop Yard Seeds", "Barn Yard Seeds" };
    private const float Widen = 1.15f;          // the patch grows by this much around its centre
    private const float SameSurface = .06f;     // m: a new seed's ground may lie this much above or below its model's
    private const float Spacing = .03f;         // m: no closer than this to another seed
    private const int EdibleLayer = 9;

    [MenuItem("Tools/Chick/Food/Triple Feed In Coop And Barn Yards (V1)")]
    private static void Menu() => Debug.Log(Run(2));

    /// <summary>Adds <paramref name="timesMore"/> times each group's seed count, spread as described above.</summary>
    public static string Run(int timesMore)
    {
        var report = new StringBuilder();
        var random = new System.Random(4217);
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Triple feed");
        int before = 0, added = 0;
        foreach (string area in Areas)
        {
            var areaRoot = GameObject.Find(Root + area);
            if (areaRoot == null) { report.Append("missing ").Append(area).Append('\n'); continue; }
            foreach (Transform patch in areaRoot.transform)
            {
                var seeds = new List<Transform>();
                foreach (Transform child in patch)
                    if (child.TryGetComponent(out EdibleObject food) && food.Category == EdibleCategory.Seed &&
                        PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) != null)   // seeds only, not the melon
                        seeds.Add(child);
                if (seeds.Count == 0) continue;
                int made = Fill(patch, seeds, seeds.Count * timesMore, random);
                before += seeds.Count;
                added += made;
                report.Append(area).Append(" / ").Append(patch.name).Append(": ").Append(seeds.Count).Append(" -> ")
                      .Append(seeds.Count + made).Append('\n');
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return "Feed: " + before + " seeds -> " + (before + added) + "\n" + report;
    }

    private static int Fill(Transform patch, List<Transform> seeds, int want, System.Random random)
    {
        // the patch's centre, how far apart its seeds lie, and where it already has seeds
        Vector3 centre = Vector3.zero;
        foreach (var s in seeds) centre += s.position;
        centre /= seeds.Count;
        var taken = new List<Vector3>();
        foreach (var s in seeds) taken.Add(s.position);
        float spread = NearestSpacing(taken);

        // names go on from the highest number among the patch's seeds
        int next = 0;
        foreach (Transform child in patch)
        {
            var m = Regex.Match(child.name, @"_(\d+)$");
            if (m.Success) next = Mathf.Max(next, int.Parse(m.Groups[1].Value) + 1);
        }

        int made = 0;
        for (int i = 0; i < want; i++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var model = seeds[random.Next(seeds.Count)];
                if (!Ground(model.position, out RaycastHit modelGround)) continue;
                float above = model.position.y - modelGround.point.y;

                Vector3 offset = model.position - centre;
                offset.y = 0f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float jitter = spread * (.5f + (float)random.NextDouble() * 1.5f);
                Vector3 spot = centre + offset * (Widen * (.85f + (float)random.NextDouble() * .3f)) +
                               new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * jitter;
                spot.y = model.position.y;
                if (!Ground(spot, out RaycastHit hit) || hit.normal.y < .8f) continue;
                if (Mathf.Abs(hit.point.y - modelGround.point.y) > SameSurface) continue;   // a sack, a fence, a wall, a hollow
                Vector3 position = hit.point + Vector3.up * above;
                if (TooClose(taken, position)) continue;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
                var seed = (GameObject)PrefabUtility.InstantiatePrefab(source, patch);
                Undo.RegisterCreatedObjectUndo(seed, "Triple feed");
                seed.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) *
                                                      Quaternion.Euler(model.eulerAngles.x, 0f, model.eulerAngles.z));
                seed.transform.localScale = model.localScale;
                seed.name = Regex.Replace(model.name, @"_(\d+)$", "") + "_" + next.ToString("000");
                next++;
                taken.Add(position);
                made++;
                break;
            }
        }
        return made;
    }

    // the surface under a point: the highest solid that is not food, a trigger or a character
    private static bool Ground(Vector3 at, out RaycastHit ground)
    {
        ground = default;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * .5f, Vector3.down, 1.5f, ~(1 << EdibleLayer), QueryTriggerInteraction.Ignore))
        {
            if (hit.collider is CharacterController || hit.collider.GetComponentInParent<EdibleObject>() != null) continue;
            if (!found || hit.point.y > ground.point.y) { ground = hit; found = true; }
        }
        return found;
    }

    private static bool TooClose(List<Vector3> taken, Vector3 p)
    {
        foreach (var t in taken)
            if ((t - p).sqrMagnitude < Spacing * Spacing) return true;
        return false;
    }

    // the median distance from a seed to its nearest neighbour
    private static float NearestSpacing(List<Vector3> points)
    {
        if (points.Count < 2) return .08f;
        var nearest = new List<float>();
        foreach (var a in points)
        {
            float best = float.MaxValue;
            foreach (var b in points) if (a != b) best = Mathf.Min(best, (a - b).sqrMagnitude);
            nearest.Add(Mathf.Sqrt(best));
        }
        nearest.Sort();
        return Mathf.Clamp(nearest[nearest.Count / 2], .03f, .3f);
    }
}
