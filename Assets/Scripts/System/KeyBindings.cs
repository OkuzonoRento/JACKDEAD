using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// キーバインド（操作の割り当て）の読み書きと、割り当て直し。
/// ・割り当ては Resources/PlayerInputActions（プレイヤーの操作）の「上書き」として PlayerPrefs に保存する。ゲーム開始時に自動で読み込む
/// ・キーボード・マウス用とコントローラー用は別々に割り当てる（どちらも同時に使える。最後に触った方が効く）
/// ・割り当て直しは Input System の対話式リバインドを使う（Esc / Start で取り消し）。すでに使っているキーを選ぶと、その操作と入れ替える
/// 画面は KeyBindingsTab（設定画面の「キー設定」タブ）。
/// </summary>
public static class KeyBindings
{
    public const string PrefsKey = "KeyBindingOverrides";
    public const string MapName = "Player";

    public enum Scheme { KeyboardMouse, Gamepad }

    /// <summary>割り当て画面に出す1行（操作1つ、または移動の上下左右のうち1方向）</summary>
    public class Entry
    {
        public string action;      // 入力アクション名
        public string part;        // 複合（移動の WASD など）の方向名。単独の操作なら null
        public string ja, en;      // 表示名
        public string descJa, descEn;
        public bool keyboardOnly;  // コントローラーでは変えない（移動はスティック固定）
    }

    public static readonly Entry[] Entries =
    {
        new Entry { action = "Move", part = "up",    ja = "前進",   en = "MOVE FORWARD", descJa = "前へ進みます。",   descEn = "MOVE FORWARD.",  keyboardOnly = true },
        new Entry { action = "Move", part = "down",  ja = "後退",   en = "MOVE BACK",    descJa = "後ろへ下がります。", descEn = "MOVE BACKWARD.", keyboardOnly = true },
        new Entry { action = "Move", part = "left",  ja = "左へ移動", en = "MOVE LEFT",   descJa = "左へ動きます。",   descEn = "MOVE LEFT.",     keyboardOnly = true },
        new Entry { action = "Move", part = "right", ja = "右へ移動", en = "MOVE RIGHT",  descJa = "右へ動きます。",   descEn = "MOVE RIGHT.",    keyboardOnly = true },
        new Entry { action = "Jump",     ja = "ジャンプ",   en = "JUMP",     descJa = "ジャンプします。",                     descEn = "JUMP." },
        new Entry { action = "Sprint",   ja = "ダッシュ",   en = "SPRINT",   descJa = "押している間、走ります（スタミナを使います）。", descEn = "HOLD TO RUN. USES STAMINA." },
        new Entry { action = "Crouch",   ja = "しゃがむ",   en = "CROUCH",   descJa = "押している間、しゃがみます。",           descEn = "HOLD TO CROUCH." },
        new Entry { action = "Interact", ja = "拾う・調べる", en = "INTERACT", descJa = "アイテムを拾う、レバーやスロットを使います（長押し）。", descEn = "PICK UP ITEMS AND USE LEVERS AND SLOTS. HOLD." },
        new Entry { action = "Use",      ja = "アイテムを使う", en = "USE ITEM", descJa = "選んでいるアイテムを使います。",       descEn = "USE THE SELECTED ITEM." },
        new Entry { action = "Drop",     ja = "投げる・捨てる", en = "THROW",    descJa = "選んでいるアイテムを投げます。",       descEn = "THROW THE SELECTED ITEM." },
        new Entry { action = "SelectSlot1", ja = "スロット 1", en = "SLOT 1", descJa = "インベントリの1番を選びます。", descEn = "SELECT INVENTORY SLOT 1." },
        new Entry { action = "SelectSlot2", ja = "スロット 2", en = "SLOT 2", descJa = "インベントリの2番を選びます。", descEn = "SELECT INVENTORY SLOT 2." },
        new Entry { action = "SelectSlot3", ja = "スロット 3", en = "SLOT 3", descJa = "インベントリの3番を選びます。", descEn = "SELECT INVENTORY SLOT 3." },
        new Entry { action = "SlotPrev", ja = "前のスロット", en = "PREVIOUS SLOT", descJa = "ひとつ前のスロットへ切り替えます（マウスホイールでも切り替えられます）。", descEn = "SWITCH TO THE PREVIOUS SLOT. THE MOUSE WHEEL ALSO WORKS." },
        new Entry { action = "SlotNext", ja = "次のスロット", en = "NEXT SLOT",     descJa = "ひとつ次のスロットへ切り替えます（マウスホイールでも切り替えられます）。", descEn = "SWITCH TO THE NEXT SLOT. THE MOUSE WHEEL ALSO WORKS." },
    };

    private static InputActionAsset asset;
    private static InputActionRebindingExtensions.RebindingOperation currentOperation;
    private static int lastEndFrame = -10;

    /// <summary>割り当て直しの入力待ち中、または終わった直後のフレーム。この間は Esc などの操作（メニューを閉じる等）を受け付けない</summary>
    public static bool Busy => currentOperation != null || Time.frameCount <= lastEndFrame + 1;

    public static InputActionAsset Asset
    {
        get
        {
            if (asset == null) asset = Resources.Load<InputActionAsset>("PlayerInputActions");
            return asset;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        asset = null;
        currentOperation = null;
        lastEndFrame = -10;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LoadSaved()
    {
        InputActionAsset a = Asset;
        if (a == null) return;

        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { a.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning("[KeyBindings] 保存された割り当てを読み込めませんでした: " + e.Message); }
        }
    }

    private static void Save()
    {
        if (Asset == null) return;
        PlayerPrefs.SetString(PrefsKey, Asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    // ───────── 割り当ての検索・表示 ─────────

    private static string GroupOf(Scheme scheme)
    {
        return scheme == Scheme.KeyboardMouse ? "Keyboard&Mouse" : "Gamepad";
    }

    private static bool HasGroup(string groups, string group)
    {
        if (string.IsNullOrEmpty(groups)) return false;
        foreach (string g in groups.Split(';'))
            if (g == group) return true;
        return false;
    }

    /// <summary>その操作の、指定した方式（キーボード/コントローラー）の最初の割り当ての番号。無ければ -1</summary>
    public static int FindBindingIndex(Entry e, Scheme scheme, out InputAction action)
    {
        action = Asset != null ? Asset.FindAction(MapName + "/" + e.action, false) : null;
        if (action == null) return -1;

        string group = GroupOf(scheme);
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding b = action.bindings[i];
            if (b.isComposite) continue;
            if (e.part != null)
            {
                if (!b.isPartOfComposite || b.name != e.part) continue;
            }
            else if (b.isPartOfComposite) continue;

            if (HasGroup(b.groups, group)) return i;
        }
        return -1;
    }

    /// <summary>画面に出す、今の割り当て（"W" や "Button West"）。未設定なら空文字</summary>
    public static string GetDisplay(Entry e, Scheme scheme)
    {
        int index = FindBindingIndex(e, scheme, out InputAction action);
        if (index < 0) return "";
        string path = action.bindings[index].effectivePath;
        if (string.IsNullOrEmpty(path)) return "";
        return LocalizedControlName(path);
    }

    // 表示名の翻訳（日本語, 英語）。ここに無いものは、Input System の標準の名前をそのまま出す
    private static readonly Dictionary<string, string[]> ControlNames = new Dictionary<string, string[]>
    {
        { "<Keyboard>/space", new[] { "スペース", "Space" } },
        { "<Keyboard>/leftShift", new[] { "左 Shift", "Left Shift" } },
        { "<Keyboard>/rightShift", new[] { "右 Shift", "Right Shift" } },
        { "<Keyboard>/leftCtrl", new[] { "左 Ctrl", "Left Ctrl" } },
        { "<Keyboard>/rightCtrl", new[] { "右 Ctrl", "Right Ctrl" } },
        { "<Keyboard>/leftAlt", new[] { "左 Alt", "Left Alt" } },
        { "<Keyboard>/rightAlt", new[] { "右 Alt", "Right Alt" } },
        { "<Keyboard>/enter", new[] { "Enter", "Enter" } },
        { "<Keyboard>/tab", new[] { "Tab", "Tab" } },
        { "<Keyboard>/capsLock", new[] { "Caps Lock", "Caps Lock" } },
        { "<Keyboard>/upArrow", new[] { "↑ キー", "Up Arrow" } },
        { "<Keyboard>/downArrow", new[] { "↓ キー", "Down Arrow" } },
        { "<Keyboard>/leftArrow", new[] { "← キー", "Left Arrow" } },
        { "<Keyboard>/rightArrow", new[] { "→ キー", "Right Arrow" } },
        { "<Mouse>/leftButton", new[] { "左クリック", "Left Click" } },
        { "<Mouse>/rightButton", new[] { "右クリック", "Right Click" } },
        { "<Mouse>/middleButton", new[] { "ホイールクリック", "Wheel Click" } },
        { "<Mouse>/backButton", new[] { "マウス戻るボタン", "Mouse Back" } },
        { "<Mouse>/forwardButton", new[] { "マウス進むボタン", "Mouse Forward" } },
        { "<Gamepad>/buttonSouth", new[] { "A ボタン", "A Button" } },
        { "<Gamepad>/buttonEast", new[] { "B ボタン", "B Button" } },
        { "<Gamepad>/buttonWest", new[] { "X ボタン", "X Button" } },
        { "<Gamepad>/buttonNorth", new[] { "Y ボタン", "Y Button" } },
        { "<Gamepad>/leftShoulder", new[] { "LB", "LB" } },
        { "<Gamepad>/rightShoulder", new[] { "RB", "RB" } },
        { "<Gamepad>/leftTrigger", new[] { "LT", "LT" } },
        { "<Gamepad>/rightTrigger", new[] { "RT", "RT" } },
        { "<Gamepad>/leftStickPress", new[] { "左スティック押し込み", "Left Stick Click" } },
        { "<Gamepad>/rightStickPress", new[] { "右スティック押し込み", "Right Stick Click" } },
        { "<Gamepad>/dpad/up", new[] { "十字キー 上", "D-Pad Up" } },
        { "<Gamepad>/dpad/down", new[] { "十字キー 下", "D-Pad Down" } },
        { "<Gamepad>/dpad/left", new[] { "十字キー 左", "D-Pad Left" } },
        { "<Gamepad>/dpad/right", new[] { "十字キー 右", "D-Pad Right" } },
        { "<Gamepad>/select", new[] { "セレクト", "Select" } },
        { "<Gamepad>/start", new[] { "スタート", "Start" } },
    };

    /// <summary>キー・ボタンの表示名。日本語 / 英語の言語設定に合わせる</summary>
    public static string LocalizedControlName(string path)
    {
        if (ControlNames.TryGetValue(path, out string[] names)) return Localization.IsEnglish ? names[1] : names[0];
        return InputControlPath.ToHumanReadableString(path,
            InputControlPath.HumanReadableStringOptions.OmitDevice | InputControlPath.HumanReadableStringOptions.UseShortNames);
    }

    public static bool IsEntryAvailable(Entry e, Scheme scheme)
    {
        if (scheme == Scheme.Gamepad && e.keyboardOnly) return false;
        return FindBindingIndex(e, scheme, out _) >= 0;
    }

    // ───────── 割り当て直し ─────────

    /// <summary>次に押したボタンを、この操作に割り当てる。終わったら（取り消しでも）onDone を呼ぶ</summary>
    public static void StartRebind(Entry e, Scheme scheme, Action onDone)
    {
        if (currentOperation != null) return;

        int index = FindBindingIndex(e, scheme, out InputAction action);
        if (index < 0) { onDone?.Invoke(); return; }

        string oldPath = action.bindings[index].effectivePath;

        // 操作中は、その操作の入力を止めておく（リバインドの条件）。終わったら元に戻す
        InputActionMap map = action.actionMap;
        bool mapWasEnabled = map != null && map.enabled;
        if (mapWasEnabled) map.Disable();

        InputActionRebindingExtensions.RebindingOperation op = action.PerformInteractiveRebinding(index)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithControlsExcluding("<Pointer>/position")
            .WithControlsExcluding("<Pointer>/delta")
            .WithControlsExcluding("<Gamepad>/leftStick")
            .WithControlsExcluding("<Gamepad>/rightStick")
            .WithControlsExcluding("<Gamepad>/start")
            .OnMatchWaitForAnother(0.1f);

        if (scheme == Scheme.KeyboardMouse)
        {
            op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
            op.WithCancelingThrough("<Keyboard>/escape");
        }
        else
        {
            // コントローラーの設定中も、キーボードの Backspace だけは「解除」に使える
            op.WithControlsHavingToMatchPath("<Gamepad>").WithControlsHavingToMatchPath("<Keyboard>/backspace");
            op.WithCancelingThrough("<Gamepad>/start");
        }

        // Backspace を押したら、この操作の割り当てを解除する（未設定にする）
        bool cleared = false;
        op.OnPotentialMatch(o =>
        {
            foreach (InputControl c in o.candidates)
            {
                if (c.path != "/Keyboard/backspace") continue;
                cleared = true;
                action.ApplyBindingOverride(index, "");
                o.Cancel();
                return;
            }
        });

        op.OnComplete(o =>
        {
            string newPath = action.bindings[index].effectivePath;
            SwapIfAlreadyUsed(e, scheme, index, action, oldPath, newPath);
            Finish(o, map, mapWasEnabled, true, onDone);
        });
        op.OnCancel(o => Finish(o, map, mapWasEnabled, cleared, onDone));

        currentOperation = op;
        op.Start();
    }

    private static void Finish(InputActionRebindingExtensions.RebindingOperation op, InputActionMap map, bool reenable, bool changed, Action onDone)
    {
        op.Dispose();
        currentOperation = null;
        lastEndFrame = Time.frameCount;
        if (reenable && map != null) map.Enable();
        if (changed) Save();
        onDone?.Invoke();
    }

    /// <summary>選んだキーをすでに別の操作が使っていたら、その操作に、こちらの元のキーを渡す（入れ替え）</summary>
    private static void SwapIfAlreadyUsed(Entry changed, Scheme scheme, int changedIndex, InputAction changedAction, string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(newPath)) return;

        foreach (Entry other in Entries)
        {
            if (other == changed) continue;
            int i = FindBindingIndex(other, scheme, out InputAction a);
            if (i < 0) continue;
            if (a == changedAction && i == changedIndex) continue;

            if (a.bindings[i].effectivePath == newPath)
            {
                a.ApplyBindingOverride(i, oldPath ?? "");
            }
        }
    }

    // ───────── 初期状態に戻す ─────────

    public static void ResetScheme(Scheme scheme)
    {
        if (Asset == null) return;

        foreach (Entry e in Entries)
        {
            int i = FindBindingIndex(e, scheme, out InputAction a);
            if (i >= 0) a.RemoveBindingOverride(i);
        }
        Save();
    }
}
