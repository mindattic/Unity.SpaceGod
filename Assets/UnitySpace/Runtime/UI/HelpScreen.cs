using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod.UI
{
    /// <summary>
    /// The Help screen (U.SPC-T494): the whole screen, every key, control and help line of <see cref="ControlsHelp"/>
    /// in its groups, flowed into as many columns as the width allows (scrolling when it runs past the bottom).
    /// The main menu opens it from Help, the game from H / F1 and the Esc menu's Help.
    /// </summary>
    public sealed class HelpScreen
    {
        const float Margin = 40, Gutter = 32, KeyW = 132, LineGap = 3, SectionGap = 22, HeadingH = 24;

        bool stylesReady;
        GUIStyle panel, title, heading, keys, text, small, button;
        Texture2D panelTexture, buttonTexture, buttonActiveTexture;
        Vector2 scroll;

        /// <summary>Draws the screen; true once the player has clicked Back (Esc and the keys that open it are the owner's to handle).</summary>
        public bool Draw(string closeHint)
        {
            EnsureStyles();
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.Box(screen, GUIContent.none, panel);
            GUI.Label(new Rect(Margin, 22, screen.width - 2 * Margin, 30), "HELP", title);
            GUI.Label(new Rect(Margin, 54, screen.width - 2 * Margin, 18), "Every key and control, by what it is for  ·  " + closeHint, small);

            var view = new Rect(Margin, 86, screen.width - 2 * Margin, screen.height - 86 - 64);
            int columns = view.width >= 1500 ? 3 : view.width >= 960 ? 2 : 1;
            float columnW = (view.width - 16 - Gutter * (columns - 1)) / columns;

            // Each section goes to the shortest column so far, keeping the columns level.
            var sections = ControlsHelp.Sections;
            var heights = new float[sections.Count];
            for (int i = 0; i < sections.Count; i++) heights[i] = SectionHeight(sections[i], columnW);
            var columnOf = new int[sections.Count];
            var tops = new float[sections.Count];
            var filled = new float[columns];
            for (int i = 0; i < sections.Count; i++)
            {
                int c = 0;
                for (int k = 1; k < columns; k++)
                    if (filled[k] < filled[c] - 1) c = k;
                columnOf[i] = c;
                tops[i] = filled[c];
                filled[c] += heights[i] + SectionGap;
            }
            float contentH = 0;
            foreach (var f in filled) contentH = Mathf.Max(contentH, f);

            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, view.width - 16, contentH));
            for (int i = 0; i < sections.Count; i++)
                DrawSection(sections[i], columnOf[i] * (columnW + Gutter), tops[i], columnW);
            GUI.EndScrollView();

            return GUI.Button(new Rect(screen.width - Margin - 120, screen.height - 50, 120, 32), "Back", button);
        }

        float SectionHeight(HelpSection section, float w)
        {
            float h = HeadingH;
            foreach (var line in section.Lines) h += LineHeight(line, w) + LineGap;
            return h;
        }

        float LineHeight(HelpLine line, float w) =>
            Mathf.Max(18, text.CalcHeight(new GUIContent(line.Text), w - KeyW));

        void DrawSection(HelpSection section, float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, HeadingH - 4), section.Title, heading);
            GUI.DrawTexture(new Rect(x, y + HeadingH - 5, w, 1), buttonActiveTexture);
            y += HeadingH;
            foreach (var line in section.Lines)
            {
                float h = LineHeight(line, w);
                if (line.Keys.Length > 0) GUI.Label(new Rect(x, y, KeyW - 8, h), line.Keys, keys);
                GUI.Label(new Rect(x + KeyW, y, w - KeyW, h), line.Text, line.Keys.Length > 0 ? text : small);
                y += h + LineGap;
            }
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            // The HUD's look, near opaque: the screen is the whole view.
            panelTexture = Hud.Solid(new Color(0.02f, 0.035f, 0.06f, 0.96f), Color.clear);
            buttonTexture = Hud.Solid(new Color(0.12f, 0.18f, 0.24f, 0.95f), new Color(0.3f, 0.5f, 0.6f, 0.6f));
            buttonActiveTexture = Hud.Solid(new Color(0.1f, 0.3f, 0.42f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));
            panel = new GUIStyle { normal = { background = panelTexture } };
            title = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            heading = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Hud.Accent } };
            keys = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = true };
            text = new GUIStyle { fontSize = 13, normal = { textColor = new Color(0.86f, 0.9f, 0.94f) }, wordWrap = true };
            small = new GUIStyle(text) { normal = { textColor = Hud.Dim }, fontStyle = FontStyle.Italic };
            button = new GUIStyle
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonActiveTexture, textColor = Color.white },
                active = { background = buttonActiveTexture, textColor = Hud.Accent },
            };
        }

        /// <summary>The screen's own textures go with whatever owns it (U.SPC-T366): DontSave outlives the scene otherwise.</summary>
        public void DestroyOwnedTextures()
        {
            if (panelTexture != null) Object.Destroy(panelTexture);
            if (buttonTexture != null) Object.Destroy(buttonTexture);
            if (buttonActiveTexture != null) Object.Destroy(buttonActiveTexture);
            panelTexture = buttonTexture = buttonActiveTexture = null;
            stylesReady = false;
        }
    }
}
