#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>
    /// CreateParticle / CreateJoint presets shared by the skill ports (ZzzEffectParticle.cpp,
    /// ZzzEffectJoint.cpp). Each returns a particle ready to spawn; texture slots are chosen by the caller.
    /// </summary>
    public static class SkillFxTemplates
    {
        /// <summary>BITMAP_FIRE / FIRE+2 default sub-type (2, 10, 18-like): 4-frame strip rising flame.</summary>
        public static SkillFxParticle Fire(byte slot, Vector3 position, Vector3 angleDeg, Vector3 light, float scale, bool shrink)
        {
            var p = SkillFxParticlePool.Create(slot, position, 24f, scale, light);
            p.Sheet = SkillFxSheet.Strip4;
            p.FadeWithLife = true;
            p.GravityAccel = 0.004f;
            p.GravityToPosition = 10f;
            if (shrink)
            {
                // sub 10: Velocity (0, -(32..47)*0.1, 0), Scale *= 0.95
                p.Velocity = SkillFxMath.Rotate(new Vector3(0f, -(SkillFxMath.Rand(16) + 32) * 0.1f, 0f), angleDeg);
                p.ScaleMul = 0.95f;
            }
            else
            {
                // default: Velocity (0, -(rand%32-16)*0.1, 0), Scale += Gravity, Velocity *= 0.98
                p.Velocity = SkillFxMath.Rotate(new Vector3(0f, -(SkillFxMath.Rand(32) - 16) * 0.1f, 0f), angleDeg);
                p.GravityToScale = 1f;
                p.VelocityDamping = 0.98f;
            }
            p.Rotation = MathHelper.ToRadians(SkillFxMath.Rand(360));
            return p;
        }

        /// <summary>BITMAP_SPARK sub 0/2 (Spark02.jpg): bouncing gravity sparks, light = Life / 16.</summary>
        public static SkillFxParticle Spark(byte slot, Vector3 position, bool big)
        {
            float scale = (SkillFxMath.Rand(4) + 4) * 0.1f;
            Vector3 local = new(0f, (SkillFxMath.Rand(20) + 20) * 0.1f, 0f);
            if (big)
            {
                scale *= 2f;
                local *= 3f;
            }

            float life = SkillFxMath.Rand(16) + 24;
            var p = SkillFxParticlePool.Create(slot, position, life, scale, Vector3.One);
            p.Velocity = SkillFxMath.RotateYaw(local, SkillFxMath.Rand(360));
            p.Gravity = SkillFxMath.Rand(16) + 6;
            p.GravityAccel = -2f;
            p.GravityToPosition = 1f;
            p.GroundBounce = true;
            p.FadeWithLife = true;
            p.MaxLife = 16f + 0.0001f; // light = LifeTime / 16 (clamped by the color conversion)
            p.Life = life;
            return p;
        }

        /// <summary>BITMAP_EXPLOTION (Explotion01.jpg 4x4 sheet, life 20).</summary>
        public static SkillFxParticle Explosion(byte slot, Vector3 position, float scale)
        {
            var p = SkillFxParticlePool.Create(slot, position, 20f, scale, Vector3.One);
            p.Sheet = SkillFxSheet.Grid4x4;
            return p;
        }

        /// <summary>BITMAP_EXPLOTION + 1 (DinoE.jpg 4-frame strip, life 12).</summary>
        public static SkillFxParticle DinoExplosion(byte slot, Vector3 position, float scale)
        {
            var p = SkillFxParticlePool.Create(slot, position, 12f, scale, Vector3.One);
            p.Sheet = SkillFxSheet.Strip4;
            return p;
        }
    }
}
