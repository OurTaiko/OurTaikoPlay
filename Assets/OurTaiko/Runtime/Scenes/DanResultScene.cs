using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場's result (YataiDON DanResultScreen with Nijiiro's dan_result art). On arrival the
    // attempt is recorded locally (DanRecordStore; not under オート) and each song played is saved
    // as a normal score: local songs in ScoreStore, a server's songs queued for upload with their
    // drum inputs. Page 1 slides in each song's plate (unreached ones dimmed); page 2 counts up the
    // totals, fills the soul gauge and each exam's bar in turn and stamps 合格 / 不合格 (timeline in
    // DanResultFlow). A pass better than any before shows the 昇段 celebration (with the course's
    // rank when a server's course names one, else 合格 alone), and the first pass of the highest
    // listed rank the 祝 popup. Those are only shown: what a server's course grants comes from its
    // server, which the attempt is uploaded to (not under オート) and which asks OurTaikoProfile;
    // a line at the bottom says what was granted, and 1P's nameplate is read again. Local courses
    // grant nothing.
    public sealed class DanResultScene : MonoBehaviour
    {
        public DanResultView view;
        public AudioSource bgm, sfx, voice;
        public AudioClip don, announce, partialIntro, partialPlate, totalIntro, countUp, achieveTamashii, gaugeUp, gaugeDown, gaugeJudgement;
        public AudioClip stampClear, stampClearGold, stampFail, atmosClear, voiceClear, voiceClearGold, voiceFail;
        public AudioClip advanceIntro, voiceAdvance, advanceShogo, voiceGlad, popupSuperlative;

        // The celebration and the popup move the nameplate here (dan_result_draw.lua nameplate_pos).
        static readonly Vector2 CelebrationPlate = new Vector2(763, -912);

        public DanPlayResult Result { get; private set; }
        public DanResultFlow Flow { get; private set; }
        public DanRecordStore.Best? PreviousBest { get; private set; }
        public bool Recorded { get; private set; }
        public bool HasLeft { get; private set; }
        public bool Celebrates { get; private set; }
        public bool Congratulates { get; private set; }
        // The upload of a server course's attempt (FanmadeClient queue key), null for none.
        public string DanUpload { get; private set; }
        public string AwardText => awardLine != null ? awardLine.text : "";

        TextMeshProUGUI awardLine;
        bool awardShown;

        SceneSwitcher switcher;
        int platesLanded;
        bool countUpPlayed, tamashiiPlayed, stampPlayed, voicePlayed, advancePlayed, shogoPlayed, congratsPlayed;
        bool[] rowFillPlayed = Array.Empty<bool>(), rowJudgePlayed = Array.Empty<bool>();
        int gaugeIndex = -1, gaugeValue;
        DanResultFlow.Page page;
        string[] totalValues = Array.Empty<string>();
        float[] totalRights = Array.Empty<float>();
        float bestScoreY;
        CanvasGroup plateGroup;

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
            sfx.PrepareAudioEffects(don, partialIntro, partialPlate, totalIntro, countUp, achieveTamashii, gaugeUp, gaugeDown, gaugeJudgement,
                stampClear, stampClearGold, stampFail, atmosClear, advanceIntro, advanceShogo, popupSuperlative);
            voice.PrepareAudioTracks(announce, voiceClear, voiceClearGold, voiceFail, voiceAdvance, voiceGlad);
            if (bgm.clip != null) { bgm.loop = true; bgm.PlayAudio(); }
            // The announce, once, as the screen opens (YataiDON's game/dan_transition is the same
            // line; it is not imported).
            Play(voice, announce);
            Play(sfx, partialIntro, true);
        }

        // Once per result: the attempt and each song's score.
        void Save()
        {
            if (Result.Saved) { Recorded = !Result.AutoPlay; PreviousBest = Result.PreviousBest; return; }
            Result.Saved = true;
            Recorded = !Result.AutoPlay;
            if (Recorded)
            {
                var entry = Result.Run.Entry;
                string language = SettingManager.EnsureInstance().Settings.general.Language;
                PreviousBest = Result.PreviousBest = DanRecordStore.Shared.Save(DanRecordStore.Key(entry), entry.Course.Title(language), Result.Verdict,
                    Result.Score, Result.MaxCombo, Result.GaugePoints, Result.Songs.Length);
            }
            for (int i = 0; i < Result.Songs.Length; i++)
                ResultScene.SaveScore(Result.Run.Song(i), Result.Songs[i], Result.Records != null && i < Result.Records.Length ? Result.Records[i] : null, 0);
            var course = Result.Run.Entry.Course;
            if (Recorded && !course.IsLocal)
                DanUpload = Result.Upload = Online.OnlineManager.EnsureInstance().Client?.SubmitDanAttempt(course.Server, course.Id, Result.Verdict, Result.Songs.Length);
        }

        // The bottom line of a server course's result: what the upload granted, or that it waits.
        void ShowAward()
        {
            if (awardLine == null || awardShown) return;
            var online = Online.OnlineManager.EnsureInstance();
            var award = online.Client?.Award(DanUpload);
            if (award == null) { awardLine.text = "報酬はアップロード後に付与されます"; return; }
            awardShown = true;
            if (!(award.Profile?["granted"] is JObject granted)) { awardLine.text = "報酬は後で付与されます"; return; }
            var parts = new List<string>();
            if (granted["dan"] is JObject dan && dan["index"]?.Type == JTokenType.Integer)
            {
                int index = (int)dan["index"], verdict = (int?)dan["verdict"] ?? 0;
                string name = DanResultFlow.RankName(index);
                parts.Add((dan["upgraded"]?.Type == JTokenType.Boolean && (bool)dan["upgraded"] ? "段位獲得：" : "段位：") + name + " " + DanResultFlow.VerdictName(verdict));
            }
            if (granted["titles"] is JArray titles)
                foreach (var title in titles.OfType<JObject>().Where(t => t["new"]?.Type == JTokenType.Boolean && (bool)t["new"]))
                    parts.Add("称号獲得：「" + (string)title["name"] + "」");
            awardLine.text = parts.Count > 0 ? string.Join("　", parts) : "新しい報酬はありません";
            if (parts.Count > 0) _ = online.RefreshNameplateAsync(0);
        }

        void Update()
        {
            if (Result == null) return;
            ShowAward();
            double now = Now;
            // The pages start once the cover has opened (ResultScene's clock does too).
            if (Flow == null)
            {
                if (switcher.IsInputBlocked) { Show(now); return; }
                Flow = new DanResultFlow(Result.Run.Count, now, page, Celebrates, Congratulates);
            }
            if (!switcher.IsInputBlocked && !HasLeft)
            {
                if (InputManager.GetKeyDown(InputKey.Back)) Leave();
                else if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Don();
            }
            if (Flow.Update(now)) Leave();
            Sounds(now);
            Show(now);
        }

        // A drum face hit: shows the plates, turns the page, lands page 2, then celebrates or leaves.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft || Flow == null) return;
            var before = Flow.State;
            double now = Now;
            if (!Flow.Hit(now))
            {
                // handle_input sounds every hit past the lock, even one that changes nothing (not on the popup).
                if (now - Flow.ChangedAt >= DanResultFlow.InputLockMs && before != DanResultFlow.Phase.Congrats && before != DanResultFlow.Phase.Left)
                    Play(sfx, don, true);
                return;
            }
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

        // update_sounds: page 2's count-up, fills and judgements as they happen (not after a skip),
        // the stamp and its voice, then the celebration's and the popup's.
        void Sounds(double now)
        {
            if (Flow.State == DanResultFlow.Phase.Congrats)
            {
                if (!congratsPlayed) { congratsPlayed = true; Play(voice, voiceGlad); Play(sfx, popupSuperlative, true); }
                return;
            }
            if (Flow.State == DanResultFlow.Phase.Celebrating)
            {
                if (!advancePlayed) { advancePlayed = true; Play(sfx, advanceIntro, true); Play(voice, voiceAdvance); }
                if (!shogoPlayed && now - Flow.CelebratedAt >= DanResultFlow.ShogoMs) { shogoPlayed = true; Play(sfx, advanceShogo, true); }
                return;
            }
            if (double.IsNaN(Flow.TotalsAt) || Flow.State == DanResultFlow.Phase.Left) return;
            double on = Flow.PageTime(now);
            if (!countUpPlayed && on >= Flow.TotalsStart && !Flow.Skipped) { countUpPlayed = true; Play(sfx, countUp, true); }
            if (!tamashiiPlayed && gaugeIndex >= 0 && gaugeValue >= 100 && on >= Flow.TotalsStart + gaugeValue * DanResultFlow.GaugeUnitMs)
            {
                tamashiiPlayed = true;
                Play(sfx, achieveTamashii, true);
            }
            var exams = Result.Run.Entry.Course.Exams;
            for (int i = 0; i < Flow.Rows.Length; i++)
            {
                if (i == gaugeIndex) continue;
                var row = Flow.Rows[i];
                if (!rowFillPlayed[i] && on >= row.Fill && row.FillMs > 0 && !Flow.Skipped)
                {
                    rowFillPlayed[i] = true;
                    Play(sfx, exams[i].Range == DanExamRange.Less ? gaugeDown : gaugeUp, true);
                }
                if (!rowJudgePlayed[i] && on >= row.Number && !Flow.Skipped) { rowJudgePlayed[i] = true; Play(sfx, gaugeJudgement, true); }
            }
            int verdict = Result.Verdict;
            if (!stampPlayed && on >= Flow.StampAt) { stampPlayed = true; Play(sfx, verdict == 0 ? stampFail : verdict <= 3 ? stampClear : stampClearGold, true); }
            if (!voicePlayed && on >= Flow.VoiceAt)
            {
                voicePlayed = true;
                if (verdict == 0) Play(voice, voiceFail);
                else { Play(sfx, atmosClear, true); Play(voice, verdict <= 3 ? voiceClear : voiceClearGold); }
            }
        }

        static void Play(AudioSource source, AudioClip clip, bool oneShot = false)
        {
            if (source == null || clip == null) return;
            if (oneShot) { source.PlayAudioOneShot(clip); return; }
            source.StopAudio();
            source.clip = clip;
            source.PlayAudio();
        }

        // ---------------------------------------------------------------- binding

        void Bind()
        {
            var run = Result.Run;
            var course = run.Entry.Course;
            DanUpload ??= Result.Upload;
            if (DanUpload != null && view.noSave != null)
            {
                // Where 記録されません would be: a recorded result never shows that.
                awardLine = SkinUi.Text("Award", view.noSave.parent, 30);
                awardLine.rectTransform.anchorMin = view.noSave.anchorMin;
                awardLine.rectTransform.anchorMax = view.noSave.anchorMax;
                awardLine.rectTransform.anchoredPosition = view.noSave.anchoredPosition;
                awardLine.rectTransform.sizeDelta = new Vector2(1600, 48);
                ShowAward();
            }
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
            // The arcade plate: the name large and top to bottom, and again small across its foot.
            string title = course.Title(language);
            if (view.danName != null) view.danName.text = string.Join("\n", title.Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()));
            view.horiName.text = title;
            view.horiName.Squeeze(240);
            var total = Result.Progress.Total;
            int[] totals = { total.Good, total.Ok, total.Bad, total.Rolls, Result.MaxCombo, total.Good + total.Ok + total.Bad + total.Rolls };
            totalValues = totals.Select(v => v.ToString()).ToArray();
            totalRights = view.totals.Select(row => RightEdge(row.digits)).ToArray();
            BindExams();
            page = new DanResultFlow.Page
            {
                Digits = totalValues.Append(Result.Score.ToString()).Max(v => v.Length),
                GaugeRow = gaugeIndex, GaugeValue = gaugeValue, Travel = ExamTravel(),
            };
            rowFillPlayed = new bool[page.Travel.Length];
            rowJudgePlayed = new bool[page.Travel.Length];
            view.stamp.sprite = view.stamps[Mathf.Clamp(Result.Verdict, 0, view.stamps.Length - 1)];
            view.stamp.rectTransform.sizeDelta = view.stamp.sprite.rect.size;
            view.stamp.rectTransform.anchoredPosition = Result.Verdict > 0 ? new Vector2(7, -396) : new Vector2(10, -476);
            // dan_result.cpp best_score_show: a pass, no worse than the best before, with a higher score.
            bool best = Recorded && PreviousBest.HasValue && Result.Verdict > 0 && Result.Verdict >= PreviousBest.Value.Verdict
                && Result.Score > PreviousBest.Value.Score;
            view.bestScore.gameObject.SetActive(best);
            bestScoreY = view.bestScore.anchoredPosition.y;
            if (best) view.bestScoreDigits = Digits(view.bestScoreDigits, (Result.Score - PreviousBest.Value.Score).ToString(), view.bestScoreDigitSprites, RightEdge(view.bestScoreDigits), 14.17f);
            view.noSave.gameObject.SetActive(!Recorded);
            BindCelebration(course);
        }

        // apply_reward: 昇段 is a pass better than every attempt before (rank = verdict + 1, a failed
        // attempt is 1); the 祝 popup follows for the first pass of the highest-ranked course listed.
        // An オート play records nothing, so it has nothing to celebrate.
        void BindCelebration(DanCourse course)
        {
            int previous = PreviousBest.HasValue ? PreviousBest.Value.Verdict + 1 : 0;
            Celebrates = Recorded && Result.Verdict > 0 && Result.Verdict + 1 > previous;
            int rank = course.DanIndex, highest = switcher.DanIndexMax;
            Congratulates = Celebrates && rank >= 0 && highest >= 0 && rank == highest && previous <= 1;
            view.celebration.gameObject.SetActive(false);
            if (!Celebrates) return;
            int verdict = Result.Verdict;
            view.shoudanBackground.sprite = verdict == 2 || verdict == 5 ? view.shoudanGold : verdict == 3 || verdict == 6 ? view.shoudanRainbow : view.shoudanWhite;
            var text = verdict > 3 ? view.textGold : view.textWhite;
            var named = DanResultFlow.RankGlyphs(rank);
            int[] frames = { named?.Left ?? -1, named?.Right ?? -1, 18, 19 };
            for (int i = 0; i < view.glyphs.Length; i++)
            {
                bool on = frames[i] >= 0 && frames[i] < text.Length;
                view.glyphs[i].sprite = on ? text[frames[i]] : null;
                view.glyphGhosts[i].sprite = view.glyphs[i].sprite;
                view.glyphs[i].gameObject.SetActive(on);
                view.glyphGhosts[i].gameObject.SetActive(on);
            }
            // The popup's text in Japanese (frame 0), as every Nijiiro screen here is.
            var popup = rank == 24 ? view.congratsText2 : view.congratsText1;
            if (popup != null && popup.Length > 0) view.congratsText.sprite = popup[0];
            plateGroup = view.nameplate.GetComponent<CanvasGroup>();
            if (plateGroup == null) plateGroup = view.nameplate.gameObject.AddComponent<CanvasGroup>();
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
            => RollDigits(digits, value, sprites, right, margin, -1, true);

        // The same, counting up (DanResultFlow.RollDigit): the ones first, a place every 500 ms.
        static Image[] RollDigits(Image[] digits, string value, Sprite[] sprites, float right, float margin, double roll, bool shown)
        {
            int n = value.Length;
            digits = Grow(digits, n);
            for (int k = 0; k < digits.Length; k++)
            {
                int index = k - (digits.Length - n);
                int digit = shown && index >= 0 ? DanResultFlow.RollDigit(value[index] - '0', n - 1 - index, roll) : -1;
                digits[k].enabled = digit >= 0;
                if (digit < 0) continue;
                digits[k].sprite = sprites[digit];
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

        // The rows as the course ended: the first gauge exam as the soul gauge row, the others as
        // bars (one per song reached for a per-song exam). Their animation is in ShowExams.
        void BindExams()
        {
            var course = Result.Run.Entry.Course;
            gaugeIndex = course.Exams.FindIndex(e => e.Type == DanExamType.Gauge);
            if (gaugeIndex >= view.exams.Length) gaugeIndex = -1;
            gaugeValue = gaugeIndex >= 0 ? Math.Max(0, Math.Min(100, Result.Progress.Value(course.Exams[gaugeIndex], -1))) : 0;
            for (int i = 0; i < view.exams.Length; i++)
            {
                var row = view.exams[i];
                bool shown = i < course.Exams.Count;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var exam = course.Exams[i];
                var state = Result.Progress.View(i, live: false);
                row.icon.sprite = view.examIcons[(int)exam.Type];
                bool gauge = i == gaugeIndex;
                row.gauge.gameObject.SetActive(gauge);
                row.all.gameObject.SetActive(!gauge && !exam.PerSong);
                if (gauge)
                {
                    int border = exam.Course.Red;
                    row.norma.rectTransform.sizeDelta = new Vector2(21 * (border / 2), row.norma.rectTransform.sizeDelta.y);
                    row.norma.enabled = border / 2 > 0;
                    row.marker.rectTransform.anchoredPosition = new Vector2(533 + 21 * (border / 2), row.marker.rectTransform.anchoredPosition.y);
                    row.gaugeCaption.text = Caption(exam, border);
                    row.gaugeCaption.rectTransform.anchoredPosition = new Vector2(498 + 21 * (border / 2) + 52, row.gaugeCaption.rectTransform.anchoredPosition.y);
                }
                else if (!exam.PerSong) row.allCaption.text = Caption(exam, state.Red);
                int reached = Reached(exam, gauge);
                for (int j = 0; j < row.songs.Length; j++)
                {
                    var song = row.songs[j];
                    bool on = j < reached;
                    song.root.gameObject.SetActive(on);
                    if (!on) continue;
                    song.badge.sprite = view.badges[Math.Min(j + 1, view.badges.Length - 1)];
                    song.caption.text = exam.BorderFor(j) is DanBorder b ? Caption(exam, b.Red) : "―";
                }
            }
        }

        int Reached(DanExam exam, bool gauge) => exam.PerSong && !gauge ? Math.Min(Math.Max(1, Result.Songs.Length), 3) : 0;

        double[] ExamTravel()
        {
            var exams = Result.Run.Entry.Course.Exams;
            var travel = new double[Math.Min(exams.Count, view.exams.Length)];
            for (int i = 0; i < travel.Length; i++)
            {
                if (i == gaugeIndex) continue;
                var state = Result.Progress.View(i, live: false);
                if (!exams[i].PerSong) { travel[i] = DanResultFlow.Travel(exams[i], state.Progress); continue; }
                var songs = new List<double>();
                for (int j = 0; j < Reached(exams[i], false); j++) if (exams[i].BorderFor(j) != null) songs.Add(state.SongProgress[j]);
                travel[i] = DanResultFlow.Travel(exams[i], 0, songs);
            }
            return travel;
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

        // ---------------------------------------------------------------- drawing

        void Show(double now)
        {
            if (Flow == null)
            {
                foreach (var plate in view.plates) plate.root.gameObject.SetActive(false);
                view.totalsPage.gameObject.SetActive(false);
                return;
            }
            bool covered = Flow.State == DanResultFlow.Phase.Celebrating || Flow.State == DanResultFlow.Phase.Congrats
                || Flow.State == DanResultFlow.Phase.Left && (!double.IsNaN(Flow.CelebratedAt) || !double.IsNaN(Flow.CongratsAt));
            view.songsPage.gameObject.SetActive(!covered);
            if (covered) view.totalsPage.gameObject.SetActive(false);
            view.noSave.gameObject.SetActive(!Recorded && !covered);
            ShowCelebration(now);
            if (covered) return;

            double t = Flow.SongsTime(now);
            for (int i = 0; i < view.plates.Length && i < Result.Run.Count; i++)
            {
                var plate = view.plates[i];
                double since = t - DanResultFlow.LandMs(i);
                plate.root.gameObject.SetActive(since > 0);
                plate.root.anchoredPosition = new Vector2((float)DanResultFlow.SlideOffset(since), plate.root.anchoredPosition.y);
                if (since >= 0 && i >= platesLanded && Flow.State == DanResultFlow.Phase.Songs) { platesLanded = i + 1; Play(sfx, partialPlate, true); }
            }
            // Page 2 fades in over page 1, whose plates stay under its board.
            bool totals = !double.IsNaN(Flow.TotalsAt);
            view.totalsPage.gameObject.SetActive(totals);
            view.songsPage.alpha = 1;
            if (!totals) return;
            double on = Flow.PageTime(now);
            view.totalsPage.alpha = (float)Math.Min(1, Math.Max(0, on / DanResultFlow.PageFadeMs));
            ShowTotals(on);
            ShowExams(on);
            ShowBestScore(on);
            view.stamp.enabled = Flow.StampShown(now);
        }

        // The six totals and the score count up from the totals' start (their labels show at once).
        void ShowTotals(double on)
        {
            bool shown = Flow.Skipped || on >= Flow.TotalsStart;
            double roll = Flow.RollTime(on);
            for (int k = 0; k < view.totals.Length && k < totalValues.Length; k++)
                view.totals[k].digits = RollDigits(view.totals[k].digits, totalValues[k], view.countDigits, totalRights[k], 28, roll, shown);
            view.scoreDigits = RollDigits(view.scoreDigits, Result.Score.ToString(), view.scoreDigitSprites, 782, 46, roll, shown);
        }

        void ShowExams(double on)
        {
            var course = Result.Run.Entry.Course;
            for (int i = 0; i < view.exams.Length && i < course.Exams.Count; i++)
            {
                var row = view.exams[i];
                if (i == gaugeIndex) { ShowGauge(row, course.Exams[i], on); continue; }
                var exam = course.Exams[i];
                var r = Flow.Rows[i];
                bool landed = Flow.Skipped || on >= r.Land;
                row.root.gameObject.SetActive(landed);
                if (!landed) continue;
                double share = Flow.FillShare(i, on);
                bool numbers = Flow.Skipped || on >= r.Number;
                var state = Result.Progress.View(i, live: false);
                if (!exam.PerSong)
                {
                    double p = DanResultFlow.FillProgress(exam, state.Progress, share);
                    string bar = share < 1 ? DanResultFlow.FillState(p) : state.BarState;
                    Fill(row.allBar, row.allRainbow, Bar(bar, false), view.rainbow, (float)(972 * p), bar);
                    bool max = share >= 1 && state.BarState == "max";
                    row.allDigits = DigitsLeft(row.allDigits, numbers ? state.Counter.ToString() : "", max ? view.valueDigitsMax : view.valueDigits, 807, 52);
                }
                for (int j = 0; j < row.songs.Length; j++)
                {
                    var song = row.songs[j];
                    if (!song.root.gameObject.activeSelf) continue;
                    bool border = exam.BorderFor(j) != null;
                    double p = border ? DanResultFlow.FillProgress(exam, state.SongProgress[j], share) : 0;
                    string bar = !border ? "empty" : share < 1 ? DanResultFlow.FillState(p) : state.SongState[j];
                    Fill(song.bar, song.rainbow, Bar(bar, true), view.songRainbow, (float)(324 * p), bar);
                    bool max = share >= 1 && state.SongState[j] == "max";
                    song.digits = DigitsLeft(song.digits, border && numbers ? state.SongCounter[j].ToString() : "", max ? view.songDigitsMax : view.songDigits, 527 + 465 * j, 27);
                }
                ScrollRainbow(row.allRainbow);
                foreach (var song in row.songs) ScrollRainbow(song.rainbow);
            }
        }

        // draw_gauge_row: the cells fill 25 ms a percent; the number shows once they have, and a
        // full gauge's soul then burns (flame, flicker and glow).
        void ShowGauge(DanResultView.ExamRowView row, DanExam exam, double on)
        {
            int border = exam.Course.Red;
            int value = Flow.GaugeAt(on, out bool landed);
            int cells = Math.Min(50, value / 2);
            bool clear = value >= border && border > 0;
            row.fill.sprite = clear ? view.gaugeFillClear : view.gaugeFill;
            row.fill.rectTransform.sizeDelta = new Vector2(21 * cells, row.fill.rectTransform.sizeDelta.y);
            row.fill.enabled = cells > 0;

            bool numbers = Flow.Skipped || on >= Flow.Rows[gaugeIndex].Number;
            var digits = gaugeValue >= 100 ? view.gaugeNumbersMax : clear ? view.gaugeNumbersClear : view.gaugeNumbersNoClear;
            var percent = gaugeValue >= 100 ? view.percentMax : clear ? view.percentClear : view.percentNoClear;
            string text = gaugeValue.ToString();
            int n = text.Length + 1;
            for (int k = 0; k < row.gaugeDigits.Length; k++)
            {
                bool shown = numbers && k < n;
                var digit = row.gaugeDigits[k];
                digit.enabled = shown;
                if (!shown) continue;
                digit.sprite = k < n - 1 ? digits[text[k] - '0'] : percent;
                digit.rectTransform.anchoredPosition = new Vector2(822 + (float)(k - (n - 1) / 2.0) * 34, digit.rectTransform.anchoredPosition.y);
            }

            bool fever = gaugeValue >= 100 && landed;
            if (row.flame != null) row.flame.enabled = false;
            if (row.feverGlow != null) row.feverGlow.enabled = false;
            if (!fever) { row.soul.sprite = clear && landed ? view.soulLit : view.soulDark; return; }
            double t0 = Flow.Skipped ? 0 : Flow.TotalsStart + gaugeValue * DanResultFlow.GaugeUnitMs;
            double f = 20 + Math.Max(0, on - t0) / DanResultFlow.FrameMs;
            bool tick = Math.Floor((f - 29) / 2) % 2 == 0;
            if (row.flame != null && view.flames != null && view.flames.Length > 0)
            {
                int cel = f < 29 ? 0 : (int)(Math.Floor((f - 29) / 3) % 8);
                row.flame.sprite = view.flames[Math.Min(cel, view.flames.Length - 1)];
                row.flame.Alpha((float)Math.Min(1, (f - 20) / 4));
            }
            row.soul.sprite = f >= 29 && tick ? view.soulDark : view.soulLit;
            if (row.feverGlow != null && f >= 24 && (f < 29 || tick)) row.feverGlow.Alpha(0.6f);
        }

        // draw_best_score: the pill drops in as the totals start counting, its number follows, and a
        // glow swells off it once.
        void ShowBestScore(double on)
        {
            if (!view.bestScore.gameObject.activeSelf) return;
            double f = Flow.Skipped ? 52 : Math.Min(52, 5 + Math.Max(0, on - Flow.TotalsStart) / DanResultFlow.FrameMs);
            bool shown = f >= 5 && (Flow.Skipped || on >= Flow.TotalsStart);
            var (y, alpha) = DanResultFlow.BestScorePill(f);
            view.bestScore.anchoredPosition = new Vector2(view.bestScore.anchoredPosition.x, bestScoreY - (float)y);
            var pill = view.bestScore.GetComponent<Image>();
            if (pill != null) pill.color = new Color(1, 1, 1, shown ? (float)alpha : 0);
            float number = shown ? (float)Math.Min(1, Math.Max(0, (f - 18) / 3)) : 0;
            foreach (var digit in view.bestScoreDigits) digit.color = new Color(1, 1, 1, number);
            if (view.bestScoreGlow == null) return;
            bool glow = shown && f >= 23 && f < 34;
            view.bestScoreGlow.enabled = glow;
            if (!glow) return;
            double u = (f - 23) / 11;
            view.bestScoreGlow.rectTransform.localScale = Vector3.one * (float)(1 + 0.6 * u);
            view.bestScoreGlow.color = new Color(1, 1, 1, (float)(1 - u));
        }

        // draw_celebration / draw_congrats: the 昇段 glyphs pop in (the rank at 35 frames, 合格 at 63),
        // throw a ghost as they settle and again at 110; the nameplate comes up beside them. The popup
        // fades its band and text in over the same background.
        void ShowCelebration(double now)
        {
            bool celebrating = Flow.State == DanResultFlow.Phase.Celebrating || Flow.State == DanResultFlow.Phase.Left && !double.IsNaN(Flow.CelebratedAt) && double.IsNaN(Flow.CongratsAt);
            bool congrats = Flow.State == DanResultFlow.Phase.Congrats || Flow.State == DanResultFlow.Phase.Left && !double.IsNaN(Flow.CongratsAt);
            view.celebration.gameObject.SetActive(celebrating || congrats);
            if (!celebrating && !congrats) return;
            var plate = view.nameplate.GetComponent<RectTransform>();
            plate.anchoredPosition = CelebrationPlate;
            view.congratsBackground.gameObject.SetActive(congrats);
            view.congratsText.gameObject.SetActive(congrats);
            if (congrats)
            {
                double f = (now - Flow.CongratsAt) / DanResultFlow.FrameMs;
                foreach (var glyph in view.glyphs.Concat(view.glyphGhosts)) glyph.enabled = false;
                view.congratsBackground.Alpha((float)Math.Min(1, f / 19));
                view.congratsText.Alpha((float)Math.Min(1, Math.Max(0, (f - 20) / 14)));
                plateGroup.alpha = 1;
                return;
            }
            double fF = (now - Flow.CelebratedAt) / DanResultFlow.FrameMs;
            double[] starts = { 35, 35, 63, 63 };
            for (int i = 0; i < view.glyphs.Length; i++)
            {
                var glyph = view.glyphs[i];
                var ghost = view.glyphGhosts[i];
                glyph.enabled = ghost.enabled = false;
                if (!glyph.gameObject.activeSelf || fF < starts[i]) continue;
                double t0 = starts[i];
                double pop = fF < t0 + 7 ? 1.5 - 0.5 * ((fF - t0) / 7) : 1;
                glyph.enabled = true;
                glyph.rectTransform.localScale = Vector3.one * (float)pop;
                double e0 = t0 + 7, u = -1, grow = 0;
                if (fF >= e0 && fF < e0 + 6) { u = (fF - e0) / 6; grow = 0.25; }
                else if (fF >= 110 && fF < 133) { u = (fF - 110) / 23; grow = 0.96; }
                if (u < 0) continue;
                ghost.enabled = true;
                ghost.rectTransform.localScale = Vector3.one * (float)(1 + grow * u);
                ghost.color = new Color(1, 1, 1, (float)(1 - u));
            }
            plateGroup.alpha = (float)Math.Min(1, Math.Max(0, (fF - 21) / 8));
        }
    }
}
