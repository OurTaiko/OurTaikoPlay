using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    public enum SettingsFocus { Types, Items, Choice }

    // An editable field: named choices, an integer adjusted by a configured step, or typed text.
    // An action is a button instead: confirming it runs it, and its value column shows Status.
    public sealed class SettingItem
    {
        public string Label { get; }
        public string Description { get; }
        public IReadOnlyList<string> Choices { get; }
        public InputKey? BindingKey { get; private set; }
        public bool IsNumber { get; private set; }
        public int DefaultValue { get; private set; }
        public int Step { get; private set; }
        public string Unit { get; private set; }
        public bool IsText { get; private set; }
        // Text shown masked (a password).
        public bool IsSecret { get; private set; }
        // Which action the settings scene runs for this row; null for a field.
        public string ActionId { get; private set; }
        public bool IsAction => ActionId != null;
        public string Status { get; set; } = "";
        Func<GameSettings, string> getText;
        Action<GameSettings, string> setText;
        readonly Func<GameSettings, int> get;
        readonly Action<GameSettings, int> set;
        int minimum = int.MinValue, maximum = int.MaxValue;
        Func<int, string> formatNumber;

        public SettingItem(string label, string description, IReadOnlyList<string> choices,
            Func<GameSettings, int> get, Action<GameSettings, int> set)
        {
            Label = label; Description = description; Choices = choices;
            this.get = get; this.set = set;
        }

        public static SettingItem Keyboard(InputKey key) => new SettingItem("Keyboard: " + KeyboardBindings.Label(key),
            "Keys move from their old action. Mouse/touch works even when unbound.",
            Array.Empty<string>(), _ => 0, (_, __) => { }) { BindingKey = key };

        public string Format(GameSettings settings) => BindingKey.HasValue ? settings.general.keyboard.Format(BindingKey.Value)
            : IsText ? FormatText(GetText(settings)) : IsAction ? Status : Format(Get(settings));

        string FormatText(string text) => text.Length == 0 ? "Not set" : IsSecret ? new string('*', Math.Min(text.Length, 12)) : text;
        public string GetText(GameSettings settings) => getText(settings) ?? "";
        public void SetText(GameSettings settings, string text) => setText(settings, text ?? "");

        public string NumberHint => $"Default: {Format(DefaultValue)} / Step: {StepDisplay}"
            + (formatNumber == null ? " / Ka: adjust / Don or tap: save" : "");
        public string StepDisplay => formatNumber?.Invoke(Step) ?? $"{Step} {Unit}";
        public int Get(GameSettings settings) => get(settings);
        public void Set(GameSettings settings, int choice) => set(settings, choice);
        public string Format(int value) => IsNumber ? formatNumber?.Invoke(value) ?? $"{value:+0;-0;0} {Unit}" : Choices[value];
        public int Move(int value, int delta) => IsNumber
            ? (int)Math.Max(minimum, Math.Min(maximum, (long)value + (long)delta * Step))
            : ((value + delta) % Choices.Count + Choices.Count) % Choices.Count;

        public static SettingItem Number(string label, string description, int defaultValue, int step, string unit,
            Func<GameSettings, int> get, Action<GameSettings, int> set,
            int minimum = int.MinValue, int maximum = int.MaxValue, Func<int, string> format = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            return new SettingItem(label, description, Array.Empty<string>(), get, set)
                { IsNumber = true, DefaultValue = defaultValue, Step = step, Unit = unit,
                    minimum = minimum, maximum = maximum, formatNumber = format };
        }

        public static SettingItem Text(string label, string description, Func<GameSettings, string> get, Action<GameSettings, string> set, bool secret = false)
            => new SettingItem(label, description, Array.Empty<string>(), _ => 0, (_, __) => { })
                { IsText = true, IsSecret = secret, getText = get, setText = set };

        public static SettingItem Action(string label, string description, string id)
            => new SettingItem(label, description, Array.Empty<string>(), _ => 0, (_, __) => { }) { ActionId = id };

        // settings_template's bool rows: "Enabled" for true, "Disabled" for false.
        public static SettingItem Toggle(string label, string description, Func<GameSettings, bool> get, Action<GameSettings, bool> set)
            => new SettingItem(label, description, new[] { "Enabled", "Disabled" },
                s => get(s) ? 0 : 1, (s, choice) => set(s, choice == 0));
    }

    public sealed class SettingType
    {
        public string Label { get; }
        public IReadOnlyList<SettingItem> Items { get; private set; }
        readonly Func<GameSettings, IReadOnlyList<SettingItem>> buildItems;
        public SettingType(string label, IReadOnlyList<SettingItem> items) { Label = label; Items = items; }
        public SettingType(string label, Func<GameSettings, IReadOnlyList<SettingItem>> buildItems)
        { Label = label; this.buildItems = buildItems; Items = Array.Empty<SettingItem>(); }
        internal SettingType ForSettings(GameSettings settings)
        {
            var copy = buildItems == null ? new SettingType(Label, Items) : new SettingType(Label, buildItems);
            copy.Refresh(settings);
            return copy;
        }
        internal void Refresh(GameSettings settings) { if (buildItems != null) Items = buildItems(settings); }
    }

    // The settings menu's navigation, drum-key first: the focus starts on the types (left list),
    // ka moves through them and don confirms, moving the focus to that type's items (right list);
    // ka moves through the items and don opens the item's choices; ka moves through the choices
    // and don applies one, returning the focus to the items. Both lists end with a Return entry:
    // the types' Return leaves the menu, the items' Return goes back to the types. Lists wrap.
    // A text item has no choices: don opens it for typing and SubmitText applies what was typed.
    // An action item opens nothing: don reports Action and the scene runs it.
    public sealed class SettingsMenu
    {
        public enum Result { None, Moved, Entered, Returned, Changed, Exit, Action }

        public IReadOnlyList<SettingType> Types { get; }
        public GameSettings Settings { get; private set; }
        public SettingsFocus Focus { get; private set; } = SettingsFocus.Types;
        // An index equal to the list's count is its Return entry.
        public int TypeIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public int ChoiceIndex { get; private set; }
        public bool CapturingKey { get; private set; }
        public bool EditingText => Focus == SettingsFocus.Choice && CurrentItem?.IsText == true;
        public IReadOnlyList<string> Choices => CurrentItem?.BindingKey is InputKey key
            ? new[] { "Add Key", "Clear All", "Defaults" }.Concat(Settings.general.keyboard.Get(key).Select(k => "Remove " + k)).ToArray()
            : CurrentItem?.Choices ?? Array.Empty<string>();

        public Result CaptureKey(Key key)
        {
            if (!CapturingKey || KeyboardBindings.Clean(new[] { key }).Length == 0) return Result.None;
            var action = CurrentItem.BindingKey.Value;
            Settings.general.keyboard.Set(action, Settings.general.keyboard.Get(action).Concat(new[] { key }).ToArray());
            CapturingKey = false;
            ChoiceIndex = 0;
            return Result.Changed;
        }

        public Result SubmitText(string text)
        {
            if (!EditingText) return Result.None;
            var settings = Settings.Clone();
            CurrentItem.SetText(settings, text);
            Settings = settings;
            Focus = SettingsFocus.Items;
            return Result.Changed;
        }

        Result EditBinding()
        {
            var key = CurrentItem.BindingKey.Value;
            var keyboard = Settings.general.keyboard;
            if (ChoiceIndex == 0) { CapturingKey = true; return Result.Entered; }
            if (ChoiceIndex == 1) keyboard.Set(key);
            else if (ChoiceIndex == 2) keyboard.Set(key, KeyboardBindings.DefaultKeys(key));
            else keyboard.Set(key, keyboard.Get(key).Where((_, i) => i != ChoiceIndex - 3).ToArray());
            ChoiceIndex = 0;
            return Result.Changed;
        }

        public SettingsMenu(IReadOnlyList<SettingType> types, GameSettings settings)
        {
            Settings = settings?.Clone() ?? new GameSettings();
            Types = types.Select(t => t.ForSettings(Settings)).ToArray();
        }

        public void RefreshItems()
        {
            foreach (var type in Types) type.Refresh(Settings);
            ItemIndex = Math.Min(ItemIndex, ItemCount - 1);
        }

        public const string RefreshSongListAction = "refresh-song-list", RefreshLoginAction = "refresh-login";

        public static IReadOnlyList<SettingType> Catalog() => new[]
        {
            new SettingType("General", new[]
            {
                new SettingItem("Language",
                    "Choose the language for song titles and subtitles. Missing translations use Japanese, then the base title. Menus and skin artwork are unchanged.",
                    GeneralSettings.LanguageNames,
                    s => Array.IndexOf(GeneralSettings.Languages, s.general.Language),
                    (s, choice) => s.general.language = GeneralSettings.Languages[choice]),
            }.Concat(Enum.GetValues(typeof(InputKey)).Cast<InputKey>().Select(SettingItem.Keyboard)).ToArray()),
            new SettingType("Play", new[]
            {
                SettingItem.Toggle("Enable Drumpad for Single Player Mode",
                    "Show the touch drum in single player mode and let touches and clicks hit it.",
                    s => s.play.singlePlayerDrumPad, (s, on) => s.play.singlePlayerDrumPad = on),
                SettingItem.Number("Offset A (Audio)",
                    "Move the chart relative to music. Negative: earlier; positive: later.",
                    PlaySettings.OffsetDefaultMs, PlaySettings.OffsetStepMs, "ms",
                    s => s.play.audioOffsetMs, (s, value) => s.play.audioOffsetMs = value),
                SettingItem.Number("Offset B (Judgment)",
                    "Move the judgment window only. Negative: earlier; positive: later.",
                    PlaySettings.OffsetDefaultMs, PlaySettings.OffsetStepMs, "ms",
                    s => s.play.judgeOffsetMs, (s, value) => s.play.judgeOffsetMs = value),
                SettingItem.Number("Input Delay",
                    "Delay drum hits before judgment, sound and effects, like the arcade. 0: off.",
                    PlaySettings.InputDelayDefaultMs, PlaySettings.OffsetStepMs, "ms",
                    s => s.play.inputDelayMs, (s, value) => s.play.inputDelayMs = value,
                    0, PlaySettings.InputDelayMaxMs),
            }),
            new SettingType("Display", new[]
            {
                new SettingItem("Target Frame Rate",
                    "The highest frame rate the game renders at. Unlimited renders as fast as the device can.",
                    new[] { "120 FPS", "60 FPS", "Unlimited" },
                    s => Math.Max(0, Array.IndexOf(DisplaySettings.FrameRates, s.display.targetFrameRate)),
                    (s, choice) => s.display.targetFrameRate = DisplaySettings.FrameRates[choice]),
                SettingItem.Toggle("VSync",
                    "Wait for the display's refresh before each frame to prevent tearing. While enabled, the refresh rate replaces Target Frame Rate.",
                    s => s.display.vSync, (s, on) => s.display.vSync = on),
            }),
            new SettingType("Sound", s => SoundSettings.Catalog(SoundSettings.Platform, s.audio.Backend)),
            // The 2P account is saved in the file but not listed until two players can play online.
            new SettingType("OurTaiko Accounts", new[]
            {
                SettingItem.Text("1P Username",
                    "1P's OurTaiko account for the built-in servers. Empty: guest, scores are not saved.",
                    s => s.accounts.p1Username, (s, text) => s.accounts.p1Username = text.Trim()),
                SettingItem.Text("1P Password",
                    "The password of 1P's OurTaiko account, saved in the settings file on this device.",
                    s => s.accounts.p1Password, (s, text) => s.accounts.p1Password = text, secret: true),
                SettingItem.Action("Refresh Song List",
                    "Load the built-in servers' song lists again.", RefreshSongListAction),
                SettingItem.Action("Refresh Login Status",
                    "Sign in to the built-in servers again with the account above.", RefreshLoginAction),
            }),
        };

        public int TypeCount => Types.Count + 1;
        public bool IsTypeReturn => TypeIndex == Types.Count;
        public SettingType CurrentType => IsTypeReturn ? null : Types[TypeIndex];
        public int ItemCount => (CurrentType?.Items.Count ?? 0) + 1;
        public bool IsItemReturn => CurrentType == null || ItemIndex == CurrentType.Items.Count;
        public SettingItem CurrentItem => IsItemReturn ? null : CurrentType.Items[ItemIndex];

        // Drum ka: left ka = -1 (up), right ka = +1 (down) within the focused list.
        public Result Ka(int delta)
        {
            if (CapturingKey || EditingText || delta == 0) return Result.None;
            switch (Focus)
            {
                case SettingsFocus.Types: TypeIndex = Wrap(TypeIndex + delta, TypeCount); ItemIndex = 0; break;
                case SettingsFocus.Items: ItemIndex = Wrap(ItemIndex + delta, ItemCount); break;
                default: ChoiceIndex = CurrentItem.BindingKey.HasValue ? Wrap(ChoiceIndex + delta, Choices.Count) : CurrentItem.Move(ChoiceIndex, delta); break;
            }
            return Result.Moved;
        }

        // Drum don: confirm the focused entry.
        public Result Don()
        {
            if (CapturingKey || EditingText) return Result.None;
            switch (Focus)
            {
                case SettingsFocus.Types:
                    if (IsTypeReturn) return Result.Exit;
                    Focus = SettingsFocus.Items;
                    ItemIndex = 0;
                    return Result.Entered;
                case SettingsFocus.Items:
                    if (IsItemReturn) { Focus = SettingsFocus.Types; return Result.Returned; }
                    if (CurrentItem.IsAction) return Result.Action;
                    Focus = SettingsFocus.Choice;
                    ChoiceIndex = CurrentItem.Get(Settings);
                    return Result.Entered;
                default:
                    if (CurrentItem.BindingKey.HasValue) return EditBinding();
                    var settings = Settings.Clone();
                    CurrentItem.Set(settings, ChoiceIndex);
                    Settings = settings;
                    RefreshItems();
                    Focus = SettingsFocus.Items;
                    return Result.Changed;
            }
        }

        // Back (Esc): one level out without changing anything; from the types it leaves the menu.
        public Result Back()
        {
            if (CapturingKey) { CapturingKey = false; return Result.Returned; }
            switch (Focus)
            {
                case SettingsFocus.Choice: Focus = SettingsFocus.Items; return Result.Returned;
                case SettingsFocus.Items: Focus = SettingsFocus.Types; return Result.Returned;
                default: return Result.Exit;
            }
        }

        // Touch: a tap on a type row. Tapping the focused type confirms it; another row is selected,
        // pulling the focus back to the types from wherever it was.
        public Result TapType(int index)
        {
            if (CapturingKey) return Result.None;
            if (index < 0 || index >= TypeCount) return Result.None;
            if (Focus == SettingsFocus.Types && index == TypeIndex) return Don();
            bool moved = index != TypeIndex;
            Focus = SettingsFocus.Types;
            TypeIndex = index;
            if (moved) ItemIndex = 0;
            return Result.Moved;
        }

        // Touch: a tap on an item row of the current type. Tapping the focused item confirms it.
        public Result TapItem(int index)
        {
            if (CapturingKey) return Result.None;
            if (CurrentType == null || index < 0 || index >= ItemCount) return Result.None;
            if (Focus == SettingsFocus.Items && index == ItemIndex) return Don();
            Focus = SettingsFocus.Items;
            ItemIndex = index;
            return Result.Moved;
        }

        // Touch: a tap on a choice of the current item applies it at once.
        public Result TapChoice(int index)
        {
            if (CapturingKey) return Result.None;
            if (Focus == SettingsFocus.Choice && CurrentItem?.IsNumber == true)
                return index == 0 ? Don() : Result.None;
            if (CurrentItem == null || index < 0 || index >= Choices.Count) return Result.None;
            Focus = SettingsFocus.Choice;
            ChoiceIndex = index;
            return Don();
        }

        // Touch: a swipe over a list moves the focus into that list and then through it.
        public Result SwipeTypes(int delta)
        {
            if (CapturingKey) return Result.None;
            Focus = SettingsFocus.Types;
            return Ka(delta);
        }

        public Result SwipeItems(int delta)
        {
            if (CapturingKey) return Result.None;
            if (CurrentType == null) return Result.None;
            Focus = SettingsFocus.Items;
            return Ka(delta);
        }

        static int Wrap(int value, int count) => ((value % count) + count) % count;
    }
}
