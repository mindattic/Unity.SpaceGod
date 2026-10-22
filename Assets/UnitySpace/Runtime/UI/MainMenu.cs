using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Views;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Unity.SpaceGod.UI
{
    /// <summary>
    /// The main menu, the scene the game opens on: Continue (the profile's latest save), New Game, Load Game,
    /// Profiles (switch to another, or make a new one), Settings (the pause menu's own pages) and Quit, over a
    /// slowly turning sky. The game scene is loaded only once the player picks a game; a run for the tour, a
    /// capture or <c>-continue</c> never stops here (<see cref="Bootstrap"/>). Esc goes back a page.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        enum Page
        {
            Main,
            Load,
            Profiles,
            AssetLibraries,
        }

        Page page;
        readonly PauseMenu settings = new PauseMenu();

        SaveSlots slots;
        List<string> saveNames = new List<string>();
        List<SaveHeader> saveHeaders = new List<SaveHeader>();
        SaveHeader latest;
        Vector2 saveScroll, profileScroll;

        /// <summary>The save or profile the player has asked to delete, waiting for the second click that confirms it.</summary>
        string deleteArmed;

        List<string> profiles = new List<string>();
        string newProfileName = "";

        /// <summary>A line under a page's buttons saying what went wrong (a name refused, a file that would not go).</summary>
        string notice;

        Camera skyCamera;

        bool stylesReady;
        GUIStyle panel, heading, title, text, small, button, buttonActive, row, field;
        Texture2D panelTexture, buttonTexture, buttonActiveTexture, rowTexture, fieldTexture;

        void Awake()
        {
            Application.targetFrameRate = 144;
            Time.timeScale = 1;
            DisplayOptions.ApplySaved(); // the profile's resolution, window mode and VSync
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            skyCamera = Camera.main;
            Starfield.Create(transform, shared: true);
            ReadProfile();
        }

        void Update()
        {
            if (skyCamera != null) skyCamera.transform.rotation = Quaternion.Euler(-12f, Time.unscaledTime * 1.5f, 0);
            var keyboard = Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (settings.IsOpen)
            {
                settings.Update(escape, Time.unscaledDeltaTime);
                return;
            }
            if (escape && page != Page.Main) Open(Page.Main);
        }

        /// <summary>The main menu scene is unloading (a game was picked, or the app is quitting): its own styles'
        /// DontSave textures, and the Settings page's, outlive the scene otherwise, so every trip back through the
        /// main menu would leave a round's worth behind (U.SPC-T366).</summary>
        void OnDestroy()
        {
            if (panelTexture != null) Object.Destroy(panelTexture);
            if (buttonTexture != null) Object.Destroy(buttonTexture);
            if (buttonActiveTexture != null) Object.Destroy(buttonActiveTexture);
            if (rowTexture != null) Object.Destroy(rowTexture);
            if (fieldTexture != null) Object.Destroy(fieldTexture);
            panelTexture = buttonTexture = buttonActiveTexture = rowTexture = fieldTexture = null;
            settings.DestroyOwnedTextures();
        }

        /// <summary>The profile in use and its saves, read again (after a switch, a delete, or coming back to the menu).</summary>
        void ReadProfile()
        {
            slots = new SaveSlots(ActiveProfile.Current.SavesFolder);
            saveNames = new List<string>();
            saveHeaders = new List<SaveHeader>();
            foreach (var slot in slots.AllSlots())
            {
                var h = slots.Header(slot);
                if (h == null) continue;
                saveNames.Add(slot);
                saveHeaders.Add(h);
            }
            latest = slots.Latest();
            profiles = ActiveProfile.Store.Names();
        }

        void Open(Page p)
        {
            page = p;
            notice = null;
            deleteArmed = null;
            ReadProfile();
        }

        void CreateProfile()
        {
            string name = newProfileName.Trim();
            if (!ProfileStore.IsValidName(name)) notice = "A profile name is 1 to 64 characters, without / \\ : * ? \" < > |";
            else if (ActiveProfile.Store.Exists(name)) notice = "There is a profile called " + name + " already.";
            else
            {
                ActiveProfile.Switch(name);
                newProfileName = "";
                Open(Page.Main);
            }
        }

        void Delete(string slot)
        {
            try
            {
                slots.Delete(slot);
                notice = null;
            }
            catch (IOException e)
            {
                notice = "Could not delete " + UnitySpaceApp.SlotName(slot) + ": " + e.Message;
            }
            deleteArmed = null;
            ReadProfile();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- drawing

        const float RowH = 26;

        void OnGUI()
        {
            EnsureStyles();
            if (settings.IsOpen)
            {
                settings.Draw();
                return;
            }
            switch (page)
            {
                case Page.Load: DrawLoad(); break;
                case Page.Profiles: DrawProfiles(); break;
                case Page.AssetLibraries: DrawAssetLibraries(); break;
                default: DrawMain(); break;
            }
        }

        static Rect Centred(float w, float h) => new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        void DrawMain()
        {
            const float w = 380, h = 420;
            var r = Centred(w, h);
            GUI.Label(new Rect(r.x, r.y - 70, w, 48), "UNITYSPACE", heading);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "PROFILE  ·  " + ActiveProfile.Current.Name.ToUpperInvariant(), title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 32),
                latest == null ? "No saves yet" : "Latest: " + UnitySpaceApp.SaveTitle(latest.Slot, latest) + "  ·  " + UnitySpaceApp.DescribeSave(latest), small);

            float y = r.y + 80, bw = w - 40;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && latest != null;
            if (GUI.Button(new Rect(r.x + 20, y, bw, 38), "Continue", latest != null ? buttonActive : button)) GameLaunch.Load(latest.Slot);
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(r.x + 20, y + 48, bw, 38), "New Game", latest == null ? buttonActive : button)) GameLaunch.NewGame();
            GUI.enabled = enabled && saveNames.Count > 0;
            if (GUI.Button(new Rect(r.x + 20, y + 96, bw, 38), "Load Game", button)) Open(Page.Load);
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(r.x + 20, y + 144, bw, 38), "Profiles", button)) Open(Page.Profiles);
            if (GUI.Button(new Rect(r.x + 20, y + 192, bw, 38), "Settings", button)) settings.OpenSettings();
            if (GUI.Button(new Rect(r.x + 20, y + 240, bw, 38), "Asset Library POC", button)) Open(Page.AssetLibraries);
            if (GUI.Button(new Rect(r.x + 20, y + 288, bw, 38), "Quit", button)) Quit();
        }

        void DrawLoad()
        {
            const float w = 760;
            float listH = Mathf.Clamp(saveNames.Count, 1, 14) * RowH;
            float h = 134 + listH;
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "LOAD GAME  ·  " + ActiveProfile.Current.Name.ToUpperInvariant(), title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 18), "Newest first  ·  Esc to go back", small);

            var view = new Rect(r.x + 20, r.y + 66, w - 40, listH + 2);
            var content = new Rect(0, 0, view.width - (saveNames.Count > 14 ? 18 : 0), Mathf.Max(1, saveNames.Count) * RowH);
            saveScroll = GUI.BeginScrollView(view, saveScroll, content);
            if (saveNames.Count == 0) GUI.Label(new Rect(6, 4, content.width, 18), "This profile has no saves.", small);
            var order = new List<int>();
            for (int i = 0; i < saveNames.Count; i++) order.Add(i);
            order.Sort((a, b) => saveHeaders[b].SavedUtc.CompareTo(saveHeaders[a].SavedUtc));
            for (int n = 0; n < order.Count; n++)
            {
                int i = order[n];
                string slot = saveNames[i];
                var hdr = saveHeaders[i];
                var row0 = new Rect(0, n * RowH, content.width, RowH - 2);
                GUI.Box(row0, GUIContent.none, row);
                GUI.Label(new Rect(row0.x + 6, row0.y + 3, 150, 20), UnitySpaceApp.SaveTitle(slot, hdr), text);
                GUI.Label(new Rect(row0.x + 160, row0.y + 4, row0.width - 330, 20),
                    UnitySpaceApp.DescribeSave(hdr) + "   ·   saved " + hdr.SavedUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture), small);
                if (GUI.Button(new Rect(row0.xMax - 156, row0.y + 2, 74, 20), "Load", buttonActive)) GameLaunch.Load(slot);
                if (GUI.Button(new Rect(row0.xMax - 78, row0.y + 2, 74, 20), deleteArmed == slot ? "Sure?" : "Delete", button))
                {
                    if (deleteArmed != slot) deleteArmed = slot;
                    else
                    {
                        Delete(slot);
                        GUIUtility.ExitGUI(); // the list just changed under this loop
                    }
                }
            }
            GUI.EndScrollView();

            if (!string.IsNullOrEmpty(notice)) GUI.Label(new Rect(r.x + 20, r.yMax - 44, w - 170, 36), notice, small);
            if (GUI.Button(new Rect(r.xMax - 130, r.yMax - 46, 110, 30), "Back", button)) Open(Page.Main);
        }

        void DrawProfiles()
        {
            const float w = 520;
            float listH = Mathf.Clamp(profiles.Count, 1, 10) * RowH;
            float h = 250 + listH;
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "PROFILES", title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 18), "Each profile keeps its own saves and settings  ·  Esc to go back", small);

            var view = new Rect(r.x + 20, r.y + 66, w - 40, listH + 2);
            var content = new Rect(0, 0, view.width - (profiles.Count > 10 ? 18 : 0), Mathf.Max(1, profiles.Count) * RowH);
            profileScroll = GUI.BeginScrollView(view, profileScroll, content);
            for (int i = 0; i < profiles.Count; i++)
            {
                string name = profiles[i];
                bool current = name == ActiveProfile.Current.Name;
                var row0 = new Rect(0, i * RowH, content.width, RowH - 2);
                GUI.Box(row0, GUIContent.none, row);
                GUI.Label(new Rect(row0.x + 6, row0.y + 3, row0.width - 100, 20), current ? name + "   (in use)" : name, current ? title : text);
                if (!current && GUI.Button(new Rect(row0.xMax - 78, row0.y + 2, 74, 20), "Use", buttonActive))
                {
                    ActiveProfile.Switch(name);
                    Open(Page.Main);
                    GUIUtility.ExitGUI();
                }
            }
            GUI.EndScrollView();

            float y = view.yMax + 18;
            GUI.Label(new Rect(r.x + 20, y, w - 40, 20), "New profile", text);
            GUI.SetNextControlName("NewProfileName");
            var e = Event.current;
            bool submit = e.type == EventType.KeyDown && newProfileName.Trim().Length > 0
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r');
            newProfileName = GUI.TextField(new Rect(r.x + 20, y + 24, w - 170, 28), newProfileName, 64, field);
            if (GUI.Button(new Rect(r.xMax - 140, y + 24, 120, 28), "Create", buttonActive) || submit)
            {
                CreateProfile();
                if (submit) e.Use();
            }
            if (!string.IsNullOrEmpty(notice)) GUI.Label(new Rect(r.x + 20, y + 58, w - 170, 36), notice, small);
            if (GUI.Button(new Rect(r.xMax - 130, r.yMax - 46, 110, 30), "Back", button)) Open(Page.Main);
        }

        /// <summary>Whether a candidate's demo scene is in the build, so the menu can load it (U.SPC-T378).</summary>
        static bool DemoInstalled(AssetCandidate c) => SceneUtility.GetBuildIndexByScenePath(c.DemoScenePath) >= 0;

        void DrawAssetLibraries()
        {
            const float w = 760;
            var list = AssetLibraryCandidates.All;
            float h = 150 + list.Count * 52;
            var r = Centred(w, h);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 22), "ASSET LIBRARY POC", title);
            GUI.Label(new Rect(r.x + 20, r.y + 38, w - 40, 32),
                "Candidate Asset Store packages, to judge in game before buying for real. Buy and import a package, add its demo scene to the build at the path shown, then launch it here  ·  Esc to go back", small);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                bool installed = DemoInstalled(c);
                var row0 = new Rect(r.x + 20, r.y + 78 + i * 52, w - 40, 48);
                GUI.Box(row0, GUIContent.none, row);
                GUI.Label(new Rect(row0.x + 6, row0.y + 3, 300, 20), c.Name, text);
                GUI.Label(new Rect(row0.x + 6, row0.y + 22, row0.width - 130, 24),
                    c.Purpose + "  ·  Unity 6: " + AssetCandidate.Describe(c.Unity6) + ", URP: " + AssetCandidate.Describe(c.Urp)
                    + ", HDRP: " + AssetCandidate.Describe(c.Hdrp) + "  ·  " + (installed ? "demo installed" : "not installed (" + c.DemoScenePath + ")"), small);
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && installed;
                if (GUI.Button(new Rect(row0.xMax - 110, row0.y + 10, 104, 28), "Launch demo", installed ? buttonActive : button))
                    SceneManager.LoadScene(c.DemoScenePath);
                GUI.enabled = enabled;
            }
            if (GUI.Button(new Rect(r.xMax - 130, r.yMax - 46, 110, 30), "Back", button)) Open(Page.Main);
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            // The pause menu's look (and so the HUD's), over the sky.
            panelTexture = Hud.Solid(new Color(0.03f, 0.05f, 0.08f, 0.88f), new Color(0.3f, 0.6f, 0.75f, 0.6f));
            buttonTexture = Hud.Solid(new Color(0.12f, 0.18f, 0.24f, 0.95f), new Color(0.3f, 0.5f, 0.6f, 0.6f));
            buttonActiveTexture = Hud.Solid(new Color(0.1f, 0.3f, 0.42f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));
            rowTexture = Hud.Solid(new Color(0.08f, 0.12f, 0.16f, 0.7f), Color.clear);
            fieldTexture = Hud.Solid(new Color(0.02f, 0.03f, 0.05f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));

            panel = new GUIStyle { normal = { background = panelTexture }, border = new RectOffset(2, 2, 2, 2) };
            heading = new GUIStyle { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            text = new GUIStyle { fontSize = 13, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            small = new GUIStyle { fontSize = 11, normal = { textColor = Hud.Dim }, wordWrap = true, clipping = TextClipping.Clip };
            button = new GUIStyle
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonActiveTexture, textColor = Color.white },
                active = { background = buttonActiveTexture, textColor = Hud.Accent },
            };
            buttonActive = new GUIStyle(button) { normal = { background = buttonActiveTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            row = new GUIStyle { normal = { background = rowTexture } };
            field = new GUIStyle
            {
                fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 0, 0), border = new RectOffset(2, 2, 2, 2),
                clipping = TextClipping.Clip,
                normal = { background = fieldTexture, textColor = Color.white },
                focused = { background = fieldTexture, textColor = Color.white },
                hover = { background = fieldTexture, textColor = Color.white },
            };
        }
    }
}
