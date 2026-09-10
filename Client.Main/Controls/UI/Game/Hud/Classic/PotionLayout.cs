namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Potion Imprint layout exported by the hud-edit-potion editor.
    /// It remains separate from ImprintLayout so both screens can evolve independently.
    /// X/Y values are relative to the panel origin and scale to the screen height at runtime.
    /// </summary>
    public static class PotionLayout
    {
        public static int PanelMaxH = 600;
        public static int PanelW = 304;
        public static int PanelH = 600;

        // Potion grid (five columns by four rows, configurable in the editor).
        public static int GridCols = 5;
        public static int GridRows = 4;
        public static int GridGap = 0;

        // PanelX/PanelY move the entire window from the screen center. Because the panel
        // uses the full height, a positive PanelY would move the footer off-screen. The
        // editor therefore exports zero; its stage position is workspace-only.
        public static bool PanelEnabled = true;
        public static float PanelX = 0f;
        public static float PanelY = 0f;

        public static bool Rect2Enabled = false; // Hidden.
        public static float Rect2X = 29f;
        public static float Rect2Y = 134f;
        public static float Rect2W = 244f;
        public static float Rect2H = 194f;

        public static bool Rect3Enabled = true;
        public static float Rect3X = 29f;
        public static float Rect3Y = 359f;
        public static float Rect3W = 244f;
        public static float Rect3H = 155f;

        public static bool TitleEnabled = true;
        public static float TitleX = 95f;
        public static float TitleY = 44f;
        public static float TitleW = 120f;
        public static float TitleH = 22f;
        public static float TitleFont = 14f;

        public static bool CloseEnabled = true;
        public static float CloseX = 247f;
        public static float CloseY = 42f;
        public static float CloseW = 26f;
        public static float CloseH = 26f;
        public static float CloseFont = 14f;

        public static bool TabBasicEnabled = true;
        public static float TabBasicX = 48f;
        public static float TabBasicY = 104f;
        public static float TabBasicW = 125f;
        public static float TabBasicH = 30f;
        public static float TabBasicFont = 11f;

        public static bool GridEnabled = true;
        public static float GridX = 30f;
        public static float GridY = 134f;
        public static float GridW = 49f;
        public static float GridH = 49f;

        public static bool DarkCard2Enabled = true;
        public static float DarkCard2X = 30f;
        public static float DarkCard2Y = 360f;
        public static float DarkCard2W = 242f;
        public static float DarkCard2H = 151f;

        public static bool DarkCard3Enabled = true;
        public static float DarkCard3X = 30f;
        public static float DarkCard3Y = 135f;
        public static float DarkCard3W = 243f;
        public static float DarkCard3H = 192f;

        // Slots 1..5 run from left to right, matching the hotbar order. This avoids the
        // visual order mismatch caused by the older Skill Imprint arc layout.
        public static bool Slot1Enabled = true;
        public static float Slot1X = 38f; public static float Slot1Y = 379f; public static float Slot1W = 44f; public static float Slot1H = 44f;
        public static bool Slot2Enabled = true;
        public static float Slot2X = 83f; public static float Slot2Y = 378f; public static float Slot2W = 44f; public static float Slot2H = 44f;
        public static bool Slot3Enabled = true;
        public static float Slot3X = 128f; public static float Slot3Y = 379f; public static float Slot3W = 44f; public static float Slot3H = 44f;
        public static bool Slot4Enabled = true;
        public static float Slot4X = 173f; public static float Slot4Y = 379f; public static float Slot4W = 44f; public static float Slot4H = 44f;
        public static bool Slot5Enabled = true;
        public static float Slot5X = 218f; public static float Slot5Y = 379f; public static float Slot5W = 44f; public static float Slot5H = 44f;

        public static bool ResetEnabled = true;
        public static float ResetX = 214f;
        public static float ResetY = 438f;
        public static float ResetW = 51f;
        public static float ResetH = 46f;

        public static bool ResetTextEnabled = true;
        public static float ResetTextX = 212f;
        public static float ResetTextY = 482f;
        public static float ResetTextW = 55f;
        public static float ResetTextH = 18f;

        // Informational message (multi-line, wrapped to width W).
        public static bool InfoTextEnabled = true;
        public static float InfoTextX = 41f;
        public static float InfoTextY = 439f;
        public static float InfoTextW = 171f;
        public static float InfoTextH = 58f;
        public static float InfoTextFont = 11f;
        public static string InfoTextMessage = "For consumables to be available, they must be in your char inventory.";

        public static bool SaveEnabled = true;
        public static float SaveX = 47f;
        public static float SaveY = 531f;
        public static float SaveW = 104f;
        public static float SaveH = 41f;
        public static float SaveFont = 12f;

        public static bool CancelEnabled = true;
        public static float CancelX = 155f;
        public static float CancelY = 531f;
        public static float CancelW = 102f;
        public static float CancelH = 41f;
        public static float CancelFont = 12f;
    }
}
