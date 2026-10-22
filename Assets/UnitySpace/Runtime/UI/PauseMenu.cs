using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod.UI
{
    /// <summary>
    /// The pause menu (U.SPC-T276), opened by Esc once there is nothing left for it to back out of (or at the
    /// helm): Resume, Save / Load, Settings, Main Menu and Quit. Settings lists every setting the game's systems
    /// declare in the <see cref="SettingsRegistry"/> (U.SPC-T431), by group, with a search across them all, each
    /// taking effect at once and remembered in the profile, with Reset per setting and per group; a new setting
    /// shows up here by being declared, never by an edit to this menu. Settings also holds the Display page, where
    /// the resolution, the window mode and VSync are picked; nothing changes there until Apply, and what Apply puts
    /// on the screen is taken back after ten real seconds unless the player keeps it. While the menu is open the whole simulation stands still
    /// (the app skips its frame), the cursor is free and the piloted hull takes no stick or trigger. Esc goes
    /// back one page, and on the first page resumes. The main menu opens the same Settings on their own
    /// (<see cref="OpenSettings"/>).
    /// </summary>
    public sealed class PauseMenu
    {
        enum Page
        {
            Main,
            Settings,
            Display,
        }

        Page page;
        float timeScaleBefore = 1;

        /// <summary>The Display page's sizes, the settings it is editing (not yet applied), and how far its list is scrolled.</summary>
        List<DisplayResolution> resolutions, allResolutions;

        /// <summary>The aspect ratio the resolution list is narrowed to (U.SPC-T284), or "All".</summary>
        string aspect = "All";
        DisplaySettings pending;
        Vector2 resolutionScroll;

        /// <summary>The countdown after Apply, or null when no new settings are waiting to be kept.</summary>
        DisplayConfirmation confirm;

        /// <summary>A line under the Display page's buttons saying what last happened (kept, reverted).</summary>
        string notice;

        /// <summary>The Settings page: the group shown (null: the first), the search typed, and how far its list is scrolled.</summary>
        string group;
        string search = "";
        Vector2 settingsScroll;

        /// <summary>The words that find the Display page in a search, as the registry's settings are found by theirs.</summary>
        const string DisplaySearchWords = "display graphics resolution screen window mode borderless fullscreen exclusive vsync aspect ratio ultrawide";

        bool stylesReady;
        GUIStyle panel, title, text, small, button, buttonActive, row, rowSelected, countdown, field, label;
        Texture2D panelTexture, shadeTexture, buttonTexture, buttonActiveTexture, rowHoverTexture, rowSelectedTexture, fieldTexture;

        public bool IsOpen { get; private set; }

        /// <summary>Opened on Settings by itself (from the main menu): backing out of Settings closes it.</summary>
        bool settingsOnly;

        /// <summary>Save / Load: the game's saves panel (the menu closes first).</summary>
        public System.Action ShowSaves;

        /// <summary>Main Menu: leaves the game for the main menu scene.</summary>
        public System.Action ToMainMenu;

        /// <summary>Opens on the first page and stops scaled time (particles, anything on <see cref="Time.deltaTime"/>);
        /// the simulation itself stands still because the app skips its frame while the menu is open.</summary>
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            settingsOnly = false;
            page = Page.Main;
            notice = null;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0;
        }

        /// <summary>Opens straight on Settings, for a screen of its own (the main menu): Back from there closes it.</summary>
        public void OpenSettings()
        {
            Open();
            settingsOnly = true;
            page = Page.Settings;
        }

        /// <summary>Closes the menu and lets time run as it was. New display settings not yet kept are taken back.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            if (confirm != null) Revert();
            IsOpen = false;
            Time.timeScale = timeScaleBefore;
        }

        /// <summary>Each frame while open: Esc goes back a page (or resumes from the first), and the countdown after
        /// Apply runs on <paramref name="realSeconds"/> of unscaled time, reverting when it runs out.</summary>
        public void Update(bool escape, float realSeconds)
        {
            if (!IsOpen) return;
            if (confirm != null)
            {
                if (escape || confirm.Tick(realSeconds)) Revert();
                return;
            }
            if (!escape) return;
            Back();
        }

        void Back()
        {
            switch (page)
            {
                case Page.Display:
                    // Leaving the page discards what was picked but not applied.
                    page = Page.Settings;
                    break;
                case Page.Settings:
                    // Esc clears a search before it leaves the page.
                    if (!string.IsNullOrEmpty(search)) search = "";
                    else if (settingsOnly) Close();
                    else page = Page.Main;
                    break;
                default:
                    Close();
                    break;
            }
        }

        void OpenDisplay()
        {
            page = Page.Display;
            allResolutions = DisplayOptions.Resolutions();
            pending = DisplayOptions.Current;
            notice = null;
            // Open on the screen's own aspect, so a widescreen or ultrawide player starts among their own sizes.
            PickAspect(DisplaySettings.AspectOf(pending.Width, pending.Height));
        }

        /// <summary>Narrows the resolution list to <paramref name="a"/> (every size if none is of it).</summary>
        void PickAspect(string a)
        {
            aspect = a;
            resolutions = DisplaySettings.OfAspect(allResolutions, aspect);
            if (resolutions.Count == 0)
            {
                aspect = "All";
                resolutions = allResolutions;
            }
            int i = DisplaySettings.IndexOfNearest(resolutions, pending.Width, pending.Height);
            resolutionScroll = new Vector2(0, Mathf.Max(0, i - 4) * RowH);
        }

        void ApplyPending()
        {
            var previous = DisplayOptions.Current;
            DisplayOptions.Apply(pending);
            confirm = new DisplayConfirmation(previous, pending);
            notice = null;
        }

        void Keep()
        {
            DisplayOptions.Save();
            confirm = null;
            notice = "Display settings kept.";
        }

        void Revert()
        {
            var previous = confirm.Previous;
            confirm = null;
            DisplayOptions.Apply(previous);
            pending = previous;
            notice = "Reverted to the previous display settings.";
        }

        static void Quit()
        {
            // The app's own quit path autosaves (UnitySpaceApp.OnApplicationQuit).
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- drawing

        const float RowH = 24;

        /// <summary>Draws the open page over the whole screen (the HUD underneath is drawn disabled by the app).</summary>
        public void Draw()
        {
            if (!IsOpen) return;
            EnsureStyles();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), shadeTexture);
            bool enabled = GUI.enabled;
            GUI.enabled = confirm == null;
            switch (page)
            {
                case Page.Main: DrawMain(); break;
                case Page.Settings: DrawSettings(); break;
                default: DrawDisplay(); break;
            }
            GUI.enabled = enabled;
            if (confirm != null) DrawConfirm();
        }

        Rect Centred(float w, float h) => new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        void DrawMain()
        {
            const float w = 320, h = 302;
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "PAUSED", title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 18), "Every system stands still  ·  Esc to resume", small);
            float y = r.y + 70;
            if (GUI.Button(new Rect(r.x + 20, y, w - 40, 34), "Resume", button)) Close();
            if (GUI.Button(new Rect(r.x + 20, y + 44, w - 40, 34), "Save / Load", button))
            {
                Close();
                ShowSaves?.Invoke();
            }
            if (GUI.Button(new Rect(r.x + 20, y + 88, w - 40, 34), "Settings", button)) page = Page.Settings;
            if (GUI.Button(new Rect(r.x + 20, y + 132, w - 40, 34), "Main Menu", button))
            {
                Close();
                ToMainMenu?.Invoke();
            }
            if (GUI.Button(new Rect(r.x + 20, y + 176, w - 40, 34), "Quit", button))
            {
                Time.timeScale = timeScaleBefore;
                Quit();
            }
        }

        const float SettingRowH = 58;

        /// <summary>
        /// The Settings page (U.SPC-T431): Display and every group the registry holds down the left, the chosen
        /// group's settings (or, while something is typed in Search, every setting it finds) on the right.
        /// </summary>
        void DrawSettings()
        {
            var registry = GameSettingsHost.Registry;
            var groups = registry.Groups();
            if (group == null || !groups.Contains(group)) group = groups.Count > 0 ? groups[0] : null;

            float w = Mathf.Min(820, Screen.width - 40), h = Mathf.Min(560, Screen.height - 40);
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "SETTINGS", title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 340, 18),
                "Changes take effect at once and are kept in profile " + ActiveProfile.Current.Name + "  ·  Esc to go back", small);

            // Search, across every group.
            GUI.Label(new Rect(r.xMax - 320, r.y + 16, 60, 24), "Search", text);
            string typed = GUI.TextField(new Rect(r.xMax - 260, r.y + 14, 240, 26), search ?? "", 48, field);
            if (typed != search)
            {
                search = typed;
                settingsScroll = Vector2.zero;
            }
            bool searching = !string.IsNullOrWhiteSpace(search);

            // The groups.
            const float navW = 190;
            float top = r.y + 70, bottom = r.yMax - 56;
            float ny = top;
            if (GUI.Button(new Rect(r.x + 20, ny, navW, 30), "Display", button)) OpenDisplay();
            ny += 36;
            foreach (var g in groups)
            {
                if (ny + 30 > bottom) break;
                if (GUI.Button(new Rect(r.x + 20, ny, navW, 30), g, !searching && g == group ? buttonActive : button))
                {
                    group = g;
                    search = "";
                    settingsScroll = Vector2.zero;
                }
                ny += 34;
            }

            // The settings.
            float px = r.x + 20 + navW + 20, pw = r.xMax - 20 - px;
            var shown = searching ? registry.Search(search) : group != null ? registry.InGroup(group) : new List<GameSetting>();
            bool displayFound = searching && MatchesAll(DisplaySearchWords, search);
            int rows = shown.Count + (displayFound ? 1 : 0);
            string heading = searching ? rows + " found for \"" + search.Trim() + "\"" : group ?? "Nothing to set";
            GUI.Label(new Rect(px, top, pw, 22), heading, text);

            var view = new Rect(px, top + 28, pw, bottom - top - 28);
            var content = new Rect(0, 0, pw - 18, Mathf.Max(rows * SettingRowH, 1));
            settingsScroll = GUI.BeginScrollView(view, settingsScroll, content);
            float y = 0;
            if (displayFound)
            {
                GUI.Label(new Rect(4, y + 4, content.width - 280, 20), "Display", label);
                GUI.Label(new Rect(4, y + 24, content.width - 280, 30), "Resolution, aspect ratio, window mode and VSync.", small);
                if (GUI.Button(new Rect(content.width - 266, y + 10, 200, 28), "Open Display", button)) OpenDisplay();
                y += SettingRowH;
            }
            foreach (var setting in shown)
            {
                DrawSetting(setting, new Rect(0, y, content.width, SettingRowH), searching);
                y += SettingRowH;
            }
            if (rows == 0)
                GUI.Label(new Rect(4, 4, content.width - 8, 40), searching ? "No setting matches that." : "No system has declared a setting yet.", small);
            GUI.EndScrollView();

            // Reset the group shown (or everything found), and Back.
            float by = r.yMax - 46;
            var resettable = shown.FindAll(s => !s.IsDefault);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && resettable.Count > 0;
            string resetLabel = searching ? "Reset these" : "Reset " + (group ?? "");
            if (GUI.Button(new Rect(px, by, 220, 30), resetLabel, button))
                foreach (var s in resettable) s.Reset();
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(r.xMax - 20 - 110, by, 110, 30), "Back", button))
            {
                search = ""; // the button leaves the page outright; only Esc stops at clearing a search first
                Back();
            }
        }

        /// <summary>One setting's row: its name and what it does on the left, its control and Reset on the right.</summary>
        void DrawSetting(GameSetting setting, Rect r, bool showGroup)
        {
            const float controlW = 250, resetW = 60;
            float textW = r.width - controlW - resetW - 24;
            GUI.Label(new Rect(r.x + 4, r.y + 4, textW, 20), showGroup ? setting.Group + "  ·  " + setting.Label : setting.Label, label);
            GUI.Label(new Rect(r.x + 4, r.y + 24, textW, SettingRowH - 26), setting.Description, small);

            float cx = r.x + textW + 12, cy = r.y + 10;
            switch (setting)
            {
                case ToggleSetting toggle:
                {
                    float half = (controlW - 6) * 0.5f;
                    if (GUI.Button(new Rect(cx, cy, half, 28), "On", toggle.Value ? buttonActive : button)) toggle.Set(true);
                    if (GUI.Button(new Rect(cx + half + 6, cy, half, 28), "Off", !toggle.Value ? buttonActive : button)) toggle.Set(false);
                    break;
                }
                case RangeSetting range:
                {
                    const float valueW = 86;
                    float f = GUI.HorizontalSlider(new Rect(cx, cy + 8, controlW - valueW - 8, 16), (float)range.Fraction, 0f, 1f);
                    if (!Mathf.Approximately(f, (float)range.Fraction)) range.SetFraction(f);
                    GUI.Label(new Rect(cx + controlW - valueW, cy + 2, valueW, 24), range.ValueText, text);
                    break;
                }
                case ChoiceSetting choice:
                {
                    if (GUI.Button(new Rect(cx, cy, 32, 28), "<", button)) choice.Cycle(-1);
                    GUI.Label(new Rect(cx + 36, cy, controlW - 72, 28), choice.Selected, rowSelected);
                    if (GUI.Button(new Rect(cx + controlW - 32, cy, 32, 28), ">", button)) choice.Cycle(1);
                    break;
                }
                default:
                    GUI.Label(new Rect(cx, cy + 4, controlW, 24), setting.ValueText, text);
                    break;
            }

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !setting.IsDefault;
            if (GUI.Button(new Rect(r.xMax - resetW, cy, resetW, 28), "Reset", button)) setting.Reset();
            GUI.enabled = enabled;
        }

        /// <summary>Whether every word of <paramref name="query"/> is among <paramref name="words"/>.</summary>
        static bool MatchesAll(string words, string query)
        {
            foreach (var q in query.ToLowerInvariant().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (words.IndexOf(q, System.StringComparison.Ordinal) < 0) return false;
            return true;
        }

        void DrawDisplay()
        {
            const float w = 600, h = 456;
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "DISPLAY", title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 18),
                "Nothing changes until Apply  ·  now: " + DisplayOptions.Current, small);

            // Aspect ratio first (U.SPC-T284): it narrows the resolution list below to widescreen, ultrawide and so on.
            GUI.Label(new Rect(r.x + 20, r.y + 64, 120, 20), "Aspect ratio", text);
            var aspects = new List<string> { "All" };
            foreach (var a in DisplaySettings.Aspects)
                if (DisplaySettings.OfAspect(allResolutions, a).Count > 0) aspects.Add(a);
            float aw = (w - 40 - (aspects.Count - 1) * 6) / aspects.Count;
            for (int i = 0; i < aspects.Count; i++)
                if (GUI.Button(new Rect(r.x + 20 + i * (aw + 6), r.y + 88, aw, 28), aspects[i], aspect == aspects[i] ? buttonActive : button))
                    PickAspect(aspects[i]);

            // Resolution: one row per size, the one picked highlighted.
            const float listW = 270, listH = 260;
            float top = r.y + 126;
            GUI.Label(new Rect(r.x + 20, top, listW, 20), "Resolution", text);
            var view = new Rect(r.x + 20, top + 24, listW, listH);
            GUI.Box(view, GUIContent.none, panel);
            var content = new Rect(0, 0, listW - 18, resolutions.Count * RowH);
            resolutionScroll = GUI.BeginScrollView(view, resolutionScroll, content);
            for (int i = 0; i < resolutions.Count; i++)
            {
                var res = resolutions[i];
                bool picked = res.SameSize(pending.Width, pending.Height);
                string label = res.ToString() + (aspect == "All" ? "   " + DisplaySettings.AspectOf(res.Width, res.Height) : "");
                if (GUI.Button(new Rect(0, i * RowH, content.width, RowH - 2), label, picked ? rowSelected : row))
                    pending = pending.WithSize(res.Width, res.Height);
            }
            GUI.EndScrollView();

            // Window mode and VSync.
            float x = r.x + 20 + listW + 24, cw = r.xMax - 20 - x;
            GUI.Label(new Rect(x, top, cw, 20), "Mode", text);
            var modes = new[] { DisplayMode.Windowed, DisplayMode.Borderless, DisplayMode.Exclusive };
            for (int i = 0; i < modes.Length; i++)
                if (GUI.Button(new Rect(x, top + 24 + i * 34, cw, 28), DisplaySettings.ModeName(modes[i]), pending.Mode == modes[i] ? buttonActive : button))
                    pending = pending.WithMode(modes[i]);
            float vy = top + 24 + modes.Length * 34 + 16;
            GUI.Label(new Rect(x, vy, cw, 20), "VSync", text);
            float half = (cw - 8) * 0.5f;
            if (GUI.Button(new Rect(x, vy + 24, half, 28), "On", pending.VSync ? buttonActive : button)) pending = pending.WithVSync(true);
            if (GUI.Button(new Rect(x + half + 8, vy + 24, half, 28), "Off", !pending.VSync ? buttonActive : button)) pending = pending.WithVSync(false);

            if (!string.IsNullOrEmpty(notice)) GUI.Label(new Rect(x, vy + 66, cw, 40), notice, small);
            // A size the screen does not list itself can be a window or a borderless window, not a mode to switch the screen to.
            else if (pending.Mode == DisplayMode.Exclusive && !allResolutions.Exists(m => m.SameSize(pending.Width, pending.Height) && m.RefreshHz > 0))
                GUI.Label(new Rect(x, vy + 66, cw, 40), "The screen does not offer this size as a mode: borderless windowed will show it", small);

            // Apply only once something differs from what is on the screen; Cancel throws the picks away.
            float by = r.yMax - 46;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && pending != DisplayOptions.Current;
            if (GUI.Button(new Rect(r.xMax - 20 - 230, by, 110, 30), "Apply", button)) ApplyPending();
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(r.xMax - 20 - 110, by, 110, 30), "Cancel", button)) page = Page.Settings;
        }

        void DrawConfirm()
        {
            const float w = 380, h = 132;
            var r = Centred(w, h);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), shadeTexture); // over the Display page
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "Keep these display settings?", title);
            GUI.Label(new Rect(r.x + 20, r.y + 40, w - 40, 22), "Reverting in " + confirm.SecondsShown, countdown);
            if (GUI.Button(new Rect(r.x + 20, r.yMax - 46, (w - 50) * 0.5f, 30), "Keep", buttonActive)) Keep();
            else if (GUI.Button(new Rect(r.x + 30 + (w - 50) * 0.5f, r.yMax - 46, (w - 50) * 0.5f, 30), "Revert", button)) Revert();
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            // The HUD's own look (see Hud's styles), a touch more opaque: the menu sits over the whole view.
            panelTexture = Hud.Solid(new Color(0.03f, 0.05f, 0.08f, 0.92f), new Color(0.3f, 0.6f, 0.75f, 0.6f));
            shadeTexture = Hud.Solid(new Color(0, 0, 0, 0.45f), Color.clear);
            buttonTexture = Hud.Solid(new Color(0.12f, 0.18f, 0.24f, 0.95f), new Color(0.3f, 0.5f, 0.6f, 0.6f));
            buttonActiveTexture = Hud.Solid(new Color(0.1f, 0.3f, 0.42f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));
            rowHoverTexture = Hud.Solid(new Color(0.2f, 0.45f, 0.6f, 0.3f), Color.clear);
            rowSelectedTexture = Hud.Solid(new Color(0.2f, 0.55f, 0.75f, 0.45f), Color.clear);

            panel = new GUIStyle { normal = { background = panelTexture }, border = new RectOffset(2, 2, 2, 2) };
            title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            text = new GUIStyle { fontSize = 13, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            small = new GUIStyle { fontSize = 11, normal = { textColor = Hud.Dim }, wordWrap = true };
            label = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            fieldTexture = Hud.Solid(new Color(0.02f, 0.03f, 0.05f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));
            field = new GUIStyle
            {
                fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 0, 0), border = new RectOffset(2, 2, 2, 2),
                clipping = TextClipping.Clip,
                normal = { background = fieldTexture, textColor = Color.white },
                focused = { background = fieldTexture, textColor = Color.white },
                hover = { background = fieldTexture, textColor = Color.white },
            };
            countdown = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Hud.Accent } };
            button = new GUIStyle
            {
                fontSize = 13, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonActiveTexture, textColor = Color.white },
                active = { background = buttonActiveTexture, textColor = Hud.Accent },
            };
            buttonActive = new GUIStyle(button) { normal = { background = buttonActiveTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            row = new GUIStyle
            {
                fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 4, 0, 0), clipping = TextClipping.Clip,
                normal = { textColor = Color.white }, hover = { background = rowHoverTexture, textColor = Color.white },
            };
            rowSelected = new GUIStyle(row) { normal = { background = rowSelectedTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
        }

        /// <summary>The menu's own styles are going with whatever owns it -- the game session or the main menu
        /// (U.SPC-T366): DontSave outlives the scene otherwise, so every session (or trip through the main menu)
        /// that opened Settings would leave its textures behind.</summary>
        public void DestroyOwnedTextures()
        {
            if (panelTexture != null) Object.Destroy(panelTexture);
            if (shadeTexture != null) Object.Destroy(shadeTexture);
            if (buttonTexture != null) Object.Destroy(buttonTexture);
            if (buttonActiveTexture != null) Object.Destroy(buttonActiveTexture);
            if (rowHoverTexture != null) Object.Destroy(rowHoverTexture);
            if (rowSelectedTexture != null) Object.Destroy(rowSelectedTexture);
            if (fieldTexture != null) Object.Destroy(fieldTexture);
            panelTexture = shadeTexture = buttonTexture = buttonActiveTexture = rowHoverTexture = rowSelectedTexture = fieldTexture = null;
        }
    }
}
