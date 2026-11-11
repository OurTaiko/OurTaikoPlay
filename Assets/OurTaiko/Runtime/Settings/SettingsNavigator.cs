using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // Where the settings overlay's focus is: the category rail, the page's rows, a row being
    // adjusted in place, the side sheet, a text row being typed or a key being captured.
    public enum SettingsZone { Rail, Rows, Adjust, Sheet, Typing, Capture }

    // The settings overlay's navigation, drum first. Ka (or up/down) moves within the focused list
    // and stops at either end (Bumped) instead of wrapping; a fresh press past the first or last row
    // returns to the rail, so the drum alone can leave a page (a held key's repeats only stop there).
    // Don (or Enter) confirms; Back (Esc) steps out one level. On the rail don opens the page; on a row it flips a toggle, starts adjusting a
    // choice or number (ka then changes it, don keeps it, Back restores it), opens the sheet of a
    // list or key row, runs an action or starts typing. Left/right only change a value: they adjust
    // a choice, number or slider row and flip a toggle (right on, left off); anywhere else they do
    // nothing. The rail ends with Close.
    public sealed class SettingsNavigator
    {
        public enum Result { None, Moved, Bumped, Entered, Returned, Changed, Action, Close }

        public IReadOnlyList<SettingPage> Pages { get; }
        public GameSettings Settings { get; private set; }
        public SettingsZone Zone { get; private set; } = SettingsZone.Rail;
        // Pages.Count is the rail's Close entry.
        public int PageIndex { get; private set; }
        public int RowIndex { get; private set; }
        // The value shown while a row is adjusted, applied by don.
        public int AdjustValue { get; private set; }
        public int SheetIndex { get; private set; }
        // The action the overlay should run after Result.Action, and the typed text for a password.
        public string PendingAction { get; private set; }
        public string PendingText { get; private set; }

        public bool OnClose => PageIndex == Pages.Count;
        public SettingPage Page => OnClose ? null : Pages[PageIndex];
        public SettingRow Row => Page != null && RowIndex < Page.Rows.Count ? Page.Rows[RowIndex] : null;

        public SettingsNavigator(IReadOnlyList<SettingPage> pages, GameSettings settings)
        {
            Settings = settings?.Clone() ?? new GameSettings();
            Pages = pages;
            foreach (var page in Pages) page.Refresh(Settings);
        }

        // The sheet's entries: a list row's options, or a key row's binding actions.
        public IReadOnlyList<string> SheetOptions => Row?.Kind == SettingKind.Keys
            ? new[] { SettingsCatalog.T("keys.add") }.Concat(Settings.general.keyboard.Get(Row.Key).Select(k => SettingsCatalog.T("keys.remove", k)))
                .Concat(new[] { SettingsCatalog.T("keys.reset"), SettingsCatalog.T("keys.clear") }).ToArray()
            : Row?.Options ?? Array.Empty<string>();
        public IReadOnlyList<string> SheetNotes => Row?.Kind == SettingKind.Keys ? Array.Empty<string>() : Row?.OptionNotes ?? Array.Empty<string>();

        // Ka or up/down; repeat is a held key's repeat rather than a new press.
        public Result Move(int delta, bool repeat = false)
        {
            if (delta == 0) return Result.None;
            switch (Zone)
            {
                case SettingsZone.Rail:
                    return Step(PageIndex, delta, Pages.Count + 1, i => { PageIndex = i; RowIndex = 0; });
                case SettingsZone.Rows:
                    var moved = Step(RowIndex, delta, Page.Rows.Count, i => RowIndex = i);
                    if (moved != Result.Bumped || repeat) return moved;
                    Zone = SettingsZone.Rail;
                    return Result.Returned;
                case SettingsZone.Adjust:
                    int next = Row.Move(AdjustValue, delta);
                    if (next == AdjustValue) return Result.Bumped;
                    AdjustValue = next;
                    return Result.Moved;
                case SettingsZone.Sheet:
                    return Step(SheetIndex, delta, SheetOptions.Count, i => SheetIndex = i);
                default: return Result.None;
            }
        }

        static Result Step(int index, int delta, int count, Action<int> set)
        {
            int next = Math.Max(0, Math.Min(count - 1, index + delta));
            if (next == index) return Result.Bumped;
            set(next);
            return Result.Moved;
        }

        // Left/right arrows: a value change on the focused row and nothing else (no navigation).
        public Result Side(int delta)
        {
            if (delta == 0) return Result.None;
            switch (Zone)
            {
                case SettingsZone.Rows:
                    if (Row.IsAdjustable) return Change(Row.Move(Row.Get(Settings), delta));
                    if (Row.Kind == SettingKind.Toggle) return Change(delta > 0 ? 1 : 0);
                    return Result.None;
                case SettingsZone.Adjust:
                    return Move(delta);
                default: return Result.None;
            }
        }

        // Don or Enter.
        public Result Confirm()
        {
            switch (Zone)
            {
                case SettingsZone.Rail:
                    if (OnClose) return Result.Close;
                    if (Page.Rows.Count == 0) return Result.Bumped;
                    Zone = SettingsZone.Rows;
                    RowIndex = 0;
                    return Result.Entered;
                case SettingsZone.Rows:
                    return Open(Row);
                case SettingsZone.Adjust:
                    Zone = SettingsZone.Rows;
                    return Change(AdjustValue, Result.Returned);
                case SettingsZone.Sheet:
                    return Row.Kind == SettingKind.Keys ? EditKeys() : Pick(SheetIndex);
                default: return Result.None;
            }
        }

        Result Open(SettingRow row)
        {
            switch (row.Kind)
            {
                case SettingKind.Toggle: return Change(1 - row.Get(Settings));
                case SettingKind.Choice:
                case SettingKind.Number:
                case SettingKind.Slider:
                    Zone = SettingsZone.Adjust;
                    AdjustValue = row.Get(Settings);
                    return Result.Entered;
                case SettingKind.List:
                    Zone = SettingsZone.Sheet;
                    SheetIndex = row.Get(Settings);
                    return Result.Entered;
                case SettingKind.Keys:
                    Zone = SettingsZone.Sheet;
                    SheetIndex = 0;
                    return Result.Entered;
                case SettingKind.Action:
                    PendingAction = row.ActionId; PendingText = null;
                    return Result.Action;
                default:
                    Zone = SettingsZone.Typing;
                    return Result.Entered;
            }
        }

        Result Pick(int index)
        {
            Zone = SettingsZone.Rows;
            return Change(index, Result.Returned);
        }

        Result EditKeys()
        {
            var keyboard = Settings.general.keyboard;
            int bound = keyboard.Get(Row.Key).Count;
            if (SheetIndex == 0) { Zone = SettingsZone.Capture; return Result.Entered; }
            var settings = Settings.Clone();
            var key = Row.Key;
            if (SheetIndex <= bound) settings.general.keyboard.Set(key, keyboard.Get(key).Where((_, i) => i != SheetIndex - 1).ToArray());
            else if (SheetIndex == bound + 1) settings.general.keyboard.Set(key, KeyboardBindings.DefaultKeys(key));
            else settings.general.keyboard.Set(key);
            Settings = settings;
            Zone = SettingsZone.Rows;
            return Result.Changed;
        }

        // Back (Esc): one level out without changing anything; from the rail it closes.
        public Result Back()
        {
            switch (Zone)
            {
                case SettingsZone.Rail: return Result.Close;
                case SettingsZone.Rows: Zone = SettingsZone.Rail; return Result.Returned;
                case SettingsZone.Capture: Zone = SettingsZone.Sheet; return Result.Returned;
                default: Zone = SettingsZone.Rows; return Result.Returned;
            }
        }

        // Applies a new value to the current row; an unchanged value reports `unchanged`.
        Result Change(int value, Result unchanged = Result.Bumped)
        {
            var row = Row;
            value = row.Clamp(value);
            if (value == row.Get(Settings)) return unchanged;
            var settings = Settings.Clone();
            row.Set(settings, value);
            Settings = settings;
            Refresh();
            return Result.Changed;
        }

        // Rebuilds the pages after a change and keeps the focus on the same row where it still exists.
        public void Refresh()
        {
            string id = Row?.Id;
            foreach (var page in Pages) page.Refresh(Settings);
            if (Page == null) return;
            int index = id == null ? -1 : Page.Rows.ToList().FindIndex(r => r.Id == id);
            RowIndex = index >= 0 ? index : Math.Min(RowIndex, Math.Max(0, Page.Rows.Count - 1));
        }

        // A key pressed while capturing joins the current key row's binding.
        public Result CaptureKey(Key key)
        {
            if (Zone != SettingsZone.Capture || KeyboardBindings.Clean(new[] { key }).Length == 0) return Result.None;
            var settings = Settings.Clone();
            settings.general.keyboard.Set(Row.Key, Settings.general.keyboard.Get(Row.Key).Concat(new[] { key }).ToArray());
            Settings = settings;
            Zone = SettingsZone.Rows;
            return Result.Changed;
        }

        // The typed text of a text row is kept; a password row's goes to its action instead.
        public Result SubmitText(string text)
        {
            if (Zone != SettingsZone.Typing) return Result.None;
            Zone = SettingsZone.Rows;
            if (Row.Kind == SettingKind.Password)
            {
                PendingAction = Row.ActionId; PendingText = text ?? "";
                return Result.Action;
            }
            var settings = Settings.Clone();
            Row.SetText(settings, text);
            Settings = settings;
            return Result.Changed;
        }

        // Touch: a category tap selects it and leaves the focus on the rail; the focused one opens.
        public Result TapPage(int index)
        {
            if (Zone == SettingsZone.Typing || Zone == SettingsZone.Capture || index < 0 || index > Pages.Count) return Result.None;
            if (Zone == SettingsZone.Rail && index == PageIndex) return Confirm();
            bool moved = index != PageIndex;
            Zone = SettingsZone.Rail;
            PageIndex = index;
            if (moved) RowIndex = 0;
            return Result.Moved;
        }

        // Touch: a row tap focuses it; tapping the focused row confirms it.
        public Result TapRow(int index)
        {
            if (Zone == SettingsZone.Typing || Zone == SettingsZone.Capture || Page == null || index < 0 || index >= Page.Rows.Count) return Result.None;
            if (Zone == SettingsZone.Rows && index == RowIndex) return Confirm();
            Zone = SettingsZone.Rows;
            RowIndex = index;
            return Result.Moved;
        }

        // Touch: a row's own button (Open, +, Run) focuses the row and confirms it in one tap.
        public Result OpenRow(int index)
        {
            var tapped = TapRow(index);
            return tapped == Result.Moved ? Confirm() : tapped;
        }

        // Touch: a row's own control (a switch, a stepper button, a choice, a slider) changes it at once.
        public Result SetRow(int index, int value)
        {
            if (Zone == SettingsZone.Typing || Zone == SettingsZone.Capture || Page == null || index < 0 || index >= Page.Rows.Count) return Result.None;
            Zone = SettingsZone.Rows;
            RowIndex = index;
            return Change(value);
        }

        // Touch: a sheet option tap chooses it.
        public Result TapSheet(int index)
        {
            if (Zone != SettingsZone.Sheet || index < 0 || index >= SheetOptions.Count) return Result.None;
            SheetIndex = index;
            return Confirm();
        }
    }
}
