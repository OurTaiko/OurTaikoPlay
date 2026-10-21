using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場's result (YataiDON DanResultScreen with Nijiiro's dan_result art). On arrival the
    // attempt is recorded locally (DanRecordStore; not under オート) and each song played is saved
    // as a normal score: local songs in ScoreStore, a server's songs queued for upload with their
    // drum inputs. Page 1 shows each song's plate (unreached ones dimmed), page 2 the totals, every
    // exam as it ended and the 合格 / 不合格 stamp; a course grants no title or dan here (local
    // courses never do; a server's come in the online stage). The count-up and bar-fill animations
    // and the rank-up / congratulation screens are not ported.
    public sealed class DanResultScene : MonoBehaviour
    {
        public DanResultView view;
        public AudioSource bgm, sfx, voice;
        public AudioClip don, announce, partialPlate, totalIntro, stampClear, stampClearGold, stampFail, voiceClear, voiceClearGold, voiceFail;

        public DanPlayResult Result { get; private set; }
        public DanResultFlow Flow { get; private set; }
        public DanRecordStore.Best? PreviousBest { get; private set; }
        public bool Recorded { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;
        int platesLanded;
        bool totalsShown;

        double Now => GameTimeline.FrameTime * 1000;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            Result = switcher.LastDan;
            if (Result == null)
            {
                Debug.LogWarning("No 段位 result to show; returning to the course select.", this);
                enabled = false;
                switcher.SwitchScene(SceneSwitcher.DanSelectScene);
                return;
            }
            try { Save(); }
            catch (Exception error) { Debug.LogException(error, this); }
            Bind();
            Show(Now);
        }

        void Start()
        {
            if (Result == null) return;
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, partialPlate, totalIntro, stampClear, stampClearGold, stampFail);
            voice.PrepareAudioTracks(announce, voiceClear, voiceClearGold, voiceFail);
            if (bgm.clip != null) { bgm.loop = true; bgm.PlayAudio(); }
            Play(voice, announce);
        }

        // Once per result: the attempt and each song's score.
        void Save()
        {
            if (Result.Saved) { Recorded = !Result.AutoPlay; return; }
            Result.Saved = true;
            Recorded = !Result.AutoPlay;
            if (Recorded)
            {
                var entry = Result.Run.Entry;
                string language = SettingManager.EnsureInstance().Settings.general.Language;
                PreviousBest = DanRecordStore.Shared.Save(DanRecordStore.Key(entry), entry.Course.Title(language), Result.Verdict,
                    Result.Score, Result.MaxCombo, Result.GaugePoints, Result.Songs.Length);
            }
            for (int i = 0; i < Result.Songs.Length; i++)
                ResultScene.SaveScore(Result.Run.Song(i), Result.Songs[i], Result.Records != null && i < Result.Records.Length ? Result.Records[i] : null, 0);
        }

        void Update()
        {
            if (Result == null) return;
            double now = Now;
            // The pages start once the cover has opened (ResultScene's clock does too).
            if (Flow == null)
            {
                if (switcher.IsInputBlocked) { Show(now); return; }
                Flow = new DanResultFlow(Result.Run.Count, now);
            }
            if (!switcher.IsInputBlocked && !HasLeft)
            {
                if (InputManager.GetKeyDown(InputKey.Back)) Leave();
                else if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Don();
            }
            if (Flow.Update(now)) Stamp();
            Show(now);
        }

        // A drum face hit: shows the plates, turns the page, stamps, then leaves.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft || Flow == null) return;
            var before = Flow.State;
            if (!Flow.Hit(Now)) return;
            Play(sfx, don, true);
            // The plates shown at once land without a sound each (page1_plates_played = song_count).
            if (before == DanResultFlow.Phase.Songs) platesLanded = Result.Run.Count;
            if (before == DanResultFlow.Phase.Songs && Flow.State == DanResultFlow.Phase.Totals) Play(sfx, totalIntro, true);
            if (Flow.State == DanResultFlow.Phase.Left) Leave();
        }

        public void Leave()
        {
            if (HasLeft) return;
            Flow?.Leave();
            HasLeft = true;
            switcher.SwitchScene(SceneSwitcher.DanSelectScene);
        }

        void Stamp()
        {
            bool passed = Result.Verdict > 0, gold = Result.Verdict >= 4;
            Play(sfx, !passed ? stampFail : gold ? stampClearGold : stampClear, true);
            Play(voice, !passed ? voiceFail : gold ? voiceClearGold : voiceClear);
        }

        static void Play(AudioSource source, AudioClip clip, bool oneShot = false)
        {
            if (source == null || clip == null) return;
            if (oneShot) { source.PlayAudioOneShot(clip); return; }
            source.StopAudio();
            source.clip = clip;
            source.PlayAudio();
        }

        // ---------------------------------------------------------------- drawing

        void Bind()
        {
            var run = Result.Run;
            var course = run.Entry.Course;
            string language = SettingManager.EnsureInstance().Settings.general.Language;
            for (int i = 0; i < view.plates.Length; i++)
            {
                var plate = view.plates[i];
                bool shown = i < run.Count;
                plate.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var song = run.Song(i);
                var reference = run.Entry.Songs[i];
                bool reached = i < Result.Songs.Length;
                // A hidden song never reached (now or before) keeps its title back.
                bool hidden = reference.Ref.Hidden && !reached && (PreviousBest?.Arrival ?? 0) <= i;
                plate.banner.sprite = view.genreBanners[GenreFrame(song.genre)];
                plate.number.sprite = view.songNumbers[Math.Min(i, view.songNumbers.Length - 1)];
                plate.difficulty.sprite = view.difficulties[Mathf.Clamp((int)reference.Ref.Difficulty, 0, view.difficulties.Length - 1)];
                string level = reference.Level > 0 ? reference.Level.ToString() : "";
                for (int j = 0; j < plate.level.Length; j++)
                {
                    bool on = j < level.Length;
                    plate.level[j].enabled = on;
                    if (!on) continue;
                    plate.level[j].sprite = view.levelDigits[level[j] - '0'];
                    // dan_result_diff_num_margin 16 with a 9 px lead per extra digit.
                    plate.level[j].rectTransform.anchoredPosition = new Vector2(563 - 9 * (level.Length - 1) + 16 * j, plate.level[j].rectTransform.anchoredPosition.y);
                }
                if (hidden) plate.title.text = "？？？？？？";
                else
                {
                    try { plate.title.text = song.ReadDisplayInfo().Title; }
                    catch (FormatException) { plate.title.text = reference.Ref.Title; }
                }
                plate.title.Squeeze(1126);
                plate.unreached.enabled = !reached;
                plate.group.alpha = 1;
                // draw_page1 dims the plate's art for an unreached song; the 未到達 mark, title and level stay.
                float dim = reached ? 1 : 0.45f;
                foreach (var image in new[] { plate.banner, plate.number, plate.difficulty })
                    image.color = new Color(1, 1, 1, dim);
                foreach (string name in new[] { "Star", "good", "ok", "bad", "drumroll" })
                    if (plate.root.Find(name) is Transform part) part.GetComponent<Image>().color = new Color(1, 1, 1, dim);
                var stats = reached ? Result.Songs[i] : null;
                int[] counts = stats != null ? new[] { stats.Good, stats.Ok, stats.Bad, stats.Rolls } : null;
                for (int k = 0; k < plate.counts.Length; k++)
                    plate.counts[k].digits = Digits(plate.counts[k].digits, counts != null ? counts[k].ToString() : "", view.countDigits, RightEdge(plate.counts[k].digits), 28);
            }

            view.emblem.sprite = view.emblems[Mathf.Clamp(course.Color, 0, view.emblems.Length - 1)];
            view.horiName.text = course.Title(language);
            view.horiName.Squeeze(240);
            view.scoreDigits = Digits(view.scoreDigits, Result.Score.ToString(), view.scoreDigitSprites, 782, 46);
            var total = Result.Progress.Total;
            int[] totals = { total.Good, total.Ok, total.Bad, total.Rolls, Result.MaxCombo, total.Good + total.Ok + total.Bad + total.Rolls };
            for (int k = 0; k < view.totals.Length && k < totals.Length; k++)
                view.totals[k].digits = Digits(view.totals[k].digits, totals[k].ToString(), view.countDigits, RightEdge(view.totals[k].digits), 28);
            ShowExams();
            view.stamp.sprite = view.stamps[Mathf.Clamp(Result.Verdict, 0, view.stamps.Length - 1)];
            view.stamp.rectTransform.sizeDelta = view.stamp.sprite.rect.size;
            view.stamp.rectTransform.anchoredPosition = Result.Verdict > 0 ? new Vector2(7, -396) : new Vector2(10, -476);
            // dan_result.cpp best_score_show: a pass, no worse than the best before, with a higher score.
            bool best = Recorded && PreviousBest.HasValue && Result.Verdict > 0 && Result.Verdict >= PreviousBest.Value.Verdict
                && Result.Score > PreviousBest.Value.Score;
            view.bestScore.gameObject.SetActive(best);
            if (best) view.bestScoreDigits = Digits(view.bestScoreDigits, (Result.Score - PreviousBest.Value.Score).ToString(), view.bestScoreDigitSprites, RightEdge(view.bestScoreDigits), 14.17f);
            view.noSave.gameObject.SetActive(!Recorded);
        }

        // genre_banner's frame_order: 0 for the default and pops, then one per genre.
        static int GenreFrame(int genre) => Math.Max(0, Math.Min(8, genre) - 1);

        // The saved right edge of a right-aligned digit row: its last digit's position.
        static float RightEdge(Image[] digits) => digits[digits.Length - 1].rectTransform.anchoredPosition.x;

        // More digit images when a value is longer than the saved row (a score exam's 1,000,000).
        static Image[] Grow(Image[] digits, int count)
        {
            if (digits.Length >= count) return digits;
            var grown = new Image[count];
            Array.Copy(digits, grown, digits.Length);
            var last = digits[digits.Length - 1];
            for (int k = digits.Length; k < count; k++)
            {
                grown[k] = Instantiate(last, last.transform.parent);
                grown[k].name = "Digit" + k;
            }
            return grown;
        }

        // Right-aligned digits whose last one sits at `right` (draw_digit_counter with x_off = margin).
        static Image[] Digits(Image[] digits, string value, Sprite[] sprites, float right, float margin)
        {
            int n = value.Length;
            digits = Grow(digits, n);
            for (int k = 0; k < digits.Length; k++)
            {
                int index = k - (digits.Length - n);
                bool on = index >= 0;
                digits[k].enabled = on;
                if (!on) continue;
                digits[k].sprite = sprites[value[index] - '0'];
                digits[k].rectTransform.anchoredPosition = new Vector2(right - (n - 1 - index) * margin, digits[k].rectTransform.anchoredPosition.y);
            }
            return digits;
        }

        // Left-aligned digits from `left`.
        static Image[] DigitsLeft(Image[] digits, string value, Sprite[] sprites, float left, float margin)
        {
            digits = Grow(digits, value.Length);
            for (int k = 0; k < digits.Length; k++)
            {
                bool on = k < value.Length;
                digits[k].enabled = on;
                if (!on) continue;
                digits[k].sprite = sprites[value[k] - '0'];
                digits[k].rectTransform.anchoredPosition = new Vector2(left + k * margin, digits[k].rectTransform.anchoredPosition.y);
            }
            return digits;
        }

        // draw_exam_info: the gauge exam as the soul gauge row, the others as bars (one per song
        // reached for a per-song exam), as they stood when the course ended.
        void ShowExams()
        {
            var course = Result.Run.Entry.Course;
            var progress = Result.Progress;
            int gaugeIndex = course.Exams.FindIndex(e => e.Type == DanExamType.Gauge);
            for (int i = 0; i < view.exams.Length; i++)
            {
                var row = view.exams[i];
                bool shown = i < course.Exams.Count;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var exam = course.Exams[i];
                var state = progress.View(i, live: false);
                row.icon.sprite = view.examIcons[(int)exam.Type];
                bool gauge = i == gaugeIndex;
                row.gauge.gameObject.SetActive(gauge);
                row.all.gameObject.SetActive(!gauge && !exam.PerSong);
                if (gauge) ShowGauge(row, exam, progress.Value(exam, -1));
                else if (!exam.PerSong)
                {
                    Fill(row.allBar, row.allRainbow, Bar(state.BarState, false), view.rainbow, 972 * state.Progress, state.BarState);
                    row.allCaption.text = Caption(exam, state.Red);
                    row.allDigits = DigitsLeft(row.allDigits, state.Counter.ToString(), state.BarState == "max" ? view.valueDigitsMax : view.valueDigits, 807, 52);
                }
                int reached = exam.PerSong && !gauge ? Math.Min(Math.Max(1, Result.Songs.Length), row.songs.Length) : 0;
                for (int j = 0; j < row.songs.Length; j++)
                {
                    var song = row.songs[j];
                    bool on = j < reached;
                    song.root.gameObject.SetActive(on);
                    if (!on) continue;
                    song.badge.sprite = view.badges[Math.Min(j + 1, view.badges.Length - 1)];
                    bool border = exam.BorderFor(j) != null;
                    Fill(song.bar, song.rainbow, Bar(state.SongState[j], true), view.songRainbow, border ? 324 * state.SongProgress[j] : 0, border ? state.SongState[j] : "empty");
                    song.caption.text = exam.BorderFor(j) is DanBorder b ? Caption(exam, b.Red) : "―";
                    song.digits = DigitsLeft(song.digits, border ? state.SongCounter[j].ToString() : "", state.SongState[j] == "max" ? view.songDigitsMax : view.songDigits, 527 + 465 * j, 27);
                }
            }
        }

        void ShowGauge(DanResultView.ExamRowView row, DanExam exam, int value)
        {
            int border = exam.Course.Red, borderCells = border / 2, cells = Math.Min(50, value / 2);
            bool clear = value >= border && border > 0;
            row.norma.rectTransform.sizeDelta = new Vector2(21 * borderCells, row.norma.rectTransform.sizeDelta.y);
            row.norma.enabled = borderCells > 0;
            row.fill.sprite = clear ? view.gaugeFillClear : view.gaugeFill;
            row.fill.rectTransform.sizeDelta = new Vector2(21 * cells, row.fill.rectTransform.sizeDelta.y);
            row.fill.enabled = cells > 0;
            row.soul.sprite = clear ? view.soulLit : view.soulDark;
            row.marker.rectTransform.anchoredPosition = new Vector2(533 + 21 * borderCells, row.marker.rectTransform.anchoredPosition.y);
            var numbers = value >= 100 ? view.gaugeNumbersMax : clear ? view.gaugeNumbersClear : view.gaugeNumbersNoClear;
            var percent = value >= 100 ? view.percentMax : clear ? view.percentClear : view.percentNoClear;
            string text = value.ToString();
            int n = text.Length + 1;
            for (int k = 0; k < row.gaugeDigits.Length; k++)
            {
                bool on = k < n;
                var digit = row.gaugeDigits[k];
                digit.enabled = on;
                if (!on) continue;
                digit.sprite = k < n - 1 ? numbers[text[k] - '0'] : percent;
                digit.rectTransform.anchoredPosition = new Vector2(822 + (float)(k - (n - 1) / 2.0) * 34, digit.rectTransform.anchoredPosition.y);
            }
            row.gaugeCaption.text = Caption(exam, border);
            row.gaugeCaption.rectTransform.anchoredPosition = new Vector2(498 + 21 * borderCells + 52, row.gaugeCaption.rectTransform.anchoredPosition.y);
        }

        static string Caption(DanExam exam, int red)
        {
            string range = exam.Range == DanExamRange.Less ? "未満" : "以上";
            return exam.Type == DanExamType.Gauge ? $"{red} ％{range}" : $"{red} {range}";
        }

        Sprite Bar(string state, bool song) => state switch
        {
            "up_50" => song ? view.songRed : view.barRed,
            "up_80" => song ? view.songGold : view.barGold,
            "down_80" => song ? view.songDown80 : view.barDown80,
            _ => song ? view.songMax : view.barMax,
        };

        // A full bar ("max") is the scrolling rainbow; the others stretch their colour across the width.
        static void Fill(Image bar, RawImage rainbow, Sprite sprite, Texture rainbowTexture, float width, string state)
        {
            bool empty = width <= 0.01f || state == "empty";
            bool rainbowOn = !empty && state == "max" && rainbowTexture != null;
            bar.enabled = !empty && !rainbowOn;
            rainbow.enabled = rainbowOn;
            if (bar.enabled)
            {
                bar.sprite = sprite;
                bar.rectTransform.sizeDelta = new Vector2(width, bar.rectTransform.sizeDelta.y);
            }
            if (rainbowOn)
            {
                rainbow.rectTransform.sizeDelta = new Vector2(width, rainbow.rectTransform.sizeDelta.y);
                ScrollRainbow(rainbow);
            }
        }

        static void ScrollRainbow(RawImage rainbow)
        {
            if (!rainbow.enabled || rainbow.texture == null) return;
            float tw = rainbow.texture.width, period = tw / 2, width = rainbow.rectTransform.sizeDelta.x;
            float phase = period - (float)(GameTimeline.FrameTime * (period * 60.0 / 80.0) % period);
            rainbow.uvRect = new Rect(phase / tw, 0, width / tw, 1);
        }

        void Show(double now)
        {
            if (Flow == null)
            {
                foreach (var plate in view.plates) plate.root.gameObject.SetActive(false);
                view.totalsPage.gameObject.SetActive(false);
                return;
            }
            double t = Flow.SongsTime(now);
            for (int i = 0; i < view.plates.Length && i < Result.Run.Count; i++)
            {
                var plate = view.plates[i];
                double since = t - DanResultFlow.LandMs(i);
                plate.root.gameObject.SetActive(since > 0);
                plate.root.anchoredPosition = new Vector2((float)DanResultFlow.SlideOffset(since), plate.root.anchoredPosition.y);
                if (since >= 0 && i >= platesLanded && Flow.State == DanResultFlow.Phase.Songs) { platesLanded = i + 1; Play(sfx, partialPlate, true); }
            }
            bool totals = Flow.State != DanResultFlow.Phase.Songs;
            float fade = totals ? (float)Math.Min(1, Math.Max(0, (now - Flow.TotalsAt) / DanResultFlow.PageFadeMs)) : 0;
            view.totalsPage.gameObject.SetActive(totals);
            view.totalsPage.alpha = fade;
            view.songsPage.alpha = 1 - fade;
            if (totals && !totalsShown) totalsShown = true;
            view.stamp.enabled = Flow.StampShown(now);
            foreach (var row in view.exams)
            {
                if (!row.root.gameObject.activeSelf) continue;
                ScrollRainbow(row.allRainbow);
                foreach (var song in row.songs) ScrollRainbow(song.rainbow);
            }
        }
    }
}
