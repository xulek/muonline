#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Client.Main.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI;

public enum UiThemeId
{
    Modern,
    Classic,

    // Kept as a source-compatible alias for older integrations. Configuration parsing
    // also accepts "Season6" and maps it to the Classic theme.
    Season6 = Classic
}

public enum UiThemeAsset
{
    BottomBarFooter,
    BottomBarExpBackground,
    BottomBarExpFill,
    BottomBarHpCrystal,
    BottomBarHpCrystalGrey,
    BottomBarMpCrystal,
    BottomBarMpCrystalGrey,
    BottomBarSlot,
    BottomBarClawLeft,
    BottomBarClawRight,
    BottomBarBarLeft,
    BottomBarBarRight,
    BottomBarBarLeftFull,
    BottomBarBarRightFull,
    BottomBarBarFadeLeft,
    BottomBarBarFadeRight,
    BottomBarBarOverlayLeft,
    BottomBarBarOverlayRight,
    BottomBarSkillFrame,
    BottomBarSkillGlow,
    BottomBarAttack,
    TouchMenuNotification,
    TouchMenuSettings,
    TouchMenuBag,
    VirtualJoystickBackground,
    VirtualJoystickKnob,
    ImprintPanel,
    PotionPanel,
    InventoryPanel,
    InventoryTitle,
    InventoryRect,
    InventoryCircle,
    InventoryGridRow,
    InventoryBottomBar,
    InventoryButtonDark,
    InventoryButtonGold,
    InventoryBoxWeapon,
    InventoryBoxShield,
    InventoryBoxArmor,
    InventoryBoxHelm,
    InventoryBoxPants,
    InventoryBoxGloves,
    InventoryBoxBoots,
    InventoryBoxWings,
    InventoryBoxPet,
    InventoryBoxPend,
    InventoryBoxRing,
    MasteryBackground,
    CharacterSlots,
    CharacterSlotBorder,
    CharacterStart,
    CharacterBack,
    CharacterDelete,
    CharacterPanelFrame,
    CharacterNameDescription,
    CharacterNameInput,
    CharacterNamePlate,
    CharacterOkCancel,
    CharacterClassButton,
    CharacterDarkLabel,
    CharacterAttributeRow,
    CharacterSpider01,
    CharacterSpider02,
    CharacterSpider03,
    CharacterSpider04,
    CharacterSpider05,
    CharacterSpider06,
    LoginGoogleNormal,
    LoginGoogleHover
}

public sealed class UiThemePalette
{
    public Color BgDarkest { get; init; }
    public Color BgDark { get; init; }
    public Color BgMid { get; init; }
    public Color BgLight { get; init; }
    public Color BgLighter { get; init; }
    public Color Accent { get; init; }
    public Color AccentBright { get; init; }
    public Color AccentDim { get; init; }
    public Color AccentGlow { get; init; }
    public Color Secondary { get; init; }
    public Color SecondaryBright { get; init; }
    public Color SecondaryDim { get; init; }
    public Color BorderOuter { get; init; }
    public Color BorderInner { get; init; }
    public Color BorderHighlight { get; init; }
    public Color SlotBg { get; init; }
    public Color SlotBorder { get; init; }
    public Color SlotHover { get; init; }
    public Color SlotSelected { get; init; }
    public Color TextWhite { get; init; }
    public Color TextGold { get; init; }
    public Color TextGray { get; init; }
    public Color TextDark { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }
    public Color Danger { get; init; }
}

public sealed class UiThemeMetrics
{
    public Point BottomBarSize { get; init; }
    public Point InventoryWindowSize { get; init; }
    public Point CharacterWindowSize { get; init; }
    public Point SkillWindowSize { get; init; }
    public Point ModalWindowSize { get; init; }
    public Point ChatLogSize { get; init; }
    public Point ChatInputSize { get; init; }
    public Point MinimapSize { get; init; }
    public int SlotSize { get; init; }
    public int SlotGap { get; init; }
    public float LabelScale { get; init; }
}

public sealed class UiThemeDefinition
{
    internal UiThemeDefinition(
        UiThemeId id,
        string displayName,
        UiThemePalette palette,
        UiThemeMetrics metrics,
        IReadOnlyDictionary<UiThemeAsset, string> assets)
    {
        Id = id;
        DisplayName = displayName;
        Palette = palette;
        Metrics = metrics;
        Assets = assets;
    }

    public UiThemeId Id { get; }
    public string DisplayName { get; }
    public UiThemePalette Palette { get; }
    public UiThemeMetrics Metrics { get; }
    internal IReadOnlyDictionary<UiThemeAsset, string> Assets { get; }
}

public sealed class UiThemeChangedEventArgs : EventArgs
{
    internal UiThemeChangedEventArgs(UiThemeDefinition previous, UiThemeDefinition current)
    {
        Previous = previous;
        Current = current;
    }

    public UiThemeDefinition Previous { get; }
    public UiThemeDefinition Current { get; }
}

/// <summary>
/// Owns the active UI definition and the asynchronous, once-per-path texture cache used by
/// theme-specific controls. The manager never performs disk work from Update or Draw.
/// </summary>
public static class UiThemeManager
{
    private static readonly ILogger? _logger = MuGame.AppLoggerFactory?.CreateLogger("UiThemeManager");
    private static readonly ConcurrentDictionary<string, Lazy<Task<Texture2D?>>> _textureTasks = new();
    private static readonly UiThemeDefinition _modern = CreateModernDefinition();
    private static readonly UiThemeDefinition _classic = CreateClassicDefinition();
    private static UiThemeDefinition _current = _modern;
    private static Action<UiThemeId>? _persist;

    public static event EventHandler<UiThemeChangedEventArgs>? ThemeChanged;

    public static UiThemeDefinition Current => _current;
    public static UiThemeId CurrentId => _current.Id;
    public static IReadOnlyList<UiThemeDefinition> AvailableThemes { get; } = new[] { _modern, _classic };

    public static void ConfigurePersistence(Action<UiThemeId> persist)
    {
        _persist = persist;
    }

    public static void Initialize(string? configuredTheme, ILogger? logger = null)
    {
        if (!TryParse(configuredTheme, out UiThemeId parsed))
        {
            (logger ?? _logger)?.LogWarning(
                "Unsupported UI theme '{ConfiguredTheme}'. Falling back to Modern.",
                configuredTheme);
            parsed = UiThemeId.Modern;
        }

        SetTheme(parsed, persist: false);
    }

    public static bool TryParse(string? value, out UiThemeId theme)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "modern":
                theme = UiThemeId.Modern;
                return true;
            case "classic":
            case "season6":
                theme = UiThemeId.Classic;
                return true;
            default:
                theme = UiThemeId.Modern;
                return false;
        }
    }

    public static bool SetTheme(UiThemeId theme, bool persist = true)
    {
        if (!Enum.IsDefined(theme))
            theme = UiThemeId.Modern;

        UiThemeDefinition next = GetDefinition(theme);
        if (ReferenceEquals(_current, next))
            return false;

        UiThemeDefinition previous = _current;
        _current = next;
        _textureTasks.Clear();

        if (persist)
            _persist?.Invoke(next.Id);

        ThemeChanged?.Invoke(null, new UiThemeChangedEventArgs(previous, next));
        return true;
    }

    public static Task<Texture2D?> LoadThemeTextureAsync(string path, string? fallbackPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<Texture2D?>(null);

        // Classic controls are instantiated once so a live switch does not duplicate UI
        // trees. Their dedicated assets must nevertheless remain completely cold in Modern.
        if (CurrentId == UiThemeId.Modern && IsClassicPath(path))
            return Task.FromResult<Texture2D?>(null);

        string key = $"{CurrentId}:{Normalize(path)}:{Normalize(fallbackPath)}";
        Lazy<Task<Texture2D?>> lazy = _textureTasks.GetOrAdd(
            key,
            _ => new Lazy<Task<Texture2D?>>(
                () => LoadTextureCoreAsync(path, fallbackPath),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    public static Task<Texture2D?> LoadNativeTextureAsync(UiThemeAsset asset, string? fallbackPath = null)
    {
        if (!Current.Assets.TryGetValue(asset, out string? path))
            return Task.FromResult<Texture2D?>(null);

        return LoadThemeTextureAsync(path, fallbackPath);
    }

    public static void ReleaseCachedAssets()
    {
        _textureTasks.Clear();
    }

    private static async Task<Texture2D?> LoadTextureCoreAsync(string path, string? fallbackPath)
    {
        Texture2D? texture = await TryLoadTextureAsync(path).ConfigureAwait(false);
        if (texture != null || string.IsNullOrWhiteSpace(fallbackPath))
            return texture;

        return await TryLoadTextureAsync(fallbackPath).ConfigureAwait(false);
    }

    private static async Task<Texture2D?> TryLoadTextureAsync(string path)
    {
        try
        {
            Texture2D? texture = await TextureLoader.Instance.PrepareAndGetTexture(path).ConfigureAwait(false);
            if (texture != null)
                return texture;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "UI texture is unavailable: {Path}", path);
        }

        string? bundledPath = FindBundledAsset(path);
        if (bundledPath == null)
            return null;

        try
        {
            return await TextureLoader.Instance.PrepareAndGetTexture(bundledPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Bundled UI texture is unavailable: {Path}", bundledPath);
            return null;
        }
    }

    private static string? FindBundledAsset(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(directory.FullName, "data", "patch", "files-e.g", path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    private static UiThemeDefinition GetDefinition(UiThemeId id) => id == UiThemeId.Classic ? _classic : _modern;

    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/').ToLowerInvariant();

    private static bool IsClassicPath(string path)
    {
        string normalized = Normalize(path);
        return normalized.StartsWith("interface/dh/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/imprint/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/skillwin/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/mastery/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/inventory/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/charcreate/", StringComparison.Ordinal) ||
               normalized.StartsWith("interface/loginrole/", StringComparison.Ordinal);
    }

    private static UiThemeDefinition CreateModernDefinition() => new(
        UiThemeId.Modern,
        "Modern",
        new UiThemePalette
        {
            BgDarkest = new Color(8, 10, 14, 252), BgDark = new Color(16, 20, 26, 250),
            BgMid = new Color(24, 30, 38, 248), BgLight = new Color(35, 42, 52, 245),
            BgLighter = new Color(48, 56, 68, 240), Accent = new Color(212, 175, 85),
            AccentBright = new Color(255, 215, 120), AccentDim = new Color(140, 115, 55),
            AccentGlow = new Color(255, 200, 80, 40), Secondary = new Color(90, 140, 200),
            SecondaryBright = new Color(130, 180, 240), SecondaryDim = new Color(50, 80, 120),
            BorderOuter = new Color(5, 6, 8, 255), BorderInner = new Color(60, 70, 85, 200),
            BorderHighlight = new Color(100, 110, 130, 120), SlotBg = new Color(12, 15, 20, 240),
            SlotBorder = new Color(45, 52, 65, 180), SlotHover = new Color(70, 85, 110, 150),
            SlotSelected = new Color(212, 175, 85, 100), TextWhite = new Color(240, 240, 245),
            TextGold = new Color(255, 220, 130), TextGray = new Color(160, 165, 175),
            TextDark = new Color(100, 105, 115), Success = new Color(80, 200, 120),
            Warning = new Color(240, 180, 60), Danger = new Color(220, 80, 80)
        },
        new UiThemeMetrics
        {
            BottomBarSize = new Point(1280, 90), InventoryWindowSize = new Point(380, 520),
            CharacterWindowSize = new Point(320, 520), SkillWindowSize = new Point(520, 460),
            ModalWindowSize = new Point(430, 500), ChatLogSize = new Point(281, 120),
            ChatInputSize = new Point(281, 47), MinimapSize = new Point(560, 475),
            SlotSize = 40, SlotGap = 4, LabelScale = 1f
        },
        new Dictionary<UiThemeAsset, string>());

    private static UiThemeDefinition CreateClassicDefinition() => new(
        UiThemeId.Classic,
        "Classic",
        new UiThemePalette
        {
            BgDarkest = new Color(5, 7, 12, 255), BgDark = new Color(11, 15, 24, 250),
            BgMid = new Color(18, 25, 39, 248), BgLight = new Color(29, 42, 62, 245),
            BgLighter = new Color(45, 62, 86, 240), Accent = new Color(212, 175, 85),
            AccentBright = new Color(255, 215, 120), AccentDim = new Color(140, 115, 55),
            AccentGlow = new Color(255, 200, 80, 40), Secondary = new Color(165, 114, 214),
            SecondaryBright = new Color(211, 160, 255), SecondaryDim = new Color(92, 60, 128),
            BorderOuter = new Color(3, 5, 9, 255), BorderInner = new Color(116, 90, 45, 220),
            BorderHighlight = new Color(220, 180, 92, 160), SlotBg = new Color(7, 12, 21, 245),
            SlotBorder = new Color(102, 77, 38, 220), SlotHover = new Color(181, 140, 58, 170),
            SlotSelected = new Color(212, 175, 85, 130), TextWhite = new Color(228, 241, 255),
            TextGold = new Color(255, 220, 130), TextGray = new Color(151, 174, 199),
            TextDark = new Color(86, 106, 131), Success = new Color(83, 219, 159),
            Warning = new Color(255, 195, 91), Danger = new Color(239, 102, 132)
        },
        new UiThemeMetrics
        {
            BottomBarSize = new Point(1280, 110), InventoryWindowSize = new Point(396, 716),
            CharacterWindowSize = new Point(346, 640), SkillWindowSize = new Point(364, 600),
            ModalWindowSize = new Point(420, 520), ChatLogSize = new Point(420, 148),
            ChatInputSize = new Point(420, 58), MinimapSize = new Point(560, 520),
            SlotSize = 44, SlotGap = 3, LabelScale = 0.95f
        },
        new Dictionary<UiThemeAsset, string>
        {
            [UiThemeAsset.BottomBarFooter] = "Interface/DH/mi_footer.OZP",
            [UiThemeAsset.BottomBarExpBackground] = "Interface/DH/mi_exp_bg.OZP",
            [UiThemeAsset.BottomBarExpFill] = "Interface/DH/mi_exp_fill.OZP",
            [UiThemeAsset.BottomBarHpCrystal] = "Interface/DH/mi_hp_crystal.OZP",
            [UiThemeAsset.BottomBarHpCrystalGrey] = "Interface/DH/mi_hp_crystal_grey.OZP",
            [UiThemeAsset.BottomBarMpCrystal] = "Interface/DH/mi_mp_crystal.OZP",
            [UiThemeAsset.BottomBarMpCrystalGrey] = "Interface/DH/mi_mp_crystal_grey.OZP",
            [UiThemeAsset.BottomBarSlot] = "Interface/DH/mi_slot.OZP",
            [UiThemeAsset.BottomBarClawLeft] = "Interface/DH/mi_claw_left.OZP",
            [UiThemeAsset.BottomBarClawRight] = "Interface/DH/mi_claw_right.OZP",
            [UiThemeAsset.BottomBarBarLeft] = "Interface/DH/mi_bar_left.OZP",
            [UiThemeAsset.BottomBarBarRight] = "Interface/DH/mi_bar_right.OZP",
            [UiThemeAsset.BottomBarBarLeftFull] = "Interface/DH/mi_bar_left_full.OZP",
            [UiThemeAsset.BottomBarBarRightFull] = "Interface/DH/mi_bar_right_full.OZP",
            [UiThemeAsset.BottomBarBarFadeLeft] = "Interface/DH/mi_bar_fade_left.OZP",
            [UiThemeAsset.BottomBarBarFadeRight] = "Interface/DH/mi_bar_fade_right.OZP",
            [UiThemeAsset.BottomBarBarOverlayLeft] = "Interface/DH/mi_bar_overlay_left.OZP",
            [UiThemeAsset.BottomBarBarOverlayRight] = "Interface/DH/mi_bar_overlay_right.OZP",
            [UiThemeAsset.BottomBarSkillFrame] = "Interface/DH/mi_skill_frame.OZP",
            [UiThemeAsset.BottomBarSkillGlow] = "Interface/DH/mi_skill_glow.OZP",
            [UiThemeAsset.BottomBarAttack] = "Interface/DH/mi_attack.OZP",
            [UiThemeAsset.TouchMenuNotification] = "Interface/DH/mi_btn_notification.OZP",
            [UiThemeAsset.TouchMenuSettings] = "Interface/DH/mi_btn_menu.OZP",
            [UiThemeAsset.TouchMenuBag] = "Interface/DH/mi_btn_bag.OZP",
            [UiThemeAsset.VirtualJoystickBackground] = "Interface/DH/mi_stick_bg.OZP",
            [UiThemeAsset.VirtualJoystickKnob] = "Interface/DH/mi_stick_knob.OZP",
            [UiThemeAsset.ImprintPanel] = "Interface/Imprint/imprint_panel.OZP",
            [UiThemeAsset.PotionPanel] = "Interface/Imprint/imprint_panel.OZP",
            [UiThemeAsset.InventoryPanel] = "Interface/Imprint/imprint_panel.OZP",
            [UiThemeAsset.InventoryTitle] = "Interface/Inventory/inv_title.OZP",
            [UiThemeAsset.InventoryRect] = "Interface/Inventory/inv_rect.OZP",
            [UiThemeAsset.InventoryCircle] = "Interface/Inventory/inv_circle.OZP",
            [UiThemeAsset.InventoryGridRow] = "Interface/Inventory/inv_grid_row.OZP",
            [UiThemeAsset.InventoryBottomBar] = "Interface/Inventory/inv_bottom_bar.OZP",
            [UiThemeAsset.InventoryButtonDark] = "Interface/Inventory/inv_btn_dark.OZP",
            [UiThemeAsset.InventoryButtonGold] = "Interface/Inventory/inv_btn_gold.OZP",
            [UiThemeAsset.InventoryBoxWeapon] = "Interface/Inventory/inv_box_weapon.OZP",
            [UiThemeAsset.InventoryBoxShield] = "Interface/Inventory/inv_box_shield.OZP",
            [UiThemeAsset.InventoryBoxArmor] = "Interface/Inventory/inv_box_armor.OZP",
            [UiThemeAsset.InventoryBoxHelm] = "Interface/Inventory/inv_box_helm.OZP",
            [UiThemeAsset.InventoryBoxPants] = "Interface/Inventory/inv_box_pants.OZP",
            [UiThemeAsset.InventoryBoxGloves] = "Interface/Inventory/inv_box_gloves.OZP",
            [UiThemeAsset.InventoryBoxBoots] = "Interface/Inventory/inv_box_boots.OZP",
            [UiThemeAsset.InventoryBoxWings] = "Interface/Inventory/inv_box_wings.OZP",
            [UiThemeAsset.InventoryBoxPet] = "Interface/Inventory/inv_box_pet.OZP",
            [UiThemeAsset.InventoryBoxPend] = "Interface/Inventory/inv_box_pend.OZP",
            [UiThemeAsset.InventoryBoxRing] = "Interface/Inventory/inv_box_ring.OZP",
            [UiThemeAsset.MasteryBackground] = "Interface/Mastery/mastery_bg.OZP",
            [UiThemeAsset.CharacterSlots] = "Interface/CharCreate/char_slots.OZT",
            [UiThemeAsset.CharacterSlotBorder] = "Interface/CharCreate/borda_retangular.OZT",
            [UiThemeAsset.CharacterStart] = "Interface/CharCreate/start_game.OZT",
            [UiThemeAsset.CharacterBack] = "Interface/CharCreate/btn_back_new.OZT",
            [UiThemeAsset.CharacterDelete] = "Interface/CharCreate/btn_delete_new.OZT",
            [UiThemeAsset.CharacterPanelFrame] = "Interface/CharCreate/panel_frame.OZT",
            [UiThemeAsset.CharacterNameDescription] = "Interface/CharCreate/name_desc.OZT",
            [UiThemeAsset.CharacterNameInput] = "Interface/CharCreate/name_input.OZT",
            [UiThemeAsset.CharacterNamePlate] = "Interface/CharCreate/name_plate.OZT",
            [UiThemeAsset.CharacterOkCancel] = "Interface/CharCreate/ok_cancel.OZT",
            [UiThemeAsset.CharacterClassButton] = "Interface/CharCreate/class_btn.OZT",
            [UiThemeAsset.CharacterDarkLabel] = "Interface/CharCreate/dark_label.OZT",
            [UiThemeAsset.CharacterAttributeRow] = "Interface/CharCreate/attr_row.OZT",
            [UiThemeAsset.CharacterSpider01] = "Interface/CharCreate/spider_newface01.OZT",
            [UiThemeAsset.CharacterSpider02] = "Interface/CharCreate/spider_newface02.OZT",
            [UiThemeAsset.CharacterSpider03] = "Interface/CharCreate/spider_newface03.OZT",
            [UiThemeAsset.CharacterSpider04] = "Interface/CharCreate/spider_newface04.OZT",
            [UiThemeAsset.CharacterSpider05] = "Interface/CharCreate/spider_newface05.OZT",
            [UiThemeAsset.CharacterSpider06] = "Interface/CharCreate/spider_newface06.OZT",
            [UiThemeAsset.LoginGoogleNormal] = "Interface/CharCreate/google_login_normal.OZT",
            [UiThemeAsset.LoginGoogleHover] = "Interface/CharCreate/google_login_hover.OZT"
        });
}
