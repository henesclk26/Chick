using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FarmReferenceTools
{
    /// <summary>
    /// Creates reusable, deliberately simple farm placeholders and saves their
    /// meshes and materials as project assets.
    /// </summary>
    public static class FarmReferencePlaceholderFactory
    {
        private const string GeneratedFolder = "Assets/Art/FarmReference/Generated";
        private const int CylinderSides = 16;

        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>(StringComparer.Ordinal);

        public static GameObject CreateCoop(Transform parent, Vector3 worldPosition)
        {
            EnsureGeneratedFolder();

            GameObject root = CreateRoot("FarmReference_Coop", parent, worldPosition);
            Material red = GetMaterial("Coop_Red", new Color32(174, 48, 43, 255));
            Material redLight = GetMaterial("Coop_Red_Light", new Color32(194, 63, 54, 255));
            Material trim = GetMaterial("Coop_Cream_Trim", new Color32(239, 222, 185, 255));
            Material roof = GetMaterial("Coop_Charcoal_Roof", new Color32(47, 54, 61, 255));
            Material wood = GetMaterial("Coop_Door_Wood", new Color32(117, 72, 42, 255));
            Material woodLight = GetMaterial("Coop_Door_Wood_Light", new Color32(145, 93, 54, 255));
            Material woodDark = GetMaterial("Coop_Door_Wood_Dark", new Color32(80, 49, 31, 255));
            Material shadow = GetMaterial("Coop_Entrance_Shadow", new Color32(31, 29, 27, 255));

            // Four short legs lift the floor clear of the ground.
            AddBox(root.transform, "Coop_Support_Front_Left", new Vector3(-1.62f, 0.25f, -1.30f),
                new Vector3(0.18f, 0.50f, 0.18f), wood, false);
            AddBox(root.transform, "Coop_Support_Front_Right", new Vector3(1.62f, 0.25f, -1.30f),
                new Vector3(0.18f, 0.50f, 0.18f), wood, false);
            AddBox(root.transform, "Coop_Support_Back_Left", new Vector3(-1.62f, 0.25f, 1.30f),
                new Vector3(0.18f, 0.50f, 0.18f), wood, false);
            AddBox(root.transform, "Coop_Support_Back_Right", new Vector3(1.62f, 0.25f, 1.30f),
                new Vector3(0.18f, 0.50f, 0.18f), wood, false);

            AddBox(root.transform, "Coop_Raised_Floor", new Vector3(0f, 0.55f, 0f),
                new Vector3(3.90f, 0.30f, 3.50f), woodLight, true);

            const float wallY = 1.68f;
            const float wallHeight = 1.96f;
            // Side and rear walls form a simple sheltered interior.
            AddBox(root.transform, "Coop_Wall_Left", new Vector3(-1.64f, wallY, 0f),
                new Vector3(0.24f, wallHeight, 3.10f), red, true);
            AddBox(root.transform, "Coop_Wall_Right", new Vector3(1.64f, wallY, 0f),
                new Vector3(0.24f, wallHeight, 3.10f), red, true);
            AddBox(root.transform, "Coop_Wall_Back", new Vector3(0f, wallY, 1.43f),
                new Vector3(3.30f, wallHeight, 0.24f), red, true);

            // The front is built around a real, dark entrance opening facing -Z.
            AddBox(root.transform, "Coop_Front_Wall_Left", new Vector3(-1.16f, wallY, -1.43f),
                new Vector3(1.20f, wallHeight, 0.24f), red, true);
            AddBox(root.transform, "Coop_Front_Wall_Right", new Vector3(1.16f, wallY, -1.43f),
                new Vector3(1.20f, wallHeight, 0.24f), red, true);
            AddBox(root.transform, "Coop_Front_Lintel", new Vector3(0f, 2.52f, -1.43f),
                new Vector3(1.15f, 0.28f, 0.24f), red, true);
            AddBox(root.transform, "Coop_Entrance_Shadow", new Vector3(0f, 1.49f, -1.275f),
                new Vector3(1.08f, 1.55f, 0.025f), shadow, false);

            // Cream corner boards and doorway trim add contrast at game scale.
            float[] cornerX = { -1.63f, 1.63f };
            float[] cornerZ = { -1.45f, 1.45f };
            for (int xIndex = 0; xIndex < cornerX.Length; xIndex++)
            {
                for (int zIndex = 0; zIndex < cornerZ.Length; zIndex++)
                {
                    AddBox(root.transform, "Coop_Cream_Corner_Trim_" + xIndex + "_" + zIndex,
                        new Vector3(cornerX[xIndex], wallY, cornerZ[zIndex]),
                        new Vector3(0.10f, wallHeight + 0.04f, 0.10f), trim, false);
                }
            }
            AddBox(root.transform, "Coop_Doorway_Trim_Left", new Vector3(-0.64f, 1.48f, -1.58f),
                new Vector3(0.13f, 1.60f, 0.12f), trim, false);
            AddBox(root.transform, "Coop_Doorway_Trim_Right", new Vector3(0.64f, 1.48f, -1.58f),
                new Vector3(0.13f, 1.60f, 0.12f), trim, false);
            AddBox(root.transform, "Coop_Doorway_Trim_Top", new Vector3(0f, 2.28f, -1.58f),
                new Vector3(1.40f, 0.13f, 0.12f), trim, false);

            // Small vents sit high on each side wall; these are visual details only.
            AddBox(root.transform, "Coop_Vent_Left", new Vector3(-1.775f, 2.03f, 0.62f),
                new Vector3(0.025f, 0.34f, 0.58f), shadow, false);
            AddBox(root.transform, "Coop_Vent_Right", new Vector3(1.775f, 2.03f, 0.62f),
                new Vector3(0.025f, 0.34f, 0.58f), shadow, false);
            AddBox(root.transform, "Coop_Vent_Left_Trim", new Vector3(-1.80f, 2.03f, 0.62f),
                new Vector3(0.035f, 0.42f, 0.66f), trim, false);
            AddBox(root.transform, "Coop_Vent_Right_Trim", new Vector3(1.80f, 2.03f, 0.62f),
                new Vector3(0.035f, 0.42f, 0.66f), trim, false);

            Mesh gableMesh = GetOrCreateMesh("Coop_Gable_End_3p5x1p0x0p18", CreateGablePrism(3.50f, 1.00f, 0.18f));
            AddMesh(root.transform, "Coop_Gable_Front", gableMesh, new Vector3(0f, 3.02f, -1.43f),
                Vector3.one, Quaternion.identity, redLight, false);
            AddMesh(root.transform, "Coop_Gable_Back", gableMesh, new Vector3(0f, 3.02f, 1.43f),
                Vector3.one, Quaternion.identity, redLight, false);

            // Two thick roof planes meet above the gable ridge.
            AddBox(root.transform, "Coop_Roof_Left", new Vector3(-0.92f, 3.24f, 0f),
                new Vector3(2.22f, 0.17f, 3.78f), roof, false, new Vector3(0f, 0f, 28.3f));
            AddBox(root.transform, "Coop_Roof_Right", new Vector3(0.92f, 3.24f, 0f),
                new Vector3(2.22f, 0.17f, 3.78f), roof, false, new Vector3(0f, 0f, -28.3f));
            AddBox(root.transform, "Coop_Ridge_Cap", new Vector3(0f, 3.75f, 0f),
                new Vector3(0.16f, 0.14f, 3.88f), roof, false);
            AddBox(root.transform, "Coop_Eave_Front", new Vector3(0f, 2.71f, -1.78f),
                new Vector3(3.72f, 0.15f, 0.13f), trim, false);
            AddBox(root.transform, "Coop_Eave_Back", new Vector3(0f, 2.71f, 1.78f),
                new Vector3(3.72f, 0.15f, 0.13f), trim, false);

            // A short inclined ramp descends out of the front entrance.
            AddBox(root.transform, "Coop_Entrance_Ramp", new Vector3(0f, 0.30f, -2.43f),
                new Vector3(1.12f, 0.12f, 1.62f), woodLight, true, new Vector3(-20f, 0f, 0f));
            AddBox(root.transform, "Coop_Ramp_Edge_Left", new Vector3(-0.56f, 0.34f, -2.43f),
                new Vector3(0.08f, 0.10f, 1.62f), wood, false, new Vector3(-20f, 0f, 0f));
            AddBox(root.transform, "Coop_Ramp_Edge_Right", new Vector3(0.56f, 0.34f, -2.43f),
                new Vector3(0.08f, 0.10f, 1.62f), wood, false, new Vector3(-20f, 0f, 0f));

            // The hinged panel is mounted at the right jamb and swung outward.
            GameObject hinge = new GameObject("Coop_Door_Hinge_Open");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(0.58f, 0.68f, -1.66f);
            hinge.transform.localRotation = Quaternion.Euler(0f, -66f, 0f);
            AddBox(hinge.transform, "Coop_Door_Panel", new Vector3(-0.44f, 0.76f, 0f),
                new Vector3(0.88f, 1.48f, 0.13f), wood, true);
            AddBox(hinge.transform, "Coop_Door_Board_Left", new Vector3(-0.72f, 0.76f, -0.075f),
                new Vector3(0.22f, 1.34f, 0.035f), woodLight, false);
            AddBox(hinge.transform, "Coop_Door_Board_Center", new Vector3(-0.44f, 0.76f, -0.075f),
                new Vector3(0.22f, 1.34f, 0.035f), woodLight, false);
            AddBox(hinge.transform, "Coop_Door_Board_Right", new Vector3(-0.16f, 0.76f, -0.075f),
                new Vector3(0.22f, 1.34f, 0.035f), woodLight, false);
            AddBox(hinge.transform, "Coop_Door_Brace_Top", new Vector3(-0.44f, 1.22f, -0.10f),
                new Vector3(0.76f, 0.10f, 0.045f), woodDark, false);
            AddBox(hinge.transform, "Coop_Door_Brace_Bottom", new Vector3(-0.44f, 0.30f, -0.10f),
                new Vector3(0.76f, 0.10f, 0.045f), woodDark, false);

            return root;
        }

        public static GameObject CreateSilo(Transform parent, Vector3 worldPosition)
        {
            EnsureGeneratedFolder();

            GameObject root = CreateRoot("FarmReference_Silo", parent, worldPosition);
            Material siloBody = GetMaterial("Silo_Cream_Gray", new Color32(203, 205, 197, 255));
            Material siloBand = GetMaterial("Silo_Band_Gray", new Color32(153, 160, 158, 255));
            Material siloRoof = GetMaterial("Silo_Roof_Gray", new Color32(112, 120, 123, 255));
            Material ladder = GetMaterial("Silo_Ladder_Dark_Gray", new Color32(73, 81, 83, 255));

            Mesh cylinder = GetOrCreateMesh("FarmReference_Faceted_Cylinder_16", CreateUnitCylinder(CylinderSides));
            Mesh cone = GetOrCreateMesh("FarmReference_Faceted_Cone_16", CreateUnitCone(CylinderSides));

            // A broad footing and single coarse collider keep the body simple to navigate around.
            AddMesh(root.transform, "Silo_Base_Footing", cylinder, new Vector3(0f, 0.16f, 0f),
                new Vector3(3.12f, 0.32f, 3.12f), Quaternion.identity, siloBand, false);
            GameObject body = AddMesh(root.transform, "Silo_Main_Body", cylinder, new Vector3(0f, 3.35f, 0f),
                new Vector3(2.90f, 6.10f, 2.90f), Quaternion.identity, siloBody, false);
            BoxCollider bodyCollider = body.AddComponent<BoxCollider>();
            bodyCollider.center = Vector3.zero;
            bodyCollider.size = Vector3.one;

            float[] bandHeights = { 1.15f, 2.65f, 4.15f, 5.65f, 6.32f };
            for (int i = 0; i < bandHeights.Length; i++)
            {
                AddMesh(root.transform, "Silo_Horizontal_Band_" + (i + 1), cylinder,
                    new Vector3(0f, bandHeights[i], 0f), new Vector3(2.99f, 0.10f, 2.99f),
                    Quaternion.identity, siloBand, false);
            }

            AddMesh(root.transform, "Silo_Conical_Roof", cone, new Vector3(0f, 7.08f, 0f),
                new Vector3(3.05f, 1.46f, 3.05f), Quaternion.identity, siloRoof, false);
            AddMesh(root.transform, "Silo_Roof_Cap", cylinder, new Vector3(0f, 7.86f, 0f),
                new Vector3(0.34f, 0.14f, 0.34f), Quaternion.identity, siloBand, false);

            // A compact ladder provides a readable scale cue without adding small-part colliders.
            AddBox(root.transform, "Silo_Ladder_Rail_Left", new Vector3(-0.36f, 2.55f, -1.48f),
                new Vector3(0.055f, 3.70f, 0.06f), ladder, false);
            AddBox(root.transform, "Silo_Ladder_Rail_Right", new Vector3(0.36f, 2.55f, -1.48f),
                new Vector3(0.055f, 3.70f, 0.06f), ladder, false);
            for (int i = 0; i < 7; i++)
            {
                float rungY = 0.95f + i * 0.53f;
                AddBox(root.transform, "Silo_Ladder_Rung_" + (i + 1), new Vector3(0f, rungY, -1.51f),
                    new Vector3(0.76f, 0.055f, 0.07f), ladder, false);
            }

            return root;
        }

        private static GameObject CreateRoot(string name, Transform parent, Vector3 worldPosition)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject AddBox(Transform parent, string name, Vector3 localPosition, Vector3 size,
            Material material, bool addCollider, Vector3 localEulerAngles = default(Vector3))
        {
            Mesh cube = GetOrCreateMesh("FarmReference_UnitCube", CreateUnitCube());
            GameObject box = AddMesh(parent, name, cube, localPosition, size,
                Quaternion.Euler(localEulerAngles), material, false);
            if (addCollider)
            {
                BoxCollider collider = box.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = Vector3.one;
            }
            return box;
        }

        private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Vector3 localPosition,
            Vector3 localScale, Quaternion localRotation, Material material, bool addBoxCollider)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;

            MeshFilter filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            if (addBoxCollider)
            {
                BoxCollider collider = part.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = Vector3.one;
            }
            return part;
        }

        private static Material GetMaterial(string shortName, Color color)
        {
            string assetName = "FarmReference_" + shortName;
            Material cached;
            if (MaterialCache.TryGetValue(assetName, out cached) && cached != null)
                return cached;

            string assetPath = GeneratedFolder + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");

                material = new Material(shader);
                material.name = assetName;
                material.color = color;
                material.enableInstancing = true;
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.18f);
                AssetDatabase.CreateAsset(material, assetPath);
            }

            MaterialCache[assetName] = material;
            return material;
        }

        private static Mesh GetOrCreateMesh(string assetName, Mesh newMesh)
        {
            Mesh cached;
            if (MeshCache.TryGetValue(assetName, out cached) && cached != null)
            {
                UnityEngine.Object.DestroyImmediate(newMesh);
                return cached;
            }

            string assetPath = GeneratedFolder + "/" + assetName + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(newMesh);
                MeshCache[assetName] = existing;
                return existing;
            }

            newMesh.name = assetName;
            AssetDatabase.CreateAsset(newMesh, assetPath);
            MeshCache[assetName] = newMesh;
            return newMesh;
        }

        private static void EnsureGeneratedFolder()
        {
            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "FarmReference");
            EnsureFolder("Assets/Art/FarmReference", "Generated");
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            string folderPath = parentPath + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string guid = AssetDatabase.CreateFolder(parentPath, folderName);
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException("Could not create project asset folder: " + folderPath);
            }
        }

        private static Mesh CreateUnitCube()
        {
            Mesh mesh = new Mesh();
            List<Vector3> vertices = new List<Vector3>(24);
            List<int> triangles = new List<int>(36);

            AddCubeFace(vertices, triangles, new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f)); // front (-Z)
            AddCubeFace(vertices, triangles, new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f)); // back (+Z)
            AddCubeFace(vertices, triangles, new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f)); // left (-X)
            AddCubeFace(vertices, triangles, new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f)); // right (+X)
            AddCubeFace(vertices, triangles, new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)); // top (+Y)
            AddCubeFace(vertices, triangles, new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f)); // bottom (-Y)

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCubeFace(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
        }

        private static Mesh CreateGablePrism(float width, float height, float depth)
        {
            float x = width * 0.5f;
            float y = height * 0.5f;
            float z = depth * 0.5f;
            Vector3 frontLeft = new Vector3(-x, -y, -z);
            Vector3 frontRight = new Vector3(x, -y, -z);
            Vector3 frontPeak = new Vector3(0f, y, -z);
            Vector3 backLeft = new Vector3(-x, -y, z);
            Vector3 backRight = new Vector3(x, -y, z);
            Vector3 backPeak = new Vector3(0f, y, z);

            List<Vector3> vertices = new List<Vector3>(18);
            List<int> triangles = new List<int>(36);
            AddTriangle(vertices, triangles, frontLeft, frontPeak, frontRight); // front (-Z)
            AddTriangle(vertices, triangles, backLeft, backRight, backPeak); // back (+Z)
            AddQuad(vertices, triangles, frontLeft, frontRight, backRight, backLeft); // bottom
            AddQuad(vertices, triangles, frontLeft, backLeft, backPeak, frontPeak); // left slope
            AddQuad(vertices, triangles, frontRight, frontPeak, backPeak, backRight); // right slope

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        private static Mesh CreateUnitCylinder(int sides)
        {
            List<Vector3> vertices = new List<Vector3>(sides * 12);
            List<int> triangles = new List<int>(sides * 12);

            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.PI * 2f * i / sides;
                float a1 = Mathf.PI * 2f * (i + 1) / sides;
                Vector3 bottom0 = CirclePoint(a0, -0.5f, 0.5f);
                Vector3 top0 = CirclePoint(a0, 0.5f, 0.5f);
                Vector3 bottom1 = CirclePoint(a1, -0.5f, 0.5f);
                Vector3 top1 = CirclePoint(a1, 0.5f, 0.5f);

                AddTriangle(vertices, triangles, bottom0, top0, top1);
                AddTriangle(vertices, triangles, bottom0, top1, bottom1);
                AddTriangle(vertices, triangles, new Vector3(0f, 0.5f, 0f), top1, top0);
                AddTriangle(vertices, triangles, new Vector3(0f, -0.5f, 0f), bottom0, bottom1);
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateUnitCone(int sides)
        {
            List<Vector3> vertices = new List<Vector3>(sides * 6);
            List<int> triangles = new List<int>(sides * 6);
            Vector3 apex = new Vector3(0f, 0.5f, 0f);
            Vector3 center = new Vector3(0f, -0.5f, 0f);

            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.PI * 2f * i / sides;
                float a1 = Mathf.PI * 2f * (i + 1) / sides;
                Vector3 base0 = CirclePoint(a0, -0.5f, 0.5f);
                Vector3 base1 = CirclePoint(a1, -0.5f, 0.5f);
                AddTriangle(vertices, triangles, base0, apex, base1);
                AddTriangle(vertices, triangles, center, base0, base1);
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 CirclePoint(float angle, float y, float radius)
        {
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
    }
}
