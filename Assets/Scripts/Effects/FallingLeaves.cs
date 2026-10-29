using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaves come off the broadleaf trees around the camera now and then and flutter down: each from a random spot in a
/// tree's crown, tumbling as it falls, settling on the ground a while and then shrinking away. Each falling leaf is one
/// of that tree's own leaves: the leaf texture is looked over once (alpha > half) for single leaves standing apart,
/// and where the leaves run together (strands, sprays), small pieces of the spray are cut out instead; each becomes a
/// little card with the tree's own texture and tint, so an autumn tree drops its orange strands and a lush tree its
/// round green leaves. In a gust (WindGusts) the trees shed more and toss leaves downwind from their crowns, more the
/// nearer and the more upwind of the camera they stand (none where no tree is near); leaves in the air and on the
/// ground are swept downwind. One particle system per leaf texture, built here. Only trees within
/// <see cref="range"/> of the camera shed; the pines and firs don't. Presentation only.
/// </summary>
public sealed class FallingLeaves : MonoBehaviour
{
    [SerializeField, Tooltip("Lit particle material the leaf cards start from (M_FallingLeaf); each tree's texture and tint go on a copy.")]
    private Material leafMaterial;
    [SerializeField, Min(1f)] private float range = 40f;
    [SerializeField, Min(0f), Tooltip("Leaves per second around the camera when it is calm.")]
    private float leavesPerSecond = 3f;
    [SerializeField, Min(0f), Tooltip("How many times as many come off at the height of a gust.")]
    private float gustShed = 5f;
    [SerializeField, Min(0f), Tooltip("How hard a gust pushes the leaves (m/s²).")]
    private float windPush = 5f;
    [SerializeField, Min(0f), Tooltip("Leaves per second tossed downwind from the crowns at the height of a gust, with trees all around upwind.")]
    private float blownPerSecond = 9f;
    [SerializeField, Min(1f), Tooltip("Trees further than this from the camera toss no leaves its way in a gust.")]
    private float blownReach = 30f;
    [SerializeField, Min(10)] private int maxLeaves = 500;
    [SerializeField] private Vector2 leafSize = new Vector2(.16f, .3f);
    [SerializeField, Min(1f), Tooltip("Trees nearer than this shed the most; further ones shed less with the square of the distance.")]
    private float nearShed = 8f;

    private static readonly string[] NoLeaves = { "Pine", "Fir", "Needle" };
    private const int Sample = 256, MaxCards = 6;

    private sealed class Kind
    {
        public ParticleSystem system;
        public Color tint;
    }

    private readonly List<(Renderer crown, Kind kind)> trees = new List<(Renderer, Kind)>();
    private readonly Dictionary<Texture, Kind> kinds = new Dictionary<Texture, Kind>();
    private readonly List<int> near = new List<int>();
    private readonly List<float> upwind = new List<float>();
    private float upwindSum, owed, owedBlown, nextLook;

    private void Start() => FindTrees();

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null || trees.Count == 0) return;
        Vector3 eye = cam.transform.position;
        float gust = WindGusts.Strength;
        if (Time.time >= nextLook)
        {
            nextLook = Time.time + .5f;
            Look(eye);
        }

        Vector3 wind = WindGusts.Direction*windPush*gust;
        foreach (var k in kinds.Values)
        {
            var force = k.system.forceOverLifetime;
            force.x = wind.x;
            force.z = wind.z;
        }
        if (near.Count == 0) return;

        // in a gust: leaves torn from the crowns upwind and tossed downwind, as many as there are trees to give them
        owedBlown += blownPerSecond*gust*gust*Mathf.Clamp01(upwindSum/3f)*Time.deltaTime;
        for (; owedBlown >= 1f && upwindSum > 0f; owedBlown -= 1f)
        {
            float roll = Random.value*upwindSum;
            int pick = near[0];
            for (int i = 0; i < near.Count; i++) { roll -= upwind[i]; if (roll <= 0f) { pick = near[i]; break; } }
            Shed(trees[pick], WindGusts.Direction*Random.Range(2.5f, 4f) + Vector3.up*Random.Range(0f, .8f), Random.Range(8f, 12f));
        }

        owed += leavesPerSecond*(1f + gustShed*gust)*Time.deltaTime;
        for (; owed >= 1f; owed -= 1f)
        {
            // the nearer trees shed the most
            int pick = near[Random.Range(0, near.Count)];
            for (int tries = 0; tries < 6; tries++)
            {
                float d = Mathf.Max((trees[pick].crown.bounds.center - eye).magnitude, nearShed);
                if (Random.value < nearShed*nearShed/(d*d)) break;
                pick = near[Random.Range(0, near.Count)];
            }
            Shed(trees[pick], Random.insideUnitSphere*.3f + WindGusts.Direction*gust*1.5f, Random.Range(10f, 16f));
        }
    }

    // which trees are around, and how much each can toss toward the camera in a gust (near and upwind)
    private void Look(Vector3 eye)
    {
        near.Clear();
        upwind.Clear();
        upwindSum = 0f;
        float r2 = range*range;
        for (int i = 0; i < trees.Count; i++)
        {
            var crown = trees[i].crown;
            if (crown == null || !crown.gameObject.activeInHierarchy) continue;
            Vector3 to = crown.bounds.center - eye;
            to.y = 0f;
            if (to.sqrMagnitude > r2) continue;
            near.Add(i);
            float d = to.magnitude;
            float w = Mathf.Clamp01(1f - d/blownReach)*(.35f + .65f*Mathf.Max(0f, Vector3.Dot(-to/Mathf.Max(d, .01f), WindGusts.Direction)));
            upwind.Add(w);
            upwindSum += w;
        }
    }

    private void Shed((Renderer crown, Kind kind) tree, Vector3 velocity, float life)
    {
        var b = tree.crown.bounds;
        // somewhere in the crown: the upper part of the tree, a little inside its outline
        var emit = new ParticleSystem.EmitParams
        {
            position = b.center + Vector3.Scale(Random.insideUnitSphere, new Vector3(b.extents.x*.8f, b.extents.y*.45f, b.extents.z*.8f)) + Vector3.up*b.extents.y*.25f,
            velocity = velocity,
            startColor = Vary(),
            startSize = Random.Range(leafSize.x, leafSize.y),
            rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
            startLifetime = life,
        };
        tree.kind.system.Emit(emit, 1);
    }

    // a little light and dark from leaf to leaf
    private static Color Vary()
    {
        float v = Random.Range(.82f, 1.1f);
        return new Color(v, v*Random.Range(.96f, 1.04f), v, 1f);
    }

    // every renderer with a broadleaf leaf material, grouped by its leaf texture
    private void FindTrees()
    {
        trees.Clear();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.bounds.size.y < 2.5f) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.name.IndexOf("Lea", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool needles = false;
                foreach (var x in NoLeaves) if (m.name.IndexOf(x, System.StringComparison.OrdinalIgnoreCase) >= 0) needles = true;
                if (needles) break;
                var kind = KindOf(m);
                if (kind != null) trees.Add((r, kind));
                break;
            }
        }
        int perKind = Mathf.Max(40, maxLeaves/Mathf.Max(1, kinds.Count));
        foreach (var k in kinds.Values)
        {
            var main = k.system.main;
            main.maxParticles = perKind;
        }
    }

    private Kind KindOf(Material m)
    {
        Texture tex = LeafTexture(m);
        if (tex == null) return null;
        if (kinds.TryGetValue(tex, out var found)) return found;
        var cards = Cards(tex);
        if (cards.Count == 0) return null;
        var material = new Material(leafMaterial) { name = "Falling " + tex.name };
        material.SetTexture("_BaseMap", tex);
        material.SetColor("_BaseColor", Tint(m));
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", .5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetFloat("_Cull", 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        var kind = new Kind { system = Build(tex.name, material, cards), tint = Tint(m) };
        kinds[tex] = kind;
        return kind;
    }

    private static Texture LeafTexture(Material m)
    {
        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
        // shader graphs: the first texture that isn't a mask
        var shader = m.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            string desc = shader.GetPropertyDescription(i);
            if (desc.Contains("Mask") || desc.Contains("Normal") || desc.Contains("Emissi")) continue;
            var t = m.GetTexture(shader.GetPropertyName(i));
            if (t != null) return t;
        }
        return null;
    }

    // the leaves' own tint: Lit's base colour, or a shader graph's leaf tint (those textures are grey)
    private static Color Tint(Material m)
    {
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        var shader = m.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
            if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color && shader.GetPropertyDescription(i).Contains("Tint"))
            {
                var c = m.GetColor(shader.GetPropertyName(i))*1.7f;
                c.a = 1f;
                return c;
            }
        return Color.white;
    }

    // Single leaves in the texture: patches of opaque texels standing apart, not too small and not whole sprays. Where
    // the leaves run together, small windows on the spray's edge instead. Rects in texture UV.
    private static List<Rect> Cards(Texture tex)
    {
        var rt = RenderTexture.GetTemporary(Sample, Sample, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(Sample, Sample, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, Sample, Sample), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var px = read.GetPixels32();
        Destroy(read);

        var solid = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) solid[i] = px[i].a > 127;
        var label = new int[px.Length];
        var patches = new List<(RectInt box, int area)>();
        var stack = new Stack<int>();
        int next = 0;
        for (int start = 0; start < px.Length; start++)
        {
            if (!solid[start] || label[start] != 0) continue;
            next++;
            int area = 0, x0 = Sample, y0 = Sample, x1 = 0, y1 = 0;
            stack.Push(start);
            label[start] = next;
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i%Sample, y = i/Sample;
                area++;
                x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
                if (x > 0 && solid[i - 1] && label[i - 1] == 0) { label[i - 1] = next; stack.Push(i - 1); }
                if (x < Sample - 1 && solid[i + 1] && label[i + 1] == 0) { label[i + 1] = next; stack.Push(i + 1); }
                if (y > 0 && solid[i - Sample] && label[i - Sample] == 0) { label[i - Sample] = next; stack.Push(i - Sample); }
                if (y < Sample - 1 && solid[i + Sample] && label[i + Sample] == 0) { label[i + Sample] = next; stack.Push(i + Sample); }
            }
            patches.Add((new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1), area));
        }

        var cards = new List<Rect>();
        var taken = new List<(RectInt box, int area)>();
        patches.Sort((a, b) => b.area.CompareTo(a.area));
        foreach (var p in patches)
        {
            if (p.area < 25 || p.box.width > 40 || p.box.height > 40) continue;
            // the textures repeat their leaves (2 x 2 copies): one of each
            if (taken.Exists(t => Mathf.Abs(t.box.width - p.box.width) <= 1 && Mathf.Abs(t.box.height - p.box.height) <= 1 && Mathf.Abs(t.area - p.area) <= 4)) continue;
            taken.Add(p);
            cards.Add(Uv(p.box, 1));
            if (cards.Count >= MaxCards) break;
        }
        if (cards.Count >= 2) return cards;

        // run together: windows a little bigger than a leaf, about half covered
        cards.Clear();
        var rng = new System.Random(tex.name.GetHashCode());
        for (int tries = 0; tries < 400 && cards.Count < MaxCards; tries++)
        {
            int cx = rng.Next(12, Sample - 12), cy = rng.Next(12, Sample - 12);
            if (!solid[cy*Sample + cx]) continue;
            var box = new RectInt(cx - 11, cy - 11, 22, 22);
            int covered = 0;
            for (int y = box.yMin; y < box.yMax; y++) for (int x = box.xMin; x < box.xMax; x++) if (solid[y*Sample + x]) covered++;
            float share = covered/(22f*22f);
            if (share < .2f || share > .6f) continue;
            // not the same spot of another copy of the spray
            if (cards.Exists(c => Mathf.Abs(Mathf.Repeat(c.center.x*2f, 1f) - Mathf.Repeat((cx + .5f)/Sample*2f, 1f)) < .06f && Mathf.Abs(Mathf.Repeat(c.center.y*2f, 1f) - Mathf.Repeat((cy + .5f)/Sample*2f, 1f)) < .06f)) continue;
            cards.Add(Uv(box, 0));
        }
        return cards;
    }

    private static Rect Uv(RectInt box, int pad) =>
        Rect.MinMaxRect((box.xMin - pad)/(float)Sample, (box.yMin - pad)/(float)Sample, (box.xMax + pad)/(float)Sample, (box.yMax + pad)/(float)Sample);

    private ParticleSystem Build(string name, Material material, List<Rect> cards)
    {
        var go = new GameObject("Falling Leaves - " + name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxLeaves;
        main.gravityModifier = .07f;
        main.startRotation3D = true;
        main.playOnAwake = true;
        main.loop = true;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        // drifting down slowly, side to side
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 99f;
        drag.drag = .6f;
        drag.multiplyDragByParticleSize = false;
        drag.multiplyDragByParticleVelocity = true;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = .9f;
        noise.frequency = .7f;
        noise.scrollSpeed = .35f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var force = ps.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.World;
        force.x = 0f; force.y = 0f; force.z = 0f;

        // tumbling while it moves, lying still once it has settled
        var spin = ps.rotationBySpeed;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(4f, -4f);
        spin.y = new ParticleSystem.MinMaxCurve(3f, -3f);
        spin.z = new ParticleSystem.MinMaxCurve(5f, -5f);
        spin.range = new Vector2(0f, 1.2f);

        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.dampen = .9f;
        collision.bounce = 0f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .3f;
        collision.collidesWith = ~LayerMask.GetMask("Ignore Raycast");

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.88f, 1f), new Keyframe(1f, 0f)));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        var meshes = new Mesh[cards.Count];
        for (int i = 0; i < cards.Count; i++) meshes[i] = Card(cards[i], name + " " + i);
        renderer.SetMeshes(meshes);
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        ps.Play();
        return ps;
    }

    // a card one unit across its longer side, the leaf's own shape on it, bent a little along its middle
    private static Mesh Card(Rect uv, string name)
    {
        float aspect = uv.width/Mathf.Max(uv.height, 1e-4f);
        float w = aspect >= 1f ? .5f : .5f*aspect, h = aspect >= 1f ? .5f/aspect : .5f;
        var mesh = new Mesh { name = "Falling Leaf " + name };
        mesh.vertices = new[]
        {
            new Vector3(-w, -h, .05f), new Vector3(0f, -h, -.04f), new Vector3(w, -h, .05f),
            new Vector3(-w, h, .05f), new Vector3(0f, h, -.04f), new Vector3(w, h, .05f),
        };
        mesh.uv = new[]
        {
            new Vector2(uv.xMin, uv.yMin), new Vector2(uv.center.x, uv.yMin), new Vector2(uv.xMax, uv.yMin),
            new Vector2(uv.xMin, uv.yMax), new Vector2(uv.center.x, uv.yMax), new Vector2(uv.xMax, uv.yMax),
        };
        mesh.triangles = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
