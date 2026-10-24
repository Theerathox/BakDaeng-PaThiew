using UnityEngine;
using UnityEngine.SceneManagement;

// Bak Daeng learns his tricks one at a time as the story goes on (not everything at once).
// Walking and barking are there from level 1; the rest unlock from the level below onwards.
// The first level of each one shows a "NEW ABILITY" card (GameState). Levels picked from the level
// menu get everything up to that level. Test scenes (PuzzleLab, sandbox) have everything.
public enum Ability { Crawl, Sniff, Run, Shield }

public static class Abilities
{
    // first level that has each ability - change here to move them around
    // spread out: something new every 1-2 levels in the morning, the night adds the dark (bark = sonar, from
    // the shelter cutscene), the spirit's shield comes last
    public static int RunFrom = 2;      // a zombie behind you: get to the gate fast
    public static int SniffFrom = 3;    // the rusty gate's key is buried
    public static int CrawlFrom = 5;    // cans by the shelter, a sleeper behind them
    public static int ShieldFrom = 11;  // the great spirit's gift

    public static int From(Ability a) => a == Ability.Crawl ? CrawlFrom : a == Ability.Sniff ? SniffFrom : a == Ability.Run ? RunFrom : ShieldFrom;

    // number of the current level (Assets/Scenes/Levels/LevelN), or a big number in any other scene
    public static int Level
    {
        get
        {
            var s = SceneManager.GetActiveScene();
            if (!s.path.Contains("/Levels/") || !s.name.StartsWith("Level")) return 999;
            return int.TryParse(s.name.Substring(5), out int n) ? n : 999;
        }
    }

    public static bool Has(Ability a) => Level >= From(a);
    public static bool NewHere(Ability a) => Level == From(a);

    public static string Title(Ability a) => Loc.T(a == Ability.Crawl ? "NEW: CRAWL" : a == Ability.Sniff ? "NEW: SNIFF" : a == Ability.Run ? "NEW: RUN" : "NEW: SPIRIT SHIELD");

    public static string Pet(Ability a) => Loc.T(a switch
    {
        Ability.Crawl => "Tilt the camera UP: your pet LOW in the picture  ->  Bak Daeng crawls.",
        Ability.Sniff => "Bring the camera CLOSE to your pet  ->  Bak Daeng sniffs.",
        Ability.Run => "Turn the camera: your pet at the FAR SIDE of the picture  ->  Bak Daeng runs.",
        _ => "Call LOUDER than a bark - past the 2nd mark on the mic meter.",
    });

    public static string What(Ability a) => Loc.T(a switch
    {
        Ability.Crawl => "Slow, but SILENT: you and your master can pass the cans without a sound.",
        Ability.Sniff => "No sound at all: shows what is close by in the dark, and digs up buried things.",
        Ability.Run => "Your master runs with you. Fast - but LOUD: footsteps wake the sleepers.",
        _ => "The spirit's light shields your master for 5 seconds - walk right through them. Then it recharges.",
    });

    public static string Button(Ability a) => Loc.T(a switch
    {
        Ability.Crawl => "No pet?  hold C  or tap CRAWL",
        Ability.Sniff => "No pet?  press F  or tap SNIFF",
        Ability.Run => "No pet?  hold Shift  or tap RUN",
        _ => "No mic?  press S  or tap SHIELD",
    });
}
