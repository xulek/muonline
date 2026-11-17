#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Client.Main.Controls;
using Client.Main.Objects.Worlds.Lorencia;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Common
{
    /// <summary>
    /// One lit map-object type: a SourceMain AddTerrainLight / glow sprite ported as a single static
    /// terrain light, or a fire emitter (CreateFire port, which owns its own terrain light).
    /// </summary>
    internal sealed class MapObjectLightSpec
    {
        public Vector3 Color;
        public float RadiusTiles = 3f;
        public float Intensity = 1f;
        /// <summary>Random intensity amplitude (+-) applied at the legacy 25 Hz step.</summary>
        public float Flicker;
        /// <summary>Sine intensity amplitude (+-), source fLumi = sinf(WorldTime * PulseSpeed).</summary>
        public float Pulse;
        public float PulseSpeed = 0.002f;
        /// <summary>Model-space offset, relative to <see cref="Bone"/> when it is set.</summary>
        public Vector3 Offset;
        public int Bone = -1;
        /// <summary>Spawn the CreateFire emitter (BITMAP_TRUE_FIRE / BITMAP_FIRE_HIK* substitute).</summary>
        public bool Fire;
        /// <summary>Source sets HiddenMesh = -2: the model is only an effect anchor.</summary>
        public bool HideMesh;
    }

    /// <summary>
    /// Fire / glow light sources of map objects, keyed by world folder index and object type.
    /// Values follow the source AddTerrainLight ranges/colours (GMAida, GMCrywolf1st, GM_kanturu_*,
    /// GMNewTown, GMSwampOfQuiet, GM_Raklion, GMSantaTown, GM_PK_Field, GMDoppelGanger2, GMHuntingGround,
    /// GMUnitedMarketPlace, GMKarutan1, GMBattleCastle, GMEmpireGuardian*, w_CursedTemple).
    /// </summary>
    internal static class MapObjectLightCatalog
    {
        private static readonly Dictionary<int, MapObjectLightSpec> Specs = new(96);

        static MapObjectLightCatalog()
        {
            // Aida: Types 30/71 glowing plants, AddTerrainLight range 2 (green / red).
            Add(34, 30, Light(0.42f, 0.62f, 0.42f, 2f, 0.9f, flicker: 0.05f, pulse: 0.15f, pulseSpeed: 0.0015f, z: 60f));
            Add(34, 71, Light(0.92f, 0.22f, 0.22f, 2f, 0.9f, flicker: 0.05f, pulse: 0.15f, pulseSpeed: 0.0015f, z: 60f));

            // Crywolf: Type 41 altar flare (range 2), Types 57/71 TRUE_FIRE braziers (57: range 3).
            Add(35, 41, Light(0.2f, 0.7f, 0.5f, 2.5f, 1f, pulse: 0.15f, z: 200f));
            Add(35, 57, Fire(1f, 0.6f, 0.2f, 3f, 1f, hide: false));
            Add(35, 71, Fire(1f, 0.6f, 0.2f, 3f, 0.9f, hide: false));

            // Kanturu 1st: Type 60 hidden red light (range 3), Type 61 hidden TRUE_FIRE (range 3).
            Add(38, 60, Light(0.9f, 0.2f, 0.1f, 3f, 1f, flicker: 0.2f, z: 40f, hide: true));
            Add(38, 61, Fire(1f, 0.6f, 0.2f, 3f, 1f));

            // Kanturu 2nd: Type 4 reactor-core glow (bone 1), Type 8 energy orb (bone 4).
            Add(39, 4, Light(0.3f, 0.5f, 1f, 2.5f, 0.8f, pulse: 0.25f, bone: 1, offset: new Vector3(-1f, 0f, 0f)));
            Add(39, 8, Light(0.8f, 0.85f, 1f, 2.5f, 0.6f, pulse: 0.4f, pulseSpeed: 0.001f, bone: 4));

            // Kanturu 3rd (Refine Tower): Type 5 core glow, Types 45/54 hidden white lights,
            // Type 50 random white flicker (range 1 + Scale).
            Add(40, 5, Light(0.3f, 0.5f, 1f, 2f, 0.8f, pulse: 0.25f, bone: 1, offset: new Vector3(-1f, 0f, 0f)));
            Add(40, 45, Light(0.85f, 0.9f, 1f, 2f, 0.8f, flicker: 0.15f, hide: true));
            Add(40, 50, Light(0.85f, 0.9f, 1f, 2.5f, 0.7f, flicker: 0.6f));
            Add(40, 54, Light(0.85f, 0.9f, 1f, 1.5f, 0.8f, flicker: 0.15f, hide: true));

            // Elbeland: Type 0 hidden TRUE_FIRE (range 3), Type 61 hidden TRUE_BLUE (range 3),
            // Type 63 lamp sprite (bone 5), Type 121 lantern string (bones 3..8).
            Add(52, 0, Fire(1f, 0.6f, 0.2f, 3f, 1f));
            Add(52, 61, Light(0.2f, 0.6f, 1f, 3f, 0.9f, flicker: 0.15f, z: 40f, hide: true));
            Add(52, 63, Light(1f, 1f, 0.6f, 3f, 0.7f, pulse: 0.3f, pulseSpeed: 0.001f, bone: 5, offset: new Vector3(-40f, -10f, 0f)));
            Add(52, 121, Light(1f, 0.5f, 0.3f, 2.5f, 0.8f, pulse: 0.25f, bone: 5, offset: new Vector3(5f, -4f, -1f)));

            // Swamp of Peace: Types 57/71 hidden TRUE_FIRE (57: range 3).
            Add(57, 57, Fire(1f, 0.6f, 0.2f, 3f, 1f));
            Add(57, 71, Fire(1f, 0.6f, 0.2f, 3f, 0.9f));

            // Raklion + Selupan boss: Type 70 hidden blue HIK fire, Type 80 hidden red HIK fire.
            for (int world = 58; world <= 59; world++)
            {
                Add(world, 70, Light(0.1f, 0.4f, 1f, 3f, 0.9f, flicker: 0.15f, z: 40f, hide: true));
                Add(world, 80, Fire(1f, 0.35f, 0.15f, 3f, 1f));
            }

            // Santa Village: Types 26/27/28 red / blue / cyan lamp glows (fLumi sin(WT*0.005)).
            Add(63, 26, Light(1f, 0.25f, 0.05f, 1.5f, 0.7f, pulse: 0.1f, pulseSpeed: 0.005f));
            Add(63, 27, Light(0.05f, 0.25f, 1f, 1.5f, 0.7f, pulse: 0.1f, pulseSpeed: 0.005f));
            Add(63, 28, Light(0.05f, 0.75f, 0.75f, 1.5f, 0.7f, pulse: 0.1f, pulseSpeed: 0.005f));

            // Vulcanus + Doppelganger Blaze: Type 6 hidden HIK fire with red LIGHT sprite.
            Add(64, 6, Fire(1f, 0.4f, 0.15f, 3f, 1.1f));
            Add(67, 6, Fire(1f, 0.4f, 0.15f, 3f, 1.1f));
            Add(67, 47, Light(0.1f, 0.4f, 1f, 3f, 0.9f, flicker: 0.15f, z: 40f, hide: true));
            // Vulcanus furnaces 67/68: red LIGHT sprites on bone 6.
            Add(64, 67, Light(1f, 0.15f, 0.05f, 4f, 1f, pulse: 0.2f, bone: 6));
            Add(64, 68, Light(1f, 0.15f, 0.05f, 4f, 1f, pulse: 0.2f, bone: 6));

            // Land of Trials (Hunting Ground): Type 49 torch, red LIGHT sprite on bone 3.
            Add(32, 49, Light(1f, 0.35f, 0.2f, 3f, 0.9f, flicker: 0.1f, pulse: 0.2f, bone: 3));

            // Loren Market: Type 30 street lamp (bone 1, range 3), Type 35 wall lamp (bone 2, range 1),
            // Type 57 hidden HIK fire.
            Add(80, 30, Light(1f, 0.72f, 0.3f, 3f, 1f, pulse: 0.2f, bone: 1));
            Add(80, 35, Light(1f, 0.72f, 0.3f, 1.5f, 0.9f, flicker: 0.15f, bone: 2));
            Add(80, 57, Fire(1f, 0.45f, 0.2f, 2.5f, 1f));

            // Karutan 1/2: Type 113 hidden HIK fire brazier (range 3).
            Add(81, 113, Fire(1f, 0.6f, 0.2f, 3f, 1f));
            Add(82, 113, Fire(1f, 0.6f, 0.2f, 3f, 1f));

            // Valley of Loren (Battle Castle): Type 0 hidden TRUE_FIRE (range 3).
            Add(31, 0, Fire(1f, 0.6f, 0.2f, 3f, 1f));

            // Imperial Guardian: Type 79 hidden HIK fire, Type 80 hidden blue glow, Type 37 lamp flare (bone 1).
            for (int world = 70; world <= 73; world++)
            {
                Add(world, 79, Fire(1f, 0.45f, 0.2f, 2.5f, 1f));
                Add(world, 80, Light(0.2f, 0.25f, 1f, 3f, 0.7f, flicker: 0.2f, hide: true));
                Add(world, 37, Light(0.9f, 0.9f, 0.85f, 2.5f, 0.7f, flicker: 0.15f, bone: 1));
            }

            // Illusion Temple: Type 76 hidden red torch, Type 77 hidden blue torch.
            Add(47, 76, Fire(1f, 0.45f, 0.3f, 3f, 1f));
            Add(47, 77, Light(0.25f, 0.3f, 1f, 3f, 0.9f, flicker: 0.2f, z: 20f, hide: true));
        }

        public static bool TryGet(int worldIndex, int type, [MaybeNullWhen(false)] out MapObjectLightSpec spec) =>
            Specs.TryGetValue(Key(worldIndex, type), out spec);

        /// <summary>
        /// Maps the lit types of a world to <see cref="MapLightObject"/>, replacing only plain
        /// <see cref="MapTileObject"/> entries so dedicated object classes keep precedence.
        /// </summary>
        public static void ApplyTileTypes(int worldIndex, Type[] mapTileObjects)
        {
            foreach (var entry in Specs)
            {
                if (entry.Key >> 16 != worldIndex)
                    continue;

                int type = entry.Key & 0xFFFF;
                if ((uint)type < (uint)mapTileObjects.Length && mapTileObjects[type] == typeof(MapTileObject))
                    mapTileObjects[type] = typeof(MapLightObject);
            }
        }

        private static int Key(int worldIndex, int type) => (worldIndex << 16) | (type & 0xFFFF);

        private static void Add(int worldIndex, int type, MapObjectLightSpec spec) => Specs[Key(worldIndex, type)] = spec;

        private static MapObjectLightSpec Light(
            float r, float g, float b, float radiusTiles, float intensity,
            float flicker = 0f, float pulse = 0f, float pulseSpeed = 0.002f,
            float z = 0f, int bone = -1, Vector3? offset = null, bool hide = false) => new()
            {
                Color = new Vector3(r, g, b),
                RadiusTiles = radiusTiles,
                Intensity = intensity,
                Flicker = flicker,
                Pulse = pulse,
                PulseSpeed = pulseSpeed,
                Offset = (offset ?? Vector3.Zero) + new Vector3(0f, 0f, z),
                Bone = bone,
                HideMesh = hide,
            };

        private static MapObjectLightSpec Fire(
            float r, float g, float b, float radiusTiles, float intensity, bool hide = true) => new()
            {
                Color = new Vector3(r, g, b),
                RadiusTiles = radiusTiles,
                Intensity = intensity,
                Fire = true,
                HideMesh = hide,
            };
    }

    /// <summary>
    /// Runtime light of one map object: a single static terrain DynamicLight (position resolved
    /// once, intensity flickered allocation-free at the legacy 25 Hz step) or a CreateFire emitter.
    /// </summary>
    internal sealed class MapObjectLight
    {
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private const int MaxBoneResolveAttempts = 25;

        private readonly ModelObject _owner;
        private readonly MapObjectLightSpec _spec;
        private readonly DynamicLight? _light;
        private readonly float _phase;
        private TerrainControl? _terrain;
        private double _accumulator = LegacyStepSeconds;
        private bool _positionResolved;
        private int _boneAttempts;

        private MapObjectLight(ModelObject owner, MapObjectLightSpec spec)
        {
            _owner = owner;
            _spec = spec;
            _phase = owner.Position.X * 0.013f + owner.Position.Y * 0.007f;

            if (spec.Fire)
            {
                var fire = new LorenciaObjectParticleEffect(owner, LorenciaObjectEffectKind.Fire, spec.Offset)
                {
                    LightRadius = spec.RadiusTiles * Constants.TERRAIN_SCALE,
                    LightTint = spec.Color,
                    LightIntensity = spec.Intensity,
                };
                owner.Children.Add(fire);
                return;
            }

            _light = new DynamicLight
            {
                Owner = owner,
                Position = owner.Position,
                Color = spec.Color,
                Radius = spec.RadiusTiles * Constants.TERRAIN_SCALE,
                Intensity = spec.Intensity,
            };
        }

        /// <summary>
        /// Creates the light of a catalogued map object. Call from the owner's Load() before
        /// base.Load() so a fire emitter child is loaded together with the model.
        /// </summary>
        public static MapObjectLight? TryAttach(ModelObject owner)
        {
            var world = owner.World;
            if (world == null || !MapObjectLightCatalog.TryGet(world.WorldIndex, owner.Type, out var spec))
                return null;

            return new MapObjectLight(owner, spec);
        }

        /// <summary>Call after base.Load(): hides anchor meshes and registers the terrain light once.</summary>
        public void Register()
        {
            if (_spec.HideMesh)
                _owner.HiddenMesh = -2;

            var terrain = _owner.World?.Terrain;
            if (_light == null || terrain == null || _terrain != null)
                return;

            ResolvePosition();
            terrain.AddDynamicLight(_light);
            _terrain = terrain;
        }

        public void Unregister()
        {
            if (_light != null && _terrain != null)
                _terrain.RemoveDynamicLight(_light);

            _terrain = null;
        }

        public void Update(GameTime gameTime)
        {
            if (_light == null || _terrain == null)
                return;

            _accumulator += gameTime.ElapsedGameTime.TotalSeconds;
            if (_accumulator < LegacyStepSeconds)
                return;

            _accumulator = 0d;

            if (!_positionResolved)
                ResolvePosition();

            if (_spec.Flicker <= 0f && _spec.Pulse <= 0f)
                return;

            float factor = 1f;
            if (_spec.Flicker > 0f)
                factor += (MuGame.Random.Next(1001) * 0.002f - 1f) * _spec.Flicker;
            if (_spec.Pulse > 0f)
                factor += MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * _spec.PulseSpeed + _phase) * _spec.Pulse;

            _light.Intensity = _spec.Intensity * MathF.Max(factor, 0.05f);
        }

        private void ResolvePosition()
        {
            if (_light == null)
                return;

            Matrix world = _owner.WorldPosition;
            Vector3 local = _spec.Offset;
            bool resolved = true;

            if (_spec.Bone >= 0)
            {
                var bones = _owner.GetBoneTransforms();
                if (bones != null && bones.Length > _spec.Bone)
                    local = Vector3.Transform(_spec.Offset, bones[_spec.Bone]);
                else
                    resolved = ++_boneAttempts >= MaxBoneResolveAttempts;
            }

            Vector3 position = Vector3.Transform(local, world);
            if (_light.Position != position)
                _light.Position = position;

            _positionResolved = resolved;
        }
    }
}
