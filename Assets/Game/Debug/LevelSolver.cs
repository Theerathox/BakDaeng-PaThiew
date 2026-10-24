#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only automatic play-test. Runs a scripted solution in a level and logs "[Solver]" lines.
// Turned on per scene with PlayerPrefs key "solver.<SceneName>" = steps separated by ';' :
//   walk X | bark | wait S | until zx NAME < V | until zx NAME > V | until dance NAME | until gateopen NAME
//   | until mnear X TOL | log
// Does nothing in builds or when no key is set.
public class LevelSolver : MonoBehaviour
{
    public string[] steps;
    DogWalker dog; BlindFollower master; DogBark bark;
    float t0; int barks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { SceneManager.sceneLoaded += (s, m) => Try(s.name); Try(SceneManager.GetActiveScene().name); }

    static void Try(string scene)
    {
        string key = "solver." + scene;
        if (!PlayerPrefs.HasKey(key) || FindFirstObjectByType<LevelSolver>() != null) return;
        var go = new GameObject("LevelSolver");
        go.AddComponent<LevelSolver>().steps = PlayerPrefs.GetString(key).Split(';');
    }

    void Log(string m)
    {
        string line = $"[Solver] {SceneManager.GetActiveScene().name} t={Time.timeSinceLevelLoad - t0:0.0} {m}";
        Debug.Log(line);
        PlayerPrefs.SetString("solver.log", PlayerPrefs.GetString("solver.log", "") + line + "\n");   // read back by the test runner
    }

    IEnumerator Start()
    {
        dog = FindFirstObjectByType<DogWalker>(); master = FindFirstObjectByType<BlindFollower>(); bark = FindFirstObjectByType<DogBark>();
        dog.mode = DogWalker.Mode.Keyboard;   // ignore the real pet camera during the test
        Time.timeScale = PlayerPrefs.GetFloat("solver.speed", 1f);
        while (!GameState.Started) yield return null;
        t0 = Time.timeSinceLevelLoad;
        Log("start " + Snap());
        foreach (var raw in steps)
        {
            var a = raw.Trim().Split(' ');
            if (a.Length == 0 || a[0] == "") continue;
            if (Done()) break;
            switch (a[0])
            {
                case "walk":
                {
                    float x = F(a[1]); float stuck = 0f; float last = dog.transform.position.x;
                    while (!Done() && Mathf.Abs(x - dog.transform.position.x) > 0.06f)
                    {
                        DogWalker.TestAxis = Mathf.Sign(x - dog.transform.position.x);
                        yield return null;
                        float now = dog.transform.position.x;
                        stuck = Mathf.Abs(now - last) < 1e-5f ? stuck + Time.deltaTime : 0f; last = now;
                        if (stuck > 0.4f) { Log("dog blocked at " + now.ToString("0.00")); break; }
                    }
                    DogWalker.TestAxis = 0f;
                    break;
                }
                case "bark": barks++; Log($"BARK #{barks} " + Snap()); bark.Bark(); for (float bt = 0f; bt < 0.85f; bt += Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime) yield return null; break;
                case "shield": Log("SHIELD " + Snap()); if (SpiritShield.Instance != null) SpiritShield.Instance.Call(); yield return null; break;
                case "crawl": DogWalker.TestCrawl = a.Length > 1 && a[1] == "on"; break;
                case "run": DogWalker.TestRun = a.Length > 1 && a[1] == "on"; break;
                case "sniff": Log("SNIFF " + Snap()); if (DogSniff.Instance != null) DogSniff.Instance.Sniff(); yield return null; break;
                case "shot": ScreenCapture.CaptureScreenshot("Assets/Claude outputs/shots/" + a[1] + ".png"); Log("shot " + a[1]); yield return null; break;
                case "wait": { float wl = F(a[1]); for (float wt = 0f; wt < wl && !Done(); wt += Time.deltaTime) yield return null; break; }
                case "log": Log(Snap()); break;
                case "until":
                {
                    float limit = 30f, t = 0f;
                    while (!Done() && !Cond(a) && t < limit) { t += Time.deltaTime; yield return null; }
                    if (t >= limit) Log("until TIMEOUT: " + raw);
                    break;
                }
            }
        }
        DogWalker.TestAxis = 0f; DogWalker.TestCrawl = false; DogWalker.TestRun = false;
        float w = 0f; while (!Done() && w < 4f) { w += Time.deltaTime; yield return null; }
        Report();
    }

    float nextTrace;
    void Update()
    {
        if (PlayerPrefs.GetInt("solver.trace", 0) == 1 && GameState.Started && !Done() && Time.time >= nextTrace)
        { nextTrace = Time.time + 1f; Log("trace " + Snap()); }
    }

    bool Done() => GameState.IsOver || (GameState.Started && GameState.Cutscene);

    void Report()
    {
        string r = GameState.IsOver ? (GameState.Won ? "WIN" : "LOSE") : GameState.Cutscene ? "CLEAR (exit)" : "NOT DONE";
        var reason = typeof(GameState).GetField("reason", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        Log($"RESULT {r}  {(GameState.IsOver ? reason : "")}  {Snap()}");
        if (GameState.IsOver && GameState.Won && PlayerPrefs.GetInt("solver.chain", 0) == 1)
        {
            int next = SceneManager.GetActiveScene().buildIndex + 1;
            if (next < SceneManager.sceneCountInBuildSettings) { Time.timeScale = 1f; LevelTransition.Go(next); }
        }
    }

    bool Cond(string[] a)
    {
        switch (a[1])
        {
            case "zx": { var z = Z(a[2]); if (z == null || !z.gameObject.activeInHierarchy) return true; float x = z.transform.position.x; return a[3] == "<" ? x < F(a[4]) : x > F(a[4]); }
            case "dance": { var z = Z(a[2]); var f = typeof(ZombieChaser).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance); return z != null && f.GetValue(z).ToString() == "Dance"; }
            case "gateopen": { var g = GameObject.Find(a[2])?.GetComponent<Gate>(); return g != null && !g.Blocking; }
            case "mnear": return Mathf.Abs(master.transform.position.x - F(a[2])) <= F(a[3]);
        }
        return true;
    }

    ZombieChaser Z(string name) { var go = GameObject.Find(name); return go != null ? go.GetComponent<ZombieChaser>() : null; }
    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    string Snap()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"dog {dog.transform.position.x:0.00} master {master.transform.position.x:0.00}");
        var st = typeof(ZombieChaser).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
        var inv = typeof(ZombieChaser).GetField("investigating", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None))
            sb.Append($" {z.name}:{z.transform.position.x:0.00}[{st.GetValue(z)}{((bool)inv.GetValue(z) ? ",inv" : "")}{(z.asleep ? ",zz" : "")}]");
        foreach (var g in FindObjectsByType<Gate>(FindObjectsSortMode.None)) if (g.Opening) sb.Append($" {g.name}:opening");
        var h = FindFirstObjectByType<HordeWall>(); if (h != null) sb.Append($" horde:{h.transform.position.x:0.00}");
        return sb.ToString();
    }
}
#endif
