#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.ImperialGuardian
{
    public enum GuardianWeather
    {
        None = 0,
        Rain = 1,
        Fog = 2,
        Storm = 3
    }

    /// <summary>
    /// Server-driven weather mirror of GMEmpireGuardian1 (WEATHER_RAIN/FOG/STORM).
    /// Worlds 70-73 share this state; SetWeather is intended to be driven by the
    /// server weather packet, defaulting to None.
    /// </summary>
    public static class ImperialGuardianWeather
    {
        public static GuardianWeather Current { get; private set; } = GuardianWeather.None;

        public static event Action<GuardianWeather>? WeatherChanged;

        public static void SetWeather(GuardianWeather weather)
        {
            if (Current == weather)
                return;

            Current = weather;
            WeatherChanged?.Invoke(weather);
        }
    }

    /// <summary>
    /// GMEmpireGuardian1::RenderFrontSideVisual + CreateRain:
    /// FOG — two CHROME+2 layers tinted (0.6,0.6,0.9): diagonal (+u*0.00005,+v*0.00008, tile 2x2)
    /// and counter-scrolling (-u*0.00005, tile 0.3x0.3);
    /// STORM — rand_fps_check(20) burst of two crossing layers tinted (0.7,0.7,0.9), tile 3x2;
    /// RAIN — tight-cone rain (Hero±(800/-300..400), fall -(rand%24+30)) with splash rings.
    /// </summary>
    public sealed class GuardianWeatherSystem : EffectObject
    {
        private readonly WalkableWorldControl _world;
        private Events.ScrollingSmokeOverlay? _fogLayers;
        private Events.EventRainSystem? _rainSystem;
        private GuardianWeather _applied = GuardianWeather.Rain; // force initial apply

        public GuardianWeatherSystem(WalkableWorldControl world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));

            ImperialGuardianWeather.WeatherChanged += OnWeatherChanged;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            await ApplyWeather(ImperialGuardianWeather.Current);
        }

        private void OnWeatherChanged(GuardianWeather weather)
        {
            if (Status == GameControlStatus.Ready)
                _ = ApplyWeather(weather);
        }

        private async Task ApplyWeather(GuardianWeather weather)
        {
            if (_applied == weather)
                return;

            _applied = weather;

            if (_rainSystem != null)
            {
                Children.Remove(_rainSystem);
                _rainSystem.Dispose();
                _rainSystem = null;
            }

            switch (weather)
            {
                case GuardianWeather.Fog:
                    _fogLayers = new Events.ScrollingSmokeOverlay(
                        new Events.ScrollingSmokeOverlay.Layer
                        {
                            TexturePath = "Effect/Map_Smoke1.jpg",
                            Tint = new Color(0.6f, 0.6f, 0.9f),
                            USpeed = 0.00005f,
                            VSpeed = 0.00008f,
                            TileU = 2f,
                            TileV = 2f,
                            Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                        },
                        new Events.ScrollingSmokeOverlay.Layer
                        {
                            TexturePath = "Effect/Map_Smoke1.jpg",
                            Tint = new Color(0.6f, 0.6f, 0.9f),
                            USpeed = -0.00005f,
                            TileU = 0.3f,
                            TileV = 0.3f,
                            Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                        });
                    await _fogLayers.Load();
                    break;

                case GuardianWeather.Storm:
                    _fogLayers = new Events.ScrollingSmokeOverlay(
                        new Events.ScrollingSmokeOverlay.Layer
                        {
                            TexturePath = "Effect/Map_Smoke1.jpg",
                            Tint = new Color(0.7f, 0.7f, 0.9f),
                            USpeed = 0.0006f,
                            VSpeed = -0.0006f,
                            TileU = 3f,
                            TileV = 2f,
                            Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                        },
                        new Events.ScrollingSmokeOverlay.Layer
                        {
                            TexturePath = "Effect/Map_Smoke1.jpg",
                            Tint = new Color(0.7f, 0.7f, 0.9f),
                            USpeed = -0.0006f,
                            VSpeed = 0.0006f,
                            TileU = 3f,
                            TileV = 2f,
                            Blend = Events.ScrollingSmokeOverlay.LayerBlend.Additive
                        });
                    await _fogLayers.Load();

                    _rainSystem = CreateRain();
                    Children.Add(_rainSystem);
                    await _rainSystem.LoadContent();
                    break;

                case GuardianWeather.Rain:
                    _rainSystem = CreateRain();
                    Children.Add(_rainSystem);
                    await _rainSystem.LoadContent();
                    break;
            }
        }

        private Events.EventRainSystem CreateRain()
        {
            // Tighter cone and fastest rain of all worlds: -(rand%24+30)
            var rain = new Events.EventRainSystem(
                _world,
                maxDrops: 120,
                speedBonus: 10f,
                streakLength: 20f,
                spawnSplashes: true,
                lightningFlicker: false);
            return rain;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            // Storm layers are gated by rand_fps_check(20) in the source; the overlay
            // itself stays continuous with reduced tint so the crossing remains visible.
            _fogLayers?.DrawOverlay(gameTime);
        }

        public override void Dispose()
        {
            ImperialGuardianWeather.WeatherChanged -= OnWeatherChanged;

            if (_rainSystem != null)
            {
                Children.Remove(_rainSystem);
                _rainSystem.Dispose();
                _rainSystem = null;
            }

            _fogLayers?.Dispose();
            _fogLayers = null;

            base.Dispose();
        }
    }
}

