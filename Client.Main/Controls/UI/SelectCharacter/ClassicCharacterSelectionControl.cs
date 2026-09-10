#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Controls.UI.SelectCharacter;

/// <summary>
/// Classic character-selection chrome. The Modern scene keeps its existing card panel;
/// this control owns the separate slot/button geometry introduced by the reference UI.
/// Character models remain owned by SelectCharacterScene and are not duplicated here.
/// </summary>
public sealed class ClassicCharacterSelectionControl : UIControl
{
    private const float PrefabScale = 720f / 750f;
    private const float SlotCenterX = 476.6f;
    private const float SlotTopY = 272.3f;
    private const float SlotPitch = 60f;
    private const float SlotWidth = 204.3f;
    private const float SlotHeight = 58.3f;
    private const float StartCenterX = -88.9f;
    private const float StartCenterY = -267.7f;
    private const float StartWidth = 237.7f;
    private const float StartHeight = 74f;
    private const float BackCenterX = -608.8f;
    private const float BackCenterY = 272.2f;
    private const float DeleteCenterX = 609.5f;
    private const float DeleteWidth = 57.5f;
    private const float DeleteHeight = 58.3f;

    private static readonly Rectangle SlotSelectedSource = new(0, 2, 231, 55);
    private static readonly Rectangle SlotHoverSource = new(0, 116, 231, 55);
    private static readonly Rectangle SlotNormalSource = new(0, 173, 231, 55);
    private static readonly Rectangle BorderSource = new(0, 0, 388, 118);
    private static readonly Rectangle StartSource = new(0, 0, 471, 146);
    private static readonly Rectangle BackSource = new(0, 0, 249, 213);
    private static readonly Rectangle DeleteSource = new(0, 0, 246, 213);

    private readonly List<Entry> _entries = new();
    private readonly List<Rectangle> _slotRects = new();
    private Texture2D? _slotTexture;
    private Texture2D? _borderTexture;
    private Texture2D? _startTexture;
    private Texture2D? _backTexture;
    private Texture2D? _deleteTexture;
    private UiThemeId _loadedTheme = (UiThemeId)(-1);
    private Task? _loadTask;
    private int _selectedIndex = -1;
    private int _pressedSlot = -1;
    private bool _pressedStart;
    private bool _pressedBack;
    private bool _pressedDelete;
    private Rectangle _startRect;
    private Rectangle _backRect;
    private Rectangle _deleteRect;

    public sealed record Entry(string Name, CharacterClassNumber Class, ushort Level);

    public bool CanEnter { get; set; }
    public int SelectedIndex => _selectedIndex;

    public event EventHandler<string>? CharacterSelected;
    public event EventHandler? EmptySlotClicked;
    public event EventHandler? EnterClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? BackClicked;

    public ClassicCharacterSelectionControl()
    {
        AutoViewSize = false;
        ControlSize = new Point(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
        ViewSize = ControlSize;
        Interactive = true;
        LayoutRects();
    }

    public override Task Load()
    {
        if (_loadedTheme == UiThemeManager.CurrentId)
            return Task.CompletedTask;
        if (_loadTask is { IsCompleted: false })
            return _loadTask;

        _loadTask = LoadThemeAssetsAsync(UiThemeManager.CurrentId);
        return _loadTask;
    }

    private async Task LoadThemeAssetsAsync(UiThemeId theme)
    {
        if (theme != UiThemeId.Classic)
        {
            _loadedTheme = theme;
            return;
        }

        _slotTexture = await UiThemeManager.LoadNativeTextureAsync(UiThemeAsset.CharacterSlots);
        _borderTexture = await UiThemeManager.LoadNativeTextureAsync(UiThemeAsset.CharacterSlotBorder);
        _startTexture = await UiThemeManager.LoadNativeTextureAsync(UiThemeAsset.CharacterStart);
        _backTexture = await UiThemeManager.LoadNativeTextureAsync(UiThemeAsset.CharacterBack);
        _deleteTexture = await UiThemeManager.LoadNativeTextureAsync(UiThemeAsset.CharacterDelete);
        _loadedTheme = theme;
    }

    public void SetCharacters(IEnumerable<Entry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.Take(10));
        _selectedIndex = _entries.Count > 0 ? 0 : -1;
    }

    public void SetSelectedIndex(int index)
    {
        _selectedIndex = index >= 0 && index < _entries.Count ? index : -1;
    }

    protected override void OnScreenSizeChanged()
    {
        base.OnScreenSizeChanged();
        LayoutRects();
    }

    protected override void OnThemeChanged(UiThemeChangedEventArgs e)
    {
        base.OnThemeChanged(e);
        _loadedTheme = (UiThemeId)(-1);
        if (UiThemeManager.CurrentId == UiThemeId.Classic && Status == GameControlStatus.Ready)
            _ = Load();
    }

    private static Vector2 PrefabPoint(float x, float y) => new(
        UiScaler.VirtualSize.X / 2f + x * PrefabScale,
        UiScaler.VirtualSize.Y / 2f - y * PrefabScale);

    private static Rectangle Centered(float x, float y, float width, float height)
    {
        Vector2 center = PrefabPoint(x, y);
        return new Rectangle(
            (int)MathF.Round(center.X - width * PrefabScale / 2f),
            (int)MathF.Round(center.Y - height * PrefabScale / 2f),
            Math.Max(1, (int)MathF.Round(width * PrefabScale)),
            Math.Max(1, (int)MathF.Round(height * PrefabScale)));
    }

    private void LayoutRects()
    {
        _slotRects.Clear();
        for (int i = 0; i < 10; i++)
            _slotRects.Add(Centered(SlotCenterX, SlotTopY - SlotPitch * i, SlotWidth, SlotHeight));

        _startRect = Centered(StartCenterX, StartCenterY, StartWidth, StartHeight);
        _backRect = Centered(BackCenterX, BackCenterY, DeleteWidth, DeleteHeight);
        float deleteY = _selectedIndex >= 0 ? SlotTopY - SlotPitch * _selectedIndex : SlotTopY;
        _deleteRect = Centered(DeleteCenterX, deleteY, DeleteWidth, DeleteHeight);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (!Visible || !Interactive)
            return;

        Point mouse = MuGame.Instance.UiMouseState.Position;
        MouseState current = MuGame.Instance.UiMouseState;
        MouseState previous = MuGame.Instance.PrevUiMouseState;
        bool pressed = current.LeftButton == ButtonState.Pressed;
        bool wasPressed = previous.LeftButton == ButtonState.Pressed;

        if (pressed && !wasPressed)
        {
            _pressedSlot = _slotRects.FindIndex(rect => rect.Contains(mouse));
            _pressedStart = CanEnter && _startRect.Contains(mouse);
            _pressedBack = _backRect.Contains(mouse);
            _pressedDelete = CanEnter && _deleteRect.Contains(mouse);
        }
        else if (!pressed && wasPressed)
        {
            if (_pressedSlot >= 0 && _slotRects[_pressedSlot].Contains(mouse))
            {
                SelectSlot(_pressedSlot);
            }
            else if (_pressedStart && CanEnter && _startRect.Contains(mouse))
            {
                EnterClicked?.Invoke(this, EventArgs.Empty);
            }
            else if (_pressedDelete && CanEnter && _deleteRect.Contains(mouse))
            {
                DeleteClicked?.Invoke(this, EventArgs.Empty);
            }
            else if (_pressedBack && _backRect.Contains(mouse))
            {
                BackClicked?.Invoke(this, EventArgs.Empty);
            }

            _pressedSlot = -1;
            _pressedStart = _pressedBack = _pressedDelete = false;
        }

        if (_selectedIndex >= 0)
        {
            float deleteY = SlotTopY - SlotPitch * _selectedIndex;
            _deleteRect = Centered(DeleteCenterX, deleteY, DeleteWidth, DeleteHeight);
        }
    }

    private void SelectSlot(int index)
    {
        _selectedIndex = index;
        if (index < _entries.Count)
            CharacterSelected?.Invoke(this, _entries[index].Name);
        else
            EmptySlotClicked?.Invoke(this, EventArgs.Empty);
    }

    public override void Draw(GameTime gameTime)
    {
        if (!Visible)
            return;

        SpriteBatch sprite = GraphicsManager.Instance.Sprite;
        Texture2D? pixel = GraphicsManager.Instance.Pixel;
        SpriteFont? font = GraphicsManager.Instance.Font;
        if (sprite == null || pixel == null || font == null)
            return;

        using var scope = new SpriteBatchScope(
            sprite, SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone,
            null, UiScaler.SpriteTransform);

        Point mouse = MuGame.Instance.UiMouseState.Position;
        for (int i = 0; i < _slotRects.Count; i++)
        {
            Rectangle rect = _slotRects[i];
            Rectangle source = i == _selectedIndex ? SlotSelectedSource :
                rect.Contains(mouse) || _pressedSlot == i ? SlotHoverSource : SlotNormalSource;

            if (TextureUsable(_slotTexture))
                sprite.Draw(_slotTexture!, rect, source, Color.White);
            else
                DrawPanel(sprite, pixel, rect, i == _selectedIndex ? ModernHudTheme.AccentDim : ModernHudTheme.BgDark);

            if (TextureUsable(_borderTexture))
                sprite.Draw(_borderTexture!, rect, BorderSource, Color.White);

            if (i < _entries.Count)
                DrawEntry(sprite, font, rect, _entries[i]);
            else
                DrawCentered(sprite, font, "Vacant Character Slot", rect, ModernHudTheme.TextGray, 0.42f);
        }

        DrawButton(sprite, pixel, font, _startTexture, _startRect, StartSource,
            CanEnter, "START GAME", ModernHudTheme.Success);
        DrawButton(sprite, pixel, font, _backTexture, _backRect, BackSource,
            true, "", ModernHudTheme.Accent);
        DrawButton(sprite, pixel, font, _deleteTexture, _deleteRect, DeleteSource,
            CanEnter, "", ModernHudTheme.Danger);
    }

    private void DrawEntry(SpriteBatch sprite, SpriteFont font, Rectangle rect, Entry entry)
    {
        string className = ClassName(entry.Class);
        DrawString(sprite, font, className, new Rectangle(rect.X + 14, rect.Y - 4, rect.Width - 28, rect.Height),
            ModernHudTheme.TextWhite, 0.36f, right: false);
        DrawString(sprite, font, "Commoner", new Rectangle(rect.X + 14, rect.Y - 4, rect.Width - 28, rect.Height),
            ModernHudTheme.TextWhite, 0.36f, right: true);
        DrawString(sprite, font, entry.Name, new Rectangle(rect.X + 14, rect.Y + 14, rect.Width - 28, rect.Height),
            ModernHudTheme.TextGold, 0.48f, right: false);
        DrawString(sprite, font, entry.Level.ToString(), new Rectangle(rect.X + 14, rect.Y + 14, rect.Width - 28, rect.Height),
            ModernHudTheme.TextGold, 0.48f, right: true);
    }

    private static void DrawButton(SpriteBatch sprite, Texture2D pixel, SpriteFont font, Texture2D? texture,
        Rectangle rect, Rectangle source, bool enabled, string fallbackText, Color fallbackColor)
    {
        if (TextureUsable(texture))
        {
            Rectangle fitted = AspectFit(rect, source);
            sprite.Draw(texture!, fitted, source, enabled ? Color.White : Color.White * 0.35f);
            return;
        }

        if (!enabled && string.IsNullOrEmpty(fallbackText))
            return;
        DrawPanel(sprite, pixel, rect, enabled ? fallbackColor * 0.75f : ModernHudTheme.BgDark);
        if (!string.IsNullOrEmpty(fallbackText))
            DrawCentered(sprite, font, fallbackText, rect, ModernHudTheme.TextWhite, 0.42f);
    }

    private static Rectangle AspectFit(Rectangle destination, Rectangle source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return destination;

        float sourceAspect = source.Width / (float)source.Height;
        float destinationAspect = destination.Width / (float)destination.Height;
        Rectangle fitted = destination;

        if (destinationAspect > sourceAspect)
        {
            fitted.Width = Math.Max(1, (int)MathF.Round(destination.Height * sourceAspect));
            fitted.X = destination.Center.X - fitted.Width / 2;
        }
        else if (destinationAspect < sourceAspect)
        {
            fitted.Height = Math.Max(1, (int)MathF.Round(destination.Width / sourceAspect));
            fitted.Y = destination.Center.Y - fitted.Height / 2;
        }

        return fitted;
    }

    private static void DrawPanel(SpriteBatch sprite, Texture2D pixel, Rectangle rect, Color color)
    {
        sprite.Draw(pixel, rect, color);
        sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), ModernHudTheme.BorderInner);
        sprite.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), ModernHudTheme.BorderOuter);
        sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), ModernHudTheme.BorderInner);
        sprite.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), ModernHudTheme.BorderOuter);
    }

    private static void DrawCentered(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect, Color color, float scale)
    {
        Vector2 size = font.MeasureString(text) * scale;
        Vector2 position = new(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
        sprite.DrawString(font, text, position + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static void DrawString(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect, Color color,
        float scale, bool right)
    {
        Vector2 size = font.MeasureString(text) * scale;
        float x = right ? rect.Right - size.X : rect.X;
        float y = rect.Y + (rect.Height - size.Y) / 2f;
        Vector2 position = new(x, y);
        sprite.DrawString(font, text, position + Vector2.One, Color.Black * 0.55f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static bool TextureUsable(Texture2D? texture) => texture != null && !texture.IsDisposed;

    private static string ClassName(CharacterClassNumber value) => value switch
    {
        CharacterClassNumber.DarkWizard or CharacterClassNumber.SoulMaster or CharacterClassNumber.GrandMaster => "Dark Wizard",
        CharacterClassNumber.DarkKnight or CharacterClassNumber.BladeKnight or CharacterClassNumber.BladeMaster => "Dark Knight",
        CharacterClassNumber.FairyElf or CharacterClassNumber.MuseElf or CharacterClassNumber.HighElf => "Elf",
        CharacterClassNumber.MagicGladiator or CharacterClassNumber.DuelMaster => "Magic Gladiator",
        CharacterClassNumber.DarkLord or CharacterClassNumber.LordEmperor => "Dark Lord",
        CharacterClassNumber.Summoner or CharacterClassNumber.BloodySummoner or CharacterClassNumber.DimensionMaster => "Summoner",
        CharacterClassNumber.RageFighter or CharacterClassNumber.FistMaster => "Rage Fighter",
        _ => value.ToString()
    };
}
