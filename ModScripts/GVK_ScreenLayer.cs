using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// Draw list for the vanilla screen-space API (MyScreenSpaceSprite / MyScreenSpaceText, SE 1.211).
    /// Layout code fills it at its own tick rate using Text HUD API units (-1..1, Y up, whole window);
    /// Draw() replays it every frame and must be called from a session component's Draw().
    /// Items draw in the order they were added.
    /// </summary>
    public class GvkScreenLayer
    {
        public const string FONT = "White";

        /// <summary>
        /// Screen API text scale per Text HUD API scale unit. Measured in game 2026-11-22 at 2560x1440 (Text HUD API
        /// width 0.15586 vs screen API 289.6 px); it holds at any resolution but only at that UI scale.
        /// </summary>
        public const float TEXT_SCALE_DEFAULT = 0.689f;

        /// <summary>Renderer switch (/gvk ss) shared by the navigation HUD and the KOTH panel.</summary>
        public static bool UseScreenApi = true;

        /// <summary>Current text scale; the navigation HUD re-measures it against Text HUD API when it can.</summary>
        public static float TextScale = TEXT_SCALE_DEFAULT;

        private struct Item
        {
            public bool IsText;
            public MyStringId Material;
            public string Text;
            public Vector2 Position; // Normalized: sprite centre or text anchor
            public Vector2 Size;     // Normalized sprite size
            public float Scale;      // Text scale
            public float Rotation;   // Clockwise radians
            public Color Color;
            public MyGuiDrawAlignEnum Align;
            public bool Shadow;
        }

        private readonly List<Item> _items = new List<Item>(128);
        private Vector2 _viewport;
        private Vector2 _outline; // Text outline thickness, normalized

        public int Count => _items.Count;

        /// <summary>Clears the list and sets the window size for this build. False if the size is unusable.</summary>
        public bool Begin(Vector2 viewportPixels)
        {
            _items.Clear();
            if (viewportPixels.X <= 0f || viewportPixels.Y <= 0f) return false;
            _viewport = viewportPixels;
            float px = Math.Max(1f, (float)Math.Round(viewportPixels.Y / 1080f));
            _outline = MyScreenSpace.PixelSizeToNormalized(new Vector2(px, px));
            return true;
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>Text HUD API point (-1..1, Y up) to the screen API's normalized space.</summary>
        public Vector2 HudToNormalized(double x, double y)
        {
            var px = new Vector2((float)((x + 1.0) * 0.5 * _viewport.X), (float)((1.0 - y) * 0.5 * _viewport.Y));
            return MyScreenSpace.PixelsToNormalized(px);
        }

        /// <summary>Text HUD API size (2 = whole window) to the screen API's normalized size.</summary>
        public Vector2 HudSizeToNormalized(double width, double height)
        {
            var px = new Vector2((float)(width * 0.5 * _viewport.X), (float)(height * 0.5 * _viewport.Y));
            return MyScreenSpace.PixelSizeToNormalized(px);
        }

        /// <summary>Sprite centred on a Text HUD API point; size in Text HUD API units.</summary>
        public void AddSprite(MyStringId material, double x, double y, double width, double height, Color color, float rotation = 0f)
        {
            if (width <= 0.0 || height <= 0.0) return;
            _items.Add(new Item
            {
                Material = material,
                Position = HudToNormalized(x, y),
                Size = HudSizeToNormalized(width, height),
                Color = color,
                Rotation = rotation
            });
        }

        /// <summary>Text anchored at a Text HUD API point; align picks which part of the text sits there.</summary>
        public void AddText(string text, double x, double y, float scale, Color color, MyGuiDrawAlignEnum align, bool shadow)
        {
            if (string.IsNullOrEmpty(text) || scale <= 0f) return;
            _items.Add(new Item
            {
                IsText = true,
                Text = text,
                Position = HudToNormalized(x, y),
                Scale = scale,
                Color = color,
                Align = align,
                Shadow = shadow
            });
        }

        private Vector2 _runPos;
        private float _runStartX;
        private float _runScale;
        private bool _runShadow;

        /// <summary>
        /// Starts a line of differently coloured text runs, top-left at a Text HUD API point. Text HUD API did
        /// this with colour tags; the screen API has none, so each run is its own draw placed after the last.
        /// </summary>
        public void BeginRuns(double x, double y, float scale, bool shadow)
        {
            _runPos = HudToNormalized(x, y);
            _runStartX = _runPos.X;
            _runScale = scale;
            _runShadow = shadow;
        }

        public void AddRun(string text, Color color)
        {
            if (string.IsNullOrEmpty(text) || _runScale <= 0f) return;
            _items.Add(new Item
            {
                IsText = true,
                Text = text,
                Position = _runPos,
                Scale = _runScale,
                Color = color,
                Align = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
                Shadow = _runShadow
            });
            _measure.Clear().Append(text);
            _runPos.X += MyScreenSpaceText.Measure(FONT, _measure, _runScale).X;
        }

        /// <summary>Moves the runs down one line of the font, back to the line's start.</summary>
        public void NewLine()
        {
            _runPos.X = _runStartX;
            _runPos.Y += MyScreenSpaceText.GetLineHeight(FONT, _runScale);
        }

        public void Draw(MyScreenSpaceLayer layer = MyScreenSpaceLayer.BelowHud)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.IsText)
                {
                    if (it.Shadow)
                    {
                        // Text HUD API's shadow is each letter 10% larger in black behind it: an even outline,
                        // about a pixel thick. Four black copies offset diagonally make the same outline.
                        var black = new Color(0, 0, 0, it.Color.A);
                        var o = _outline;
                        MyScreenSpaceText.Draw(FONT, it.Text, it.Position + o, it.Scale, black, it.Align, float.PositiveInfinity, layer);
                        MyScreenSpaceText.Draw(FONT, it.Text, it.Position - o, it.Scale, black, it.Align, float.PositiveInfinity, layer);
                        MyScreenSpaceText.Draw(FONT, it.Text, it.Position + new Vector2(o.X, -o.Y), it.Scale, black, it.Align, float.PositiveInfinity, layer);
                        MyScreenSpaceText.Draw(FONT, it.Text, it.Position + new Vector2(-o.X, o.Y), it.Scale, black, it.Align, float.PositiveInfinity, layer);
                    }
                    MyScreenSpaceText.Draw(FONT, it.Text, it.Position, it.Scale, it.Color, it.Align, float.PositiveInfinity, layer);
                }
                else
                {
                    MyScreenSpaceSprite.Draw(it.Material, it.Position, it.Size, it.Color, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, it.Rotation, layer);
                }
            }
        }

        /// <summary>True while the player has the HUD hidden; screen-space layers keep drawing unless we check.</summary>
        public static bool IsHudHidden()
        {
            var cfg = MyAPIGateway.Session?.Config;
            return cfg != null && cfg.MinimalHud;
        }

        private static readonly StringBuilder _measure = new StringBuilder(32);

        /// <summary>Width in window pixels of text in FONT at the given screen API scale.</summary>
        public static float MeasureWidthPixels(string text, float scale)
        {
            _measure.Clear().Append(text);
            return MyScreenSpaceText.MeasurePixels(FONT, _measure, scale).X;
        }
    }
}
