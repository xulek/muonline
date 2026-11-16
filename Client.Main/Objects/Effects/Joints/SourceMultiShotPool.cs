#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controls;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// The MODEL_MULTI_SHOT1 / 2 / 3 ring set created by Multi-Shot (WSclient.cpp:5067) and the Rage Fighter kicks
    /// (MonkSystem.cpp:1199, 1278): 2 x SHOT3 and 2 x SHOT2 at pos + rot(0, 20, 0), 3 x SHOT1 at pos, Light (0.8, 0.9, 1.6).
    /// init   ZzzEffect.cpp:1681 (Position += rot(-10, -60, 135), Velocity 1, Scale 0, BlendMesh -2, LifeTime 16 / 13 / 12)
    /// move   ZzzEffect.cpp:7045 (Scale += 0.2 / 0.3 / 0.25 per tick, BlendMeshLight = LifeTime / 18), epilogue PlayAnimation(1)
    /// render ZzzEffect.cpp:18121 (PlayAnimation(Velocity / 6)), ZzzObject.cpp:1520 (BodyLight = Light, RENDER_BRIGHT)
    /// Copies created together are identical, so one entry is drawn Count times.
    /// </summary>
    internal sealed class SourceMultiShotPool
    {
        private static readonly string[] Paths = { "Effect/multishot01.bmd", "Effect/multishot02.bmd", "Effect/multishot03.bmd" };
        private static readonly float[] LifeTimes = { 16f, 13f, 12f };
        private static readonly float[] Growth = { 0.2f, 0.3f, 0.25f };

        private struct Ring
        {
            public bool Live;
            public bool Animating;
            public int Type;
            public int Count;
            public Vector3 Position;
            public Vector3 AngleRadians;
            public Vector3 Light;
            public float Scale;
            public float BlendMeshLight;
            public float LifeTime;
            public float Frame;
        }

        private readonly Ring[] _rings;
        private readonly SourceModelRenderer?[] _renderers = new SourceModelRenderer?[3];
        private readonly int[] _keys = { 1, 1, 1 };
        private int _live;

        public SourceMultiShotPool(int sets)
        {
            _rings = new Ring[sets * 3];
        }

        public bool HasLive => _live > 0;

        public async Task LoadAsync(WorldControl? world)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!await Client.Main.Content.BMDLoader.Instance.AssestExist(Paths[i]))
                    continue;

                var renderer = new SourceModelRenderer(Paths[i]) { AnimationSpeed = 0f };
                if (!await renderer.LoadInto(world))
                    continue;

                _renderers[i] = renderer;
                _keys[i] = renderer.Model?.Actions is { Length: > 0 } actions ? Math.Max(1, actions[0].NumAnimationKeys) : 1;
            }
        }

        public void SpawnSet(Vector3 position, Vector3 angleDeg, Vector3 light)
        {
            Vector3 forward = SourceJointMath.Rotate(new Vector3(0f, 20f, 0f), angleDeg);
            Add(2, 2, position + forward, angleDeg, light);
            Add(0, 3, position, angleDeg, light);
            Add(1, 2, position + forward, angleDeg, light);
        }

        private void Add(int type, int count, Vector3 position, Vector3 angleDeg, Vector3 light)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring ring = ref _rings[i];
                if (ring.Live)
                    continue;

                ring.Live = true;
                ring.Animating = true;
                ring.Type = type;
                ring.Count = count;
                ring.Position = position + SourceJointMath.Rotate(new Vector3(-10f, -60f, 135f), angleDeg);
                ring.AngleRadians = new Vector3(
                    MathHelper.ToRadians(angleDeg.X), MathHelper.ToRadians(angleDeg.Y), MathHelper.ToRadians(angleDeg.Z));
                ring.Light = light;
                ring.Scale = 0f;
                ring.LifeTime = LifeTimes[type];
                ring.BlendMeshLight = ring.LifeTime / 18f;
                ring.Frame = 0f;
                _live++;
                return;
            }
        }

        public void Move(float factor)
        {
            if (_live == 0)
                return;

            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring ring = ref _rings[i];
                if (!ring.Live)
                    continue;

                ring.Scale += Growth[ring.Type] * factor;
                ring.BlendMeshLight = ring.LifeTime / 18f;

                int keys = _keys[ring.Type];
                if (ring.Animating && keys > 1)
                {
                    // MoveEffect epilogue: PlayAnimation(Velocity 1) stops at the last key.
                    ring.Frame += factor;
                    if (ring.Frame >= keys)
                    {
                        ring.Frame = keys;
                        ring.Animating = false;
                    }
                }

                ring.LifeTime -= factor;
                if (ring.LifeTime <= 0f)
                {
                    ring.Live = false;
                    _live--;
                    continue;
                }

                if (ring.Animating && keys > 1)
                {
                    // RenderEffects: PlayAnimation(Velocity / 6) wraps.
                    ring.Frame += factor / 6f;
                    if (ring.Frame >= keys)
                    {
                        int whole = (int)ring.Frame;
                        ring.Frame = whole % keys + (ring.Frame - whole);
                    }
                }
            }
        }

        public void Draw(GameTime gameTime)
        {
            if (_live == 0)
                return;

            for (int i = 0; i < _rings.Length; i++)
            {
                ref Ring ring = ref _rings[i];
                SourceModelRenderer? renderer = ring.Live ? _renderers[ring.Type] : null;
                if (renderer == null)
                    continue;

                renderer.SetAnimationTime(MathF.Min(ring.Frame, _keys[ring.Type] - 0.01f));
                renderer.Update(gameTime);
                Vector3 light = ring.Light * ring.BlendMeshLight;
                for (int c = 0; c < ring.Count; c++)
                    renderer.DrawInstance(ring.Position, ring.AngleRadians, ring.Scale, light, 1f);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                _renderers[i]?.Dispose();
                _renderers[i] = null;
            }
        }
    }
}
