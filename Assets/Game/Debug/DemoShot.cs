#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only: records one guide demo clip. Lives on the "Game" object of a scene in Assets/Scenes/Demos
// (made by Tools/Pet Game/Record Guide Demos). Plays the scene with scripted moves (LevelSolver steps),
// renders the fixed camera into small frames and saves them as Resources/GuideDemos/<id>.png.
// The queue of scenes to record is kept in PlayerPrefs "demo.queue" (paths, comma separated); when one
// clip is done the next scene loads, and play mode stops after the last one.
public class DemoShot : MonoBehaviour
{
    public string id;
    [TextArea] public string steps;
    public float camX, camY = -0.8f, camSize = 3.2f;
    public float seconds = 8f;
    public const string OutDir = "Assets/Resources/GuideDemos";

    void Awake() { GuideDemo.Recording = true; }

    IEnumerator Start()
    {
        var cam = Camera.main;
        var follow = cam.GetComponent<CameraFollow>(); if (follow != null) follow.enabled = false;
        cam.transform.position = new Vector3(camX, camY, -10f);
        cam.orthographicSize = camSize;
        var rt = new RenderTexture(GuideDemo.FW, GuideDemo.FH, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;

        Time.captureFramerate = 30;   // fixed game time per frame: same clip however fast the editor runs
        while (!GameState.Started) yield return null;
        var dog = FindFirstObjectByType<DogWalker>(); if (dog != null) dog.mode = DogWalker.Mode.Keyboard;
        var solver = new GameObject("DemoSolver").AddComponent<LevelSolver>();
        solver.steps = steps.Split(';');

        int frames = Mathf.CeilToInt(seconds * GuideDemo.Fps / GuideDemo.Cols) * GuideDemo.Cols;
        int rows = frames / GuideDemo.Cols;
        var sheet = new Texture2D(GuideDemo.Cols * GuideDemo.FW, rows * GuideDemo.FH, TextureFormat.RGB24, false);
        int every = 30 / GuideDemo.Fps;
        for (int i = 0; i < frames; i++)
        {
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            int col = i % GuideDemo.Cols, row = i / GuideDemo.Cols;
            sheet.ReadPixels(new Rect(0, 0, GuideDemo.FW, GuideDemo.FH), col * GuideDemo.FW, (rows - 1 - row) * GuideDemo.FH);
            RenderTexture.active = prev;
            for (int k = 0; k < every; k++) yield return null;
        }
        sheet.Apply();
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes($"{OutDir}/{id}.png", sheet.EncodeToPNG());
        Debug.Log($"[DemoShot] saved {id} ({frames} frames)");
        PlayerPrefs.SetString("demo.log", PlayerPrefs.GetString("demo.log", "") + $"== {id} ok {frames}\n" + PlayerPrefs.GetString("solver.log", ""));
        PlayerPrefs.SetString("solver.log", "");

        cam.targetTexture = null; rt.Release();
        Next();
    }

    static void Next()
    {
        var q = new System.Collections.Generic.List<string>(PlayerPrefs.GetString("demo.queue", "").Split(','));
        q.RemoveAll(s => s == "" || s == SceneManager.GetActiveScene().path);
        PlayerPrefs.SetString("demo.queue", string.Join(",", q)); PlayerPrefs.Save();
        if (q.Count > 0) { EditorSceneManager.LoadSceneInPlayMode(q[0], new LoadSceneParameters(LoadSceneMode.Single)); return; }
        Time.captureFramerate = 0; GuideDemo.Recording = false;
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += AssetDatabase.Refresh;
    }

    void OnDestroy() { Time.captureFramerate = 0; }
}
#endif
