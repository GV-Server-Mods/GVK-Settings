using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// The nav HUD's colour palette, one setting for map, compass, minimap, radar and KOTH panel alike.
    /// Standard keeps the game's own green friend and red foe. The two colour-blind palettes were picked by simulating
    /// protanopia and deuteranopia (Red-green safe) or tritanopia (Blue-yellow safe) with the Machado 2009 matrices and
    /// keeping every pair of relation colours, and the faint territory tints, far apart by CIEDE2000 under each
    /// (owner, 2026-11-24). Under deuteranopia Standard's friendly and hostile territory tints come out nearly identical.
    /// </summary>
    public static class GvkPalette
    {
        public const int STANDARD = 0, RED_GREEN = 1, BLUE_YELLOW = 2, COUNT = 3;
        public static readonly string[] NAMES = { "Standard", "Red-green safe", "Blue-yellow safe" };

        public static int Current { get; private set; }

        // Relations: your own, your faction's and Friendly (green in Standard), hostile, NPC neutral, neutral, KOTH
        public static Color Own, Friendly, Hostile, Npc, Neutral, Koth;
        public static Color SettingOn, SettingOff;        // Settings screen On / Off values; Off also marks a removal
        public static readonly Color[] ZoneText = new Color[4];   // Zone names in text
        public static readonly Color[] ZoneAccent = new Color[4]; // Zone stripes, bars and radar direction lines

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
                    Own = new Color(150, 200, 255);
                    Friendly = new Color(40, 120, 255);
                    Hostile = new Color(230, 100, 0);    // Was 255,150,20; Zone 3's orange keeps it apart from Zone 2's amber
                    Npc = new Color(255, 240, 170);
                    Koth = new Color(204, 121, 167);
                    SettingOn = new Color(100, 170, 255);
                    SettingOff = new Color(255, 150, 20);
                    SetZones(new Color(230, 230, 230), new Color(255, 200, 0));
                    break;
                case BLUE_YELLOW:
                    Own = new Color(255, 110, 230);
                    Friendly = new Color(60, 230, 90);
                    Hostile = new Color(255, 50, 50);
                    Npc = new Color(255, 190, 0);
                    Koth = new Color(180, 120, 255);
                    SettingOn = new Color(60, 230, 90);
                    SettingOff = new Color(255, 50, 50);
                    SetZones(new Color(230, 230, 230), new Color(255, 150, 170));
                    break;
                default:
                    Own = new Color(100, 230, 255);
                    Friendly = new Color(100, 240, 100);
                    Hostile = new Color(255, 60, 60);
                    Npc = new Color(240, 180, 50);
                    Koth = new Color(255, 128, 31);
                    SettingOn = new Color(80, 255, 120);
                    SettingOff = new Color(255, 90, 90);
                    SetZones(new Color(255, 230, 50), new Color(255, 165, 0));
                    break;
            }
        }

        /// <summary>
        /// One colour per zone for text, lines and bars alike. Zone 0 is the Friendly colour and Zone 3 the Hostile one
        /// (owner, 2026-11-25: "use one red", "make the greens consistent (friendly and Z0)"); until then each zone had a
        /// text colour and a slightly different accent, and neither matched the relation colours.
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
}
