using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds Tourist + Zombie cutout prefabs (walk rigs) and the game scenes:
//   Assets/Scenes/Levels/Level1..Level5  (Build Settings 0..4, played in order)
//   Assets/Scenes/DogCameraGame          (sandbox, not in the build)
// Each level = dog (pet camera) + blind tourist + leash + its own gates / zombies / radio / shrine / exit.
// Runs once automatically; rebuild any time: Tools > Pet Game > Build Game Scene
[InitializeOnLoad]
public static partial class GameSceneBuilder
{
    [System.Serializable] public class Bone { public string n; public string parent; public float pvx, pvy, lpx, lpy; public int order; }
    [System.Serializable] public class Character { public string name; public float groundOffset; public Bone[] bones; }
    [System.Serializable] public class RigData { public float ppu; public Character[] characters; }

    const string Dir = "Assets/Game/Characters";
    const string ScenePath = "Assets/Scenes/DogCameraGame.unity";
    const string LevelDir = "Assets/Scenes/Levels";
    const string LabPath = "Assets/Scenes/PuzzleLab.unity";
    const string TitlePath = "Assets/Scenes/Title.unity";
    const string CutDir = "Assets/Scenes/Cutscenes";
    // story cutscenes and the level they come after (0 = before level 1, -1 = after the last level)
    static readonly (string file, int after, CutscenePlayer.Story story)[] Cuts = {
        ("Cut1_Opening", 0, CutscenePlayer.Story.Opening),
        ("Cut2_Shelter", 5, CutscenePlayer.Story.Shelter),
        ("Cut3_Spirit", 10, CutscenePlayer.Story.Spirit),
        ("Cut4_Ending", -1, CutscenePlayer.Story.Ending) };
    static readonly HashSet<int> Force = new HashSet<int>();   // levels to rebuild even if they exist
    const float GroundY = -3.4f;

    static Sprite Spr(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Game/Sprites/{name}.png");

    static SpriteRenderer Put(string name, Transform parent, string sprite, Vector3 pos, Vector3 scale, Color col, int order)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.transform.localPosition = pos;
        sr.transform.localScale = scale;
        sr.sprite = Spr(sprite);
        sr.color = col;
        sr.sortingOrder = order;
        return sr;
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    // colours of the backdrop: morning levels (1-5) and night levels (6-10)
    class Theme
    {
        public string sky, sun, hills, palms, huts, field, rows, path, edge, front, tuft, stone;
        public bool day;
    }
    static readonly Theme Day = new Theme { day = true, sky = "#EBCFA6", sun = "#FFF3D2", hills = "#C9A27E", palms = "#6B6F4E", huts = "#8C6A50",
        field = "#9AA862", rows = "#8B9A55", path = "#A88A62", edge = "#8C7350", front = "#5E7341", tuft = "#4E6435", stone = "#8C7A62" };
    static readonly Theme Night = new Theme { day = false, sky = "#0D1324", sun = "#E9E3C9", hills = "#151D33", palms = "#1B2640", huts = "#1F2A45",
        field = "#13261F", rows = "#1A3329", path = "#2E2A22", edge = "#3D372C", front = "#0F1E19", tuft = "#1A3329", stone = "#3D372C" };

    // Night Isan backdrop with parallax layers, so camera movement can be felt
    static void BuildWorld(float LevelMin, float LevelMax, Theme th)
    {
        foreach (var s in new[] { "white", "palm", "hut", "hills", "tuft", "stone" })
            ImportSimpleSprite($"Assets/Game/Sprites/{s}.png", 100f, s == "white" ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f));
        float width = LevelMax - LevelMin + 30f, mid = (LevelMin + LevelMax) / 2f;
        var white = Spr("white");
        float px = 100f / 8f;   // white.png is 8 px -> 0.08 units; scale up by this factor per unit

        // layer: moon (stuck to the camera)
        var sky = new GameObject("Sky (parallax 1)"); sky.AddComponent<Parallax>().factor = 1f;
        Put(th.day ? "sun" : "moon", sky.transform, "circle", new Vector3(th.day ? 4.8f : -4.5f, 2.6f, 0f), Vector3.one * (th.day ? 1.7f : 1.3f), Hex(th.sun), -40);

        // layer: far hills
        var far = new GameObject("Hills (parallax 0.8)"); far.AddComponent<Parallax>().factor = 0.8f;
        for (int i = 0; i < 4; i++)
            Put($"hills{i}", far.transform, "hills", new Vector3(LevelMin - 8f + i * 16f, GroundY + 1.0f, 0f), Vector3.one, Hex(th.hills), -35);

        // layer: palms and huts
        var midL = new GameObject("Palms (parallax 0.5)"); midL.AddComponent<Parallax>().factor = 0.5f;
        int pi = 0;
        for (float x = LevelMin + 2f; x <= LevelMax + 4f; x += 7f + 1.5f * (pi % 2), pi++)
            Put($"palm{pi}", midL.transform, "palm", new Vector3(x, GroundY + 0.9f, 0f), Vector3.one * (0.9f + 0.15f * (pi % 3)), Hex(th.palms), -30);
        Put("hut0", midL.transform, "hut", new Vector3(LevelMin + 13.5f, GroundY + 0.9f, 0f), Vector3.one, Hex(th.huts), -29);
        if (LevelMax - LevelMin > 30f)
            Put("hut1", midL.transform, "hut", new Vector3(LevelMin + 34f, GroundY + 0.9f, 0f), Vector3.one * 0.85f, Hex(th.huts), -29);

        // layer: rice field behind the path (slow)
        var field = new GameObject("Field (parallax 0.3)"); field.AddComponent<Parallax>().factor = 0.3f;
        Put("field", field.transform, "white", new Vector3(mid, GroundY + 0.45f, 0f), new Vector3(width * px, 0.9f * px, 1f), Hex(th.field), -26);
        for (int i = 0; i < 3; i++)
            Put($"row{i}", field.transform, "white", new Vector3(mid, GroundY + 0.15f + i * 0.28f, 0f), new Vector3(width * px, 0.05f * px, 1f), Hex(th.rows), -25);

        // world layer: path the characters walk on + front ground, with tufts and stones as distance markers
        var ground = new GameObject("Ground");
        Put("path", ground.transform, "white", new Vector3(mid, GroundY - 0.2f, 0f), new Vector3(width * px, 0.4f * px, 1f), Hex(th.path), -12);
        Put("pathEdge", ground.transform, "white", new Vector3(mid, GroundY - 0.02f, 0f), new Vector3(width * px, 0.05f * px, 1f), Hex(th.edge), -11);
        Put("front", ground.transform, "white", new Vector3(mid, GroundY - 2.4f, 0f), new Vector3(width * px, 4f * px, 1f), Hex(th.front), -12);
        for (float x = LevelMin; x <= LevelMax; x += 2.3f)
        {
            Put("tuft", ground.transform, "tuft", new Vector3(x, GroundY - 0.42f, 0f), Vector3.one * 0.9f, Hex(th.tuft), -9);
            Put("stone", ground.transform, "stone", new Vector3(x + 1.1f, GroundY - 0.32f, 0f), Vector3.one * 0.6f, Hex(th.stone), -9);
        }
    }

    static GameSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string json = Path.Combine(Dir, "walk_rig.json");
            if (!File.Exists(json)) return;
            string zp = $"{Dir}/Zombie.prefab", tp = $"{Dir}/Tourist.prefab";
            bool missing = !File.Exists(tp) || !File.Exists(zp);
            // rig data updated after the prefabs were made -> rebuild automatically
            bool stale = !missing && File.GetLastWriteTimeUtc(json) > File.GetLastWriteTimeUtc(zp);
            if (missing || stale) BuildAll();
        };
    }

    // Builds characters + any level scene that does NOT exist yet. Existing level scenes are left alone,
    // so level designers can edit them by hand. Use "Rebuild ALL Levels" to regenerate everything.
    [MenuItem("Tools/Pet Game/Build Game Scene")]
    public static void BuildAll() => Build(false);

    [MenuItem("Tools/Pet Game/Rebuild ALL Levels (overwrites hand edits!)")]
    static void RebuildAllMenu()
    {
        if (EditorUtility.DisplayDialog("Rebuild all levels?", "This overwrites Level1-10, the Puzzle Lab and any edits made to them by hand.", "Rebuild", "Cancel"))
            Build(true);
    }

    // Build Settings = every Assets/Scenes/Levels/LevelN.unity in number order (add a Level8.unity and run this)
    [MenuItem("Tools/Pet Game/Rebuild Puzzle Lab")]
    static void RebuildLabMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(LabPath)) AssetDatabase.DeleteAsset(LabPath);
        Build(false);
        EditorSceneManager.OpenScene(LabPath);
    }

    [MenuItem("Tools/Pet Game/Update Level Order")]
    public static void UpdateLevelOrder()
    {
        if (!Directory.Exists(LevelDir)) return;
        var levels = Directory.GetFiles(LevelDir, "Level*.unity")
            .Select(f => f.Replace('\\', '/'))
            .Select(f => (path: f, n: int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(5), out var k) ? k : 999))
            .OrderBy(t => t.n).ToList();
        string Cut(int after) { foreach (var c in Cuts) if (c.after == after) { var path = $"{CutDir}/{c.file}.unity"; return File.Exists(path) ? path : null; } return null; }
        var flow = new List<string>();
        if (File.Exists(TitlePath)) flow.Add(TitlePath);
        if (Cut(0) != null) flow.Add(Cut(0));
        foreach (var l in levels) { flow.Add(l.path); if (Cut(l.n) != null) flow.Add(Cut(l.n)); }
        if (Cut(-1) != null) flow.Add(Cut(-1));
        var rest = EditorBuildSettings.scenes.Where(s => !flow.Contains(s.path))
            .Select(s => s.path == ScenePath ? new EditorBuildSettingsScene(ScenePath, false) : s).ToList();   // sandbox not in the build
        rest.InsertRange(0, flow.Select(x => new EditorBuildSettingsScene(x, true)));
        EditorBuildSettings.scenes = rest.ToArray();
        Debug.Log("[GameSceneBuilder] Game order: " + string.Join(", ", flow.Select(Path.GetFileNameWithoutExtension)));
    }

    // rebuild only these levels (+ the puzzle lab), keeping every other scene as it is
    public static void RebuildLevels(int[] levels, bool lab)
    {
        Force.Clear(); foreach (var n in levels) Force.Add(n);
        labForce = lab;
        try { Build(false); }
        finally { Force.Clear(); labForce = false; }
    }
    static bool labForce;

    // title + the 4 cutscenes + the levels the story changed (5 shelter, 10 shrine, 11 taxi). Other levels are kept.
    [MenuItem("Tools/Pet Game/Rebuild Story (title, cutscenes, levels 5, 10, 11)")]
    public static void BuildStory()
    {
        Force.Clear(); Force.Add(5); Force.Add(10); Force.Add(11);
        storyOverwrite = true;
        try { Build(false); } finally { Force.Clear(); storyOverwrite = false; }
    }
    static bool storyOverwrite;

    public static void Build(bool overwriteLevels)
    {
        if (!File.Exists("Assets/Dog/Dog.prefab")) DogRigBuilder.Build();
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "walk_rig.json")));
        var prefabs = new Dictionary<string, (GameObject prefab, float ground)>();
        foreach (var c in data.characters)
        {
            ImportSprites(c, data.ppu);
            prefabs[c.name] = (BuildCharacter(c), c.groundOffset);
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string open = EditorSceneManager.GetActiveScene().path;
        bool any = BuildLevels(prefabs, overwriteLevels);
        if (overwriteLevels || !File.Exists(ScenePath)) { BuildSandbox(prefabs); any = true; }
        if (overwriteLevels || labForce || !File.Exists(LabPath)) { BuildPuzzleLab(prefabs); any = true; }
        any |= BuildStoryScenes(prefabs, overwriteLevels || storyOverwrite);
        UpdateLevelOrder();
        if (any) EditorSceneManager.OpenScene(File.Exists(open) ? open : $"{LevelDir}/Level1.unity");
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
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{c.name}/{b.n}.png");
            sr.sortingOrder = b.order;
            map[b.n] = go.transform;
        }

        var rig = root.AddComponent<CutoutRig>();
        if (c.name == "tourist")
        {
            rig.legFrontStand = -20f; rig.legBackStand = 20f; rig.legSwing = 20f; rig.strideLength = 1.4f;
            rig.armNearSwing = 8f; rig.armFarSwing = 3f; rig.headBob = 1.5f; rig.bodyBob = 0.03f; rig.idleSway = 1.2f;
            root.AddComponent<BlindFollower>();
        }
        else if (c.name == "zombie")
        {
            rig.legFrontStand = -10f; rig.legBackStand = 10f; rig.legSwing = 12f; rig.strideLength = 0.9f;
            rig.armNearSwing = 6f; rig.armFarSwing = 6f; rig.headBob = 4f; rig.bodyBob = 0.04f; rig.idleSway = 4f; rig.idleSpeed = 0.8f;
            rig.armNearRaise = 80f; rig.armFarRaise = 110f; rig.armNearStrike = 15f; rig.armFarStrike = 25f;
            root.AddComponent<ZombieChaser>();
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{title}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material LineMaterial()
    {
        const string path = "Assets/Game/LineMat.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        m = new Material(sh);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void ImportSimpleSprite(string path, float ppu, Vector2 pivot)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        var s = new TextureImporterSettings();
        imp.ReadTextureSettings(s);
        s.spritePixelsPerUnit = ppu;
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spritePivot = pivot;
        imp.SetTextureSettings(s);
        imp.SaveAndReimport();
    }

    static Gate MakeGate(string name, float x, bool open)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(x, GroundY, 0f);
        var gate = go.AddComponent<Gate>();
        gate.isOn = open;

        var panel = new GameObject("panel").AddComponent<SpriteRenderer>();
        panel.transform.SetParent(go.transform, false);
        panel.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Sprites/gate.png");
        panel.sortingOrder = -5;
        gate.panel = panel;

        var lamp = new GameObject("lamp").AddComponent<SpriteRenderer>();
        lamp.transform.SetParent(go.transform, false);
        lamp.transform.localPosition = new Vector3(0f, 3.2f, 0f);
        lamp.transform.localScale = Vector3.one * 0.35f;
        lamp.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Sprites/circle.png");
        lamp.sortingOrder = -4;
        gate.lamp = lamp;
        gate.markerHeight = 4.0f;
        return gate;
    }

    // ------------------------------------------------------------------ level building
    class Ctx
    {
        public Dictionary<string, (GameObject prefab, float ground)> prefabs;
        public Material lineMat;
        public PetZoneDetector det;
        public Transform tourist;
        public float min, max;
        public Theme th;
        public GameState gs;
    }

    // a gate only the lever can move: barking at it does nothing, a small LEVER sign says so
    static Gate LeverGate(string name, float x, bool open, string sign = "LEVER")
    {
        var g = MakeGate(name, x, open);
        g.radius = 0f;
        var txt = new GameObject("leverSign").AddComponent<TextMesh>();
        txt.transform.SetParent(g.transform, false);
        txt.transform.localPosition = new Vector3(0f, 3.75f, 0f);
        txt.text = sign; txt.anchor = TextAnchor.MiddleCenter; txt.alignment = TextAlignment.Center;
        txt.characterSize = 0.05f; txt.fontSize = 40; txt.fontStyle = FontStyle.Bold; txt.color = Hex("#E9A23B");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.font = font; var mr = txt.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material;
        mr.sortingOrder = Darkness.DarkOrder + 5;   // readable at night too
        return g;
    }

    // Common base of every level: camera, game state, voice, world, pet cam, dog, tourist, leash.
    static Ctx BeginLevel(Dictionary<string, (GameObject prefab, float ground)> prefabs, string title, string goal,
                          float min, float max, float dogX, bool night = false)
    {
        var th = night ? Night : Day;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var c = new Ctx { prefabs = prefabs, min = min, max = max, th = th };
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 4.6f;   // zoomed out
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex(th.sky);

        var game = new GameObject("Game");
        var gs = game.AddComponent<GameState>();
        gs.levelTitle = title; gs.levelGoal = goal;
        c.gs = gs;
        game.AddComponent<VoiceTrigger>();   // player's voice -> dog bark (human-only filter)
        if (night) game.AddComponent<Darkness>();   // dark + bark reveals (finds dog, lights, zombies by itself)

        c.lineMat = LineMaterial();
        ImportSimpleSprite("Assets/Game/Sprites/gate.png", 100f, new Vector2(0.5f, 0f));
        ImportSimpleSprite("Assets/Game/Sprites/circle.png", 64f, new Vector2(0.5f, 0.5f));
        BuildWorld(min, max, th);

        var detGo = new GameObject("PetZoneDetector");
        c.det = detGo.AddComponent<PetZoneDetector>();
        c.det.modelAsset = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Models/yolo26n.onnx");
        c.det.cameraIndex = 1;
        c.det.pipMode = true;
        c.det.pipWidth = 300f;

        // dog
        var dogData = JsonUtility.FromJson<DogRigBuilder.RigData>(File.ReadAllText("Assets/Dog/dog_rig.json"));
        var dog = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Dog/Dog.prefab"));
        dog.transform.position = new Vector3(dogX, GroundY + dogData.groundOffset, 0f);
        var walker = dog.GetComponent<DogWalker>();
        walker.mode = DogWalker.Mode.PetCamera;
        walker.detector = c.det;
        walker.minX = min + 1.8f; walker.maxX = max - 1f;   // stops in front of the bamboo fence at the left edge
        dog.AddComponent<DogBark>().ringMaterial = c.lineMat;

        // blind tourist (follows the dog with a delay, 2.5 behind)
        var tp = prefabs["tourist"];
        var tourist = (GameObject)PrefabUtility.InstantiatePrefab(tp.prefab);
        tourist.transform.position = new Vector3(dogX - 2.5f, GroundY + tp.ground, 0f);
        var follower = tourist.GetComponent<BlindFollower>();
        follower.dog = dog.transform;
        follower.minX = min + 1.2f; follower.maxX = max - 0.5f;
        MakeFence(min + 0.6f, th);   // the left edge is a real fence you can see, not an invisible wall
        c.tourist = tourist.transform;

        // side-scroll camera follows the dog (leaning toward the tourist)
        var follow = cam.gameObject.AddComponent<CameraFollow>();
        follow.dog = dog.transform;
        follow.tourist = tourist.transform;
        follow.levelMinX = min; follow.levelMaxX = max;
        cam.transform.position = new Vector3(dogX, 0f, -10f);

        // leash: dog harness handle -> tourist's hand
        var ropeGo = new GameObject("Rope");
        var lr = ropeGo.AddComponent<LineRenderer>();
        lr.material = c.lineMat;
        lr.widthMultiplier = 0.045f;
        lr.startColor = lr.endColor = new Color(0.79f, 0.81f, 0.87f);
        lr.numCapVertices = 2;
        lr.sortingOrder = 50;
        var rope = ropeGo.AddComponent<Rope>();
        rope.dog = dog.transform;
        rope.tourist = tourist.transform;
        return c;
    }

    static ZombieChaser Zombie(Ctx c, string name, float x, ZombieChaser.Kind kind, float speed = 0.55f)
    {
        var zp = c.prefabs["zombie"];
        var z = (GameObject)PrefabUtility.InstantiatePrefab(zp.prefab);
        z.name = name;
        float scale = kind == ZombieChaser.Kind.Guard ? 1.12f : 1f;   // guards are a bit bigger
        z.transform.localScale = new Vector3(scale, scale, 1f);
        z.transform.position = new Vector3(x, GroundY + zp.ground * scale, 0f);
        var zc = z.GetComponent<ZombieChaser>();
        zc.target = c.tourist;
        zc.kind = kind;
        zc.speed = speed;
        if (kind == ZombieChaser.Kind.Guard)   // darker, reddish: reads as "different, dangerous"
            foreach (var sr in z.GetComponentsInChildren<SpriteRenderer>()) sr.color = new Color(0.86f, 0.62f, 0.6f);
        return zc;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector2 size, string hex, int order, float rot = 0f)
    {
        float px = 100f / 8f;   // white.png is 8 px at 100 ppu
        var sr = Put(name, parent, "white", pos, new Vector3(size.x * px, size.y * px, 1f), Hex(hex), order);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
        return sr.gameObject;
    }

    static Radio MakeRadio(Ctx c, float x, float playTime)
    {
        var go = new GameObject("Radio_MorLam");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var t = go.transform;
        Box("stand", t, new Vector3(0f, 0.3f, 0f), new Vector2(0.9f, 0.6f), "#3A2E25", -6);
        Box("box", t, new Vector3(0f, 0.92f, 0f), new Vector2(1.1f, 0.64f), "#B8463A", -5);
        Box("dial", t, new Vector3(0.28f, 1.0f, 0f), new Vector2(0.34f, 0.16f), "#E9E3C9", -4);
        Box("antenna", t, new Vector3(0.25f, 1.55f, 0f), new Vector2(0.04f, 0.8f), "#8A8F9C", -6, -20f);
        var radio = go.AddComponent<Radio>();
        radio.playTime = playTime;
        radio.markerHeight = 2.4f;
        radio.speaker = Put("speaker", t, "circle", new Vector3(-0.25f, 0.9f, 0f), Vector3.one * 0.28f, Hex("#2A1C1A"), -4);
        radio.lamp = Put("lamp", t, "circle", new Vector3(0.42f, 0.82f, 0f), Vector3.one * 0.09f, Hex("#5A4D47"), -4);
        var notes = new List<SpriteRenderer>();
        string[] cols = { "#C8FF5E", "#E9A23B", "#80D1C2" };
        for (int i = 0; i < 3; i++)
        {
            var n = Put($"note{i}", t, "circle", new Vector3(0f, 1.4f, 0f), Vector3.one * 0.12f, Hex(cols[i]), -3);
            Box("stem", n.transform, new Vector3(0.45f, 1.1f, 0f), new Vector2(0.18f, 2.2f), cols[i], -3);   // in note space (scaled)
            notes.Add(n);
        }
        radio.notes = notes.ToArray();
        return radio;
    }

    static Shrine MakeShrine(Ctx c, float x)
    {
        var go = new GameObject("Shrine_SpiritHouse");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var t = go.transform;
        var shrine = go.AddComponent<Shrine>();
        shrine.radius = 3f;
        shrine.markerHeight = 3.6f;
        shrine.glow = Put("glow", t, "circle", new Vector3(0f, 2.1f, 0f), Vector3.one * 2.2f, Hex("#E9A23B"), -8);
        var gc = shrine.glow.color; gc.a = 0.1f; shrine.glow.color = gc;
        Box("pole", t, new Vector3(0f, 0.8f, 0f), new Vector2(0.2f, 1.6f), "#E8D8B0", -6);
        Box("platform", t, new Vector3(0f, 1.66f, 0f), new Vector2(1.5f, 0.14f), "#C9A14A", -6);
        Box("house", t, new Vector3(0f, 2.12f, 0f), new Vector2(1.0f, 0.8f), "#C9A14A", -6);
        Box("door", t, new Vector3(0f, 2.0f, 0f), new Vector2(0.32f, 0.5f), "#3A1E1A", -5);
        Box("roofL", t, new Vector3(-0.3f, 2.72f, 0f), new Vector2(0.85f, 0.16f), "#8C2F2B", -5, 32f);
        Box("roofR", t, new Vector3(0.3f, 2.72f, 0f), new Vector2(0.85f, 0.16f), "#8C2F2B", -5, -32f);
        Box("spire", t, new Vector3(0f, 3.05f, 0f), new Vector2(0.08f, 0.35f), "#C9A14A", -5);
        for (int i = 0; i < 3; i++)   // garlands
            Put($"garland{i}", t, "circle", new Vector3(-0.45f + i * 0.45f, 1.55f, 0f), Vector3.one * 0.12f, Hex("#E9A23B"), -4);
        return shrine;
    }

    static void MakeExit(float x)
    {
        var go = new GameObject("Exit_NextStreet");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var t = go.transform;
        Box("post", t, new Vector3(0f, 1.1f, 0f), new Vector2(0.12f, 2.2f), "#6B5A44", -6);
        Box("board", t, new Vector3(0f, 2.0f, 0f), new Vector2(1.7f, 0.6f), "#E9E3C9", -5);
        var txt = new GameObject("label").AddComponent<TextMesh>();
        txt.transform.SetParent(t, false);
        txt.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        txt.text = "NEXT >>";
        txt.anchor = TextAnchor.MiddleCenter;
        txt.alignment = TextAlignment.Center;
        txt.characterSize = 0.07f; txt.fontSize = 48; txt.fontStyle = FontStyle.Bold;
        txt.color = Hex("#0D1324");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.font = font;
        var mr = txt.GetComponent<MeshRenderer>();
        mr.sharedMaterial = font.material;
        mr.sortingOrder = -4;
        go.AddComponent<LevelExit>();
    }

    static Lantern MakeLantern(float x, float radius = 1.8f)
    {
        var go = new GameObject("Lantern_SpiritCandle");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var t = go.transform;
        var l = go.AddComponent<Lantern>();
        l.radius = radius;
        l.glow = Put("glow", t, "circle", new Vector3(0f, 0.6f, 0f), Vector3.one * radius * 2f, new Color(0.91f, 0.64f, 0.23f, 0.16f), -7);
        Box("tray", t, new Vector3(0f, 0.06f, 0f), new Vector2(0.5f, 0.12f), "#C9A14A", -5);
        Box("candle", t, new Vector3(0f, 0.32f, 0f), new Vector2(0.12f, 0.42f), "#F2EBDD", -5);
        l.flame = Put("flame", t, "circle", new Vector3(0f, 0.62f, 0f), new Vector3(0.14f, 0.22f, 1f), Hex("#F2B347"), -4);
        return l;
    }

    // bamboo fence that closes the street behind you (left edge of every level)
    static void MakeFence(float x, Theme th)
    {
        var go = new GameObject("Fence_LeftEdge");
        go.transform.position = new Vector3(x, GroundY, 0f);
        string post = th.day ? "#9C8450" : "#3A3326", rail = th.day ? "#7A6440" : "#2C271D";
        for (int i = 0; i < 6; i++)
            Box($"bamboo{i}", go.transform, new Vector3(-0.6f + i * 0.24f, 0.8f + 0.12f * (i % 2), 0f), new Vector2(0.14f, 1.6f + 0.24f * (i % 2)), post, -6);
        Box("railTop", go.transform, new Vector3(0f, 1.25f, 0f), new Vector2(1.5f, 0.1f), rail, -5);
        Box("railLow", go.transform, new Vector3(0f, 0.55f, 0f), new Vector2(1.5f, 0.1f), rail, -5);
    }

    // ------------------------------------------------------------------ puzzle pieces
    static Transform Pivot(string name, Transform parent, Vector3 local)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = local; return t;
    }

    static void Label(float x, string text, float y = 4.6f)
    {
        var txt = new GameObject("Label: " + text).AddComponent<TextMesh>();
        txt.transform.position = new Vector3(x, GroundY + y, 0f);
        txt.text = text; txt.anchor = TextAnchor.MiddleCenter; txt.alignment = TextAlignment.Center;
        txt.characterSize = 0.06f; txt.fontSize = 48; txt.color = Hex("#2A2340");   // the lab uses the morning sky
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.font = font; var mr = txt.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material; mr.sortingOrder = 5;
    }

    static Canal MakeCanal(Ctx c, float x, float width = 3f)
    {
        var go = new GameObject("Canal");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var canal = go.AddComponent<Canal>(); canal.width = width;
        bool day = c.th == null || c.th.day;
        Box("water", go.transform, new Vector3(0f, -0.9f, 0f), new Vector2(width, 1.8f), day ? "#4C8DB0" : "#16324A", -10);
        Box("shine", go.transform, new Vector3(0f, -0.25f, 0f), new Vector2(width * 0.8f, 0.06f), day ? "#9CC9DF" : "#2C5878", -9);
        Box("bankL", go.transform, new Vector3(-width / 2f, -0.6f, 0f), new Vector2(0.18f, 1.3f), day ? "#8C7350" : "#3D372C", -9);
        Box("bankR", go.transform, new Vector3(width / 2f, -0.6f, 0f), new Vector2(0.18f, 1.3f), day ? "#8C7350" : "#3D372C", -9);
        return canal;
    }

    // the deck spans the canal; the winch you bark at stands on the FAR (right) bank,
    // so you can cross, stand by it with your master off the deck, and raise it when a zombie steps on
    static Drawbridge MakeDrawbridge(Canal canal, bool down)
    {
        var t = canal.transform;
        var winch = new GameObject("Drawbridge_Winch");
        winch.transform.position = new Vector3(canal.Right + 2.0f, GroundY, 0f);   // far enough that your master waits off the deck
        var br = winch.AddComponent<Drawbridge>();
        br.canal = canal; br.isOn = down; br.markerHeight = 2.2f;
        Box("post", winch.transform, new Vector3(0f, 0.6f, 0f), new Vector2(0.16f, 1.2f), "#3D372C", -6);
        Put("wheel", winch.transform, "circle", new Vector3(0f, 1.1f, 0f), Vector3.one * 0.55f, Hex("#6B4A2F"), -5);
        Put("hub", winch.transform, "circle", new Vector3(0f, 1.1f, 0f), Vector3.one * 0.16f, Hex("#C9A14A"), -4);
        br.deck = Pivot("deckPivot", t, new Vector3(-canal.width / 2f, 0f, 0f));
        Box("deck", br.deck, new Vector3(canal.width / 2f, -0.06f, 0f), new Vector2(canal.width, 0.16f), "#6B4A2F", -6);
        for (int i = 0; i < 4; i++)
            Box($"plank{i}", br.deck, new Vector3(canal.width * (i + 0.5f) / 4f, -0.06f, 0f), new Vector2(0.05f, 0.18f), "#3A2A1C", -5);
        Box("chain", br.deck, new Vector3(canal.width * 0.95f, 0.5f, 0f), new Vector2(0.03f, 1.0f), "#8A8F9C", -7);
        return br;
    }

    static PalmBridge MakePalmBridge(Canal canal)
    {
        var go = new GameObject("Palm_Leaning");
        go.transform.position = new Vector3(canal.Left - 0.3f, GroundY, 0f);
        var pb = go.AddComponent<PalmBridge>();
        pb.canal = canal; pb.length = canal.width + 1.4f; pb.markerHeight = 3.2f;
        pb.tree = Pivot("treePivot", go.transform, Vector3.zero);
        pb.tree.localRotation = Quaternion.Euler(0f, 0f, -12f);   // leaning over the water
        Box("trunk", pb.tree, new Vector3(0f, pb.length / 2f, 0f), new Vector2(0.3f, pb.length), "#7A5A3A", -6);
        for (int i = 0; i < 5; i++)
            Box($"frond{i}", pb.tree, new Vector3(0f, pb.length, 0f), new Vector2(1.6f, 0.14f), "#4E6B3A", -5, -60f + i * 30f);
        return pb;
    }

    static Lever MakeLever(float x, params Switchable[] targets)
    {
        var go = new GameObject("Lever");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var lv = go.AddComponent<Lever>();
        lv.targets = targets; lv.markerHeight = 2.2f;
        Box("base", go.transform, new Vector3(0f, 0.15f, 0f), new Vector2(0.6f, 0.3f), "#3D372C", -6);
        lv.handle = Pivot("handlePivot", go.transform, new Vector3(0f, 0.3f, 0f));
        Box("handle", lv.handle, new Vector3(0f, 0.55f, 0f), new Vector2(0.1f, 1.1f), "#8A8F9C", -5);
        Put("knob", lv.handle, "circle", new Vector3(0f, 1.1f, 0f), Vector3.one * 0.25f, Hex("#B8463A"), -4);
        return lv;
    }

    // buried key: only sniffing digs it up; toggles its targets (use LeverGate for "the gate with no handle")
    static ScentSpot MakeScent(float x, params Switchable[] targets)
    {
        var go = new GameObject("Scent_Buried");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var sp = go.AddComponent<ScentSpot>();
        sp.targets = targets;
        var m = Put("mound", go.transform, "circle", new Vector3(0f, -0.05f, 0f), new Vector3(1.1f, 0.35f, 1f), Hex("#6B5238"), -8);
        sp.mound = m;
        for (int i = 0; i < 3; i++)   // faint scent lines
            Box($"scent{i}", go.transform, new Vector3(-0.25f + i * 0.25f, 0.45f, 0f), new Vector2(0.04f, 0.35f), "#9C8A6A", -8, 12f - i * 12f);
        var key = Put("key", go.transform, "circle", new Vector3(0f, 0.2f, 0f), Vector3.one * 0.32f, Hex("#F2D04B"), Darkness.DarkOrder + 6);
        Box("keyStem", key.transform, new Vector3(1.3f, 0f, 0f), new Vector2(2.2f, 0.5f), "#F2D04B", Darkness.DarkOrder + 6);   // in key space (scaled)
        sp.item = key;
        return sp;
    }

    static NoiseTrap MakeCans(float x)
    {
        var go = new GameObject("Cans_NoiseTrap");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var trap = go.AddComponent<NoiseTrap>();
        Box("postL", go.transform, new Vector3(-0.8f, 0.7f, 0f), new Vector2(0.1f, 1.4f), "#6B5A44", -6);
        Box("postR", go.transform, new Vector3(0.8f, 0.7f, 0f), new Vector2(0.1f, 1.4f), "#6B5A44", -6);
        Box("string", go.transform, new Vector3(0f, 1.3f, 0f), new Vector2(1.6f, 0.03f), "#C9C2B0", -6);
        var cans = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            var piv = Pivot($"can{i}", go.transform, new Vector3(-0.55f + i * 0.37f, 1.3f, 0f));
            Box("cord", piv, new Vector3(0f, -0.12f, 0f), new Vector2(0.02f, 0.24f), "#C9C2B0", -6);
            Box("tin", piv, new Vector3(0f, -0.36f, 0f), new Vector2(0.2f, 0.26f), "#9AA3B5", -5);
            cans.Add(piv);
        }
        trap.cans = cans.ToArray();
        return trap;
    }

    static HordeWall MakeHorde(Ctx c, float startX, float triggerX, int count = 4, float speed = 1.3f)
    {
        var go = new GameObject("Horde");
        go.transform.position = new Vector3(startX, 0f, 0f);
        var h = go.AddComponent<HordeWall>();
        h.triggerX = triggerX; h.speed = speed;
        var zp = c.prefabs["zombie"];
        var rigs = new List<CutoutRig>();
        for (int i = 0; i < count; i++)
        {
            var z = (GameObject)PrefabUtility.InstantiatePrefab(zp.prefab);
            z.name = $"HordeZombie{i}";
            Object.DestroyImmediate(z.GetComponent<ZombieChaser>());   // just a body, the wall moves it
            z.transform.SetParent(go.transform, false);
            z.transform.localPosition = new Vector3(-i * 0.9f, GroundY + zp.ground, 0f);
            foreach (var sr in z.GetComponentsInChildren<SpriteRenderer>()) sr.color = new Color(0.7f, 0.72f, 0.85f);
            rigs.Add(z.GetComponent<CutoutRig>());
        }
        h.members = rigs.ToArray();
        return h;
    }

    // test scene with every puzzle piece in a row, labelled. Copy pieces from here into real levels.
    static void BuildPuzzleLab(Dictionary<string, (GameObject prefab, float ground)> p)
    {
        var c = BeginLevel(p, "Puzzle Lab", "Every puzzle piece in one place. Copy what you need into a level.", -14f, 84f, -10f);
        MakeCans(-5f); Label(-5f, "CANS: noise when walked through (CRAWL = silent)");
        var sleeper = Zombie(c, "SleepingZombie", 0f, ZombieChaser.Kind.Chaser); sleeper.asleep = true; Label(0f, "SLEEPER: wakes on noise", 3f);
        var canalA = MakeCanal(c, 8f); MakeDrawbridge(canalA, false); Label(8f, "DRAWBRIDGE: bark to lower / raise");
        var canalB = MakeCanal(c, 18f); MakePalmBridge(canalB); Label(18f, "PALM: bark once, falls across");
        var gate = MakeGate("Gate_Remote", 30f, false);
        MakeLever(25f, gate); Label(25f, "LEVER: opens the gate at x=30");
        MakeLantern(36f); Label(36f, "CANDLE: zombies can't enter", 3.2f);
        MakeRadio(c, 42f, 15f); Label(42f, "RADIO: 15 s song");
        Zombie(c, "GuardZombie", 48f, ZombieChaser.Kind.Guard); Label(48f, "GUARD", 4.4f);
        MakeHorde(c, 55f, 60f); Label(55f, "HORDE: runs when the dog passes x=60", 4.4f);
        var buried = LeverGate("Gate_Buried", 76f, false, "LOCKED");
        MakeScent(72f, buried); Label(72f, "SCENT: camera close to your pet (SNIFF) -> digs up the key", 3.2f);
        MakeExit(80f);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), LabPath);
    }

    // one level = its number + how to build it; only built when the scene file is missing (or on Rebuild ALL)
    static bool Level(int n, bool overwrite, System.Action build)
    {
        string path = $"{LevelDir}/Level{n}.unity";
        if (!overwrite && !Force.Contains(n) && File.Exists(path)) return false;
        build();
        SaveLevel(n);
        return true;
    }

    static string SaveLevel(int n)
    {
        Directory.CreateDirectory(LevelDir);
        string path = $"{LevelDir}/Level{n}.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
        return path;
    }

    static bool BuildLevels(Dictionary<string, (GameObject prefab, float ground)> p, bool ow)
    {
        bool any = false;
        // ---------- MORNING: learn the rules ----------
        // 1 - walk, stop at a closed gate, bark it open, go right
        any |= Level(1, ow, () => {
            BeginLevel(p, "Level 1 - First Steps", "Walk to the gate, bark to open it, then lead your master right.", -11f, 12f, -6f);
            MakeGate("Gate_A", 2f, false);
            MakeExit(9.5f);
        });
        // 2 - zombie behind: get through, call the master, bark the gate shut
        any |= Level(2, ow, () => {
            var c = BeginLevel(p, "Level 2 - Lock It Behind", "Get through the gate, bark to call your master, then shut the zombie out.", -20f, 14f, -6f);
            Zombie(c, "Zombie", -17f, ZombieChaser.Kind.Chaser, 0.55f);
            MakeGate("Gate_B", 0f, false);
            MakeExit(11f);
        });
        // 3 - the rusty gate is LOCKED and opens slowly: its key is buried in front of it - SNIFF (new) for it.
        //     Shut the back gate on the zombie first (call your master through, bark it shut), then dig, then wait.
        any |= Level(3, ow, () => {
            var c = BeginLevel(p, "Level 3 - The Rusty Gate", "The rusty gate is locked - sniff out its key. And deal with the zombie first!", -16f, 14f, -6f);
            Zombie(c, "Zombie", -11.5f, ZombieChaser.Kind.Chaser, 0.8f);
            MakeGate("Gate_A", -2f, true);
            var rusty = LeverGate("Gate_Rusty", 3.5f, false, "LOCKED");
            rusty.rusty = true; rusty.openTime = 10.5f;
            rusty.panel.color = Hex("#B0704A");
            var key = MakeScent(1.5f, rusty);
            key.foundMessage = "Bak Daeng dug up a rusty key! The gate creaks open... slowly.";
            MakeExit(11f);
        });
        // 4 - inverse lever: one bark opens one gate and shuts the other. Lure the guard to the radio behind you,
        //     walk past it while it dances, then flip the lever: the gate behind locks it in, the gate ahead opens.
        //     Song is 16 s - after that the guard walks home, so lock it in before it gets out.
        any |= Level(4, ow, () => {
            var c = BeginLevel(p, "Level 4 - Mor Lam Switchback", "One lever: one gate opens, the other shuts. Lure the guard back, then lock it in.", -16f, 16f, -10f);
            MakeRadio(c, -7f, 16f);
            var g1 = LeverGate("Gate_Back", -4f, true);
            var g2 = LeverGate("Gate_Front", 9f, false);
            MakeLever(0f, g1, g2);
            Zombie(c, "GuardZombie", 4f, ZombieChaser.Kind.Guard);
            MakeExit(13f);
        });
        // 5 - end of the morning. One palm, one shot: an empty bark (nothing in range) brings the guard to the bank,
        //     THEN drop the palm on it. Drop it first and the guard walks over your new bridge.
        //     Shelter (old sala): stand ~1.3 past it so your master is next to it, bark -> hide until dark (cutscene).
        any |= Level(5, ow, () => {
            var c = BeginLevel(p, "Level 5 - One Palm, One Shot", "The palm falls only once - bring the guard to the bank first. Then crawl past the cans to the shelter.", -17f, 10f, -13f);
            var canal = MakeCanal(c, -5.5f);
            MakePalmBridge(canal);
            var guard = Zombie(c, "GuardZombie", 0f, ZombieChaser.Kind.Guard);
            guard.noiseHearing = 13f;   // hears a bark from anywhere left of the palm
            MakeCans(2f);   // CRAWL (new) past them, or the sleeper by the shelter wakes up
            var sleeper = Zombie(c, "SleepingZombie", 4f, ZombieChaser.Kind.Chaser, 0.6f); sleeper.asleep = true;
            var shrine = MakeShelter(6f);
            shrine.winMessage = "You found a shelter - an old sala. Rest here until dark...";
            shrine.masterRange = 1.5f;   // stand just past the shrine: your master stops right next to it
        });

        // ---------- NIGHT: darkness, the bark is your eyes ----------
        // 6 - you pass two sleepers in the dark, then the dog walks into a string of cans: both wake behind you.
        //     Run to the candle by the rusty gate and bark it at once (your master runs into the light),
        //     then HOLD STILL while it creaks open (9 s) and the zombies wait at the edge of the light.
        any |= Level(6, ow, () => {
            var c = BeginLevel(p, "Level 6 - Candle in the Dark", "Cans wake the dead. Get your master into the candle light, then hold still.", -15f, 16f, -9.5f, true);
            var z1 = Zombie(c, "SleepingZombie", -5.5f, ZombieChaser.Kind.Chaser, 0.75f); z1.asleep = true; z1.wakeHearing = 7.5f;
            var z2 = Zombie(c, "SleepingZombie2", -4f, ZombieChaser.Kind.Chaser, 0.75f); z2.asleep = true; z2.wakeHearing = 7.5f;
            MakeCans(1.5f);
            MakeLantern(3.5f);
            var rusty = MakeGate("Gate_Rusty", 5f, false);
            rusty.rusty = true; rusty.openTime = 9f; rusty.panel.color = Hex("#B0704A");
            MakeGate("Gate", 11f, false);
            MakeExit(14f);
        });
        // 7 - the lever works the drawbridge, and its noise calls the guard: it walks onto the bridge - flip again!
        //     Cross right after the first bark and you meet the guard halfway.
        any |= Level(7, ow, () => {
            var c = BeginLevel(p, "Level 7 - Drop the Bridge", "Drop the bridge out from under the guard. The last gate's key is buried - SNIFF for it.", -14f, 15f, -8.5f, true);
            var canal = MakeCanal(c, -0.5f);
            var bridge = MakeDrawbridge(canal, false);
            bridge.radius = 0f;   // only the lever moves it
            MakeLever(-4.5f, bridge);
            var guard = Zombie(c, "GuardZombie", 4.5f, ZombieChaser.Kind.Guard);
            guard.noiseHearing = 12f;
            var last = LeverGate("Gate_Buried", 9f, false, "LOCKED");   // no handle: its key is buried by the road
            var scent = MakeScent(6.8f, last);
            scent.foundMessage = "Bak Daeng dug up the gate key! The gate swings open.";
            MakeExit(13f);
        });
        // 8 - horde + only 3 barks. Don't spend one shutting the first gate: the candle stops a horde too.
        //     Bark the rusty gate (your master runs into the light), hold still 9 s, go.
        any |= Level(8, ow, () => {
            var c = BeginLevel(p, "Level 8 - Hold the Light", "A horde, and only a few barks. Candle light stops a horde too.", -17f, 18f, -9.5f, true);
            MakeHorde(c, -16f, -6f, 5, 1.3f);
            MakeGate("Gate_Open", -2f, true);
            MakeLantern(4f);
            var rusty = MakeGate("Gate_Rusty", 5.5f, false);
            rusty.rusty = true; rusty.openTime = 9f; rusty.panel.color = Hex("#B0704A");
            var z = Zombie(c, "SleepingZombie", 9.5f, ZombieChaser.Kind.Chaser, 0.75f); z.asleep = true;
            MakeGate("Gate", 13f, false);
            MakeExit(16f);
        });
        // 9 - short song. The lever opens the radio room (and shuts the way out). Your master is always BEHIND you:
        //     walk past the radio, turn round, bark it -> the guard runs in, walk past it dancing, flip the lever
        //     to lock it in before the song ends (16 s) and it walks out.
        any |= Level(9, ow, () => {
            var c = BeginLevel(p, "Level 9 - Behind the Radio", "Short song! Get your master BEHIND the radio, lure the guard in, lock it in.", -18f, 18f, -4f, true);
            c.gs.walkInDistance = 2.5f;   // the walk-in must not start behind the closed radio-room gate
            MakeRadio(c, -13.5f, 16f);
            var a1 = LeverGate("Gate_RadioRoom", -10f, false);
            var a2 = LeverGate("Gate_WayOut", 9f, true);
            MakeLever(-7f, a1, a2);
            Zombie(c, "GuardZombie", 6f, ZombieChaser.Kind.Guard);
            MakeGate("Gate", 13f, false);
            MakeExit(16f);
        });
        // 10 - finale. The horde wakes behind you (back up through the open gate and bark it shut, or race it).
        //      The lever is the only bark the shrine guard can hear: it comes to the bank -> drop the palm on it.
        //      But the lever also shut the shrine gate: flip it back, cross, bring your master to the great shrine.
        any |= Level(10, ow, () => {
            var c = BeginLevel(p, "Level 10 - The Great Shrine", "The horde is awake! Bring the guard to the palm, cross, and ask the great spirit for help.", -26f, 22f, -11f, true);
            MakeHorde(c, -24f, -8f, 5, 1.1f);
            MakeGate("Gate_Horde", -6f, true);
            var a2 = LeverGate("Gate_Shrine", 14f, true);
            MakeLever(1f, a2);
            var canal = MakeCanal(c, 6.5f);
            MakePalmBridge(canal);
            var guard = Zombie(c, "ShrineGuard", 10.5f, ZombieChaser.Kind.Guard);
            guard.noiseHearing = 12f;
            var shrine = MakeShrine(c, 17.5f);
            shrine.winMessage = "The great spirit hears your call...";
            shrine.masterRange = 1.5f;   // stand just past the shrine: your master stops right next to it
        });
        // ---------- FINALE: the spirit's shield ----------
        // 11 - too many to sneak past. LOUD call = spirit shield (5 s, recharges 12 s): walk straight through a crowd
        //      of guards. Rest in the big candle light to recharge (it also stops the horde), then the second crowd:
        //      don't bark in the middle of it (the guards follow the noise), bark only once you're past - then the taxi.
        any |= Level(11, ow, () => {
            var c = BeginLevel(p, "Level 11 - Run for the Taxi", "Too many of them! Call LOUD for the spirit's shield, run through, and find a taxi.", -22f, 32f, -17f, true);
            c.gs.gameObject.AddComponent<SpiritShield>();
            MakeHorde(c, -21f, -12f, 5, 1.2f);
            float[] crowdA = { 0f, 0.75f, 1.5f }, crowdB = { 15f, 16f, 17f };
            for (int i = 0; i < crowdA.Length; i++) Zombie(c, $"CrowdA{i}", crowdA[i], ZombieChaser.Kind.Guard);
            MakeLantern(8f, 2.4f);
            for (int i = 0; i < crowdB.Length; i++) Zombie(c, $"CrowdB{i}", crowdB[i], ZombieChaser.Kind.Guard);
            MakeTaxi(26f).AddComponent<Taxi>();
        });
        return any;
    }

    // ------------------------------------------------------------------ story props
    // old open pavilion (ศาลา): the end-of-morning shelter
    static Transform MakeSala(float x)
    {
        var go = new GameObject("Shelter_Sala");
        go.transform.position = new Vector3(x, GroundY, 0f);
        var t = go.transform;
        Box("floor", t, new Vector3(0f, 0.15f, 0f), new Vector2(4.4f, 0.3f), "#8C6A50", -7);
        foreach (float px in new[] { -2f, -0.7f, 0.7f, 2f })
            Box("post", t, new Vector3(px, 1.55f, 0f), new Vector2(0.16f, 2.5f), "#6B4A2F", -7);
        Box("beam", t, new Vector3(0f, 2.85f, 0f), new Vector2(4.6f, 0.18f), "#6B4A2F", -6);
        Box("roofL", t, new Vector3(-1.25f, 3.45f, 0f), new Vector2(2.9f, 0.22f), "#8C2F2B", -6, 28f);
        Box("roofR", t, new Vector3(1.25f, 3.45f, 0f), new Vector2(2.9f, 0.22f), "#8C2F2B", -6, -28f);
        Box("gable", t, new Vector3(0f, 3.3f, 0f), new Vector2(1.6f, 0.7f), "#C9A14A", -7);
        Box("finial", t, new Vector3(0f, 4.15f, 0f), new Vector2(0.1f, 0.4f), "#C9A14A", -6);
        return t;
    }

    static Shrine MakeShelter(float x)
    {
        var t = MakeSala(x);
        var sh = t.gameObject.AddComponent<Shrine>();
        sh.radius = 3f; sh.markerHeight = 4.2f; sh.placeName = "shelter";
        return sh;
    }

    static GameObject Wheel(string name, Transform parent, float x)
    {
        var w = Put(name, parent, "circle", new Vector3(x, 0.35f, 0f), Vector3.one * 0.7f, Hex("#1A1A1F"), -3).gameObject;
        Box("spoke", w.transform, Vector3.zero, new Vector2(0.9f, 0.12f), "#8A8F9C", -2);
        return w;
    }

    // blue songthaew (pickup bus) for the opening
    static Transform MakeSongthaew(float x)
    {
        var t = new GameObject("Songthaew").transform;
        t.position = new Vector3(x, GroundY, 0f);
        Box("bed", t, new Vector3(-0.6f, 1.0f, 0f), new Vector2(3.2f, 0.9f), "#2F5E8C", -5);
        Box("roof", t, new Vector3(-0.6f, 2.35f, 0f), new Vector2(3.3f, 0.16f), "#2F5E8C", -5);
        Box("postL", t, new Vector3(-2.1f, 1.8f, 0f), new Vector2(0.1f, 1.1f), "#24476B", -5);
        Box("postR", t, new Vector3(0.9f, 1.8f, 0f), new Vector2(0.1f, 1.1f), "#24476B", -5);
        Box("cab", t, new Vector3(1.75f, 1.25f, 0f), new Vector2(1.5f, 1.4f), "#3C74A8", -5);
        Box("window", t, new Vector3(1.95f, 1.55f, 0f), new Vector2(0.8f, 0.5f), "#BFD8E8", -4);
        Box("bumper", t, new Vector3(2.55f, 0.6f, 0f), new Vector2(0.2f, 0.3f), "#8A8F9C", -4);
        Wheel("wheelB", t, -1.4f); Wheel("wheelF", t, 1.6f);
        return t;
    }

    // pink-and-green Bangkok-style taxi
    static GameObject MakeTaxi(float x)
    {
        var t = new GameObject("Taxi").transform;
        t.position = new Vector3(x, GroundY, 0f);
        Box("body", t, new Vector3(0f, 0.85f, 0f), new Vector2(3.6f, 0.8f), "#E2558F", -5);
        Box("cabin", t, new Vector3(-0.2f, 1.6f, 0f), new Vector2(2.0f, 0.75f), "#3FAE6A", -5);
        Box("windowF", t, new Vector3(0.35f, 1.6f, 0f), new Vector2(0.75f, 0.5f), "#BFD8E8", -4);
        Box("windowB", t, new Vector3(-0.65f, 1.6f, 0f), new Vector2(0.75f, 0.5f), "#BFD8E8", -4);
        Box("sign", t, new Vector3(-0.2f, 2.12f, 0f), new Vector2(0.8f, 0.26f), "#F2D04B", -4);
        var txt = new GameObject("label").AddComponent<TextMesh>();
        txt.transform.SetParent(t, false); txt.transform.localPosition = new Vector3(-0.2f, 2.12f, 0f);
        txt.text = "TAXI"; txt.anchor = TextAnchor.MiddleCenter; txt.characterSize = 0.05f; txt.fontSize = 40; txt.fontStyle = FontStyle.Bold;
        txt.color = Hex("#1A1A1F");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.font = font; var mr = txt.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material; mr.sortingOrder = Darkness.DarkOrder + 5;
        Put("headlight", t, "circle", new Vector3(1.75f, 0.95f, 0f), Vector3.one * 0.22f, Hex("#FFF3C2"), -3);
        Wheel("wheelB", t, -1.1f); Wheel("wheelF", t, 1.1f);
        return t.gameObject;
    }

    // ------------------------------------------------------------------ title + cutscenes
    // a scene with the world and the characters as puppets (no game logic), for the title and the cutscenes
    static Ctx BeginStage(Dictionary<string, (GameObject prefab, float ground)> prefabs, float min, float max, bool night, float camX,
                          float dogX, float touristX)
    {
        var th = night ? Night : Day;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var c = new Ctx { prefabs = prefabs, min = min, max = max, th = th };
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 4.6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex(th.sky);
        cam.transform.position = new Vector3(camX, 0f, -10f);
        c.lineMat = LineMaterial();
        ImportSimpleSprite("Assets/Game/Sprites/gate.png", 100f, new Vector2(0.5f, 0f));
        ImportSimpleSprite("Assets/Game/Sprites/circle.png", 64f, new Vector2(0.5f, 0.5f));
        BuildWorld(min, max, th);

        var dogData = JsonUtility.FromJson<DogRigBuilder.RigData>(File.ReadAllText("Assets/Dog/dog_rig.json"));
        var dog = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Dog/Dog.prefab"));
        dog.name = "Dog";
        dog.transform.position = new Vector3(dogX, GroundY + dogData.groundOffset, 0f);
        Object.DestroyImmediate(dog.GetComponent<DogWalker>());   // a puppet: the cutscene moves it
        var tp = prefabs["tourist"];
        var tourist = (GameObject)PrefabUtility.InstantiatePrefab(tp.prefab);
        tourist.name = "Tourist";
        tourist.transform.position = new Vector3(touristX, GroundY + tp.ground, 0f);
        Object.DestroyImmediate(tourist.GetComponent<BlindFollower>());
        c.tourist = tourist.transform;
        var ropeGo = new GameObject("Rope");
        var lr = ropeGo.AddComponent<LineRenderer>();
        lr.material = c.lineMat; lr.widthMultiplier = 0.045f;
        lr.startColor = lr.endColor = new Color(0.79f, 0.81f, 0.87f);
        lr.numCapVertices = 2; lr.sortingOrder = 50;
        var rope = ropeGo.AddComponent<Rope>();
        rope.dog = dog.transform; rope.tourist = tourist.transform;
        return c;
    }

    static void Puppet(Ctx c, int i, float x, bool faceLeft, bool guard = false)
    {
        var zp = c.prefabs["zombie"];
        var z = (GameObject)PrefabUtility.InstantiatePrefab(zp.prefab);
        z.name = "Zombie" + i;
        Object.DestroyImmediate(z.GetComponent<ZombieChaser>());
        float sc = guard ? 1.12f : 1f;
        z.transform.localScale = new Vector3(faceLeft ? -sc : sc, sc, 1f);
        z.transform.position = new Vector3(x, GroundY + zp.ground * sc, 0f);
        if (guard) foreach (var sr in z.GetComponentsInChildren<SpriteRenderer>()) sr.color = new Color(0.86f, 0.62f, 0.6f);
    }

    static void Player(CutscenePlayer.Story story)
    {
        var go = new GameObject("Cutscene");
        go.AddComponent<CutscenePlayer>().story = story;
    }

    static bool BuildStoryScenes(Dictionary<string, (GameObject prefab, float ground)> p, bool overwrite)
    {
        bool any = false;
        Directory.CreateDirectory(CutDir);
        if (overwrite || !File.Exists(TitlePath))
        {
            var c = BeginStage(p, -14f, 14f, false, 0f, 6.2f, 3.7f);   // characters on the right, menu in the middle
            Puppet(c, 0, -6f, false); Puppet(c, 1, -7.8f, false);
            new GameObject("Title").AddComponent<TitleScreen>();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), TitlePath);
            any = true;
        }
        foreach (var cut in Cuts)
        {
            string path = $"{CutDir}/{cut.file}.unity";
            if (!overwrite && File.Exists(path)) continue;
            Ctx c;
            switch (cut.story)
            {
                case CutscenePlayer.Story.Opening:
                    c = BeginStage(p, -20f, 40f, false, 0f, -0.5f, -3f);
                    MakeSongthaew(-16f);
                    Puppet(c, 0, 11f, true); Puppet(c, 1, 13f, true); Puppet(c, 2, 15.5f, true);
                    break;
                case CutscenePlayer.Story.Shelter:
                    c = BeginStage(p, -20f, 20f, false, 0f, 1f, -0.9f);
                    MakeSala(0f);
                    MakeRadio(c, 2.3f, 0f).radius = 0f;
                    Puppet(c, 0, -8f, false); Puppet(c, 1, -10.5f, false); Puppet(c, 2, 7.5f, true); Puppet(c, 3, 10f, true);
                    break;
                case CutscenePlayer.Story.Spirit:
                    c = BeginStage(p, -16f, 20f, true, 1f, 1.1f, -1.4f);
                    MakeShrine(c, 0f).radius = 0f;
                    Puppet(c, 0, 8f, true, true); Puppet(c, 1, 10f, true, true);
                    break;
                default:
                    c = BeginStage(p, -24f, 70f, false, 0f, -3.5f, -6f);
                    MakeTaxi(3f);
                    Puppet(c, 0, -13f, false); Puppet(c, 1, -14.6f, false); Puppet(c, 2, -16.4f, false);
                    break;
            }
            Player(cut.story);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
            any = true;
        }
        return any;
    }

    // free-play test scene: two gates + a zombie (older layout)
    static void BuildSandbox(Dictionary<string, (GameObject prefab, float ground)> p)
    {
        var c = BeginLevel(p, "", "", -12f, 36f, -0.3f, true);
        Zombie(c, "Zombie", -7.6f, ZombieChaser.Kind.Chaser);
        MakeGate("Gate_Back", -4.8f, true);
        MakeGate("Gate_Front", 3.8f, false);
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
    }
}
