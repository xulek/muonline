namespace Client.Main.Controls.UI.Game.Hud;

/// <summary>
/// Geometry for the Classic inventory. All coordinates are local to the inventory window.
/// Modern continues to use its existing procedural layout.
/// </summary>
public static class InventoryLayout
{
    public static int PanelW { get; set; } = 396;
    public static int PanelH { get; set; } = 716;
    public static int DragBarH { get; set; } = 82;

    public static int CloseX { get; set; } = 332;
    public static int CloseY { get; set; } = 50;
    public static int CloseW { get; set; } = 28;
    public static int CloseH { get; set; } = 28;

    public static int EquipmentX { get; set; } = 16;
    public static int EquipmentY { get; set; } = 91;
    public static int EquipmentW { get; set; } = 364;
    public static int EquipmentH { get; set; } = 314;

    public static int PaperdollX { get; set; } = 38;
    public static int PaperdollY { get; set; } = 98;
    public static int PaperdollW { get; set; } = 319;
    public static int PaperdollH { get; set; } = 284;

    public static int PetX { get; set; } = 24;
    public static int PetY { get; set; } = 99;
    public static int PetW { get; set; } = 70;
    public static int PetH { get; set; } = 70;

    public static int PendantX { get; set; } = 104;
    public static int PendantY { get; set; } = 118;
    public static int PendantW { get; set; } = 50;
    public static int PendantH { get; set; } = 50;

    public static int HelmetX { get; set; } = 163;
    public static int HelmetY { get; set; } = 99;
    public static int HelmetW { get; set; } = 70;
    public static int HelmetH { get; set; } = 70;

    public static int WingsX { get; set; } = 242;
    public static int WingsY { get; set; } = 99;
    public static int WingsW { get; set; } = 131;
    public static int WingsH { get; set; } = 70;

    public static int WeaponX { get; set; } = 24;
    public static int WeaponY { get; set; } = 170;
    public static int WeaponW { get; set; } = 70;
    public static int WeaponH { get; set; } = 95;

    public static int ArmorX { get; set; } = 163;
    public static int ArmorY { get; set; } = 169;
    public static int ArmorW { get; set; } = 70;
    public static int ArmorH { get; set; } = 95;

    public static int ShieldX { get; set; } = 303;
    public static int ShieldY { get; set; } = 170;
    public static int ShieldW { get; set; } = 70;
    public static int ShieldH { get; set; } = 95;

    public static int RingLeftX { get; set; } = 104;
    public static int RingLeftY { get; set; } = 285;
    public static int RingLeftW { get; set; } = 50;
    public static int RingLeftH { get; set; } = 50;

    public static int RingRightX { get; set; } = 244;
    public static int RingRightY { get; set; } = 285;
    public static int RingRightW { get; set; } = 50;
    public static int RingRightH { get; set; } = 50;

    public static int GlovesX { get; set; } = 24;
    public static int GlovesY { get; set; } = 265;
    public static int GlovesW { get; set; } = 70;
    public static int GlovesH { get; set; } = 70;

    public static int PantsX { get; set; } = 163;
    public static int PantsY { get; set; } = 266;
    public static int PantsW { get; set; } = 70;
    public static int PantsH { get; set; } = 70;

    public static int BootsX { get; set; } = 303;
    public static int BootsY { get; set; } = 266;
    public static int BootsW { get; set; } = 70;
    public static int BootsH { get; set; } = 70;

    // The current Classic inventory has no reference side-button column, so the
    // eight-column grid is centered inside the window and under the equipment.
    public static int GridX { get; set; } = (PanelW - GridCols * GridCellSize) / 2;
    public static int GridY { get; set; } = 408;
    public static int GridFrameX { get; set; } = 16;
    public static int GridFrameY { get; set; } = 404;
    public static int GridFrameW { get; set; } = 364;
    public static int GridFrameH { get; set; } = 292;
    public static int GridCellSize { get; set; } = 34;
    public static int GridCols { get; set; } = 8;
    public static int GridRows { get; set; } = 7;

    public static int MoneyY { get; set; } = 659;
    public static int FooterY { get; set; } = 646;
}
