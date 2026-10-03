using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum SettingsFocus { Types, Items, Choice }

    // One editable setting: a label, a description and a fixed list of choices mapped onto a field.
    public sealed class SettingItem
    {
        public string Label { get; }
        public string Description { get; }
        public IReadOnlyList<string> Choices { get; }
        readonly Func<GameSettings, int> get;
        readonly Action<GameSettings, int> set;

        public SettingItem(string label, string description, IReadOnlyList<string> choices,
            Func<GameSettings, int> get, Action<GameSettings, int> set)
        {
            Label = label; Description = description; Choices = choices;
            this.get = get; this.set = set;
        }

        public int Get(GameSettings settings) => get(settings);
        public void Set(GameSettings settings, int choice) => set(settings, choice);

        // settings_template's bool rows: "Enabled" for true, "Disabled" for false.
        public static SettingItem Toggle(string label, string description, Func<GameSettings, bool> get, Action<GameSettings, bool> set)
            => new SettingItem(label, description, new[] { "Enabled", "Disabled" },
                s => get(s) ? 0 : 1, (s, choice) => set(s, choice == 0));
    }

    public sealed class SettingType
    {
        public string Label { get; }
        public IReadOnlyList<SettingItem> Items { get; }
        public SettingType(string label, IReadOnlyList<SettingItem> items) { Label = label; Items = items; }
    }

    // The settings menu's navigation, drum-key first: the focus starts on the types (left list),
    // ka moves through them and don confirms, moving the focus to that type's items (right list);
    // ka moves through the items and don opens the item's choices; ka moves through the choices
    // and don applies one, returning the focus to the items. Both lists end with a Return entry:
    // the types' Return leaves the menu, the items' Return goes back to the types. Lists wrap.
    public sealed class SettingsMenu
    {
        public enum Result { None, Moved, Entered, Returned, Changed, Exit }

        public IReadOnlyList<SettingType> Types { get; }
        public GameSettings Settings { get; private set; }
        public SettingsFocus Focus { get; private set; } = SettingsFocus.Types;
        // An index equal to the list's count is its Return entry.
        public int TypeIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public int ChoiceIndex { get; private set; }

        public SettingsMenu(IReadOnlyList<SettingType> types, GameSettings settings)
        {
            Types = types;
            Settings = settings?.Clone() ?? new GameSettings();
        }

        public static IReadOnlyList<SettingType> Catalog() => new[]
        {
            new SettingType("Play", new[]
            {
                SettingItem.Toggle("Enable Drumpad for Single Player Mode",
                    "Show the touch drum in single player mode and let touches and clicks hit it.",
                    s => s.play.singlePlayerDrumPad, (s, on) => s.play.singlePlayerDrumPad = on),
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
            if (delta == 0) return Result.None;
            switch (Focus)
            {
                case SettingsFocus.Types: TypeIndex = Wrap(TypeIndex + delta, TypeCount); ItemIndex = 0; break;
                case SettingsFocus.Items: ItemIndex = Wrap(ItemIndex + delta, ItemCount); break;
                default: ChoiceIndex = Wrap(ChoiceIndex + delta, CurrentItem.Choices.Count); break;
            }
            return Result.Moved;
        }

        // Drum don: confirm the focused entry.
        public Result Don()
        {
            switch (Focus)
            {
                case SettingsFocus.Types:
                    if (IsTypeReturn) return Result.Exit;
                    Focus = SettingsFocus.Items;
                    ItemIndex = 0;
                    return Result.Entered;
                case SettingsFocus.Items:
                    if (IsItemReturn) { Focus = SettingsFocus.Types; return Result.Returned; }
                    Focus = SettingsFocus.Choice;
                    ChoiceIndex = CurrentItem.Get(Settings);
                    return Result.Entered;
                default:
                    var settings = Settings.Clone();
                    CurrentItem.Set(settings, ChoiceIndex);
                    Settings = settings;
                    Focus = SettingsFocus.Items;
                    return Result.Changed;
            }
        }

        // Back (Esc): one level out without changing anything; from the types it leaves the menu.
        public Result Back()
        {
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
            if (CurrentType == null || index < 0 || index >= ItemCount) return Result.None;
            if (Focus == SettingsFocus.Items && index == ItemIndex) return Don();
            Focus = SettingsFocus.Items;
            ItemIndex = index;
            return Result.Moved;
        }

        // Touch: a tap on a choice of the current item applies it at once.
        public Result TapChoice(int index)
        {
            if (CurrentItem == null || index < 0 || index >= CurrentItem.Choices.Count) return Result.None;
            Focus = SettingsFocus.Choice;
            ChoiceIndex = index;
            return Don();
        }

        // Touch: a swipe over a list moves the focus into that list and then through it.
        public Result SwipeTypes(int delta)
        {
            Focus = SettingsFocus.Types;
            return Ka(delta);
        }

        public Result SwipeItems(int delta)
        {
            if (CurrentType == null) return Result.None;
            Focus = SettingsFocus.Items;
            return Ka(delta);
        }

        static int Wrap(int value, int count) => ((value % count) + count) % count;
    }
}
