using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds bone-rigged (cutout) characters from Assets/Characters/<name>/*.png + rig_data.json
// Menu: Tools > Pet Game > Build Character Rigs
public static class CharacterRigBuilder
{
    [System.Serializable] public class Bone { public string n; public string parent; public float lpx, lpy, lr, pvx, pvy, scale, amp, phase, freq; public int order; }
    [System.Serializable] public class Character { public string name; public string root; public float cycle, bob; public Bone[] bones; }
    [System.Serializable] public class RigData { public float ppu; public Character[] characters; }

    const string Dir = "Assets/Characters";
    static readonly Dictionary<string, float> WorldScale = new Dictionary<string, float> { { "dog", 0.62f }, { "tourist", 1f }, { "zombie", 1.12f } };

    [MenuItem("Tools/Pet Game/Build Character Rigs")]
    public static void BuildAll()
    {
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "rig_data.json")));
        var order = new[] { "zombie", "tourist", "dog" };
        var prefabs = new List<GameObject>();
        foreach (var name in order)
        {
            var c = data.characters.FirstOrDefault(x => x.name == name);
            if (c == null) continue;
            ImportSprites(c, data.ppu);
            prefabs.Add(BuildCharacter(c));
        }
        BuildPreviewScene(prefabs);
        Debug.Log("Character rigs built: " + string.Join(", ", prefabs.Select(p => p.name)));
    }

    static void ImportSprites(Character c, float ppu)
    {
        foreach (var b in c.bones)
        {
            var path = $"{Dir}/{c.name}/{b.n}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning("Missing " + path); continue; }
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spritePixelsPerUnit = ppu;
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(b.pvx, b.pvy);
            s.spriteMeshType = SpriteMeshType.Tight;
            imp.SetTextureSettings(s);
            imp.SaveAndReimport();
        }
    }

    static string GetPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        while (t != null && t != root) { parts.Insert(0, t.name); t = t.parent; }
        return string.Join("/", parts);
    }

    static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
    }

    static GameObject BuildCharacter(Character c)
    {
        string title = char.ToUpper(c.name[0]) + c.name.Substring(1);
        var root = new GameObject(title);
        root.AddComponent<SortingGroup>();
        var map = new Dictionary<string, Transform>();
        foreach (var b in c.bones)
        {
            var go = new GameObject(b.n);
            go.transform.SetParent(string.IsNullOrEmpty(b.parent) ? root.transform : map[b.parent], false);
            go.transform.localPosition = new Vector3(b.lpx, b.lpy, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, b.lr);
            if (Mathf.Abs(b.scale - 1f) > 0.001f) go.transform.localScale = Vector3.one * b.scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{c.name}/{b.n}.png");
            sr.sortingOrder = b.order;
            map[b.n] = go.transform;
        }

        // ---- Walk cycle (looping) ----
        var clip = new AnimationClip { name = title + "_Walk", frameRate = 30 };
        const int samples = 16;
        float w = 2f * Mathf.PI / c.cycle;
        foreach (var b in c.bones)
        {
            if (b.amp < 0.01f) continue;
            string path = GetPath(map[b.n], root.transform);
            var cz = new AnimationCurve();
            for (int i = 0; i <= samples; i++)
            {
                float t = c.cycle * i / samples;
                float arg = w * b.freq * t + b.phase * Mathf.Deg2Rad;
                float val = b.lr + b.amp * Mathf.Sin(arg);
                float tan = b.amp * w * b.freq * Mathf.Cos(arg);
                cz.AddKey(new Keyframe(t, val, tan, tan));
            }
            SetCurve(clip, path, "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, c.cycle, 0f));
            SetCurve(clip, path, "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, c.cycle, 0f));
            SetCurve(clip, path, "localEulerAnglesRaw.z", cz);
        }
        var rb = c.bones.First(x => x.n == c.root);
        string rpath = GetPath(map[c.root], root.transform);
        var cy = new AnimationCurve();
        for (int i = 0; i <= samples; i++) cy.AddKey(c.cycle * i / samples, rb.lpy + c.bob * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * i / samples)));
        for (int i = 0; i < cy.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cy, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(cy, i, AnimationUtility.TangentMode.ClampedAuto); }
        SetCurve(clip, rpath, "m_LocalPosition.x", AnimationCurve.Constant(0f, c.cycle, rb.lpx));
        SetCurve(clip, rpath, "m_LocalPosition.y", cy);
        SetCurve(clip, rpath, "m_LocalPosition.z", AnimationCurve.Constant(0f, c.cycle, 0f));
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, st);

        string clipPath = $"{Dir}/{c.name}/{title}_Walk.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);
        string ctrlPath = $"{Dir}/{c.name}/{title}.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
        root.AddComponent<Animator>().runtimeAnimatorController = ctrl;

        float ws; if (WorldScale.TryGetValue(c.name, out ws)) root.transform.localScale = Vector3.one * ws;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{c.name}/{title}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void BuildPreviewScene(List<GameObject> prefabs)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 4.2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.055f, 0.078f, 0.141f);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        float cursor = -7.2f, ground = -3.2f;
        foreach (var p in prefabs)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(p);
            var rs = inst.GetComponentsInChildren<SpriteRenderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(cursor - b.min.x, ground - b.min.y, 0f);
            cursor += b.size.x + 0.6f;
        }
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CharacterPreview.unity");
    }
}
