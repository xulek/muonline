#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// MODEL_FENRIR_THUNDER sub 2 / 3 (Effect/lightning_type01.bmd). Shared, owner-tagged slots like the global Effects array.
    /// init    ZzzEffect.cpp:4453 (sub 2: LifeTime 4, Scale 0.1 + rand%100*0.002, jitter (rand%40-20)*F;
    ///         sub 3: Scale 0.5 + rand%100*0.002, jitter (rand%160-80)*F; Angle rand%360 x3, Alpha 1;
    ///         CreateSprite(BITMAP_LIGHT, Position, 2, Light - 0.3) once)
    /// move    no sub 2/3 case; epilogue ZzzEffect.cpp:17884 PlayAnimation(Velocity 0.3), Direction 0, LifeTime -= F
    /// render  ZzzObject.cpp:1695 RenderBody(RENDER_TEXTURE) with BodyLight = RequestTerrainLight + Light (BodyLight 204);
    ///         the only mesh (pannilleff02_R.jpg) is a "_R" bright script mesh -> blend mesh, additive. One animation key: no bake.
    /// </summary>
    internal sealed class SourceFenrirThunderPool
    {
        public const string ModelPath = "Effect/lightning_type01.bmd";
        public const string LightTexturePath = "Effect/flare01.jpg"; // BITMAP_LIGHT

        private const int Capacity = 256;

        private struct Bolt
        {
            public object? Owner;
            public Vector3 Position;
            public Vector3 AngleRadians;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public bool Fresh;
        }

        public static readonly SourceFenrirThunderPool Shared = new();

        private readonly Bolt[] _bolts = new Bolt[Capacity];

        private SourceFenrirThunderPool()
        {
        }

        /// <summary>CreateEffect(MODEL_FENRIR_THUNDER, position, angle, light, subType 2 | 3, owner).</summary>
        public void Spawn(object owner, int subType, Vector3 position, Vector3 light, float factor)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt b = ref _bolts[i];
                if (b.Owner != null)
                    continue;

                b.Owner = owner;
                b.LifeTime = 4f;
                b.Scale = (subType == 3 ? 0.5f : 0.1f) + MuGame.Random.Next(100) * 0.002f;
                b.AngleRadians = new Vector3(
                    MathHelper.ToRadians(MuGame.Random.Next(360)),
                    MathHelper.ToRadians(MuGame.Random.Next(360)),
                    MathHelper.ToRadians(MuGame.Random.Next(360)));
                b.Light = light;
                int range = subType == 3 ? 160 : 40;
                int half = range / 2;
                position.X += (MuGame.Random.Next(range) - half) * factor;
                position.Y += (MuGame.Random.Next(range) - half) * factor;
                position.Z += (MuGame.Random.Next(range) - half) * factor;
                b.Position = position;
                b.Fresh = true;
                return;
            }
        }

        public bool HasLive(object owner)
        {
            for (int i = 0; i < _bolts.Length; i++) if (_bolts[i].Owner == owner) return true;
            return false;
        }

        public void Release(object owner)
        {
            for (int i = 0; i < _bolts.Length; i++) if (_bolts[i].Owner == owner) _bolts[i].Owner = null;
        }

        /// <summary>Call before spawning this frame's bolts: ages the existing ones and ends their one-frame sprite.</summary>
        public void Move(object owner, float factor)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt b = ref _bolts[i];
                if (b.Owner != owner)
                    continue;

                b.Fresh = false;
                b.LifeTime -= factor;
                if (b.LifeTime <= 0f)
                    b.Owner = null;
            }
        }

        /// <summary>The init-time BITMAP_LIGHT sprites of bolts created this frame (additive).</summary>
        public void DrawSprites(object owner, SourceQuadBatch batch, Texture2D? lightTexture)
        {
            if (!batch.Begin(lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt b = ref _bolts[i];
                if (b.Owner == owner && b.Fresh)
                    batch.AddSprite(b.Position, 2f, b.Light - new Vector3(0.3f), 0f);
            }
            batch.End();
        }

        public void DrawModels(object owner, SourceModelRenderer? renderer, TerrainControl? terrain)
        {
            if (renderer == null)
                return;

            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt b = ref _bolts[i];
                if (b.Owner != owner)
                    continue;

                Vector3 light = (terrain?.EvaluateTerrainLight(b.Position.X, b.Position.Y) ?? Vector3.Zero) + b.Light;
                renderer.DrawInstance(b.Position, b.AngleRadians, b.Scale, light, 1f);
            }
        }
    }
}
