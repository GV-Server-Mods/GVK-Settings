using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// The nav HUD's colour palette, one setting for map, compass, radar and KOTH panel alike: each meaning has one colour,
    /// used everywhere it shows (owner, 2026-11-25: "consistent and distinct"). Standard takes Own, Friendly and Hostile
    /// from the vanilla HUD's signal markers (MyHudMarkerRender) so ours and Keen's match on screen. The rest, and the two
    /// colour-blind palettes, were picked by simulating protanopia and deuteranopia (Red-green safe) or tritanopia
    /// (Blue-yellow safe) with the Machado 2009 matrices and keeping every pair far apart by CIEDE2000: at least 24
    /// in Standard, 17 in Red-green, 21 in Blue-yellow. Neutral NPCs are Neutral white, as in vanilla.
    /// </summary>
    public static class GvkPalette
    {
        public const int STANDARD = 0, RED_GREEN = 1, BLUE_YELLOW = 2, COUNT = 3;
        public static readonly string[] NAMES = { "Standard", "Red-green safe", "Blue-yellow safe" };

        public static int Current { get; private set; }

        // Relations: your own (also your player arrow), your faction's and Friendly, hostile, neutral (NPC or player), KOTH
        public static Color Own, Friendly, Hostile, Neutral, Koth;
        public static Color SettingOn, SettingOff;        // Settings screen On / Off values (Friendly / Hostile); Off also marks a removal
        public static readonly Color[] ZoneText = new Color[4];   // Zone names in text
        public static readonly Color[] ZoneAccent = new Color[4]; // Zone stripes, bars, map lines and radar direction lines

        static GvkPalette()
        {
            Apply(STANDARD);
        }

        public static void Apply(int palette)
        {
            if (palette < 0 || palette >= COUNT) palette = STANDARD;
            Current = palette;
            Neutral = Color.White;
            switch (palette)
            {
                case RED_GREEN:
                    Own = new Color(105, 195, 255);
                    Friendly = new Color(40, 120, 255);
                    Hostile = new Color(230, 100, 0);
                    Koth = new Color(240, 60, 120);
                    SetZones(new Color(150, 180, 180), new Color(255, 240, 0));
                    break;
                case BLUE_YELLOW:
                    Own = new Color(90, 135, 210);
                    Friendly = new Color(60, 230, 90);
                    Hostile = new Color(255, 50, 50);
                    Koth = new Color(180, 90, 255);
                    SetZones(new Color(165, 165, 165), new Color(240, 165, 180));
                    break;
                default:
                    Own = new Color(117, 201, 241);      // Vanilla HUD owner
                    Friendly = new Color(101, 178, 90);  // Vanilla HUD friends and faction
                    Hostile = new Color(227, 62, 63);    // Vanilla HUD enemies
                    Koth = new Color(195, 75, 255);      // Was orange 255,128,31, too close to Zone 2
                    SetZones(new Color(255, 255, 0), new Color(240, 150, 15));
                    break;
            }
            SettingOn = Friendly;
            SettingOff = Hostile;
        }

        /// <summary>
        /// One colour per zone for text, lines and bars alike. Zone 0 is the Friendly colour and Zone 3 the Hostile one
        /// (owner, 2026-11-25: "use one red", "make the greens consistent (friendly and Z0)").
        /// </summary>
        private static void SetZones(Color z1, Color z2)
        {
            ZoneText[0] = Friendly;
            ZoneText[1] = z1;
            ZoneText[2] = z2;
            ZoneText[3] = Hostile;
            for (int z = 0; z < 4; z++) ZoneAccent[z] = ZoneText[z];
        }
    }

    /// <summary>
    /// Screen chrome shared by the map, radar, compass, zone bar, settings screen and KOTH panel. These colours carry no
    /// meaning and stay the same in every palette; before 2026-11-25 each view had its own near-identical copies.
    /// </summary>
    public static class GvkUi
    {
        public static readonly Color Accent = new Color(187, 233, 246);    // Keen's UI cyan: radar grid, selection bars, choices
        public static readonly Color Title = new Color(255, 220, 0);       // Screen titles, compass cardinals, settings values
        public static readonly Color Action = new Color(255, 180, 50);     // Settings screen action rows
        public static readonly Color Text = new Color(220, 230, 240);      // Labels and hints
        public static readonly Color TextDim = new Color(180, 195, 210);   // Secondary text: header fields, distances, Cancel
        public static readonly Color Separator = new Color(100, 140, 180); // Zone bar separators
        public static readonly Color Space = new Color(255, 80, 255);      // Above Kharak's gravity, in place of a zone colour
        public static readonly Color Dim = new Color(150, 150, 150, 150);  // A site with nothing there
        public static readonly Color Panel = new Color(15, 22, 30, 240);   // Settings panel, menus and tooltips
        public static readonly Color PanelDark = new Color(10, 16, 24, 250); // Headers and footers
        public static readonly Color HudPanel = new Color(10, 16, 24, 220);  // KOTH panel over the game view
        public static readonly Color Button = new Color(41, 54, 62);
        public static readonly Color ButtonDim = new Color(31, 41, 48);    // Map filter on, settings tab idle
        public static readonly Color ButtonOff = new Color(18, 23, 27);    // Map filter off
        public static readonly Color ButtonHover = new Color(70, 95, 112);
        public static readonly Color Track = new Color(90, 110, 125);      // Slider and bar tracks
    }
}
