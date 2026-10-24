using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the bone-rigged dog (body, head, tail, 4 legs) with Idle + Walk animations,
// a prefab, and a test scene where the dog walks back and forth.
// Runs automatically once (when Dog.prefab is missing); rebuild any time via
// Tools > Pet Game > Build Dog Rig
[InitializeOnLoad]
public static class DogRigBuilder
{
    [System.Serializable] public class Bone { public string n; public string parent; public float pvx, pvy, lpx, lpy; public int order; }
    [System.Serializable] public class RigData { public float ppu; public float groundOffset; public Bone[] bones; }

    const string Dir = "Assets/Dog";
    const string PrefabPath = Dir + "/Dog.prefab";
    const string ScenePath = "Assets/Scenes/DogWalkTest.unity";

    const string GameScenePath = "Assets/Scenes/DogCameraGame.unity";

    // Auto-build is handled by GameSceneBuilder (Assets/Game/Editor).
    static DogRigBuilder() { }

    // Dog + pet camera (small window, top-right). The real pet's position in the camera drives the dog.
    public static void BuildCameraGameScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "dog_rig.json")));
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(13f / 255f, 19f / 255f, 36f / 255f);
        cam.transform.position = new Vector3(0f, 0f, -10f);

        const float groundY = -1.6f;
        var ground = new GameObject("Ground");
        var gsr = ground.AddComponent<SpriteRenderer>();
        gsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/Parts/ground.png");
        gsr.sortingOrder = -10;
        ground.transform.position = new Vector3(0f, groundY - 0.15f, 0f);
        ground.transform.localScale = new Vector3(14f, 0.3f, 1f);

        var detGo = new GameObject("PetZoneDetector");
        var det = detGo.AddComponent<PetZoneDetector>();
        det.modelAsset = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Models/yolo26n.onnx");
        det.cameraIndex = 1;   // laptop webcam on this machine; switch in-game with the Cameras button
        det.pipMode = true;

        var dog = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        dog.transform.position = new Vector3(-2f, groundY + data.groundOffset, 0f);
        var walker = dog.GetComponent<DogWalker>();
        walker.mode = DogWalker.Mode.PetCamera;
        walker.detector = det;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, GameScenePath);
        Debug.Log("[DogRigBuilder] Built " + GameScenePath);
    }

    [MenuItem("Tools/Pet Game/Build Dog Rig")]
    public static void Build()
    {
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "dog_rig.json")));
        ImportSprites(data);

        // ---- Bones ----
        var root = new GameObject("Dog");
        root.AddComponent<SortingGroup>();
        var map = new Dictionary<string, Transform>();
        foreach (var b in data.bones)
        {
            var go = new GameObject(b.n);
            go.transform.SetParent(string.IsNullOrEmpty(b.parent) ? root.transform : map[b.parent], false);
            go.transform.localPosition = new Vector3(b.lpx, b.lpy, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/Parts/{b.n}.png");
            sr.sortingOrder = b.order;
            map[b.n] = go.transform;
        }

        // ---- Animations ----
        var idle = MakeClip("Dog_Idle", 2.4f, new Dictionary<string, (float amp, int freq, float phase)>
        {
            { "body/head", (3f, 1, 0f) },
            { "body/tail", (10f, 3, 0f) },
        }, bodyBob: 0.008f, bobFreq: 1);

        // diagonal gait: hind-near + front-far together, hind-far + front-near opposite
        var walk = MakeClip("Dog_Walk", 0.7f, new Dictionary<string, (float amp, int freq, float phase)>
        {
            { "body/leg_hind_near", (14f, 1, 0f) },
            { "body/leg_front_far", (14f, 1, 0f) },
            { "body/leg_hind_far", (14f, 1, 180f) },
            { "body/leg_front_near", (14f, 1, 180f) },
            { "body/head", (3f, 2, 90f) },
            { "body/tail", (8f, 2, 0f) },
        }, bodyBob: 0.02f, bobFreq: 2);

        string ctrlPath = Dir + "/Dog.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
        var sm = ctrl.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
        var sWalk = sm.AddState("Walk"); sWalk.motion = walk;
        sm.defaultState = sIdle;
        var toWalk = sIdle.AddTransition(sWalk);
        toWalk.hasExitTime = false; toWalk.duration = 0.1f;
        toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsWalking");
        var toIdle = sWalk.AddTransition(sIdle);
        toIdle.hasExitTime = false; toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsWalking");

        root.AddComponent<Animator>().runtimeAnimatorController = ctrl;
        root.AddComponent<DogWalker>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        BuildScene(prefab, data.groundOffset);
        Debug.Log("[DogRigBuilder] Dog rig built: " + PrefabPath + " and " + ScenePath);
    }

    static void ImportSprites(RigData data)
    {
        foreach (var b in data.bones)
        {
            var path = $"{Dir}/Parts/{b.n}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning("Missing " + path); continue; }
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spritePixelsPerUnit = data.ppu;
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(b.pvx, b.pvy);
            s.spriteMeshType = SpriteMeshType.Tight;
            imp.SetTextureSettings(s);
            imp.SaveAndReimport();
        }
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

    static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
    }

    static AnimationClip MakeClip(string name, float cycle, Dictionary<string, (float amp, int freq, float phase)> rot, float bodyBob, int bobFreq)
    {
        var clip = new AnimationClip { name = name, frameRate = 30 };
        foreach (var kv in rot)
        {
            SetCurve(clip, kv.Key, "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, cycle, 0f));
            SetCurve(clip, kv.Key, "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, cycle, 0f));
            SetCurve(clip, kv.Key, "localEulerAnglesRaw.z", Sine(cycle, 0f, kv.Value.amp, kv.Value.freq, kv.Value.phase));
        }
        SetCurve(clip, "body", "m_LocalPosition.x", AnimationCurve.Constant(0f, cycle, 0f));
        SetCurve(clip, "body", "m_LocalPosition.y", Sine(cycle, 0f, bodyBob, bobFreq, 90f));
        SetCurve(clip, "body", "m_LocalPosition.z", AnimationCurve.Constant(0f, cycle, 0f));
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, st);
        string path = $"{Dir}/{name}.anim";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static void BuildScene(GameObject prefab, float groundOffset)
    {
        // never discard unsaved work in the scene that is open now
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[DogRigBuilder] Prefab built; test scene skipped because the current scene was not saved.");
            return;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(13f / 255f, 19f / 255f, 36f / 255f);
        cam.transform.position = new Vector3(0f, 0f, -10f);

        const float groundY = -1.6f;
        string gpath = $"{Dir}/Parts/ground.png";
        if (AssetImporter.GetAtPath(gpath) is TextureImporter gimp)
        {
            gimp.textureType = TextureImporterType.Sprite;
            gimp.spriteImportMode = SpriteImportMode.Single;
            gimp.spritePixelsPerUnit = 100f;
            gimp.SaveAndReimport();
        }
        var ground = new GameObject("Ground");
        var gsr = ground.AddComponent<SpriteRenderer>();
        gsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(gpath);
        gsr.sortingOrder = -10;
        ground.transform.position = new Vector3(0f, groundY - 0.15f, 0f);
        ground.transform.localScale = new Vector3(14f, 0.3f, 1f);

        var dog = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        dog.transform.position = new Vector3(0f, groundY + groundOffset, 0f);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
