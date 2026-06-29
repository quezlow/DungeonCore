using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rebindable gameplay actions. Esc (cancel/pause), mouse buttons, the hotbar digits and
/// the F8 debug overlay are intentionally NOT here — they stay hard-bound.
/// </summary>
public enum GameAction
{
    Mine, Build, Summon, Claim,
    PanUp, PanDown, PanLeft, PanRight,
    ToggleTraps, ToggleAlerts, AvatarMenu
}

/// <summary>
/// Central, rebindable keyboard bindings. Static + PlayerPrefs-backed, mirroring
/// DcrAudioSettings. Call Keybinds.Load() once at startup (any path also lazy-loads).
/// Gameplay reads input through WasPressed / IsHeld; the Controls UI (2c-2) uses
/// Rebind / Reset* / DisplayName / ConflictingAction.
/// </summary>
public static class Keybinds
{
    private const string PREF_PREFIX = "DCR.Keybind.";

    private static readonly Dictionary<GameAction, Key> Defaults = new()
    {
        { GameAction.Mine,         Key.M },
        { GameAction.Build,        Key.B },
        { GameAction.Summon,       Key.V },
        { GameAction.Claim,        Key.C },
        { GameAction.PanUp,        Key.W },
        { GameAction.PanDown,      Key.S },
        { GameAction.PanLeft,      Key.A },
        { GameAction.PanRight,     Key.D },
        { GameAction.ToggleTraps,  Key.T },
        { GameAction.ToggleAlerts, Key.L },
        { GameAction.AvatarMenu,   Key.Tab },
    };

    private static readonly Dictionary<GameAction, Key> Current = new();

    /// <summary>Fires whenever a binding changes (rebind or reset), so UI such as the
    /// action bar can refresh its shortcut hints.</summary>
    public static event System.Action OnRebind;

    // True while a TMP_InputField is focused (renaming a floor/save, etc.) — used
    // to suppress gameplay hotkeys so typed characters don't fire mode switches.
    public static bool IsTextInputActive()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel != null
            && sel.TryGetComponent(out TMPro.TMP_InputField field)
            && field.isFocused;
    }

    /// <summary>All rebindable actions, in display order.</summary>
    public static readonly GameAction[] All =
    {
        GameAction.Mine, GameAction.Build, GameAction.Summon, GameAction.Claim,
        GameAction.PanUp, GameAction.PanDown, GameAction.PanLeft, GameAction.PanRight,
        GameAction.ToggleTraps, GameAction.ToggleAlerts, GameAction.AvatarMenu
    };

    /// <summary>Display labels for the Controls list.</summary>
    public static string Label(GameAction a) => a switch
    {
        GameAction.Mine => "Mine",
        GameAction.Build => "Build",
        GameAction.Summon => "Summon",
        GameAction.Claim => "Claim",
        GameAction.PanUp => "Pan Up",
        GameAction.PanDown => "Pan Down",
        GameAction.PanLeft => "Pan Left",
        GameAction.PanRight => "Pan Right",
        GameAction.ToggleTraps => "Toggle Traps",
        GameAction.ToggleAlerts => "Toggle Alerts",
        GameAction.AvatarMenu => "Avatar Menu",
        _ => a.ToString()
    };

    /// <summary>Loads saved bindings (falling back to defaults). Safe to call repeatedly.</summary>
    public static void Load()
    {
        Current.Clear();
        foreach (var kv in Defaults)
        {
            int saved = PlayerPrefs.GetInt(PREF_PREFIX + kv.Key, (int)kv.Value);
            Current[kv.Key] = System.Enum.IsDefined(typeof(Key), saved) ? (Key)saved : kv.Value;
        }
    }

    private static void EnsureLoaded()
    {
        if (Current.Count == 0) Load();
    }

    public static Key KeyFor(GameAction action)
    {
        EnsureLoaded();
        return Current.TryGetValue(action, out var k) ? k : Defaults[action];
    }

    public static Key DefaultFor(GameAction action) => Defaults[action];

    public static void Rebind(GameAction action, Key key)
    {
        EnsureLoaded();
        Current[action] = key;
        PlayerPrefs.SetInt(PREF_PREFIX + action, (int)key);
        PlayerPrefs.Save();
        OnRebind?.Invoke();
    }

    public static void ResetToDefault(GameAction action) => Rebind(action, Defaults[action]);

    public static void ResetAll()
    {
        foreach (var a in All) ResetToDefault(a);
    }

    /// <summary>The action currently bound to <paramref name="key"/>, ignoring
    /// <paramref name="except"/>; null if none. Used for conflict checks when rebinding.</summary>
    public static GameAction? ConflictingAction(Key key, GameAction except)
    {
        EnsureLoaded();
        foreach (var a in All)
            if (a != except && Current[a] == key) return a;
        return null;
    }

    // ── Reads used by gameplay ────────────────────────────────────
    public static bool WasPressed(GameAction action)
    {
        if (IsTextInputActive()) return false;
        var kb = Keyboard.current;
        if (kb == null) return false;
        var key = KeyFor(action);
        if (key == Key.None) return false;
        return kb[key].wasPressedThisFrame;
    }

    public static bool IsHeld(GameAction action)
    {
        if (IsTextInputActive()) return false;
        var kb = Keyboard.current;
        if (kb == null) return false;
        var key = KeyFor(action);
        if (key == Key.None) return false;
        return kb[key].isPressed;
    }

    // ── Display ───────────────────────────────────────────────────
    public static string DisplayName(GameAction action) => DisplayName(KeyFor(action));

    public static string DisplayName(Key key)
    {
        switch (key)
        {
            case Key.None: return "—";
            case Key.Space: return "Space";
            case Key.Tab: return "Tab";
            case Key.Enter: return "Enter";
            case Key.LeftShift: return "LShift";
            case Key.RightShift: return "RShift";
            case Key.LeftCtrl: return "LCtrl";
            case Key.RightCtrl: return "RCtrl";
            case Key.LeftAlt: return "LAlt";
            case Key.RightAlt: return "RAlt";
            case Key.UpArrow: return "Up";
            case Key.DownArrow: return "Down";
            case Key.LeftArrow: return "Left";
            case Key.RightArrow: return "Right";
            case Key.Backquote: return "`";
        }
        string s = key.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        if (s.StartsWith("Numpad")) return "Num " + s.Substring(6);
        return s;
    }
}