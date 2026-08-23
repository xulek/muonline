using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Ambient desert crawler bug in Tarkan (SourceMain5.2 GOBoid.cpp MODEL_BUG01 + 1 / Bug02.bmd).
    /// Features smooth alpha fading (fade-in on spawn, fade-out on despawn) to prevent sudden pop-in/pop-out.
    /// </summary>
    public class TarkanBugObject : ModelObject
    {
        private WeaponTrailEffect _trailEffect;

        public bool Live { get; set; } = true;
        public float Velocity { get; set; } = 2.5f;
        public float Gravity { get; set; } = 9.0f; // Max turn angle in degrees per frame
        public float LifeTime { get; set; } = 40.0f; // Seconds of active lifespan
        public int SubType { get; set; }
        public Vector3 Direction { get; set; }

        public bool IsFadedOut => !Live && Alpha <= 0.01f;

        public TarkanBugObject()
        {
            Alpha = 0f; // Smooth fade-in on spawn (SourceMain5.2 o->Alpha = 0.f; o->AlphaTarget = 1.f)
            LightEnabled = true;
            Light = new Vector3(0.5f, 0.5f, 0.5f);
            BlendState = BlendState.AlphaBlend;
            IsTransparent = false;

            // Tail energy joint effect (SourceMain5.2 CreateJoint(BITMAP_JOINT_ENERGY, o->Position, o->Position, o->Angle, 4, o, 30.f))
            _trailEffect = new WeaponTrailEffect();
            _trailEffect.SetTrailColor(new Color(100, 180, 255, 230));
            _trailEffect.SamplePoint = () => Position;
            Children.Add(_trailEffect);
        }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare("Object9/Bug02.bmd");

            if (Model != null)
            {
                CurrentAction = 0;
                AnimationSpeed = 1.0f;
            }

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Smooth alpha transitions matching SourceMain5.2 Alpha(o)
            if (Live)
            {
                if (Alpha < 1.0f)
                {
                    Alpha = MathF.Min(1.0f, Alpha + dt * 2.0f); // 0.5s fade-in
                }
            }
            else
            {
                if (Alpha > 0.0f)
                {
                    Alpha = MathF.Max(0.0f, Alpha - dt * 2.5f); // 0.4s fade-out
                }
            }
        }

        public void TurnAngle(float targetAngleRad, float turnSpeedDeg, float deltaTime)
        {
            float currentAngleRad = Angle.Z;
            float diff = MathHelper.WrapAngle(targetAngleRad - currentAngleRad);
            float maxStepRad = MathHelper.ToRadians(turnSpeedDeg) * (deltaTime * 25f);
            float step = MathHelper.Clamp(diff, -maxStepRad, maxStepRad);
            Angle = new Vector3(Angle.X, Angle.Y, MathHelper.WrapAngle(currentAngleRad + step));
        }
    }
}
