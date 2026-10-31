using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using Fx = Client.Main.Objects.Effects.ItemLinkEffect.Fx;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of the per-item effect switch at the end of RenderLinkObject (ZzzCharacter.cpp,
    /// SourceMain5.2). Every case mirrors the original CreateSprite / CreateParticle calls;
    /// joints (CreateJoint) and model effects (CreateEffect) are not reproduced.
    /// </summary>
    public static class ItemLinkEffectEmitter
    {
        private const int Sword = 0, Mace = 2, Spear = 3, Bow = 4, Staff = 5, Shield = 6;

        private static Vector3 V(float r, float g, float b) => new(r, g, b);
        private static float Sin(double x) => (float)Math.Sin(x);
        private static float AbsSin(double x) => MathF.Abs((float)Math.Sin(x));

        public static void Emit(ItemLinkEffect e, int group, int index, bool standalone = false)
        {
            if (standalone && group != 12 && group != 14)
                return;

            double t = e.WorldTime;
            // Luminosity = (rand() % 30 + 70) * 0.005f, evaluated once before the switch.
            float lum = (e.Rand(30) + 70) * 0.005f;

            switch (group)
            {
                case Bow: EmitBow(e, index, t, lum); break;
                case Staff: EmitStaff(e, index, t, lum); break;
                case Mace: EmitMace(e, index, t, lum); break;
                case Sword: EmitSword(e, index, t, lum); break;
                case Spear: EmitSpear(e, index, lum); break;
                case Shield: EmitShield(e, index, t, lum); break;
                case 12: EmitWing(e, index, t); break;
                case 14: EmitPotion(e, index, t); break;
            }
        }

        /// <summary>Sprites of special potions (RenderPartObjectEffect).</summary>
        private static void EmitPotion(ItemLinkEffect e, int index, double t)
        {
            switch (index)
            {
                case 18: // Devil's Key
                case 19: // Devil's Invitation
                {
                    float l = Sin(t * 0.002) * 0.35f + 0.65f;
                    var light = V(l * 2f, l * 0.32f, l * 0.32f);
                    int first = index == 18 ? 1 : 9;
                    e.Sprite(Fx.Spark1, e.Bone(first), l * 0.8f, light);
                    e.Sprite(Fx.Spark1, e.Bone(first + 1), l * 0.8f, light);
                    break;
                }
                case 21: // Potion + 21
                {
                    float l = Sin(t * 0.002) * 0.25f + 0.75f;
                    e.Sprite(Fx.Spark1, e.ToWorld(Vector3.Zero), 2.5f, V(l, l * 0.5f, 0f));
                    break;
                }
            }
        }

        /// <summary>Wing sprites, particles and joints from RenderPartObjectEffect (ZzzObject.cpp).</summary>
        private static void EmitWing(ItemLinkEffect e, int index, double t)
        {
            switch (index)
            {
                case 6: // Wings of Darkness
                {
                    float scale = (Sin(t * 0.004) * 0.3f + 0.3f) * 10f + 20f;
                    var light = V(0.6f, 0.3f, 0.8f);
                    for (int i = 0; i < 5; ++i)
                    {
                        DarknessArc(e, e.Bone(22 - i), e.Bone(30 - i), scale, light);
                        DarknessArc(e, e.Bone(7 - i), e.Bone(11 + i), scale, light);
                    }
                    break;
                }
                case 36: // Wing of Storm
                {
                    float l = AbsSin(t * 0.0004) * 0.4f;
                    var cloud = V(0.5f + l, 0.5f + l, 0.5f + l);
                    int[] cloudBones = { 9, 20, 19, 10, 18, 28, 27, 36, 35, 38, 37, 53, 48, 62, 70, 72, 71, 78, 79, 80, 87, 90, 91, 106, 102 };
                    foreach (int bone in cloudBones)
                        e.DarkSprite(Fx.Clud64, e.Bone(bone), 0.5f, cloud, (float)(t * 0.01));

                    // CreateEffect(MODEL_FENRIR_THUNDER, bone, ..., (0.6, 0.6, 0.9), 1, o).
                    if (e.Owner is Wings.WingObject stormWing && e.Chance(2))
                    {
                        foreach (int bone in new[] { 11, 21, 29, 63, 81, 89 })
                        {
                            if (!e.Chance(20))
                                continue;
                            var local = e.BoneLocal(bone) + new Vector3(e.Rand(40) - 20, e.Rand(40) - 20, e.Rand(40) - 20);
                            stormWing.SpawnLightning(local, 0.3f + e.Rand(100) * 0.002f);
                            e.Sprite(Fx.Light, e.ToWorld(local), 2f, V(0.3f, 0.3f, 0.6f));
                        }
                    }

                    float scale = AbsSin(t * 0.003) * 0.2f;
                    int[] lightBones = { 64, 61, 69, 77, 86, 98, 97, 99, 104, 103, 105, 12, 8, 17, 26, 34, 52, 44, 51, 50, 49, 45 };
                    foreach (int bone in lightBones)
                    {
                        bool red = bone is 12 or 64 or 98 or 52;
                        e.Sprite(Fx.Light, e.Bone(bone), red ? scale + 1.4f : scale + 0.3f, red ? V(0.9f, 0f, 0f) : V(0.8f, 0.5f, 0.2f));
                    }
                    break;
                }
                case 37: // Wing of Eternal
                {
                    float scale = AbsSin(t * 0.003) * 0.2f;
                    float l = AbsSin(t * 0.003) * 0.3f;
                    var light = V(0.5f + l, 0.5f + l, 0.6f + l);
                    foreach (int bone in new[] { 24, 31, 15, 8, 53, 35 })
                        e.Sprite(Fx.Light, e.Bone(bone), scale + 1.3f, light);
                    light = V(0.1f, 0.1f, 0.9f);
                    foreach (int bone in new[] { 22, 23, 25, 29, 30, 28, 32, 13, 16, 14, 12, 9, 7, 6, 57, 58, 40, 39 })
                        e.Sprite(Fx.Light, e.Bone(bone), scale + 1.5f, light);
                    foreach (int bone in new[] { 56, 38, 51, 45 })
                        e.Sprite(Fx.Light, e.Bone(bone), scale + 0.5f, light);
                    break;
                }
                case 38: // Wing of Illusion
                {
                    float scale = AbsSin(t * 0.002) * 0.2f;
                    float l = AbsSin(t * 0.002) * 0.4f;
                    foreach (int bone in new[] { 5, 6, 7, 8, 18, 19, 23, 24, 25, 27, 37, 38 })
                        e.Sprite(Fx.Flare, e.Bone(bone), scale + 0.6f, V(0.5f + l, l, l));
                    Vector3 last = Vector3.Zero;
                    foreach (int bone in new[] { 4, 9, 13, 14, 26, 32, 31, 33 })
                    {
                        last = e.Bone(bone);
                        e.Sprite(Fx.Light, last, 1.3f, V(l, 0.5f + l, l));
                    }
                    float f = (Sin(t * 0.004) + 1f) * 0.05f;
                    var glint = V(0.8f + f, 0.8f + f, 0.3f + f);
                    e.Sprite(Fx.Light, last, 0.4f, glint, 0.5f);
                    if (e.Chance(2))
                    {
                        e.ShinyParticle(e.Bone(13), glint, 0.5f);
                        e.ShinyParticle(e.Bone(31), glint, 0.5f);
                    }
                    break;
                }
                case 39: // Wing of Ruin
                {
                    float scale = AbsSin(t * 0.003) * 0.2f;
                    float l = AbsSin(t * 0.003) * 0.3f;
                    foreach (int bone in new[] { 6, 15, 24, 56, 47, 38 })
                        e.Sprite(Fx.Light, e.Bone(bone), scale + 1.5f, V(0.7f + l, 0.5f + l, 0.8f + l));
                    if (e.TicksThisFrame > 0)
                    {
                        var light = V(0.6f, 0.4f, 0.7f);
                        int[] sparks = { 7, 16, 25, 57, 48, 39, 11, 22, 31, 63, 54, 40, 10, 21, 30, 62, 53, 41, 9, 20, 29, 61, 52, 42, 8, 19, 28, 60, 51, 43, 18, 27, 59, 50, 17, 26, 58, 49 };
                        for (int i = 0; i < sparks.Length; i++)
                        {
                            float size = i < 6 ? 0.1f : i < 18 ? 0.3f : i < 30 ? 0.5f : 0.7f;
                            e.EnergyParticle(e.Bone(sparks[i]), light, size);
                        }
                    }
                    break;
                }
                case 43: // Wing of Dimension
                {
                    float scale = AbsSin(t * 0.002) * 0.2f;
                    float l = AbsSin(t * 0.002) * 0.4f;
                    int[] flares = { 7, 30, 31, 43, 8, 20 };
                    for (int i = 0; i < flares.Length; i++)
                    {
                        float div = i < 2 ? 2f : 4f;
                        e.Sprite(Fx.Flare, e.Bone(flares[i]), scale + (i < 2 ? 2f : 0.5f),
                            V((1f + l) / div, (0.7f + l) / div, (0.2f + l) / div));
                    }
                    foreach (int bone in new[] { 29, 38, 42, 19, 15, 6 })
                        e.Sprite(Fx.Flare, e.Bone(bone), scale + 2f, V((0.5f + l) / 2f, (0.1f + l) / 2f, (0.4f + l) / 2f));
                    break;
                }
            }
        }

        /// <summary>
        /// Wings of Darkness: JOINT_THUNDER 14 and JOINT_SPIRIT 4 are straight one-frame joints with
        /// Light (0.3, 0.3, 1), plus a blue flare at the inner bone.
        /// </summary>
        private static void DarknessArc(ItemLinkEffect e, Vector3 center, Vector3 tip, float scale, Vector3 light)
        {
            var jointLight = V(0.3f, 0.3f, 1f);
            e.BeamBetween(Fx.JointThunder, tip, center, scale, jointLight);
            e.BeamBetween(Fx.JointSpirit, center, tip, scale + 5f, jointLight);
            e.Sprite(Fx.FlareBlue, center, scale / 28f, light);
        }

        private static void EmitBow(ItemLinkEffect e, int index, double t, float lum)
        {
            switch (index)
            {
                case 4:  // Tiger Bow
                case 5:  // Silver Bow
                {
                    var light = V(lum * 1f, lum * 0.6f, lum * 0.2f);
                    e.Sprite(Fx.Shiny1, e.Bone(2), 1f, light);
                    e.Sprite(Fx.Shiny1, e.Bone(6), 1f, light);
                    break;
                }
                case 6:  // Chaos Nature Bow
                {
                    var light = V(lum * 0.6f, lum * 1f, lum * 0.8f);
                    for (int i = 13; i <= 18; i++)
                        e.Sprite(Fx.Shiny1, e.Bone(i), 1f, light);
                    break;
                }
                case 17: // Celestial Bow
                {
                    var light = V(lum * 0.5f, lum * 0.5f, lum * 0.8f);
                    for (int i = 13; i <= 18; i++)
                        e.Sprite(Fx.Shiny1, e.Bone(i), 1f, light);
                    for (int i = 5; i <= 8; i++)
                        e.Sprite(Fx.Shiny1, e.Bone(i), 1f, light);
                    break;
                }
                case 21: // Sylph Wind Bow
                {
                    var light = V(0.8f, 0.8f, 0.2f);
                    if (e.Chance(2))
                    {
                        e.SparkParticle(e.Bone(4), light, 11, 0.8f);
                        e.SparkParticle(e.Bone(12), light, 11, 0.8f);
                    }
                    light = V(0.5f, 0.5f, 0.1f);
                    e.Sprite(Fx.Light, e.Bone(7), 2f, light);
                    e.Sprite(Fx.Light, e.Bone(15), 2f, light);
                    var tip = e.Bone(0, 20f);
                    light = V(1f, 1f, 0.4f);
                    e.Sprite(Fx.Light, tip, 0.8f, light);
                    e.Sprite(Fx.Light, tip, 1.3f, light);
                    e.Sprite(Fx.Light, tip, 2f, light);
                    break;
                }
                case 22: // Albatross Bow
                {
                    var light = V(0f, 0.7f, 0f);
                    int[] bones = { 10, 28, 34, 16 };
                    foreach (int bone in bones)
                        e.Sprite(Fx.Light, e.Bone(bone), 1.2f, light);
                    if (e.Chance(2))
                    {
                        light = V(0.3f, 0.9f, 0.2f);
                        foreach (int bone in bones)
                            e.SparkParticle(e.Bone(bone), light, 14, 0.05f);
                    }
                    break;
                }
                case 19: // Great Reign Crossbow
                    for (int i = 1; i < 6; i++)
                    {
                        var position = e.Bone(i, 0f, 0f, 10f);
                        var light = V(lum * 0.5f, lum * 0.5f, lum * 0.8f);
                        e.Sprite(Fx.Shiny1, position, 1f, light);
                        e.Sprite(Fx.Light, position, 2f, i == 5 ? Vector3.One : light);
                    }
                    break;
                case 20: // Arrow Viper Bow
                {
                    float scale = Sin(t * 0.001) + 1f;
                    var light = V(lum * 3f, lum, lum);
                    e.Sprite(Fx.Light, e.Bone(6), scale * 0.8f, light);
                    e.Sprite(Fx.Light, e.Bone(2), scale * 0.8f, light);
                    float rotation = Sin(t * 0.01) * 360f;
                    float l = Sin(t * 0.001) * 0.3f + 0.3f;
                    // Hidden in safe zones in the original; the safe-zone flag is not known here.
                    var position = e.Bone(9, 10f);
                    e.Sprite(Fx.Light, position, scale * 0.8f, V(l, l, l));
                    e.Sprite(Fx.Shiny1, position, 0.8f, V(l * 3f, l, l), 360f - rotation);
                    e.Sprite(Fx.Shiny1, position, 0.8f, V(l * 3f, l, l), rotation);
                    break;
                }
                case 23: // Stinger Bow
                {
                    var light = V(0.2f, 0.25f, 0.3f);
                    for (int i = 0; i <= 43; i++)
                    {
                        if (i != 1)
                            e.Sprite(Fx.Light, e.Bone(i), 0.8f, light);
                    }
                    break;
                }
                case 24: // Air Lyn Bow
                {
                    var light = V(0.2f, 0.8f, 0.5f);
                    foreach (int bone in new[] { 7, 11 })
                    {
                        var position = e.Bone(bone);
                        e.Sprite(Fx.Light, position, 0.8f, light);
                        e.Sprite(Fx.Light, position, 0.8f, light);
                        PulseRing(e, position, Fx.ShockWave, t * 0.02, 90, 0, 0.3f, V(0.2f, 0.8f, 0.5f));
                        PulseRing(e, position, Fx.ShockWave, t * 0.05, 60, 30, 0.3f, V(0.2f, 0.8f, 0.5f));
                    }
                    int action = e.CharacterAction;
                    if (action >= (int)PlayerAction.PlayerAttackFist && action <= (int)PlayerAction.PlayerRideSkill)
                    {
                        for (int i = 0; i < 8; i++)
                            e.Sprite(Fx.Flare, e.Bone(22 + i), 0.4f, light);
                    }
                    break;
                }
            }
        }

        /// <summary>The quarterAngle/theta pattern used by Raven Stick and Air Lyn Bow.</summary>
        private static void PulseRing(ItemLinkEffect e, Vector3 position, Fx texture, double time, int period, int offset,
            float maxSize, Vector3 color)
        {
            float quarterAngle = MathF.PI / 180f * ((int)time % period + offset);
            float theta = MathF.Abs(MathF.Sin(quarterAngle + MathF.PI / 2f));
            float size = MathF.Abs(MathF.Sin(quarterAngle)) * maxSize;
            e.Sprite(texture, position, size, color * theta);
        }

        private static void EmitStaff(ItemLinkEffect e, int index, double t, float lum)
        {
            switch (index)
            {
                case 10: // Divine Staff of Archangel
                {
                    var light = V(lum * 1f, lum * 0.3f, lum * 0.1f);
                    for (int i = 0; i < 10; ++i)
                    {
                        var position = e.Bone(0, i * 30f - 180f, -40f);
                        if (e.Chance(3))
                            e.Sprite(Fx.Shiny1, position, 0.6f, V(0.4f, 0.4f, 0.4f), e.Rand(360));
                        e.Sprite(Fx.Light, position, 2f, light);
                    }
                    break;
                }
                case 12: // Grand Viper Staff
                {
                    float randomPos = e.Rand(60) / 20f - 1.5f;
                    float randomScale = e.Rand(15) / 20f + 1.8f;
                    var position = e.Bone(0, 0f, -170f + randomPos, randomPos);
                    e.Sprite(Fx.Spark1, position, randomScale, V(0.4f, 0.4f, 0.4f));

                    randomScale = e.Rand(15) / 20f + 1f;
                    var light = V(1f, 0.4f, 1f);
                    e.Sprite(Fx.Light, position, randomScale, light);
                    e.Sprite(Fx.Shiny1, position, randomScale, light);
                    e.Sprite(Fx.Shiny1, position, randomScale - 0.3f, light, 90f);
                    if (e.Chance(1))
                        e.SparkParticle(position, light, 11, 2f);
                    e.EnergyTrail(0, position, V(0.8f, 0.2f, 1f), 30f); // JOINT_ENERGY 17, scale 30

                    float l = Sin(t * 0.7) * 0.2f + 0.5f;
                    float rotation = (float)(t * 0.0006 * 360.0);
                    light = V(l - 0.1f, 0.1f, l - 0.1f);
                    position = e.Bone(2, 0f, 10f);
                    e.Sprite(Fx.Shiny1, position, 1.5f, light, rotation);
                    e.Sprite(Fx.Shiny1, position, 1.2f, light, 90f + rotation);
                    position = e.Bone(1, -40f, -10f);
                    e.Sprite(Fx.Shiny1, position, 1f, light, rotation);
                    e.Sprite(Fx.Shiny1, position, 0.7f, light, 90f + rotation);
                    position = e.Bone(1, -160f, -5f);
                    e.Sprite(Fx.Shiny1, position, 1.2f, light, rotation);
                    e.Sprite(Fx.Shiny1, position, 1f, light, 90f + rotation);
                    break;
                }
                case 13: // Platina Staff
                {
                    float l = Sin(t * 0.7) * 0.2f + 0.3f;
                    var bone9 = e.Bone(9);
                    float scale = e.Rand(10) / 500f;
                    e.Sprite(Fx.Light, bone9, 0.3f + scale, V(0.7f, 0.7f, 0.7f));
                    scale = e.Rand(10) / 500f;
                    e.Sprite(Fx.Light, bone9, 4f + scale, V(0.2f, 0.6f + l, 0.2f));
                    scale = e.Rand(30) / 60f;
                    float rotation = (float)(t * 0.0004 * 360.0);
                    e.Sprite(Fx.Shiny1, bone9, 1.2f + scale, V(0.4f, 0.5f + l, 0.4f), -rotation);
                    scale = e.Rand(10) / 500f;
                    rotation = (float)(t * 0.0006 * 360.0);
                    e.Sprite(Fx.Magic, bone9, 0.25f + scale, V(0.6f, 1f, 0.6f), rotation);

                    for (int i = 0; i < 4; ++i)
                    {
                        e.Sprite(Fx.Light, e.Bone(5 + i), 0.2f, V(0.1f, 0.8f, 0.1f));
                        e.Sprite(Fx.Light, e.Bone(10 + i), 0.3f, V(0.5f, 0.5f, 0.5f));
                    }
                    e.Sprite(Fx.Shiny1, e.Bone(4), 0.6f + scale, V(0.1f, 0.8f, 0.1f), rotation);

                    var bone2 = e.Bone(2);
                    scale = e.Rand(10) / 500f;
                    e.Sprite(Fx.Magic, bone2, 0.15f + scale, V(0.6f, 1f, 0.6f), rotation);
                    e.Sprite(Fx.Light, bone2, 0.7f + scale, V(0.8f, 0.8f, 0.8f), -rotation);
                    e.Sprite(Fx.Shiny1, bone2, 0.5f + scale, V(0.1f, 1f, 0.1f), rotation);
                    e.Sprite(Fx.Light, bone2, 2f + scale, V(0.2f, 0.6f + l, 0.2f));
                    break;
                }
                case 11: // Staff of Kundun
                {
                    float l = Sin(t * 0.002) * 0.3f + 0.7f;
                    float rotation = e.Rand(360);
                    var light = V(0f, l * 0.5f, l);
                    foreach (var (bone, lightScale, shinyScale) in new[] { (5, 1f, 0.5f), (6, 1f, 0.5f), (8, 2f, 1f) })
                    {
                        var position = e.Bone(bone);
                        e.Sprite(Fx.Light, position, lightScale, light);
                        e.Sprite(Fx.Shiny1, position, shinyScale, V(0.5f, 0.5f, 0.5f), rotation);
                        e.Sprite(Fx.Shiny1, position, shinyScale, V(0.5f, 0.5f, 0.5f), 360f - rotation);
                    }
                    break;
                }
                case 30: // Deadly Staff
                {
                    var light = V(0.8f, 0.3f, 0.1f);
                    e.Sprite(Fx.Light, e.Bone(4), 0.8f, light);
                    e.Sprite(Fx.Light, e.Bone(5), 0.4f, light);
                    e.Sprite(Fx.Light, e.Bone(6), 0.8f, light);
                    e.Sprite(Fx.Light, e.Bone(7), 0.8f, light);
                    e.Sprite(Fx.Light, e.Bone(8), 2f, light);
                    e.Sprite(Fx.Light, e.Bone(9), 0.8f, light);

                    float l = AbsSin(t * 0.001) * 0.8f + 0.2f;
                    var pulse = V(l * 0.8f, l * 0.1f, l * 0.3f);
                    float randomScale = e.Rand(10) / 20f + 0.8f;
                    light = V(0.8f, 0.2f, 0.4f);
                    foreach (int bone in new[] { 2, 3 })
                    {
                        var position = e.Bone(bone);
                        e.Sprite(Fx.Light, position, 2f, pulse);
                        e.Sprite(Fx.Shiny6, position, randomScale, light);
                    }
                    e.EnergyTrail(0, e.Bone(3), V(0.9f, 0f, 0f), 25f); // JOINT_ENERGY 47 at Red02
                    break;
                }
                case 31: // Imperial Staff
                {
                    var light = V(0.3f, 0.3f, 0.9f);
                    var position = e.Bone(2);
                    e.Sprite(Fx.Light, position, 2f, light);
                    e.Sprite(Fx.Light, position, 2f, light);
                    position = e.Bone(3);
                    e.Sprite(Fx.Light, position, 2.5f, light);
                    e.Sprite(Fx.Light, position, 2.5f, light);
                    e.Sprite(Fx.Light, e.Bone(4), 3f, light);
                    position = e.Bone(5);
                    e.Sprite(Fx.Light, position, 2f, V(0.7f, 0.1f, 0.2f));
                    light = V(0.9f, 0.3f, 0.5f);
                    e.Sprite(Fx.Shiny6, position, 0.8f, light);
                    float randomScale = e.Rand(15) / 20f + 1f;
                    e.Sprite(Fx.Shiny1, position, randomScale, light);
                    e.Sprite(Fx.Shiny1, position, randomScale - 0.3f, light, 90f);
                    if (e.Chance(1))
                        e.SparkParticle(position, light, 11, 2f);
                    e.EnergyTrail(0, position, V(0.8f, 0.2f, 1f), 30f); // JOINT_ENERGY 17
                    e.Sprite(Fx.Shiny2, position, 2f, V(0.7f, 0.7f, 0.7f));
                    break;
                }
                case 32: // MODEL_STAFF + 32
                {
                    var position = e.Bone(1);
                    var light = V(1f, 0.1f, 0.2f);
                    e.Sprite(Fx.Shiny6, position, 0.7f, light);
                    e.Sprite(Fx.LightMarks, position, 0.6f, light);
                    e.Sprite(Fx.Light, position, 2f, light);
                    e.Sprite(Fx.ShockWave, position, 0.65f, V(0.7f, 0.1f, 0.6f), -((int)(t * 0.05) % 360));
                    // Object->Timer: grows 0.15 -> 0.9 at 0.01 per tick while fading after 0.5.
                    float cycle = (float)((t / 40.0) % 75.0) * 0.01f + 0.15f;
                    float fade = cycle > 0.5f ? MathF.Pow(0.95f, (cycle - 0.5f) * 100f) : 1f;
                    e.Sprite(Fx.ShockWave, position, cycle, V(0.7f, 0.1f, 0.6f) * fade);
                    e.Sprite(Fx.LightMarks, position, e.Rand(5) / 25f + 0.3f, V(0.9f, 0.5f, 0.2f));
                    var light1 = V(0.9f, 0.7f, 0.4f);
                    var light2 = V(0.9f, 0.1f, 0.3f);
                    position = e.Bone(2);
                    e.Sprite(Fx.Shiny6, position, 0.3f, light1);
                    e.Sprite(Fx.LightMarks, position, 0.4f, light2);
                    position = e.Bone(3);
                    e.Sprite(Fx.Shiny6, position, 0.2f, light1);
                    e.Sprite(Fx.LightMarks, position, 0.3f, light2);
                    break;
                }
                case 33: // Chromatic Staff
                {
                    var position = e.Bone(8);
                    e.Sprite(Fx.Light, position, 3.5f, V(0.9f, 0.1f, 0.4f));
                    float s = AbsSin(t * 0.0008);
                    e.Sprite(Fx.Magic, position, 0.3f, V(0.1f + 0.8f * s, 0.1f * s, 0.1f + 0.4f * s));
                    e.Sprite(Fx.Shiny1, position, 1f, V(0.8f, 0.8f, 0.2f));
                    for (int i = 1; i < 8; i++)
                    {
                        position = e.Bone(i);
                        var light = V(0.8f, 0.1f, 0.4f);
                        e.Sprite(Fx.Light, position, 0.6f, light);
                        e.Sprite(Fx.Magic, position, 0.12f, light);
                        e.Sprite(Fx.Shiny1, position, 0.3f, V(0.8f, 0.8f, 0.2f));
                    }
                    break;
                }
                case 34: // Raven Stick
                {
                    var position = e.Bone(1);
                    e.Sprite(Fx.Shiny6, position, 1.4f, V(0.4f, 0.2f, 1f));
                    e.Sprite(Fx.Light, position, 1.5f, V(0.7f, 0.1f, 0.9f));
                    e.Sprite(Fx.Shiny2, position, 1.5f, V(0f, 0f, 1f));
                    var light = V(0.4f, 0.4f, 1f);
                    e.Sprite(Fx.PinLight, position, 0.7f, light, -((int)(t * 0.04) % 360));
                    e.Sprite(Fx.PinLight, position, 0.8f, light, -((int)(t * 0.03) % 360));
                    PulseRing(e, position, Fx.Magic, t * 0.02, 90, 0, 0.5f, V(0.7f, 0.1f, 0.9f));
                    float quarterAngle = MathF.PI / 180f * ((int)(t * 0.05) % 60 + 30);
                    float theta = MathF.Abs(MathF.Sin(quarterAngle + MathF.PI / 2f));
                    float size = MathF.Abs(MathF.Sin(quarterAngle)) * 0.5f;
                    e.Sprite(Fx.Magic, position, size, V(0.1f + 0.7f * theta, 0.1f * theta, 0.1f + 0.3f * theta));
                    e.Sprite(Fx.Shiny6, e.Bone(2), 0.8f, V(0.9f, 0f, 0.1f));
                    e.Sprite(Fx.Shiny6, e.Bone(3), 0.6f, V(0.9f, 0.4f, 0.7f));
                    break;
                }
            }
        }

        private static void EmitMace(ItemLinkEffect e, int index, double t, float lum)
        {
            switch (index)
            {
                case 14: // Soleil Scepter
                {
                    var light = V(1f, 0.3f, 0f);
                    float randomPos = e.Rand(60) / 20f - 1.5f;
                    float randomScale = e.Rand(10) / 20f + 1.4f;
                    var position = e.Bone(0, 0f, -100f + randomPos, randomPos);
                    e.Sprite(Fx.Shiny1, position, randomScale, light);
                    e.Sprite(Fx.Shiny1, position, randomScale - 0.3f, light, 20f);

                    randomPos = e.Rand(60) / 20f - 1.5f;
                    randomScale = e.Rand(10) / 20f + 1f;
                    position = e.Bone(0, 0f, -100f + randomPos, randomPos);
                    e.Sprite(Fx.Light, position, randomScale + 0.3f, light);

                    randomPos = e.Rand(40) / 20f - 1f;
                    randomScale = e.Rand(8) / 20f + 0.4f;
                    position = e.Bone(0, 0f, 100f + randomPos, randomPos);
                    e.Sprite(Fx.Shiny1, position, randomScale, light);
                    e.Sprite(Fx.Shiny1, position, randomScale - 0.2f, light, 90f);
                    break;
                }
                case 15: // Shining Scepter
                {
                    float l = Sin(t * 0.002) * 0.3f + 0.1f;
                    float scale = e.Rand(10) / 30f + 1.7f;
                    var bone1 = e.Bone(1);
                    e.Sprite(Fx.Shiny1, bone1, scale, V(0.8f + l, 0.5f + l, 0.1f + l));
                    e.Sprite(Fx.Light, bone1, 3f, V(0.7f, 0.5f, 0.3f));
                    e.Sprite(Fx.Light, e.Bone(2), 1.5f, V(0.8f, 0.6f, 0.4f));
                    e.Sprite(Fx.Light, e.Bone(3), 1.2f, V(0.8f, 0.6f, 0.4f));
                    break;
                }
                case 12: // Great Lord Scepter
                {
                    float rotation = e.Rand(360);
                    float l = Sin(t * 0.002) * 0.3f + 0.7f;
                    var position = e.Bone(1);
                    e.Sprite(Fx.Light, position, 2f, V(0f, l * 0.5f, l));
                    e.Sprite(Fx.Shiny1, position, 1f, V(0.5f, 0.5f, 0.5f), rotation);
                    e.Sprite(Fx.Shiny1, position, 1f, V(0.5f, 0.5f, 0.5f), 360f - rotation);
                    break;
                }
                case 7: // Elemental Mace
                {
                    var position = e.Bone(1);
                    e.Sprite(Fx.Light, position, 2f, V(lum, lum * 0.9f, 0f));
                    e.Sprite(Fx.Light, position, Sin(t * 0.002) + 0.5f, V(0.5f, 0.5f, 0.5f));
                    break;
                }
                case 8: // Battle Scepter
                {
                    float scale = Sin(t * 0.001) + 1f;
                    var light = V(lum * 0.2f, lum * 0.1f, lum * 3f);
                    e.Sprite(Fx.Light, e.Bone(1, -15f), scale, light);
                    var position = e.Bone(1, 10f);
                    e.Sprite(Fx.Light, position, scale, light);
                    float rotation = Sin(t * 0.01) * 360f;
                    float l = Sin(t * 0.001) * 0.5f + 0.5f;
                    e.Sprite(Fx.Shiny1, position, 0.6f, V(l, l, l), 360f - rotation);
                    e.Sprite(Fx.Shiny1, position, 0.6f, V(l, l, l), rotation);
                    break;
                }
                case 9: // Master Scepter
                    for (int i = 1; i < 5; i++)
                    {
                        float l = Sin(t * 0.002) * 0.35f + 0.65f;
                        var position = e.Bone(i + 1, -10f);
                        e.Sprite(Fx.Light, position, 1f, V(l * 0.6f, l * 0.8f, l));
                        if (i == 3)
                            e.Sprite(Fx.Shiny1, position, 0.6f, V(0.5f, 0.5f, 0.5f), e.Rand(360));
                    }
                    break;
                case 10: // Great Scepter
                {
                    float l = Sin(t * 0.001) * 0.5f + 0.7f;
                    var light = V(l, l * 0.8f, l * 0.6f);
                    var position = e.Bone(2);
                    e.Sprite(Fx.Light, position, 1.5f, light);
                    e.Sprite(Fx.Shiny1, position, l * 0.5f, light);
                    position = e.Bone(1, l * -10f);
                    light = V(0.6f, 0.8f, 1f);
                    float rotation = e.Rand(360);
                    e.Sprite(Fx.Light, position, 0.7f, light);
                    e.Sprite(Fx.Shiny1, position, 0.5f, light, rotation);
                    e.Sprite(Fx.Shiny1, position, 0.4f, light, 360f - rotation);
                    break;
                }
                case 11: // Lord Scepter
                {
                    var light = V(1f, 0.6f, 0.3f);
                    e.Sprite(Fx.Light, e.Bone(1), 1.3f, light);
                    for (int i = 0; i < 3; ++i)
                    {
                        var position = e.Bone(2, i * 15f - 10f);
                        e.Sprite(Fx.Light, position, 1.3f, light);
                        e.Sprite(Fx.Shiny1, position, 0.4f, light, e.Rand(360));
                    }
                    break;
                }
                case 13: // Divine Scepter of Archangel
                {
                    var position = e.Bone(0);
                    if (e.Chance(3))
                        e.Sprite(Fx.Shiny1, position, 0.6f, V(0.4f, 0.4f, 0.4f), e.Rand(360));
                    e.Sprite(Fx.Light, position, 2f, V(lum, lum * 0.3f, lum * 0.1f));
                    break;
                }
                case 16: // Frost Mace
                {
                    var light = V(0.5f, 0.8f, 0.5f);
                    e.Sprite(Fx.Shiny6, e.Bone(11), 0.2f, light);
                    e.Sprite(Fx.Shiny6, e.Bone(10), 0.2f, light);
                    light = V(0.9f, 0.1f, 0.3f);
                    e.Sprite(Fx.Shiny6, e.Bone(9), 0.2f, light);
                    e.Sprite(Fx.Shiny6, e.Bone(8), 0.2f, light);
                    var position = e.Bone(1);
                    e.Sprite(Fx.Shiny6, position, 0.2f, light);
                    e.Sprite(Fx.Light, position, 0.8f, light);

                    light = V(0.5f, 0.8f, 0.6f);
                    position = e.Bone(24);
                    e.Sprite(Fx.Shiny6, position, 0.3f, light);
                    e.Sprite(Fx.Light, position, 2f, light);
                    e.Sprite(Fx.PinLight, position, 0.5f, light, (int)(t * 0.04) % 360);
                    e.Sprite(Fx.PinLight, position, 0.7f, light, -((int)(t * 0.03) % 360));
                    e.Sprite(Fx.PinLight, position, 0.9f, light, (int)(t * 0.02) % 360);

                    if (e.Chance(10))
                    {
                        var jitter = new Vector3(e.Rand(20) - 10, e.Rand(20) - 10, e.Rand(20) - 10) * 3f;
                        e.StarShine(position + jitter, V(0.5f, 0.8f, 0.6f), 0.22f);
                    }

                    for (int bone = 12; bone <= 23; bone++)
                        e.Sprite(Fx.Light, e.Bone(bone), bone <= 15 ? 0.2f : bone <= 20 ? 0.3f : 0.4f, light);
                    break;
                }
                case 17: // Absolute Scepter
                {
                    float l = AbsSin(t * 0.0008) * 0.8f + 0.2f;
                    var pulse = V(l * 0.6f, l * 0.5f, l * 0.8f);
                    var light1 = V(0.6f, 0.5f, 0.8f);
                    var light2 = V(0.8f, 0.8f, 0.8f);
                    var zx01 = e.Bone(3);
                    var zx02 = e.Bone(4);
                    Fx mega = Fx.Mega1 + ((int)(t / 1000.0) % 3);

                    e.Sprite(Fx.Light, zx01, e.Rand(10) / 10f + 1f, light1);
                    e.Sprite(Fx.Shiny1, zx01, 0.5f, pulse);
                    var jitter1 = new Vector3(e.Rand(10) - 5, e.Rand(10) - 5, e.Rand(10) - 5);
                    e.Sprite(mega, zx01 + jitter1, (e.Rand(11) - 20) / 100f + 0.5f, light2, e.Rand(380));

                    e.Sprite(Fx.Light, zx02, e.Rand(10) / 5f + 1.5f, light1);
                    e.Sprite(Fx.Shiny1, zx02, 1f, pulse);
                    var jitter2 = new Vector3(e.Rand(10) - 5, e.Rand(10) - 5, e.Rand(10) - 5) * 1.2f;
                    e.Sprite(mega, zx02 + jitter2, (e.Rand(11) - 20) / 50f + 0.8f, light2, e.Rand(380));
                    break;
                }
                case 18: // Stryker Scepter
                {
                    var position = e.Bone(5);
                    e.Sprite(Fx.Shiny1, position, 0.5f, V(0.5f, 0.8f, 0.9f));
                    e.Sprite(Fx.LightMarks, position, 1f, V(0.4f, 0.7f, 0.9f));
                    e.Sprite(Fx.Shiny1, e.Bone(6), 1.1f, V(0.5f, 0.6f, 0.9f));
                    var light = V(0.8f, 0.5f, 0.2f);
                    e.Sprite(Fx.Shiny1, e.Bone(2), 0.5f, light);
                    e.Sprite(Fx.Shiny1, e.Bone(3), 0.5f, light);
                    e.Sprite(Fx.Flare, e.Bone(4), 0.5f * AbsSin(t * 0.0015), Vector3.One);
                    break;
                }
            }
        }

        private static void EmitSword(ItemLinkEffect e, int index, double t, float lum)
        {
            switch (index)
            {
                case 22: // Bone Blade
                {
                    float l = Sin(t * 0.4) * 0.25f + 0.7f;
                    var light = V(l, l - 0.5f, l - 0.5f);
                    e.Sprite(Fx.Light1, e.Bone(0, 5f, -22f, -10f), 0.75f, light);
                    e.Sprite(Fx.Light1, e.Bone(0, -5f, -22f, -10f), 0.75f, light);
                    break;
                }
                case 23: // Explosion Blade
                {
                    float randomPos = e.Rand(60) / 20f - 1.5f;
                    float randomScale = e.Rand(30) / 20f + 1.5f;
                    float l = Sin(t * 0.7) * 0.2f + 0.5f;
                    float rotation = (float)(t * 0.0006 * 360.0);
                    var light = V(0.2f, 0.2f, l);
                    var position = e.Bone(4, 0f, randomPos, randomPos);
                    e.Sprite(Fx.Shiny1, position, randomScale, light, rotation);
                    e.Sprite(Fx.Shiny1, position, randomScale - 0.4f, light, 90f + rotation);
                    e.Sprite(Fx.Light, position, 2.3f, light);
                    e.Sprite(Fx.Light, e.Bone(4, 30f), 2f, light);
                    e.Sprite(Fx.Light, e.Bone(6), 2f, light);
                    e.Sprite(Fx.Light, e.Bone(7), 2f, light);
                    e.Sprite(Fx.Light, e.Bone(8), 1f, light);
                    break;
                }
                case 24: // Daybreak
                {
                    var light = V(0.6f, 0.6f, 0.6f);
                    e.Sprite(Fx.Light1, e.Bone(2, 0f, 0f, 3f), 0.45f, light);
                    e.Sprite(Fx.Light1, e.Bone(2, 0f, 0f, -3f), 0.45f, light);
                    var bone2 = e.Bone(2);
                    e.Sprite(Fx.Shiny1, bone2, 0.7f, light);
                    float scale = 0.5f + e.Rand(100) / 180f;
                    float rotation = (float)(t * 0.0006 * 360.0);
                    e.Sprite(Fx.Shiny1, bone2, scale, light, rotation);
                    float l = Sin(t * 0.4) * 0.25f + 0.6f;
                    e.Sprite(Fx.Light, bone2, 3f, V(l, l, l));
                    break;
                }
                case 25: // Sword Dancer
                {
                    var bone1 = e.Bone(1);
                    e.Sprite(Fx.Light, bone1, 1f, V(1f, 0.1f, 0f));
                    float scale = e.Rand(30) / 60f + 0.2f;
                    float l = Sin(t * 0.7) * 0.2f + 0.3f;
                    float rotation = (float)(t * 0.0006 * 360.0);
                    e.Sprite(Fx.Light3, bone1, scale, V(0.1f + l, 0.2f, 0f), rotation);

                    // A spark travels 0..20 along a random blade bone (2..6), 1.5 units per frame.
                    int cycle = (int)(t / 40.0);
                    int travelBone = 2 + (int)((uint)(cycle / 14 * 2654435761u) % 5);
                    float travel = (cycle % 14) * 1.5f;
                    var position = e.Bone(travelBone, 0f, travel);
                    e.Sprite(Fx.Waterfall4, position, 0.7f, V(1f - l, 0f, 0f));
                    e.Sprite(Fx.Waterfall2, position, 0.3f, V(0.1f, 0.1f, 0.1f));

                    l = Sin(t * 0.4) * 0.25f + 0.2f;
                    for (int i = 0; i < 5; ++i)
                        e.Sprite(Fx.Light, e.Bone(2 + i), 1.1f, V(0.8f + l, 0.1f, 0f));

                    EmitRunSparks(e, 6);
                    break;
                }
                case 19: // Divine Sword of Archangel
                {
                    var light = V(lum, lum * 0.3f, lum * 0.1f);
                    for (int i = 0; i < 7; ++i)
                    {
                        var position = e.Bone(i + 2);
                        if (e.Chance(3))
                            e.Sprite(Fx.Shiny1, position, 0.6f, V(0.4f, 0.4f, 0.4f), e.Rand(360));
                        e.Sprite(Fx.Light, position, 2f, light);
                    }
                    break;
                }
                case 20: // Knight Blade
                    for (int i = 0; i < 2; i++)
                    {
                        var position = e.Bone(i + 1);
                        float l = Sin(t * 0.002) * 0.35f + 0.65f;
                        e.Sprite(Fx.Light, position, l * 0.9f, V(l * 0.43f, l * 0.14f, l * 0.6f));
                        e.Sprite(Fx.Light, position, Sin(t * 0.002) * 0.5f + 0.4f, V(0.3f, 0.3f, 0.3f));
                    }
                    break;
                case 21: // Dark Reign Blade
                case 31: // Rune Blade
                    EmitRunSparks(e, 1);
                    break;
                case 26: // Flamberge
                {
                    var light = V(0.8f, 0.6f, 0.2f);
                    var position = e.Bone(11);
                    e.Sprite(Fx.Light, position, 0.6f, light);
                    e.Sprite(Fx.Light, position, 0.6f, light);
                    light = V(0.3f, 0.8f, 0.7f);
                    e.Sprite(Fx.Light, e.Bone(12), 0.5f, light);
                    e.Sprite(Fx.Light, e.Bone(13), 0.5f, light);
                    e.Sprite(Fx.Light, e.Bone(12), 0.3f, light);
                    e.Sprite(Fx.Light, e.Bone(13), 0.3f, light);
                    light = V(0.9f, 0.1f, 0.1f);
                    float[] scales = { 0.8f, 0.7f, 0.7f, 0.7f, 0.6f, 0.5f, 0.5f };
                    for (int i = 0; i < scales.Length; i++)
                        e.Sprite(Fx.LightMarks, e.Bone(i + 1), scales[i], light);
                    break;
                }
                case 27: // Sword Breaker
                {
                    var light = V(0.1f, 0.9f, 0.1f);
                    float[] scales = { 0.6f, 0.5f, 0.5f, 0.5f, 0.4f, 0.3f, 0.3f };
                    for (int i = 0; i < scales.Length; i++)
                        e.Sprite(Fx.LightMarks, e.Bone(i + 1), scales[i], light);
                    break;
                }
                case 28: // Imperial Sword
                {
                    float randomScale = e.Rand(15) / 30f + 0.5f;
                    var light = V(0.1f, 0.4f, 0.9f);
                    // CreateSprite(BITMAP_FLARE_BLUE, p, 0.4f, o->Light, o): the character light.
                    var characterLight = V(0.6f, 0.6f, 0.6f);
                    for (int bone = 8; bone <= 11; bone++)
                    {
                        var position = e.Bone(bone);
                        e.Sprite(Fx.FlareBlue, position, 0.4f, characterLight);
                        e.Sprite(Fx.Shiny6, position, bone <= 9 ? randomScale : 0.4f, light);
                    }
                    e.EnergyTrail(0, e.Bone(8), V(0.8f, 0.2f, 1f), 25f); // JOINT_ENERGY 17 at Zx01
                    light = V(0f, 0.3f, 0.7f);
                    float[] scales = { 1f, 0.8f, 0.6f, 0.4f, 0.2f, 0.1f };
                    for (int i = 0; i < scales.Length; i++)
                        e.Sprite(Fx.LightMarks, e.Bone(i + 2), scales[i], light);
                    break;
                }
            }
        }

        // ───────────────────────────── +15 equipment ─────────────────────────────

        // (character bone, link offset) of the MODEL_15GRADE_ARMOR_OBJ_* ornaments per body part.
        private static readonly (int Bone, Vector3 Offset)[] HelmOrnaments = { (20, new(28f, 20f, 0f)) };
        private static readonly (int Bone, Vector3 Offset)[] ArmorOrnaments = { (35, new(5f, -20f, 0f)), (26, new(5f, -20f, 0f)) };
        private static readonly (int Bone, Vector3 Offset)[] PantsOrnaments = { (3, new(25f, 5f, -5f)), (10, new(25f, 5f, 5f)) };
        private static readonly (int Bone, Vector3 Offset)[] GlovesOrnaments = { (36, new(30f, 0f, 20f)), (27, new(30f, 0f, -20f)) };
        private static readonly (int Bone, Vector3 Offset)[] BootsOrnaments = { (4, new(20f, 15f, -10f)), (11, new(20f, 15f, 10f)) };

        /// <summary>
        /// NextGradeObjectRender (ZzzObject.cpp): glow on +15 weapons and on the ornaments of
        /// +15 armor parts. The ornament models themselves are not rendered.
        /// </summary>
        /// <summary>
        /// ExtendState (complete ancient set, RenderCharacter): every 100 ticks while standing a
        /// BITMAP_LIGHT subtype 3 effect spawns two rising "ancient blur" helix joints.
        /// </summary>
        public static void EmitAncientSet(ItemLinkEffect e, Player.PlayerObject player, ref float extendStateTime)
        {
            if (!player.Appearance.FullAncientSet && !player.AncientSetCompleteFromEquipment)
            {
                extendStateTime = 0f;
                return;
            }

            int action = (int)player.CurrentAction;
            bool standing = action < (int)PlayerAction.PlayerWalkMale ||
                            action == (int)PlayerAction.PlayerDarklordStand ||
                            action == (int)PlayerAction.PlayerStopRideHorse ||
                            action == (int)PlayerAction.PlayerStopTwoHandSwordTwo;
            if (standing && extendStateTime >= 100f)
            {
                float yaw = MathHelper.ToDegrees(player.Angle.Z);
                e.SpawnAncientHelix(yaw + 180f);
                e.SpawnAncientHelix(yaw + 360f);
                extendStateTime = 0f;
            }
            extendStateTime += e.TicksThisFrame;
        }

        /// <summary>
        /// Elf Soldier: CreateJoint(BITMAP_FLARE, ..., 42, o, 15). The orbit point turns 15 degrees per
        /// tick at radius 50; every 45 degrees it releases (50 %) a rising flare ribbon.
        /// </summary>
        public static void EmitElfOrbit(ItemLinkEffect e, ref float angle)
        {
            for (int tick = 0; tick < e.TicksThisFrame; tick++)
            {
                angle = (angle + 15f) % 360f;
                if ((int)angle % 45 != 0 || e.Rand(100) > 50)
                    continue;

                float radians = MathHelper.ToRadians(angle);
                Vector3 position = e.CharacterPosition + new Vector3(MathF.Sin(radians) * 50f, -MathF.Cos(radians) * 50f, 0f);
                var light = V(1f - e.Rand(300) / 1000f, 1f - e.Rand(300) / 1000f, 1f);
                e.SpawnRisingRibbon(position, light, 15f);
            }
        }

        /// <summary>
        /// PC-room helper in plate armor (Leo the Helper): CreateSprite glows at the root bone from
        /// RenderPartObject for MODEL_PLATE_ARMOR.
        /// </summary>
        public static void EmitPcRoomPlate(ItemLinkEffect e)
        {
            var center = e.Bone(0);
            e.Sprite(Fx.Light, center, 6f, V(0.4f, 0.6f, 0.8f), 0.5f);
            float l = Sin(e.WorldTime * 0.05) * 0.4f + 0.9f;
            e.Sprite(Fx.Light, center, 2f, V(l * 0.3f, l * 0.5f, l * 0.8f));
        }

        public static void EmitNextGrade(ItemLinkEffect e, Player.PlayerObject player)
        {
            double t = e.WorldTime;

            EmitNextGradeWeapon(e, player.Weapon1, 27, 28, t);
            bool rightIsBow = player.Weapon2?.MaterialItemGroup == Bow;
            EmitNextGradeWeapon(e, player.Weapon2, rightIsBow ? 27 : 36, rightIsBow ? 28 : 37, t);

            float l = AbsSin(t * 0.01);
            EmitNextGradeArmor(e, player.Helm, HelmOrnaments, l);
            EmitNextGradeArmor(e, player.Armor, ArmorOrnaments, l);
            EmitNextGradeArmor(e, player.Pants, PantsOrnaments, l);
            EmitNextGradeArmor(e, player.Gloves, GlovesOrnaments, l);
            EmitNextGradeArmor(e, player.Boots, BootsOrnaments, l);
        }

        private static void EmitNextGradeWeapon(ItemLinkEffect e, ModelObject weapon, int bone, int bone2, double t)
        {
            if (weapon == null || weapon.Hidden || weapon.Model == null || weapon.ItemLevel < 15)
                return;

            if (weapon.MaterialItemGroup == Bow)
            {
                bone = 27;
                bone2 = 28;
            }

            var orange = V(1f, 0.6f, 0f);
            e.Sprite(Fx.Light, e.Bone(bone), 0.6f, orange);
            e.Sprite(Fx.Light, e.Bone(bone, 10f), 0.6f, orange);
            e.Sprite(Fx.Light, e.Bone(bone, 20f), 0.6f, orange);

            var position = e.Bone(bone2, 10f);
            float l = AbsSin(t * 0.002);
            e.Sprite(Fx.Magic, position, 0.35f, V(0.7f * l + 0.3f, 0.1f * l + 0.1f, 0f));
            e.Sprite(Fx.FlareRed, position, 0.6f * (e.Rand(60) * 0.01f) + 0.4f, Vector3.One);
            // CreateParticle(BITMAP_LIGHTNING_MEGA1 + rand() % 3, ...) as a one-frame spark.
            e.Sprite(Fx.Mega1 + e.Rand(3), position, (e.Rand(80) + 10) * 0.01f, V(1f, 0.2f, 0f), e.Rand(360));
        }

        private static void EmitNextGradeArmor(ItemLinkEffect e, ModelObject part,
            (int Bone, Vector3 Offset)[] ornaments, float l)
        {
            if (part == null || part.Hidden || part.Model == null || part.ItemLevel < 15)
                return;

            foreach (var (bone, offset) in ornaments)
            {
                var position = e.Bone(bone, offset.X, offset.Y, offset.Z);
                e.Sprite(Fx.Magic, position, 0.12f, V(0.2f * l, 0.4f * l, 1f * l));
                e.Sprite(Fx.Shiny6, position, 0.4f, V(0.4f, 0.7f, 1f));
                e.Sprite(Fx.PinLight, position, 0.6f, V(0.1f, 0.3f, 1f), 90f);
            }
        }

        /// <summary>Two-handed sword running sparks (Sword Dancer, Rune Blade, Dark Reign Blade).</summary>
        private static void EmitRunSparks(ItemLinkEffect e, int bone)
        {
            if (e.CharacterAction != (int)PlayerAction.PlayerRunTwoHandSwordTwo || !e.Chance(2))
                return;

            var position = e.Bone(bone) + new Vector3(e.Rand(30) - 15f, e.Rand(30) - 15f, 20f);
            for (int i = 0; i < 4; i++)
                e.GravitySpark(position, e.Rand(60) + 60 + 90);
        }

        private static void EmitSpear(ItemLinkEffect e, int index, float lum)
        {
            switch (index)
            {
                case 10: // Dragon Spear
                {
                    var light = V(lum * 0.2f, lum * 0.1f, lum * 0.8f);
                    for (int i = 1; i < 9; i++)
                        e.Sprite(Fx.Light, e.Bone(i), 1.3f, light);
                    break;
                }
                case 11: // Beuroba
                {
                    var light = V(0.9f, 0.2f, 0.7f);
                    foreach (int bone in new[] { 3, 4 })
                    {
                        var position = e.Bone(bone);
                        e.Sprite(Fx.Shiny6, position, 0.8f, light);
                        e.Sprite(Fx.Shiny6, position, 0.3f, light);
                    }
                    e.Sprite(Fx.Light, e.Bone(5), 1.5f, V(0.2f, 0.9f, 0.1f));
                    break;
                }
            }
        }

        private static void EmitShield(ItemLinkEffect e, int index, double t, float lum)
        {
            switch (index)
            {
                case 15: // Grand Soul Shield
                {
                    var light = V(lum * 0.6f, lum * 0.6f, lum * 2f);
                    var position = e.Bone(1, 15f, -15f);
                    e.Sprite(Fx.Shiny1, position, 1.5f, light);
                    e.Sprite(Fx.Light, position, lum + 1.5f, light);
                    break;
                }
                case 17: // Crimson Glory
                {
                    var light = V(0.8f, 0.6f, 0.2f);
                    for (int bone = 4; bone <= 10; bone++)
                        e.Sprite(Fx.Light, e.Bone(bone), 1.5f, light);
                    float l = AbsSin(t * 0.0005);
                    var pulse = V(l, l, l);
                    e.Sprite(Fx.FlareRed, e.Bone(1), 0.5f, pulse);
                    e.Sprite(Fx.FlareRed, e.Bone(2), 0.3f, pulse);
                    e.Sprite(Fx.FlareRed, e.Bone(3), 0.3f, pulse);
                    break;
                }
                case 18: // Salamander Shield
                    e.Sprite(Fx.Light, e.Bone(1), 2f, V(0.9f, 0f, 0.2f));
                    break;
                case 20: // Guardian Shield
                {
                    float l = MathF.Abs(Sin(t * 0.001)) + 0.1f;
                    var light = V(0.8f * l, 0.3f * l, 0.8f * l);
                    for (int bone = 1; bone <= 9; bone++)
                        e.Sprite(Fx.Light, e.Bone(bone), 1.5f, light);
                    break;
                }
                case 21: // Cross Shield
                {
                    float l = MathF.Abs(Sin(t * 0.001)) + 0.1f;
                    var light = V(0.2f * l, 0.2f * l, 0.8f * l);
                    for (int bone = 4; bone <= 7; bone++)
                        e.Sprite(Fx.Light, e.Bone(bone), 1.2f, light);
                    light = V(0.2f * l, 0.6f * l, 0.6f * l);
                    for (int bone = 8; bone <= 13; bone++)
                        e.Sprite(Fx.Light, e.Bone(bone), 1.4f, light);
                    break;
                }
            }
        }
    }
}
