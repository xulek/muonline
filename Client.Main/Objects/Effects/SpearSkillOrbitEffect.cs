#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// 5 x CreateJoint(MODEL_SPEARSKILL, owner, sub 0 / 4) orbiting the owner, SourceMain5.2:
    /// sub 0 (eBuff_WizDefense, WSclient.cpp:15046, scale 50, light 1) and sub 4 (eBuff_Defense / eBuff_Attack,
    /// ZzzCharacter.cpp:10571/10596, scale 20, light (0.4, 0.8, 0.2)).
    /// init ZzzEffectJoint.cpp:1498 (MaxTails 30, TexType BITMAP_FLARE_BLUE), move 4392-4485 (tails follow the owner,
    /// magic-screw orbit radius 80 / 120 at +110), render 6957 (min(LifeTime, 20) * 0.05) and 7081 (darkening above
    /// owner + 50, BITMAP_FLARE_BLUE 0.7 sprite at the middle tail).
    /// </summary>
    internal sealed class SpearSkillOrbitEffect : SourceSkillEffectBase
    {
        private const string TexturePath = "Effect/flareBlue.jpg"; // BITMAP_FLARE_BLUE
        private const int JointCount = 5;

        private struct Joint
        {
            public int Index;
            public SourceTrail Trail;
        }

        private readonly Joint[] _joints = new Joint[JointCount];
        private readonly bool _elfSupport;
        private float _scale;
        private readonly Vector3 _light;
        private Texture2D? _texture;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _lastTarget;
        private bool _stopped;
        private bool _fenrir; // SubType 9: owner rides a Fenrir outside the safe zone (ZzzEffectJoint.cpp:4204)

        internal bool IsElfSupportOrbit => _elfSupport;

        internal bool IsOwnedBy(WalkerObject owner) => ReferenceEquals(Caster, owner);

        public SpearSkillOrbitEffect(WalkerObject owner, float scale, Vector3 light)
            : base(owner, 0f, 0f, JointCount * 2 * 200)
        {
            _scale = scale;
            _light = light;
            _elfSupport = light != Vector3.One; // sub 4 / 9
        }

        /// <summary>
        /// DeleteJoint(MODEL_SPEARSKILL, to, 0) + 5 x CreateJoint(..., 0, to, scale): the release switch of
        /// AT_SKILL_WIZARDDEFENSE (ZzzCharacter.cpp:4850-4854) replaces the joints of the buff packet.
        /// </summary>
        public void Restart(float scale)
        {
            _scale = scale;
            if (Started)
                CreateJoints();
        }

        public void Stop()
        {
            _stopped = true;
            RemoveSelf();
        }

        protected override async Task LoadEffectContent()
        {
            _texture = await LoadTexture(TexturePath);
        }

        protected override void OnStart() => CreateJoints();

        private void CreateJoints()
        {
            // The joint keeps the owner angle it was created with (AngleMatrix(o->Angle) for every tail).
            SourceJointMath.AngleBasis(new Vector3(0f, 0f, CasterYawDegrees), out _right, out _, out _up);
            _lastTarget = CasterPosition + new Vector3(0f, 0f, 10f);
            int maxTails = SourceJointMath.ScaledMaxTails(30);
            for (int i = 0; i < JointCount; i++)
            {
                _joints[i].Index = MuGame.Random.Next(1, 500); // pool slot of the original joint
                if (_joints[i].Trail == null)
                    _joints[i].Trail = new SourceTrail(maxTails); // Reset clamps a later re-create to this capacity
                _joints[i].Trail.Reset(maxTails);
                _joints[i].Trail.Push(CasterPosition, _right, _up, _scale);
            }
        }

        protected override void OnTick(float factor)
        {
            if (_elfSupport)
                _fenrir = Caster is Player.PlayerObject { IsRidingFenrir: true } rider && !IsInSafeZone(rider);

            float heightBase = _fenrir ? 140f : 110f; // case 9 / case 0, 4
            Vector3 target = CasterPosition + new Vector3(0f, 0f, 10f);
            Vector3 delta = target - _lastTarget;
            _lastTarget = target;
            int worldFrame = (int)(FPSCounter.Instance.WorldTime / 40.0);

            for (int i = 0; i < JointCount; i++)
            {
                ref Joint j = ref _joints[i];

                // Tails are owner-relative: subtract the old target position, add the new one.
                j.Trail.Translate(delta);

                int frame = ((j.Index % 2) != 0 ? worldFrame : -worldFrame) + j.Index * 53731;
                float s0 = MathF.Sin((frame + 55555) * 0.048f);
                float dx = s0 * MathF.Cos(frame * 0.0613f);
                float dy = s0 * MathF.Sin(frame * 0.0613f);
                float dz = MathF.Cos((frame + 55555) * 0.048f);
                float sinAdd = MathF.Sin((frame + 11111) * 0.1113f);
                float cosAdd = MathF.Cos((frame + 11111) * 0.1113f);
                var dir = new Vector3(cosAdd * dy - sinAdd * dz, sinAdd * dy + cosAdd * dz, dx);

                var position = new Vector3(
                    target.X + dir.X * 80f,
                    target.Y + dir.Y * 80f,
                    heightBase + target.Z + dir.Z * 120f);
                j.Trail.Push(position, _right, _up, _scale);
            }
        }

        private bool IsInSafeZone(Player.PlayerObject player)
        {
            var terrain = World?.Terrain;
            return terrain != null &&
                   terrain.RequestTerrainFlag((int)player.Location.X, (int)player.Location.Y).HasFlag(Client.Data.ATT.TWFlags.SafeZone);
        }

        protected override bool HasActiveContent() => !_stopped;

        protected override void OnDraw()
        {
            if (_texture == null || !Batch.Begin(_texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            float waist = CasterPosition.Z + (_fenrir ? 180f : 50f);
            for (int i = 0; i < JointCount; i++)
            {
                SourceTrail trail = _joints[i].Trail;
                int segments = trail.SegmentCount;
                float denominator = MathF.Max(trail.MaxTails - 1, 1);
                for (int s = 0; s < segments; s++)
                {
                    // fJointHeight = (tail[0].z - (owner.z + 50 | 180 for sub 9)) * 0.01; darker above the waist.
                    float height = (trail.Get(s, 0).Z - waist) * 0.01f;
                    Vector3 light = height > 0f ? _light - new Vector3(height) : _light;
                    Color color = SourceQuadBatch.ToColor(light);
                    float l1 = (segments - s) / denominator;
                    float l2 = (segments - (s + 1)) / denominator;

                    Batch.AddQuad(
                        trail.Get(s, 2), new Vector2(l1, 1f), trail.Get(s, 3), new Vector2(l1, 0f),
                        trail.Get(s + 1, 3), new Vector2(l2, 0f), trail.Get(s + 1, 2), new Vector2(l2, 1f), color);
                    Batch.AddQuad(
                        trail.Get(s, 0), new Vector2(l1, 0f), trail.Get(s, 1), new Vector2(l1, 1f),
                        trail.Get(s + 1, 1), new Vector2(l2, 1f), trail.Get(s + 1, 0), new Vector2(l2, 0f), color);

                    if (s == segments / 2)
                        Batch.AddSprite(trail.GetCenter(s), 0.7f, light);
                }
            }
            Batch.End();
        }
    }
}
