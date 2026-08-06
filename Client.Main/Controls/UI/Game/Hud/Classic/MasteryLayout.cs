namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Mastery window layout exported by the hud-edit-mastery editor. Coordinates are in
    /// artwork space (mastery_bg = 1024x1023); the control scales the panel to fit the
    /// screen, matching the Skill Imprint layout.
    /// </summary>
    public static class MasteryLayout
    {
        // Panel geometry.
        public static float ArtW = 1024f;
        public static float ArtH = 1023f;
        // The panel uses the full virtual height and UiScaler maps it to the physical screen.

        // Title bar (red). The class name is centered in its left section.
        public static float ClassNameCX = 255.56f;
        public static float TitleTextY = 73.39f;
        public static float TitleFont = 15f;
        // Three right-side fields (Level / Points / EXP): center X for each field.
        public static float Field1CX = 560f;
        public static float Field2CX = 705f;
        public static float Field3CX = 855f;
        public static float FieldTextY = 74.78f;
        public static float FieldFont = 12f;
        // Height of the draggable title-bar strip.
        public static float DragBarH = 100f;
        // Close button (X) in the title bar corner.
        public static float CloseX = 957.83f;
        public static float CloseY = 57.61f;
        public static float CloseSize = 30f;

        // Three skill-tree columns.
        public static float Col1X = 33f;
        public static float Col2X = 353f;
        public static float Col3X = 688f;
        public static float ColW = 305f;
        // Colored header strip with the column title centered inside it.
        public static float ColHeaderY = 132f;
        public static float ColHeaderFont = 13f;
        // Usable node area inside the column.
        public static float ColTop = 160f;
        public static float ColBottom = 990f;

        // Column titles (the database only provides Left/Middle/Right; the editor can rename them).
        public static string ColTitleLeft = "Common Skills";
        public static string ColTitleMiddle = "Specialty I";
        public static string ColTitleRight = "Specialty II";

        // Nodes (circular socket, icon, and count).
        public static int SubCols = 5;          // Sub-columns per tree.
        public static float NodeSize = 52f;     // Socket diameter.
        public static float RowH = 92f;         // Vertical rank step.
        public static float FirstRowCY = 210f;  // Rank-one center Y.
        public static float SubColPad = 10f;    // Horizontal padding inside a column.
        public static float CountFont = 11f;    // Font for the node count.
        public static float CountDX = 2f;       // Count offset from the node edge.
        public static float CountDY = 16f;
        // Prerequisite connection lines.
        public static float LinkWidth = 2f;

        // Tooltip.
        public static float TooltipFont = 11f;
    }
}
