using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Guide demo clips: tiny scenes (Assets/Scenes/Demos, not in the build) that play one trick for real,
// with scripted moves. "Record Guide Demos" builds them and records each into Resources/GuideDemos/<id>.png
// (see DemoShot). The guide cards and the new-ability cards play these clips.
public static partial class GameSceneBuilder
{
    const string DemoDir = "Assets/Scenes/Demos";

    class Demo
    {
        public string id; public bool night; public float dogX, camX, seconds; public string steps;
        public System.Action<Ctx> build;
    }

    static List<Demo> Demos() => new List<Demo>
    {
        new Demo { id = "gate", dogX = -3.5f, camX = 0.5f, seconds = 8f,
            build = c => MakeGate("Gate", 1.2f, false),
            steps = "walk 0.5;wait 0.6;bark;wait 1.2;walk 5.5;wait 1" },

        new Demo { id = "chaser", dogX = 0.8f, camX = 0.3f, seconds = 10f,
            build = c => { Zombie(c, "Zombie", -5.5f, ZombieChaser.Kind.Chaser, 0.7f); MakeGate("Gate", 0f, true); },
            steps = "walk 4.2;walk 1.3;wait 0.2;bark;wait 5" },

        new Demo { id = "rusty", dogX = -3.5f, camX = 0.5f, seconds = 12f,
            build = c => { var g = MakeGate("Gate_Rusty", 1.2f, false); g.rusty = true; g.openTime = 7f; g.panel.color = Hex("#B0704A"); },
            steps = "walk 0.5;wait 0.3;bark;until gateopen Gate_Rusty;walk 5.5;wait 1" },

        new Demo { id = "key", dogX = -3f, camX = 1f, seconds = 10f,
            build = c => { var g = LeverGate("Gate_Locked", 2.2f, false, "LOCKED"); MakeScent(0.3f, g); },
            steps = "walk 0.3;wait 0.4;bark;wait 0.8;sniff;wait 2.2;walk 6;wait 1" },

        new Demo { id = "lever", dogX = -0.5f, camX = 0.5f, seconds = 9f,
            build = c => { var a = LeverGate("Gate_A", -4.5f, true); var b = LeverGate("Gate_B", 3f, false); MakeLever(0f, a, b); },
            steps = "walk 0.2;wait 1.6;bark;wait 1.5;walk 6;wait 1" },

        // dog turned round (master on its right), the zombie comes from the left: it stops at the radio and dances,
        // then you walk right past it
        new Demo { id = "radio", dogX = -0.5f, camX = -3.5f, seconds = 12f,
            build = c => { MakeRadio(c, -3.5f, 0f); Zombie(c, "Zombie", -8.5f, ZombieChaser.Kind.Chaser, 0.7f); },
            steps = "walk -2.2;wait 1;bark;until dance Zombie;wait 0.8;walk -8.5;wait 1" },

        new Demo { id = "guard", dogX = -3.5f, camX = 0.5f, seconds = 8.5f,
            build = c => Zombie(c, "GuardZombie", 2.5f, ZombieChaser.Kind.Guard),
            steps = "walk 5;wait 3" },

        new Demo { id = "canal", dogX = -3.5f, camX = 1f, seconds = 7f,
            build = c => { MakeCanal(c, 1.5f); Zombie(c, "Zombie", 5.8f, ZombieChaser.Kind.Chaser, 0.8f); },
            steps = "walk 1;wait 5" },

        new Demo { id = "palm", dogX = -3.5f, camX = 1f, seconds = 10f,
            build = c => { var k = MakeCanal(c, 1f); MakePalmBridge(k); Zombie(c, "GuardZombie", 3.1f, ZombieChaser.Kind.Guard); },
            steps = "walk 0;wait 0.8;bark;wait 2.5;walk 5.5;wait 1" },

        new Demo { id = "drawbridge", dogX = 3.3f, camX = 0f, seconds = 10f,
            build = c => { var k = MakeCanal(c, -1f); MakeDrawbridge(k, true); Zombie(c, "Zombie", -6.2f, ZombieChaser.Kind.Chaser, 0.8f); },
            steps = "wait 0.5;until zx Zombie > -1.7;bark;wait 3" },

        new Demo { id = "cans_walk", dogX = -2f, camX = 0.5f, seconds = 8f,
            build = c => { MakeCans(-0.8f); var z = Zombie(c, "SleepingZombie", 4.6f, ZombieChaser.Kind.Chaser, 0.6f); z.asleep = true; },
            steps = "walk 3;wait 4" },

        new Demo { id = "crawl", dogX = -2f, camX = 0.5f, seconds = 9f,
            build = c => { MakeCans(-0.8f); var z = Zombie(c, "SleepingZombie", 4.6f, ZombieChaser.Kind.Chaser, 0.6f); z.asleep = true; },
            steps = "crawl on;walk 3;wait 1.2;crawl off;wait 1" },

        new Demo { id = "sleeper", dogX = -2.5f, camX = 0f, seconds = 7f,
            build = c => { var z = Zombie(c, "SleepingZombie", 2.5f, ZombieChaser.Kind.Chaser, 0.6f); z.asleep = true; },
            steps = "wait 1;bark;wait 4" },

        new Demo { id = "candle", night = true, dogX = 1.6f, camX = -0.5f, seconds = 8.5f,
            build = c => { MakeLantern(0.5f); Zombie(c, "Zombie", -5.5f, ZombieChaser.Kind.Chaser, 0.75f); },
            steps = "wait 1.2;bark;wait 6" },

        new Demo { id = "horde", dogX = -2.5f, camX = 1.5f, seconds = 12f,
            build = c => { MakeHorde(c, -10f, -1f, 4, 1.3f); MakeGate("Gate", 1.5f, true); },
            steps = "walk 5.5;walk 3;wait 0.1;bark;wait 4" },

        new Demo { id = "dark", night = true, dogX = -2f, camX = -0.5f, seconds = 9f,
            build = c => { var z = Zombie(c, "SleepingZombie", 3.2f, ZombieChaser.Kind.Chaser, 0.6f); z.asleep = true; z.wakeHearing = 2f; MakeCans(1f); },
            steps = "wait 1;bark;wait 2.6;walk -0.6;sniff;wait 3" },

        new Demo { id = "run", dogX = -2f, camX = 0f, seconds = 5f,
            build = c => Zombie(c, "Zombie", -5.6f, ZombieChaser.Kind.Chaser, 0.8f),
            steps = "wait 0.3;run on;walk 7.5;run off;wait 1" },

        new Demo { id = "shield", night = true, dogX = -1f, camX = 2f, seconds = 8f,
            build = c => { foreach (var (n, x) in new[] { ("CrowdA0", 0.8f), ("CrowdA1", 1.6f) }) Zombie(c, n, x, ZombieChaser.Kind.Guard).noiseHearing = 0f; },
            steps = "wait 0.3;shield;run on;walk 8;run off;wait 3" },
    };

    static Dictionary<string, (GameObject prefab, float ground)> CharacterPrefabs()
    {
        if (!File.Exists("Assets/Dog/Dog.prefab")) DogRigBuilder.Build();
        var data = JsonUtility.FromJson<RigData>(File.ReadAllText(Path.Combine(Dir, "walk_rig.json")));
        var prefabs = new Dictionary<string, (GameObject prefab, float ground)>();
        foreach (var ch in data.characters) { ImportSprites(ch, data.ppu); prefabs[ch.name] = (BuildCharacter(ch), ch.groundOffset); }
        return prefabs;
    }

    // builds every demo scene (always overwritten: they are generated, nobody edits them)
    public static List<string> BuildDemoScenes(string only = null)
    {
        var p = CharacterPrefabs();
        Directory.CreateDirectory(DemoDir);
        var paths = new List<string>();
        foreach (var d in Demos())
        {
            if (only != null && System.Array.IndexOf(only.Split(','), d.id) < 0) continue;
            var c = BeginLevel(p, "", "", d.camX - 9f, d.camX + 9f, d.dogX, d.night);
            Object.DestroyImmediate(c.det.gameObject);   // no pet camera in a demo
            var dog = Object.FindFirstObjectByType<DogWalker>();
            dog.detector = null; dog.mode = DogWalker.Mode.Keyboard;
            d.build(c);
            var shot = c.gs.gameObject.AddComponent<DemoShot>();
            shot.id = d.id; shot.steps = d.steps; shot.camX = d.camX; shot.seconds = d.seconds;
            string path = $"{DemoDir}/Demo_{d.id}.unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
            paths.Add(path);
        }
        return paths;
    }

    [MenuItem("Tools/Pet Game/Record Guide Demos")]
    static void RecordDemosMenu() => RecordDemos(null);

    // only = comma separated ids (null = all)
    public static void RecordDemos(string only)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop play mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var paths = BuildDemoScenes(only);
        if (paths.Count == 0) return;
        PlayerPrefs.SetString("demo.queue", string.Join(",", paths));
        PlayerPrefs.SetString("demo.log", ""); PlayerPrefs.SetString("solver.log", "");
        PlayerPrefs.Save();
        EditorSceneManager.OpenScene(paths[0]);
        EditorSceneManager.playModeStartScene = null;   // record the demo scenes themselves (PlayFromTitle puts it back after)
        EditorApplication.isPlaying = true;
    }
}

// the recorded clips: no mipmaps, kept sharp, compressed
class GuideDemoImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/GuideDemos/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 4096;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.sRGBTexture = true;
    }
}
