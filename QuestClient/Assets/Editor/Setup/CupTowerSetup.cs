using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Replaces the placeholder cube visual on the grabbable hologram with a transparent
/// 3-2-1 red solo cup pyramid built from the decimated CAD mesh. The hologram GameObject
/// itself is kept so the Building Blocks grab wiring (Grabbable, HandGrabInteractable,
/// Rigidbody) survives the swap.
/// </summary>
public static class CupTowerSetup
{
    const string MeshPath = "Assets/Models/RedSoloCup.obj";
    const string MaterialPath = "Assets/Materials/HologramCup.mat";
    const string ShaderName = "AssemblyGuide/HologramTransparent";
    const string HologramName = "Cup Tower Hologram";

    [MenuItem("Tools/Quest Client/Build Cup Tower Hologram")]
    public static void BuildCupTower()
    {
        var hologram = GameObject.Find(HologramName) ?? GameObject.Find("Hologram");
        if (hologram == null)
        {
            Debug.LogError("[CupTower] No 'Hologram' object in the open scene. Open Assets/Scenes/MrHologram.unity first.");
            return;
        }

        var mesh = LoadCupMesh();
        if (mesh == null)
            return;

        var material = LoadOrCreateMaterial();
        if (material == null)
            return;

        hologram.name = HologramName;

        // The cube placeholder was scaled to 0.2; the cup mesh is already real-world size.
        hologram.transform.localScale = Vector3.one;
        hologram.transform.position = new Vector3(0f, 0.9f, 0.6f);

        RemoveComponent<MeshRenderer>(hologram);
        RemoveComponent<MeshFilter>(hologram);
        ClearPreviousCups(hologram.transform);

        var bounds = mesh.bounds;
        var height = bounds.size.y;
        var diameter = Mathf.Max(bounds.size.x, bounds.size.z);

        // 3-2-1 pyramid. Rows alternate orientation: inverted cups support upright cups,
        // whose rims in turn support the inverted cup on top.
        var layout = new[]
        {
            new CupPlacement(-diameter, 0, true),
            new CupPlacement(0f, 0, true),
            new CupPlacement(diameter, 0, true),
            new CupPlacement(-diameter * 0.5f, 1, false),
            new CupPlacement(diameter * 0.5f, 1, false),
            new CupPlacement(0f, 2, true),
        };

        for (var i = 0; i < layout.Length; i++)
            CreateCup(hologram.transform, mesh, material, layout[i], i + 1, height);

        FitCollider(hologram, diameter, height, layout.Length);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log($"[CupTower] Built a 6-cup pyramid on '{hologram.name}'. " +
                  $"Cup {bounds.size.x:F3} x {height:F3} x {bounds.size.z:F3} m, " +
                  $"tower {diameter * 3f:F2} m wide and {height * 3f:F2} m tall, " +
                  $"{mesh.triangles.Length / 3 * layout.Length} triangles total.");
    }

    readonly struct CupPlacement
    {
        public readonly float X;
        public readonly int Row;
        public readonly bool Inverted;

        public CupPlacement(float x, int row, bool inverted)
        {
            X = x;
            Row = row;
            Inverted = inverted;
        }
    }

    static void CreateCup(Transform parent, Mesh mesh, Material material, CupPlacement placement, int order, float height)
    {
        var cup = new GameObject($"Cup {order:00}");
        cup.transform.SetParent(parent, false);

        var rowBase = placement.Row * height;

        // The mesh origin sits at the centre of the cup's base, so an inverted cup has to be
        // lifted by one height after the flip.
        if (placement.Inverted)
        {
            cup.transform.localPosition = new Vector3(placement.X, rowBase + height, 0f);
            cup.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        }
        else
        {
            cup.transform.localPosition = new Vector3(placement.X, rowBase, 0f);
            cup.transform.localRotation = Quaternion.identity;
        }

        cup.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = cup.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void FitCollider(GameObject hologram, float diameter, float height, int cupCount)
    {
        var collider = hologram.GetComponent<BoxCollider>();
        if (collider == null)
            collider = hologram.AddComponent<BoxCollider>();

        var width = diameter * 3f;
        var totalHeight = height * 3f;
        collider.center = new Vector3(0f, totalHeight * 0.5f, 0f);
        collider.size = new Vector3(width, totalHeight, diameter);
    }

    static void ClearPreviousCups(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith("Cup "))
                Object.DestroyImmediate(child.gameObject);
        }
    }

    static void RemoveComponent<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component != null)
            Object.DestroyImmediate(component);
    }

    static Mesh LoadCupMesh()
    {
        var importer = AssetImporter.GetAtPath(MeshPath) as ModelImporter;
        if (importer != null)
        {
            // The OBJ carries no normals, so Unity has to generate smooth ones.
            var changed = importer.importNormals != ModelImporterNormals.Calculate
                          || !Mathf.Approximately(importer.normalSmoothingAngle, 60f)
                          || importer.isReadable != true;
            if (changed)
            {
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 60f;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath);
            var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
            mesh = filter != null ? filter.sharedMesh : null;
        }

        if (mesh == null)
            Debug.LogError($"[CupTower] Could not load a mesh from {MeshPath}.");
        return mesh;
    }

    static Material LoadOrCreateMaterial()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[CupTower] Shader '{ShaderName}' not found.");
            return null;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", new Color(0.30f, 0.68f, 1f, 0.055f));
        material.SetColor("_RimColor", new Color(0.55f, 0.85f, 1f, 1f));
        material.SetFloat("_RimPower", 2.5f);
        material.SetFloat("_RimStrength", 0.32f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
