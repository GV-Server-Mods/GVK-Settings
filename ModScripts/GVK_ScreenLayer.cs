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
    /// Layout code fills it at its own tick rate in HUD units (-1..1, Y up, whole window; the units the HUD
    /// was laid out in under Text HUD API); Draw() replays it every frame and must be called from a session
    /// component's Draw(). Items draw in the order they were added.
    /// </summary>
    public class GvkScreenLayer
    {
        public const string FONT = "White";

        /// <summary>Text scale used when the font can't be measured (2560x1440 at the owner's UI scale).</summary>
        public const float TEXT_SCALE_DEFAULT = 0.689f;

        /// <summary>
        /// Text HUD API drew text at a size set by the window height alone: CAL_TEXT at scale 1 was
        /// CAL_HEIGHT_RATIO x window height wide (its FontTexture.cs and ModHUDMessage.GetLength; measured
        /// 0.15586 of the width at 2560x1440 in game, 2026-11-22). The screen API's font follows the UI scale,
        /// so its scale is measured against that, which keeps text the size the HUD was laid out for.
        /// </summary>
        private const string CAL_TEXT = "0123456789NESW";
        private const float CAL_HEIGHT_RATIO = 0.13854f;

        /// <summary>Screen API text scale per HUD text scale unit, from the last MeasureTextScale.</summary>
        public static float TextScale = TEXT_SCALE_DEFAULT;

        private static Vector2 _calViewport;
        private static float _calWidth = -1f;
        private static float _calScale = TEXT_SCALE_DEFAULT; // Scale for _calViewport and _calWidth

        /// <summary>Screen API text scale per HUD text scale unit for this window; re-measured when the
        /// window size or the UI scale changes. Also sets TextScale.</summary>
        public static float MeasureTextScale(Vector2 viewport)
        {
            float width = MeasureWidthPixels(CAL_TEXT, 1f);
            if (width <= 0f || viewport.Y <= 0f)
                return TEXT_SCALE_DEFAULT; // Not measurable this call (font or window not ready); the cache is kept
            if (viewport == _calViewport && Math.Abs(width - _calWidth) < 0.01f)
                return TextScale = _calScale;
            _calViewport = viewport;
            _calWidth = width;
            _calScale = CAL_HEIGHT_RATIO * viewport.Y / width;
            TextScale = _calScale;
            MyLog.Default.WriteLineAndConsole($"[GVK] Screen API text scale {TextScale:0.0000} at {viewport.X:0}x{viewport.Y:0} (font width {width:0.0} px)");
            return TextScale;
        }

        private const byte KIND_SPRITE = 0;
        private const byte KIND_TEXT = 1;
        private const byte KIND_CLIP = 2;   // Starts clipping to the rectangle at Position, Size
        private const byte KIND_UNCLIP = 3; // Ends the last clip

        private struct Item
        {
            public byte Kind;
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
        private readonly List<MyScreenSpace.MyScissorScope> _clips = new List<MyScreenSpace.MyScissorScope>(2);
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

        /// <summary>HUD point (-1..1, Y up) to the screen API's normalized space.</summary>
        public Vector2 HudToNormalized(double x, double y)
        {
            var px = new Vector2((float)((x + 1.0) * 0.5 * _viewport.X), (float)((1.0 - y) * 0.5 * _viewport.Y));
            return MyScreenSpace.PixelsToNormalized(px);
        }

        /// <summary>HUD size (2 = whole window) to the screen API's normalized size.</summary>
        public Vector2 HudSizeToNormalized(double width, double height)
        {
            var px = new Vector2((float)(width * 0.5 * _viewport.X), (float)(height * 0.5 * _viewport.Y));
            return MyScreenSpace.PixelSizeToNormalized(px);
        }

        /// <summary>The mouse cursor as a HUD point. False when there is no cursor (gamepad in use).</summary>
        public bool TryGetCursorHud(out Vector2D hud)
        {
            Vector2 px;
            if (_viewport.X <= 0f || !MyScreenSpace.TryGetCursorPositionPixels(out px))
            {
                hud = Vector2D.Zero;
                return false;
            }
            hud = new Vector2D(px.X / _viewport.X * 2.0 - 1.0, 1.0 - px.Y / _viewport.Y * 2.0);
            return true;
        }

        /// <summary>Width of text in FONT, in HUD units (2 = whole window), at a screen API scale.</summary>
        public double MeasureWidthHud(string text, float scale)
        {
            return _viewport.X > 0f ? MeasureWidthPixels(text, scale) / _viewport.X * 2.0 : 0.0;
        }

        /// <summary>One line of FONT, in HUD units, at a screen API scale.</summary>
        public double LineHeightHud(float scale)
        {
            if (_viewport.Y <= 0f) return 0.0;
            float px = MyScreenSpace.NormalizedSizeToPixels(new Vector2(0f, MyScreenSpaceText.GetLineHeight(FONT, scale))).Y;
            return px / _viewport.Y * 2.0;
        }

        /// <summary>Clips everything added after this to a rectangle centred on a HUD point, until EndClip.</summary>
        public void BeginClip(double x, double y, double width, double height)
        {
            _items.Add(new Item
            {
                Kind = KIND_CLIP,
                Position = HudToNormalized(x - width * 0.5, y + height * 0.5),
                Size = HudSizeToNormalized(width, height)
            });
        }

        public void EndClip()
        {
            _items.Add(new Item { Kind = KIND_UNCLIP });
        }

        /// <summary>Sprite centred on a HUD point; size in HUD units.</summary>
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

        /// <summary>Text anchored at a HUD point; align picks which part of the text sits there.</summary>
        public void AddText(string text, double x, double y, float scale, Color color, MyGuiDrawAlignEnum align, bool shadow)
        {
            if (string.IsNullOrEmpty(text) || scale <= 0f) return;
            _items.Add(new Item
            {
                Kind = KIND_TEXT,
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
        /// Starts a line of differently coloured text runs, top-left at a HUD point. Text HUD API did
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
                Kind = KIND_TEXT,
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

        /// <summary>Draws the list. Alpha below 1 fades everything, as a game screen's open and close fades need.</summary>
        public void Draw(MyScreenSpaceLayer layer = MyScreenSpaceLayer.BelowHud, float alpha = 1f)
        {
            bool fade = alpha < 1f;
            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var it = _items[i];
                    Color color = fade ? it.Color * alpha : it.Color;
                    switch (it.Kind)
                    {
                        case KIND_TEXT:
                            if (it.Shadow)
                            {
                                // Text HUD API's shadow is each letter 10% larger in black behind it: an even outline,
                                // about a pixel thick. Four black copies offset diagonally make the same outline.
                                var black = new Color(0, 0, 0, color.A);
                                var o = _outline;
                                MyScreenSpaceText.Draw(FONT, it.Text, it.Position + o, it.Scale, black, it.Align, float.PositiveInfinity, layer);
                                MyScreenSpaceText.Draw(FONT, it.Text, it.Position - o, it.Scale, black, it.Align, float.PositiveInfinity, layer);
                                MyScreenSpaceText.Draw(FONT, it.Text, it.Position + new Vector2(o.X, -o.Y), it.Scale, black, it.Align, float.PositiveInfinity, layer);
                                MyScreenSpaceText.Draw(FONT, it.Text, it.Position + new Vector2(-o.X, o.Y), it.Scale, black, it.Align, float.PositiveInfinity, layer);
                            }
                            MyScreenSpaceText.Draw(FONT, it.Text, it.Position, it.Scale, color, it.Align, float.PositiveInfinity, layer);
                            break;
                        case KIND_CLIP:
                            _clips.Add(MyScreenSpace.UsingScissorRectangle(new RectangleF(it.Position.X, it.Position.Y, it.Size.X, it.Size.Y), layer));
                            break;
                        case KIND_UNCLIP:
                            if (_clips.Count > 0)
                            {
                                _clips[_clips.Count - 1].Dispose();
                                _clips.RemoveAt(_clips.Count - 1);
                            }
                            break;
                        default:
                            MyScreenSpaceSprite.Draw(it.Material, it.Position, it.Size, color, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, it.Rotation, layer);
                            break;
                    }
                }
            }
            finally
            {
                // A clip left open would cut off everything else drawn this frame
                for (int i = _clips.Count - 1; i >= 0; i--)
                    _clips[i].Dispose();
                _clips.Clear();
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
