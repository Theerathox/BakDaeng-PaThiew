using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Title screen (first scene in the build): START plays the opening cutscene (the next scene),
// LEVELS opens the level / cutscene picker for demos.
public class TitleScreen : MonoBehaviour
{
    public string title = "BAK DAENG";
    public string subtitle = "PA THIEW";
    [TextArea] public string tagline = "Guide a blind traveller out of a zombie town - with your real pet!";

    float t;

    void Update()
    {
        t += Time.unscaledDeltaTime;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (!LevelSelect.Open && k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) StartGame();
#else
        if (!LevelSelect.Open && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) StartGame();
#endif
    }

    void StartGame()
    {
        Time.timeScale = 1f;
        LevelTransition.Go(SceneManager.GetActiveScene().buildIndex + 1);
    }

    void OnGUI()
    {
        if (LevelSelect.Open || LevelTransition.Busy) return;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;

        GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(0, H * 0.12f, W, 230f), Texture2D.whiteTexture); GUI.color = Color.white;
        float bob = Mathf.Sin(t * 2f) * 4f;
        Label(new Rect(0, H * 0.12f + 14f + bob, W, 100f), Loc.Thai ? "บักแดง" : title, 84, new Color(0.85f, 0.25f, 0.2f), Loc.Bold);
        Label(new Rect(0, H * 0.12f + 104f + bob, W, 60f), Loc.Thai ? Loc.Shape("พาเที่ยว") : subtitle, 44, new Color(1f, 0.85f, 0.5f), Loc.Bold);
        Label(new Rect(0, H * 0.12f + 168f, W, 40f), Loc.T(tagline), 20, new Color(0.95f, 0.92f, 0.87f), FontStyle.Normal);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = Loc.Bold };
        if (GUI.Button(new Rect(W / 2f - 130f, H * 0.62f, 260f, 64f), Loc.T("START"), btn)) StartGame();
        if (GUI.Button(new Rect(W / 2f - 130f, H * 0.62f + 78f, 260f, 54f), Loc.T("LEVELS"), new GUIStyle(btn) { fontSize = 20 })) LevelSelect.Show();
        // language: Thai / English
        if (GUI.Button(new Rect(W / 2f - 130f, H * 0.62f + 142f, 260f, 44f), Loc.Thai ? "ภาษา: ไทย  |  English" : "Language: English  |  ไทย", new GUIStyle(btn) { fontSize = 16, font = Loc.ThaiFont }))
            Loc.Thai = !Loc.Thai;
    }

    static void Label(Rect r, string text, int size, Color c, FontStyle f)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = f, alignment = TextAnchor.MiddleCenter };
        var sh = new GUIStyle(st); sh.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), text, sh);
        st.normal.textColor = c;
        GUI.Label(r, text, st);
    }
}
