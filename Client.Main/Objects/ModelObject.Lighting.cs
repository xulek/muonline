using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private sealed class ModelEffectBindings
        {
            private readonly Effect _effect;
            private readonly Dictionary<string, EffectTechnique> _techniques = new(StringComparer.Ordinal);

            public ModelEffectBindings(Effect effect)
            {
                _effect = effect;
                BoneMatrices = effect.Parameters["BoneMatrices"];
                CrowdBonePaletteTexture = effect.Parameters["CrowdBonePaletteTexture"];
                CrowdBonePaletteRowCount = effect.Parameters["CrowdBonePaletteRowCount"];
                World = effect.Parameters["World"];
                WorldViewProjection = effect.Parameters["WorldViewProjection"];
                ViewProjection = effect.Parameters["ViewProjection"];
                EyePosition = effect.Parameters["EyePosition"];
                SunDirection = effect.Parameters["SunDirection"];
                LightDirection = effect.Parameters["LightDirection"];
                SunColor = effect.Parameters["SunColor"];
                SunStrength = effect.Parameters["SunStrength"];
                ShadowStrength = effect.Parameters["ShadowStrength"];
                Alpha = effect.Parameters["Alpha"];
                TerrainDynamicIntensityScale = effect.Parameters["TerrainDynamicIntensityScale"];
                AmbientLight = effect.Parameters["AmbientLight"];
                DebugLightingAreas = effect.Parameters["DebugLightingAreas"];
                DiffuseTexture = effect.Parameters["DiffuseTexture"];
                Chrome02Texture = effect.Parameters["Chrome02Texture"];
                Shiny01Texture = effect.Parameters["Shiny01Texture"];
                Chrome01Texture = effect.Parameters["Chrome01Texture"];
                ItemOptions = effect.Parameters["ItemOptions"];
                ItemMaterialGroup = effect.Parameters["ItemMaterialGroup"];
                ItemMaterialIndex = effect.Parameters["ItemMaterialIndex"];
                HighLevelTexturesAvailable = effect.Parameters["HighLevelTexturesAvailable"];
                Time = effect.Parameters["Time"];
                IsAncient = effect.Parameters["IsAncient"];
                IsExcellent = effect.Parameters["IsExcellent"];
                GlowColor = effect.Parameters["GlowColor"];
                GlowIntensity = effect.Parameters["GlowIntensity"];
                EnableGlow = effect.Parameters["EnableGlow"];
                SimpleColorMode = effect.Parameters["SimpleColorMode"];
                HighlightColor = effect.Parameters["HighlightColor"];
                ShadowTint = effect.Parameters["ShadowTint"];
                ShadowTexture = effect.Parameters["ShadowTexture"];
                LightViewProjection = effect.Parameters["LightViewProjection"];
                ShadowMapTexelSize = effect.Parameters["ShadowMapTexelSize"];
                ShadowBias = effect.Parameters["ShadowBias"];
                ShadowNormalBias = effect.Parameters["ShadowNormalBias"];
                UseProceduralTerrainUv = effect.Parameters["UseProceduralTerrainUV"];
                IsWaterTexture = effect.Parameters["IsWaterTexture"];
                TextureCoordinateOffset = effect.Parameters["TextureCoordinateOffset"];
                MaterialTint = effect.Parameters["MaterialTint"];
            }

            public EffectParameter BoneMatrices { get; }
            public EffectParameter CrowdBonePaletteTexture { get; }
            public EffectParameter CrowdBonePaletteRowCount { get; }
            public EffectParameter World { get; }
            public EffectParameter WorldViewProjection { get; }
            public EffectParameter ViewProjection { get; }
            public EffectParameter EyePosition { get; }
            public EffectParameter SunDirection { get; }
            public EffectParameter LightDirection { get; }
            public EffectParameter SunColor { get; }
            public EffectParameter SunStrength { get; }
            public EffectParameter ShadowStrength { get; }
            public EffectParameter Alpha { get; }
            public EffectParameter TerrainDynamicIntensityScale { get; }
            public EffectParameter AmbientLight { get; }
            public EffectParameter DebugLightingAreas { get; }
            public EffectParameter DiffuseTexture { get; }
            public EffectParameter Chrome02Texture { get; }
            public EffectParameter Shiny01Texture { get; }
            public EffectParameter Chrome01Texture { get; }
            public EffectParameter ItemOptions { get; }
            public EffectParameter ItemMaterialGroup { get; }
            public EffectParameter ItemMaterialIndex { get; }
            public EffectParameter HighLevelTexturesAvailable { get; }
            public EffectParameter Time { get; }
            public EffectParameter IsAncient { get; }
            public EffectParameter IsExcellent { get; }
            public EffectParameter GlowColor { get; }
            public EffectParameter GlowIntensity { get; }
            public EffectParameter EnableGlow { get; }
            public EffectParameter SimpleColorMode { get; }
            public EffectParameter HighlightColor { get; }
            public EffectParameter ShadowTint { get; }
            public EffectParameter ShadowTexture { get; }
            public EffectParameter LightViewProjection { get; }
            public EffectParameter ShadowMapTexelSize { get; }
            public EffectParameter ShadowBias { get; }
            public EffectParameter ShadowNormalBias { get; }
            public EffectParameter UseProceduralTerrainUv { get; }
            public EffectParameter IsWaterTexture { get; }
            public EffectParameter TextureCoordinateOffset { get; }
            public EffectParameter MaterialTint { get; }

            // Tracks the palette currently resident in this shared Effect instance.
            // Rendering another object invalidates the owner, while consecutive meshes of
            // the same object and pose can reuse the existing 256-matrix constant upload.
            public ModelObject BonePaletteOwner;
            public Matrix[] BonePaletteSource;
            public uint BonePalettePoseVersion = uint.MaxValue;
            public int BonePaletteCount;

            public EffectTechnique GetTechnique(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return null;

                if (_techniques.TryGetValue(name, out EffectTechnique cached))
                    return cached;

                EffectTechnique resolved = null;
                var techniques = _effect.Techniques;
                for (int i = 0; i < techniques.Count; i++)
                {
                    EffectTechnique technique = techniques[i];
                    if (string.Equals(technique.Name, name, StringComparison.Ordinal))
                    {
                        resolved = technique;
                        break;
                    }
                }

                _techniques[name] = resolved;
                return resolved;
            }
        }

        private static readonly ConditionalWeakTable<Effect, ModelEffectBindings> _modelEffectBindings = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ModelEffectBindings GetModelEffectBindings(Effect effect) =>
            effect == null ? null : _modelEffectBindings.GetValue(effect, static value => new ModelEffectBindings(value));

        private static EffectTechnique TryGetTechnique(Effect effect, string name) =>
            GetModelEffectBindings(effect)?.GetTechnique(name);

        private bool TryUploadGpuSkinBoneMatrices(Effect effect, int requiredBoneCount)
        {
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            return bindings != null && TryUploadGpuSkinBoneMatrices(effect, bindings, requiredBoneCount);
        }

        private bool TryUploadGpuSkinBoneMatrices(Effect effect, ModelEffectBindings bindings, int requiredBoneCount)
        {
            if (effect == null || bindings?.BoneMatrices == null ||
                requiredBoneCount <= 0 || requiredBoneCount > MaxGpuSkinBones)
            {
                return false;
            }

            ModelObject paletteOwner = LinkParentAnimation && Parent is ModelObject parentModel
                ? parentModel
                : this;
            Matrix[] bones = paletteOwner.GetEffectiveBoneTransforms();
            bones = GetRenderBoneTransforms(bones) ?? bones;
            uint poseVersion = paletteOwner.GetEffectiveBonePoseVersion();

            if (ReferenceEquals(bindings.BonePaletteSource, bones) &&
                bindings.BonePalettePoseVersion == poseVersion &&
                bindings.BonePaletteCount >= requiredBoneCount)
            {
                return true;
            }

            if (_gpuSkinBoneUploadBuffer == null || _gpuSkinBoneUploadBuffer.Length != MaxGpuSkinBones)
            {
                _gpuSkinBoneUploadBuffer = new Matrix[MaxGpuSkinBones];
                for (int i = 0; i < MaxGpuSkinBones; i++)
                    _gpuSkinBoneUploadBuffer[i] = Matrix.Identity;
                _gpuSkinBoneUploadCount = 0;
                _gpuSkinPreparedBoneSource = null;
                _gpuSkinPreparedPoseVersion = uint.MaxValue;
            }

            bool paletteChanged = !ReferenceEquals(_gpuSkinPreparedBoneSource, bones) ||
                                  _gpuSkinPreparedPoseVersion != poseVersion;
            if (paletteChanged)
            {
                if (bones != null && bones.Length > 0)
                {
                    int copyCount = Math.Min(MaxGpuSkinBones, bones.Length);
                    Array.Copy(bones, 0, _gpuSkinBoneUploadBuffer, 0, copyCount);

                    int clearTo = Math.Max(copyCount, _gpuSkinBoneUploadCount);
                    for (int i = copyCount; i < clearTo && i < MaxGpuSkinBones; i++)
                        _gpuSkinBoneUploadBuffer[i] = Matrix.Identity;

                    _gpuSkinBoneUploadCount = copyCount;
                }
                else if (_gpuSkinBoneUploadCount == 0)
                {
                    _gpuSkinBoneUploadCount = requiredBoneCount;
                }

                _gpuSkinPreparedBoneSource = bones;
                _gpuSkinPreparedPoseVersion = poseVersion;
            }

            bindings.BoneMatrices.SetValue(_gpuSkinBoneUploadBuffer);
            bindings.BonePaletteOwner = paletteOwner;
            bindings.BonePaletteSource = bones;
            bindings.BonePalettePoseVersion = poseVersion;
            bindings.BonePaletteCount = Math.Max(requiredBoneCount, _gpuSkinBoneUploadCount);
            return true;
        }

        private void PrepareDynamicLightingEffect(Effect effect, bool useGpuSkinning = false, int requiredBoneCount = 0)
        {
            if (effect == null)
                return;

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            Vector3 worldTranslation = WorldPosition.Translation;
            bool useVertexLighting = ShouldUseVertexDynamicLighting(worldTranslation);

            EffectTechnique pixelTechnique = bindings.GetTechnique("DynamicLighting");
            EffectTechnique vertexTechnique = useVertexLighting
                ? bindings.GetTechnique("DynamicLighting_VertexLit")
                : null;
            EffectTechnique dynamicLightingTechnique = vertexTechnique ?? pixelTechnique;
            if (dynamicLightingTechnique == null)
                return;

            EffectTechnique skinnedTechnique = null;
            if (useGpuSkinning)
            {
                skinnedTechnique = useVertexLighting
                    ? bindings.GetTechnique("DynamicLighting_Skinned_VertexLit")
                    : null;
                skinnedTechnique ??= bindings.GetTechnique("DynamicLighting_Skinned");
            }

            bool usingSkinnedTechnique = skinnedTechnique != null &&
                                         TryUploadGpuSkinBoneMatrices(effect, bindings, requiredBoneCount);

            effect.CurrentTechnique = usingSkinnedTechnique ? skinnedTechnique : dynamicLightingTechnique;
            EffectTechnique sunOnlyTechnique = bindings.GetTechnique("DynamicLighting_SunOnly");
            EffectTechnique skinnedSunOnlyTechnique = usingSkinnedTechnique
                ? bindings.GetTechnique("DynamicLighting_Skinned_SunOnly")
                : null;
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

            var camera = Camera.Instance;
            if (camera == null)
                return;

            bindings.World?.SetValue(WorldPosition);
            bindings.WorldViewProjection?.SetValue(WorldPosition * camera.ViewProjection);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);

            bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
            bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();

            bindings.SunDirection?.SetValue(sunDir);
            bindings.SunColor?.SetValue(_sunColor);
            bindings.SunStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveSunStrength() : 0f);
            bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

            bindings.Alpha?.SetValue(TotalAlpha);
            // DynamicLightingEffect is shared across every object and terrain pass.
            // Keep its default neutral; the selected mesh applies its offset immediately
            // before drawing and resets it afterward.
            bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);
            bindings.TerrainDynamicIntensityScale?.SetValue(1.5f);
            bindings.AmbientLight?.SetValue(_ambientLightVector * SunCycleManager.AmbientMultiplier);
            bindings.DebugLightingAreas?.SetValue(Constants.DEBUG_LIGHTING_AREAS ? 1.0f : 0.0f);

            if (!Constants.ENABLE_DYNAMIC_LIGHTS)
            {
                _dynamicLightUploader.Clear(effect);
                SelectSunOnlyTechnique(
                    effect,
                    usingSkinnedTechnique,
                    sunOnlyTechnique,
                    skinnedSunOnlyTechnique);
                return;
            }

            var terrain = World?.Terrain;
            var visibleLights = terrain?.VisibleLights;
            if (visibleLights == null || visibleLights.Count == 0)
            {
                _dynamicLightUploader.Clear(effect);
                SelectSunOnlyTechnique(
                    effect,
                    usingSkinnedTechnique,
                    sunOnlyTechnique,
                    skinnedSunOnlyTechnique);
                return;
            }

            int maxLights = ResolveDynamicObjectLightBudget(worldTranslation);
            var focus = new Vector2(worldTranslation.X, worldTranslation.Y);
            float focusRadius = ResolveDynamicObjectLightFocusRadius();
            int uploadedLightCount = _dynamicLightUploader.Upload(
                effect,
                visibleLights,
                focus,
                maxLights,
                focusRadius,
                terrain.VisibleLightsVersion,
                cacheCellSize: 96f);
            if (uploadedLightCount == 0)
            {
                SelectSunOnlyTechnique(
                    effect,
                    usingSkinnedTechnique,
                    sunOnlyTechnique,
                    skinnedSunOnlyTechnique);
            }
        }

        private static void SelectSunOnlyTechnique(
            Effect effect,
            bool usingSkinnedTechnique,
            EffectTechnique sunOnlyTechnique,
            EffectTechnique skinnedSunOnlyTechnique)
        {
            EffectTechnique selected = usingSkinnedTechnique
                ? skinnedSunOnlyTechnique
                : sunOnlyTechnique;
            if (selected != null)
                effect.CurrentTechnique = selected;
        }

        private bool ShouldUseVertexDynamicLighting(Vector3 worldTranslation)
        {
            if (!Constants.ENABLE_DISTANCE_VERTEX_LIGHTING)
                return false;

            // Keep the local player and directly interacted actors on per-pixel lighting.
            if ((this is WalkerObject walker && walker.IsMainWalker) || IsMouseHover)
                return false;

            if (LowQuality)
                return true;

            Camera camera = Camera.Instance;
            if (camera == null)
                return false;

            float threshold = MathF.Max(256f, Constants.DYNAMIC_LIGHT_VERTEX_DISTANCE);
            float dx = camera.Position.X - worldTranslation.X;
            float dy = camera.Position.Y - worldTranslation.Y;
            return dx * dx + dy * dy >= threshold * threshold;
        }

        private int ResolveDynamicObjectLightBudget(Vector3 worldTranslation)
        {
            bool isMonster = this is MonsterObject;
            int maxLights = isMonster
                ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 4)
                : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 4 : 12);

            if (LowQuality)
            {
                maxLights = Math.Min(maxLights, isMonster
                    ? 1
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 6));
            }

            var camera = Camera.Instance;
            if (camera == null)
                return Math.Max(1, maxLights);

            var camPos = camera.Position;
            float dx = camPos.X - worldTranslation.X;
            float dy = camPos.Y - worldTranslation.Y;
            float distSq = dx * dx + dy * dy;

            const float nearSq = 1500f * 1500f;
            const float midSq = 3200f * 3200f;
            const float farSq = 5200f * 5200f;

            if (distSq > farSq)
                maxLights = Math.Min(maxLights, Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 1 : 3);
            else if (distSq > midSq)
                maxLights = Math.Min(maxLights, isMonster
                    ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 1 : 2)
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 4));
            else if (distSq > nearSq)
                maxLights = Math.Min(maxLights, isMonster
                    ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 3)
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 3 : 6));

            return Math.Max(1, maxLights);
        }

        private float ResolveDynamicObjectLightFocusRadius()
        {
            var bounds = BoundingBoxWorld;
            Vector3 extent = (bounds.Max - bounds.Min) * 0.5f;
            float radius = MathF.Sqrt(extent.X * extent.X + extent.Y * extent.Y);

            if (!float.IsFinite(radius) || radius < 32f)
                return 32f;

            return radius;
        }
    }
}
