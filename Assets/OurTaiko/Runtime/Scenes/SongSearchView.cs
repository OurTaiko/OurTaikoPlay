using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // Saved uGUI dialog; the manager owns results so a played song returns to the same search.
    public sealed class SongSearchView : MonoBehaviour, IPointerClickHandler
    {
        public GameObject panel;
        public TMP_InputField keyword;
        public UnityEngine.UI.Button apply, close;
        public UnityEngine.UI.Button[] previous, next;
        public TextMeshProUGUI[] values, labels;
        public TextMeshProUGUI title, status, summary, hint;
        public RectTransform cursor;
        SongSelectManager manager;
        CancellationTokenSource request;
        int difficulty, level, order, row, closedFrame = -1, fieldSubmitFrame = -1, openedFrame = -1, editingFrame = -1, compositionFrame = -1;
        bool busy, editingKeyword;
        string composition = "";
        Keyboard keyboard;
        void OnEnable() { keyboard = Keyboard.current; if (keyboard != null) keyboard.onIMECompositionChange += OnComposition; }
        void OnComposition(UnityEngine.InputSystem.LowLevel.IMECompositionString value) { composition = value.ToString(); compositionFrame = Time.frameCount; }
        public bool IsOpen => panel.activeSelf;
        public bool EditingKeyword => editingKeyword;
        public int FocusedRow => row;
        public bool BlocksInput => IsOpen || Time.frameCount == closedFrame;
        static string Words(string key) => L10n.Get("Menu", key);
        // The difficulty filter's values, from Easy.
        static readonly string[] DifficultyKeys = { "difficulty.easy", "difficulty.normal", "difficulty.hard", "difficulty.oni", "difficulty.ura" };

        public void Bind(SongSelectManager model)
        {
            manager = model;
            apply.onClick.AddListener(Submit);
            close.onClick.AddListener(Close);
            for (int i = 0; i < previous.Length; i++)
            {
                int index = i;
                previous[i].onClick.AddListener(() => Change(index, -1));
                next[i].onClick.AddListener(() => Change(index, 1));
            }
            keyword.onSubmit.AddListener(_ => FinishKeyword());
            keyword.onTouchScreenKeyboardStatusChanged.AddListener(OnTouchKeyboardStatusChanged);
            keyword.onValueChanged.AddListener(_ => { if (busy) { Cancel(); status.text = ""; Draw(); } });
            keyword.shouldActivateOnSelect = false;
            keyword.enabled = false;
            panel.SetActive(false);
            RefreshSummary();
        }
        void OnDisable() { editingKeyword = false; if (keyboard != null) keyboard.onIMECompositionChange -= OnComposition; Cancel(); if (manager != null) manager.SearchDialogOpen = false; }
        void Cancel() { manager?.CancelSearch(); request?.Cancel(); request?.Dispose(); request = null; busy = false; }
        public void Open()
        {
            if (manager == null || manager.Phase != SongSelectManager.State.Browsing || SceneSwitcher.EnsureInstance().IsInputBlocked) return;
            var query = manager.SearchQuery;
            difficulty = query.Difficulty.HasValue ? (int)query.Difficulty.Value + 1 : 0;
            level = query.Level; order = (int)query.Order; row = 1;
            editingKeyword = false; composition = ""; keyword.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
            keyword.SetTextWithoutNotify(query.Keyword);
            openedFrame = Time.frameCount;
            manager.SearchDialogOpen = true;
            panel.SetActive(true);
            status.text = "";
            Draw();
        }
        public void Close() => CloseDialog(true);
        void CloseDialog(bool playSound)
        {
            if (editingKeyword || Time.frameCount == fieldSubmitFrame) return;
            if (playSound) manager.PlaySearchSound(SongSelectManager.Sound.Don);
            Cancel(); keyword.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
            panel.SetActive(false); manager.SearchDialogOpen = false; closedFrame = Time.frameCount;
            RefreshSummary();
        }
        // The disabled TMP field lets its pointer clicks bubble to this saved parent view.
        // Selecting a row and activating text entry are deliberately separate gestures.
        public void OnPointerClick(PointerEventData eventData)
        {
            var hit = eventData.pointerPressRaycast.gameObject;
            if (!IsOpen || editingKeyword || Time.frameCount == fieldSubmitFrame || eventData.button != PointerEventData.InputButton.Left
                || hit == null || !hit.transform.IsChildOf(keyword.transform)) return;
            if (row == 0) BeginKeyword();
            else { MoveRow(-row, SongSelectManager.Sound.Ka); Draw(); }
        }
        void BeginKeyword()
        {
            manager.PlaySearchSound(SongSelectManager.Sound.Ka);
            Cancel(); status.text = ""; editingKeyword = true; editingFrame = Time.frameCount;
            keyword.enabled = true;
            keyword.Select(); keyword.ActivateInputField(); Draw();
        }
        void FinishKeyword()
        {
            if (!editingKeyword || Time.frameCount == editingFrame || !string.IsNullOrEmpty(composition) || Time.frameCount == compositionFrame) return;
            EndKeywordEditing();
        }
        void OnTouchKeyboardStatusChanged(TouchScreenKeyboard.Status status)
        {
            if (!editingKeyword || status == TouchScreenKeyboard.Status.Visible) return;
            // TMP has copied the native keyboard text before reporting dismissal.
            // Done, Cancel and LostFocus all end this session, even during IME composition.
            composition = ""; compositionFrame = -1;
            EndKeywordEditing();
        }
        void EndKeywordEditing()
        {
            fieldSubmitFrame = Time.frameCount;
            editingKeyword = false;
            keyword.DeactivateInputField(); keyword.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
            Draw();
        }
        void Change(int index, int delta)
        {
            if (editingKeyword || Time.frameCount == fieldSubmitFrame) return;
            manager.PlaySearchSound(SongSelectManager.Sound.Ka);
            Cancel(); row = index + 1;
            keyword.DeactivateInputField(); EventSystem.current?.SetSelectedGameObject(null);
            if (index == 0) difficulty = (difficulty + delta + 6) % 6;
            if (index == 1) level = (level + delta + 11) % 11;
            if (index == 2) order = (order + delta + 3) % 3;
            status.text = ""; Draw();
        }
        // The search button (a tap, Enter): the lone player's side, 1P with two players.
        public void Submit() => Submit(InputManager.Lead.Index());

        // `seat`: the side whose don confirmed; its account's plays order the result.
        public async void Submit(int seat)
        {
            if (!IsOpen || busy || editingKeyword || Time.frameCount == fieldSubmitFrame || Time.frameCount == openedFrame) return;
            SongSearchQuery query;
            try { query = new SongSearchQuery(keyword.text, difficulty == 0 ? (Difficulty?)null : (Difficulty)(difficulty - 1), level, (SongSearchOrder)order, seat); }
            catch (ArgumentException) { status.text = Words("search.keywordTooLong"); return; }
            manager.PlaySearchSound(SongSelectManager.Sound.Don);
            Cancel(); request = new CancellationTokenSource(); var current = request;
            busy = true; Draw(); status.text = Words("search.searching");
            try
            {
                await manager.SearchAsync(query, current.Token);
                if (request != current || !IsOpen) return;
                CloseDialog(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (request == current) status.text = L10n.Format("Menu", "search.failed", error.Message); }
            finally { if (request == current) { busy = false; request.Dispose(); request = null; Draw(); } }
        }
        void MoveRow(int delta, SongSelectManager.Sound sound)
        {
            row = (row + delta + 6) % 6;
            manager.PlaySearchSound(sound);
        }
        // The side whose drum pressed either face this frame, or -1: the lone player's own drum, or
        // either player's with two.
        int DrumSeat(InputKey a, InputKey b)
        {
            for (int seat = 0; seat < InputManager.Players.Count; seat++)
            {
                if (SceneSwitcher.EnsureInstance().Seated.Count < 2 && seat != InputManager.Lead.Index()) continue;
                if (InputManager.Players[seat].GetKeyDown(a) || InputManager.Players[seat].GetKeyDown(b)) return seat;
            }
            return -1;
        }

        public void Tick()
        {
            summary.gameObject.SetActive(manager.Phase == SongSelectManager.State.Browsing && !IsOpen);
            if (SceneSwitcher.EnsureInstance().IsInputBlocked) return;
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (Time.frameCount == fieldSubmitFrame) return;
            // Confirmation or native keyboard dismissal releases text entry. TMP owns typing, arrows and IME;
            // escape, Tab, drum bindings and outside clicks never operate the menu here.
            if (editingKeyword)
            {
                if (InputManager.GetKeyDown(InputKey.Confirm)) FinishKeyword();
                if (editingKeyword && !keyword.isFocused) { keyword.Select(); keyword.ActivateInputField(); }
                return;
            }
            if (InputManager.GetKeyDown(InputKey.Back)) { Close(); return; }
            if (Keyboard.current?.tabKey.wasPressedThisFrame == true)
            {
                keyword.DeactivateInputField(); EventSystem.current?.SetSelectedGameObject(null);
                MoveRow(Keyboard.current.shiftKey.isPressed ? -1 : 1, SongSelectManager.Sound.Ka); Draw(); return;
            }
            int donSeat = DrumSeat(InputKey.LeftDon, InputKey.RightDon);
            if (InputManager.GetKeyDown(InputKey.MenuUp)) MoveRow(-1, SongSelectManager.Sound.Ka);
            else if (InputManager.GetKeyDown(InputKey.MenuDown)) MoveRow(1, SongSelectManager.Sound.Ka);
            else if (row == 0 && InputManager.GetKeyDown(InputKey.Confirm)) BeginKeyword();
            else if (DrumSeat(InputKey.LeftKa, InputKey.LeftKa) >= 0 || InputManager.GetKeyDown(InputKey.MenuLeft))
            { if (row > 0 && row < 4) Change(row - 1, -1); else MoveRow(-1, SongSelectManager.Sound.Ka); }
            else if (DrumSeat(InputKey.RightKa, InputKey.RightKa) >= 0 || InputManager.GetKeyDown(InputKey.MenuRight))
            { if (row > 0 && row < 4) Change(row - 1, 1); else MoveRow(1, SongSelectManager.Sound.Ka); }
            else if (donSeat >= 0 || InputManager.GetKeyDown(InputKey.Confirm))
            {
                if (row < 4) MoveRow(1, SongSelectManager.Sound.Don);
                // The search is made by the side whose don confirmed it.
                else if (row == 4) Submit(donSeat >= 0 ? donSeat : InputManager.Lead.Index()); else Close();
            }
            Draw();
        }
        void Draw()
        {
            title.text = Words("search.title");
            string all = Words("search.all");
            labels[0].text = Words("search.difficulty"); labels[1].text = Words("search.stars");
            labels[2].text = Words("search.sortBy"); labels[3].text = Words("search.keyword");
            ((TMP_Text)keyword.placeholder).text = Words("search.keywordPlaceholder");
            values[0].text = difficulty == 0 ? all : L10n.Get("Common", DifficultyKeys[difficulty - 1]);
            values[1].text = level == 0 ? all : "★ " + level;
            values[2].text = order == 0 ? Words("search.order.default") : order == 1 ? Words("search.order.notFc") : Words("search.order.notAp");
            apply.GetComponentInChildren<TMP_Text>().text = busy ? Words("search.searching") : Words("search.search");
            close.GetComponentInChildren<TMP_Text>().text = Words("search.back");
            hint.text = Application.isMobilePlatform
                ? editingKeyword
                    ? Words("search.hint.touchTyping")
                    : row == 0
                        ? Words("search.hint.touchKeyword")
                        : Words("search.hint.touchRows")
                : editingKeyword
                    ? Words("search.hint.keysTyping")
                    : row == 0
                        ? Words("search.hint.keysKeyword")
                        : Words("search.hint.keysRows");
            cursor.gameObject.SetActive(row < 4);
            if (row < 4) cursor.anchoredPosition = new Vector2(cursor.anchoredPosition.x, labels[row == 0 ? 3 : row - 1].rectTransform.anchoredPosition.y);
            apply.interactable = !busy && !editingKeyword;
            close.interactable = !editingKeyword;
            foreach (var button in previous) button.interactable = !editingKeyword;
            foreach (var button in next) button.interactable = !editingKeyword;
            apply.targetGraphic.color = row == 4 ? new Color(1, .82f, .15f) : new Color(1, .92f, .45f);
            close.targetGraphic.color = row == 5 ? new Color(1, .85f, .35f) : Color.white;
        }
        public void RefreshSummary()
        {
            summary.text = manager.SearchActive
                ? L10n.Format("Menu", "search.results", manager.SearchCount) + (manager.SearchQuery.Keyword.Length > 0 ? "  ·  " + manager.SearchQuery.Keyword : "")
                  + (manager.SearchStatus.Length > 0 ? "\n" + manager.SearchStatus : "")
                : "";
        }
    }
}
