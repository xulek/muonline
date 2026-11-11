using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 RenderMeshEffect approximation for Blood Castle gates
    /// and saint statues. The original emits short-lived Gate/StoneCoffin
    /// model fragments from mesh vertices when the NPC dies.
    /// </summary>
    internal sealed class BloodCastleDeathFragmentEffect : EffectObject
    {
        private const int FragmentCount = 16;
        private const float LifetimeFrames = 48f;

        private readonly MonsterObject _owner;
        private readonly string _firstModelPath;
        private readonly string _secondModelPath;
        private readonly ModelObject[] _fragments = new ModelObject[FragmentCount];
        private Client.Data.BMD.BMD _firstModel;
        private Client.Data.BMD.BMD _secondModel;
        private float _lifeFrames = LifetimeFrames;
        private bool _started;

        public BloodCastleDeathFragmentEffect(
            MonsterObject owner,
            string firstModelPath,
            string secondModelPath)
        {
            _owner = owner;
            _firstModelPath = firstModelPath;
            _secondModelPath = secondModelPath;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-300f, -300f, -100f),
                new Vector3(300f, 300f, 500f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            _firstModel = await BMDLoader.Instance.Prepare(_firstModelPath);
            _secondModel = await BMDLoader.Instance.Prepare(_secondModelPath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            if (!_started && (_owner.IsDead || _owner.CurrentAction == (int)MonsterActionType.Die))
                StartFragments();

            if (!_started)
                return;

            _lifeFrames -= (float)gameTime.ElapsedGameTime.TotalSeconds * 25f;
            if (_lifeFrames <= 0f)
            {
                Parent?.Children.Remove(this);
                Dispose();
            }
        }

        private void StartFragments()
        {
            // The original sets Live = false on the first death frame: the body vanishes at once.
            _started = true;
            _owner.HiddenMesh = -2;

            if (_firstModel == null || _secondModel == null)
                return;

            _lifeFrames = LifetimeFrames;

            for (int i = 0; i < _fragments.Length; i++)
            {
                Client.Data.BMD.BMD model = (i & 1) == 0 ? _firstModel : _secondModel;
                _ = AddFragmentAsync(model, i);
            }
        }

        private async Task AddFragmentAsync(Client.Data.BMD.BMD model, int index)
        {
            var fragment = new BloodCastleFragment(model);
            _fragments[index] = fragment;
            Children.Add(fragment);
            await fragment.Load();
        }

        private sealed class BloodCastleFragment : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;
            private Vector3 _velocity;
            private float _lifeFrames;

            public BloodCastleFragment(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                Position = new Vector3(
                    RandomRange(-80f, 80f),
                    RandomRange(-80f, 80f),
                    RandomRange(0f, 180f));
                Scale = RandomRange(0.8f, 1.1f);
                Angle = new Vector3(
                    RandomRange(0f, MathHelper.TwoPi),
                    RandomRange(0f, MathHelper.TwoPi),
                    RandomRange(0f, MathHelper.TwoPi));

                float yaw = RandomRange(0f, MathHelper.TwoPi);
                float speed = RandomRange(6.4f, 32f);
                _velocity = new Vector3(
                    MathF.Cos(yaw) * speed,
                    MathF.Sin(yaw) * speed,
                    RandomRange(8f, 23f));
                _lifeFrames = RandomRange(32f, 47f);
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f;
                Position += _velocity * frameFactor;
                _velocity *= MathF.Pow(0.9f, frameFactor);
                _velocity.Z -= 3f * frameFactor;
                Angle = new Vector3(
                    Angle.X - MathHelper.ToRadians(Scale * 32f) * frameFactor,
                    Angle.Y,
                    Angle.Z + MathHelper.ToRadians(20f) * frameFactor);
                _lifeFrames -= frameFactor;

                if (_lifeFrames <= 0f && Parent != null)
                    Parent.Children.Remove(this);
            }

            private static float RandomRange(float min, float max) =>
                min + (float)MuGame.Random.NextDouble() * (max - min);
        }
    }
}
