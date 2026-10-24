using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Put this file in Assets/Editor/
// Builds idle-animated cutout rigs from Assets/CharactersIdle (parts cut from the composite artwork).
// Menu: Tools > Pet Game > Build Idle Rigs
public static class IdleRigBuilder
{
    [System.Serializable] public class Bone { public string n; public string parent; public float pvx, pvy, lpx, lpy; public int order; public float[] piv; }
    [System.Serializable] public class Character { public string name; public Bone[] bones; }
    [System.Serializable] public class RigData { public float ppu; public Character[] characters; }

    const string Dir = "Assets/CharactersIdle";

    // loop length in seconds per character
    static readonly Dictionary<string, float> Cycle = new Dictionary<string, float> { { "dog", 2.4f }, { "tourist", 3.0f }, { "zombie", 3.6f } };

    // idle rotation per part: amplitude (deg), cycles per loop, phase (deg)
    static (float amp, int freq, float phase) Rot(string ch, string bone)
    {
        switch (ch + "/" + bone)
        {
            case "dog/head": return (3f, 1, 0f);
            case "dog/tail": return (10f, 3, 0f);
            case "tourist/head": return (1.8f, 1, 60f);
            case "tourist/arm_near": return (2.5f, 1, 180f);
            case "tourist/arm_far": return (1.5f, 1, 0f);
            case "zombie/head": return (5f, 1, 0f);
            case "zombie/arm_near": return (4f, 1, 140f);
            case "zombie/arm_far": return (4f, 1, 260f);
        }
        return (0f, 1, 0f);
    }

    [MenuItem("Tools/Pet Game/Build Idle Rigs")]
    public static void BuildAll()
    {
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "rig_idle.json")));
        var built = new List<(GameObject prefab, Vector2 pos)>();
        foreach (var c in data.characters)
        {
            ImportSprites(c, data.ppu);
            var basePiv = c.bones.First(b => b.n == "base").piv;
            var pos = new Vector2((basePiv[0] - 836f) / data.ppu, -(basePiv[1] - 470f) / data.ppu);
            built.Add((BuildCharacter(c), pos));
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 2.45f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(13f / 255f, 19f / 255f, 36f / 255f);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        foreach (var (prefab, pos) in built)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = new Vector3(pos.x, pos.y, 0f);
        }
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/IdlePreview.unity");
        Debug.Log("Idle rigs built: " + string.Join(", ", built.Select(b => b.prefab.name)));
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

    static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
    }

    static AnimationCurve Sine(float cycle, float baseVal, float amp, int freq, float phaseDeg)
    {
        var cv = new AnimationCurve();
        const int samples = 24;
        float w = 2f * Mathf.PI * freq / cycle;
        for (int i = 0; i <= samples; i++)
        {
            float t = cycle * i / samples;
            float arg = w * t + phaseDeg * Mathf.Deg2Rad;
            float tan = amp * w * Mathf.Cos(arg);
            cv.AddKey(new Keyframe(t, baseVal + amp * Mathf.Sin(arg), tan, tan));
        }
        return cv;
    }

    static GameObject BuildCharacter(Character c)
    {
        string title = char.ToUpper(c.name[0]) + c.name.Substring(1) + "_Idle";
        float cycle = Cycle[c.name];
        var root = new GameObject(title);
        root.AddComponent<SortingGroup>();
        var map = new Dictionary<string, Transform>();
        foreach (var b in c.bones)
        {
            var go = new GameObject(b.n);
            go.transform.SetParent(string.IsNullOrEmpty(b.parent) ? root.transform : map[b.parent], false);
            go.transform.localPosition = new Vector3(b.lpx, b.lpy, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{c.name}/{b.n}.png");
            sr.sortingOrder = b.order;
            map[b.n] = go.transform;
        }

        var clip = new AnimationClip { name = title, frameRate = 30 };
        // breathing on the base (pivot at the feet, so the feet stay planted)
        SetCurve(clip, "base", "m_LocalScale.x", AnimationCurve.Constant(0f, cycle, 1f));
        SetCurve(clip, "base", "m_LocalScale.y", Sine(cycle, 1f, 0.012f, 1, 90f));
        SetCurve(clip, "base", "m_LocalScale.z", AnimationCurve.Constant(0f, cycle, 1f));
        foreach (var b in c.bones)
        {
            if (b.n == "base") continue;
            var r = Rot(c.name, b.n);
            if (r.amp <= 0f) continue;
            string path = "base/" + b.n;
            SetCurve(clip, path, "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, cycle, 0f));
            SetCurve(clip, path, "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, cycle, 0f));
            SetCurve(clip, path, "localEulerAnglesRaw.z", Sine(cycle, 0f, r.amp, r.freq, r.phase));
        }
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, st);

        string clipPath = $"{Dir}/{c.name}/{title}.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);
        string ctrlPath = $"{Dir}/{c.name}/{title}.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
        root.AddComponent<Animator>().runtimeAnimatorController = ctrl;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{c.name}/{title}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }
}
