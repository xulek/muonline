using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Objects.Wings;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        // Local animation optimization - per object only
        private struct LocalAnimationState : IEquatable<LocalAnimationState>
        {
            public int ActionIndex;
            public int Frame0;
            public int Frame1;
            public float InterpolationFactor;

            public bool Equals(LocalAnimationState other)
            {
                return ActionIndex == other.ActionIndex &&
                       Frame0 == other.Frame0 &&
                       Frame1 == other.Frame1 &&
                       MathF.Abs(InterpolationFactor - other.InterpolationFactor) < 0.001f;
            }

            public override bool Equals(object obj) => obj is LocalAnimationState other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(ActionIndex, Frame0, Frame1, InterpolationFactor);
        }

        private readonly struct SharedAnimationPaletteKey : IEquatable<SharedAnimationPaletteKey>
        {
            public SharedAnimationPaletteKey(BMD model, int actionIndex, int frame0, int frame1, int interpolationBucket, int bodyHeightBucket)
            {
                ModelId = RuntimeHelpers.GetHashCode(model);
                ActionIndex = actionIndex;
                Frame0 = frame0;
                Frame1 = frame1;
                InterpolationBucket = interpolationBucket;
                BodyHeightBucket = bodyHeightBucket;
            }

            private int ModelId { get; }
            private int ActionIndex { get; }
            private int Frame0 { get; }
            private int Frame1 { get; }
            private int InterpolationBucket { get; }
            private int BodyHeightBucket { get; }

            public bool Equals(SharedAnimationPaletteKey other) =>
                ModelId == other.ModelId &&
                ActionIndex == other.ActionIndex &&
                Frame0 == other.Frame0 &&
                Frame1 == other.Frame1 &&
                InterpolationBucket == other.InterpolationBucket &&
                BodyHeightBucket == other.BodyHeightBucket;

            public override bool Equals(object obj) => obj is SharedAnimationPaletteKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(ModelId, ActionIndex, Frame0, Frame1, InterpolationBucket, BodyHeightBucket);
        }

        private sealed class SharedAnimationPaletteEntry
        {
            public Matrix[] Bones;
            public int LastFrame;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix[] GetEffectiveBoneTransforms()
        {
            if (LinkParentAnimation && Parent is ModelObject parentModel)
                return parentModel.GetEffectiveBoneTransforms();

            return _sharedAnimationRenderBones ?? BoneTransform;
        }

        private void EnsureWritableBoneTransforms(int boneCount)
        {
            if (boneCount <= 0)
                return;

            if (BoneTransform == null || BoneTransform.Length != boneCount)
                BoneTransform = new Matrix[boneCount];

            if (_sharedAnimationRenderBones != null)
            {
                int copyCount = Math.Min(boneCount, _sharedAnimationRenderBones.Length);
                Array.Copy(_sharedAnimationRenderBones, BoneTransform, copyCount);
                for (int i = copyCount; i < boneCount; i++)
                    BoneTransform[i] = Matrix.Identity;
                _sharedAnimationRenderBones = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetEffectiveBonePoseVersion()
        {
            // Shared arrays are immutable and their reference identifies the exact pose.
            // Local arrays are mutated in-place, therefore they still require the version.
            return _sharedAnimationRenderBones != null ? uint.MaxValue : _animationPoseVersion;
        }

        /// <summary>
        /// Keeps the currently generated bone palette unchanged. This is used by cinematic
        /// preview actors which must remain stable while their scene is being constructed and
        /// while UI selection changes are applied. Linked children continue to consume the
        /// parent's frozen palette.
        /// </summary>
        public bool FreezeAnimationPose { get; set; }

        internal void SetStaticAnimationPose(int actionIndex, int frameIndex = 0)
        {
            if (Model?.Actions == null || Model.Actions.Length == 0 || Model.Bones == null)
                return;

            int resolvedAction = Math.Clamp(actionIndex, 0, Model.Actions.Length - 1);
            var action = Model.Actions[resolvedAction];
            if (action == null)
                return;

            int totalFrames = Math.Max(
                action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys,
                1);
            int resolvedFrame = Math.Clamp(frameIndex, 0, totalFrames - 1);
            int nextFrame = totalFrames > 1 ? (resolvedFrame + 1) % totalFrames : resolvedFrame;

            CurrentAction = resolvedAction;
            CurrentFrame = resolvedFrame;
            _animTime = resolvedFrame;
            _priorActionIndex = resolvedAction;
            _blendFromAction = -1;
            _blendFromTime = 0d;
            _blendElapsed = 0f;
            _isBlending = false;
            _lastAnimationTotalSeconds = 0d;
            _animationStepAccumulatorSeconds = 0f;

            GenerateBoneMatrix(resolvedAction, resolvedFrame, nextFrame, 0f);
            UpdateBoundings();
        }

        private const int MaxSharedAnimationPaletteEntries = 512;
        private const int SharedAnimationPaletteMaxIdleFrames = 180;
        private static readonly Dictionary<SharedAnimationPaletteKey, SharedAnimationPaletteEntry> _sharedAnimationPalettes = new(256);

        private void Animation(GameTime gameTime)
        {
            if (LinkParentAnimation || Model?.Actions == null || Model.Actions.Length == 0) return;

            int currentActionIndex = Math.Clamp(CurrentAction, 0, Model.Actions.Length - 1);
            var action = Model.Actions[currentActionIndex];
            if (action == null) return; // Skip animation if action is null

            int totalFrames = Math.Max(action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys, 1);
            float frameDelta;
            double totalSeconds = gameTime.TotalGameTime.TotalSeconds;
            if (_lastAnimationTotalSeconds > 0)
                frameDelta = (float)(totalSeconds - _lastAnimationTotalSeconds);
            else
                frameDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _lastAnimationTotalSeconds = totalSeconds;
            bool actionChanged = _priorActionIndex != currentActionIndex;

            // Detect death action for walkers to clamp on second-to-last key
            bool isDeathAction = false;
            if (this is WalkerObject)
            {
                if (this is PlayerObject)
                {
                    var pa = (PlayerAction)currentActionIndex;
                    isDeathAction = pa == PlayerAction.PlayerDie1 || pa == PlayerAction.PlayerDie2;
                }
                else if (this is MonsterObject)
                {
                    isDeathAction = currentActionIndex == (int)Client.Main.Models.MonsterActionType.Die;
                }
                else if (this is NPCObject)
                {
                    var pa = (PlayerAction)currentActionIndex;
                    isDeathAction = pa == PlayerAction.PlayerDie1 || pa == PlayerAction.PlayerDie2;
                }
            }

            if (totalFrames == 1 && !ContinuousAnimation)
            {
                if (actionChanged)
                {
                    GenerateBoneMatrix(currentActionIndex, 0, 0, 0);
                    _priorActionIndex = currentActionIndex;
                }
                CurrentFrame = 0;
                return;
            }

            if (actionChanged)
            {
                _blendFromAction = _priorActionIndex;
                _blendFromTime = _animTime;
                _blendElapsed = 0f;
                _isBlending = true;
                _animTime = 0.0;

                // _blendFromBones is pre-allocated in LoadContent - no need to allocate here
            }

            if (!TryConsumeAnimationDelta(frameDelta, actionChanged || AnimateEveryFrame, out float delta))
            {
                _priorActionIndex = currentActionIndex;
                return;
            }

            _animTime += delta * (action.PlaySpeed == 0 ? 1.0f : action.PlaySpeed) * AnimationSpeed;
            double framePos;

            if (isDeathAction || HoldOnLastFrame)
            {
                int endIdx = Math.Max(0, totalFrames - 2);
                _animTime = Math.Min(_animTime, endIdx + 0.0001f);
                framePos = _animTime;
            }
            else if (this is WalkerObject walker && walker.IsOneShotPlaying)
            {
                int endIdx = Math.Max(0, totalFrames - 1);
                if (_animTime >= endIdx)
                {
                    _animTime = endIdx;
                    framePos = _animTime;
                    walker.NotifyOneShotAnimationCompleted();
                }
                else
                {
                    framePos = _animTime;
                }
            }
            else
            {
                framePos = _animTime % totalFrames;
            }

            int f0 = (int)framePos;
            int f1 = (f0 + 1) % totalFrames;
            float t = (float)(framePos - f0);
            CurrentFrame = f0;

            GenerateBoneMatrix(currentActionIndex, f0, f1, t);

            if (_isBlending)
            {
                _blendElapsed += delta;
                float blendFactor = MathHelper.Clamp(_blendElapsed / _blendDuration, 0f, 1f);

                if (_blendFromAction >= 0 && _blendFromBones != null)
                {
                    var prevAction = Model.Actions[_blendFromAction];
                    _blendFromTime += delta * (prevAction.PlaySpeed == 0 ? 1.0f : prevAction.PlaySpeed) * AnimationSpeed;
                    int prevTotal = Math.Max(prevAction.NumAnimationKeys, 1);
                    double pf = _blendFromTime % prevTotal;
                    int pf0 = (int)pf;
                    int pf1 = (pf0 + 1) % prevTotal;
                    float pt = (float)(pf - pf0);
                    ComputeBoneMatrixTo(_blendFromAction, pf0, pf1, pt, _blendFromBones);

                    // blending
                    for (int i = 0; i < BoneTransform.Length; i++)
                    {
                        Matrix.Lerp(ref _blendFromBones[i], ref BoneTransform[i], blendFactor, out BoneTransform[i]);
                    }
                }

                if (blendFactor >= 1.0f)
                {
                    _isBlending = false;
                    _blendFromAction = -1;
                }

                // Cross-fade mutates the local palette after GenerateBoneMatrix().
                // Advance the pose version so multi-pose instancing and effect palette
                // caches never reuse an earlier blend step for the current object.
                unchecked { _animationPoseVersion++; }
                InvalidateBuffers(MeshDirtyFlags.Animation);
            }

            _priorActionIndex = currentActionIndex;
        }

        private bool TryConsumeAnimationDelta(float frameDelta, bool forceStep, out float animationDelta)
        {
            animationDelta = 0f;

            if (!float.IsFinite(frameDelta) || frameDelta <= 0f)
                return false;

            if (forceStep)
            {
                _animationStepAccumulatorSeconds = 0f;
                animationDelta = frameDelta;
                return true;
            }

            int animationUpdateFps = Constants.ClampPerformanceFps(Constants.ANIMATION_UPDATE_FPS);
            float animationStepInterval = 1f / animationUpdateFps;

            _animationStepAccumulatorSeconds = MathF.Min(_animationStepAccumulatorSeconds + frameDelta, 0.5f);
            int availableSteps = (int)(_animationStepAccumulatorSeconds / animationStepInterval);
            if (availableSteps <= 0)
                return false;

            animationDelta = availableSteps * animationStepInterval;
            _animationStepAccumulatorSeconds -= animationDelta;
            return animationDelta > 0f;
        }

        protected void GenerateBoneMatrix(int actionIdx, int frame0, int frame1, float t)
        {
            var bones = Model?.Bones;

            if (bones == null || bones.Length == 0)
            {
                // Reset animation cache for invalid models
                _animationStateValid = false;
                _animationSampleValid = false;
                return;
            }

            // Armor items use the player's idle pose so they match equipped visuals
            if (TryApplyPlayerIdlePose(bones))
            {
                _animationStateValid = true;
                _lastAnimationState = default;
                _animationSampleActionIndex = actionIdx;
                _animationSampleFrame0 = frame0;
                _animationSampleFrame1 = frame1;
                _animationSampleInterpolationBucket = QuantizeAnimationInterpolation(t);
                _animationSampleValid = true;
                return;
            }

            if (Model.Actions == null || Model.Actions.Length == 0)
            {
                _animationStateValid = false;
                _animationSampleValid = false;
                return;
            }

            actionIdx = Math.Clamp(actionIdx, 0, Model.Actions.Length - 1);
            var action = Model.Actions[actionIdx];
            _animationSampleActionIndex = actionIdx;
            _animationSampleFrame0 = frame0;
            _animationSampleFrame1 = frame1;
            _animationSampleInterpolationBucket = QuantizeAnimationInterpolation(t);
            _animationSampleValid = true;

            // Create animation state for comparison - only for animated objects
            LocalAnimationState currentAnimState = default;
            bool shouldCheckCache = !RequiresPerFrameAnimation &&
                                   !LinkParentAnimation &&
                                   ParentBoneLink < 0 &&
                                   action.NumAnimationKeys > 1; // Only cache non-critical animated objects
            bool canUseSharedPalette = CanUseSharedAnimationPalette(actionIdx);
            SharedAnimationPaletteKey sharedPaletteKey = default;

            if (shouldCheckCache)
            {
                currentAnimState = new LocalAnimationState
                {
                    ActionIndex = actionIdx,
                    Frame0 = frame0,
                    Frame1 = frame1,
                    InterpolationFactor = t
                };

                // Check if we can skip expensive calculation using local cache
                // But be more conservative - only skip if frames and interpolation are identical
                Matrix[] activeBones = GetEffectiveBoneTransforms();
                if (_animationStateValid && currentAnimState.Equals(_lastAnimationState) &&
                    activeBones != null && activeBones.Length == bones.Length)
                {
                    // Animation state hasn't changed - no need to recalculate
                    return;
                }
            }

            if (canUseSharedPalette)
            {
                sharedPaletteKey = new SharedAnimationPaletteKey(
                    Model,
                    actionIdx,
                    frame0,
                    frame1,
                    _animationSampleInterpolationBucket,
                    (int)MathF.Round(BodyHeight));

                if (TryApplySharedAnimationPalette(sharedPaletteKey, bones.Length, shouldCheckCache, currentAnimState))
                    return;

                RegisterSharedAnimationPaletteMiss();
            }

            // A shared palette is read-only. Detach only when this object really needs
            // a unique pose (cache miss, blend, procedural post-process, etc.).
            EnsureWritableBoneTransforms(bones.Length);

            bool lockPositions = action.LockPositions;
            float bodyHeight = BodyHeight;
            bool anyBoneChanged = false;

            // Pre-clamp frame indices to valid ranges
            int maxFrameIndex = action.NumAnimationKeys - 1;
            frame0 = Math.Clamp(frame0, 0, maxFrameIndex);
            frame1 = Math.Clamp(frame1, 0, maxFrameIndex);

            // If frames are the same, no interpolation is needed.
            if (frame0 == frame1)
                t = 0f;

            // BoneTransform is writable at this point and BMD bones are ordered parent-first.
            // Writing the new pose directly removes one ArrayPool rent/return and one full
            // Matrix[] copy for every sampled animation pose while preserving the exact
            // parent-to-child calculation order.
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                Matrix previousTransform = BoneTransform[i];
                Matrix worldTransform;

                if (bone == BMDTextureBone.Dummy || bone.Matrixes == null || actionIdx >= bone.Matrixes.Length)
                {
                    worldTransform = Matrix.Identity;
                }
                else
                {
                    var bm = bone.Matrixes[actionIdx];
                    int numPosKeys = bm.Position?.Length ?? 0;
                    int numQuatKeys = bm.Quaternion?.Length ?? 0;

                    if (numPosKeys == 0 || numQuatKeys == 0)
                    {
                        worldTransform = Matrix.Identity;
                    }
                    else
                    {
                        int boneMaxFrame = Math.Min(numPosKeys, numQuatKeys) - 1;
                        int boneFrame0 = Math.Min(frame0, boneMaxFrame);
                        int boneFrame1 = Math.Min(frame1, boneMaxFrame);
                        float boneT = boneFrame0 == boneFrame1 ? 0f : t;

                        Matrix localTransform;
                        if (boneT == 0f)
                        {
                            localTransform = Matrix.CreateFromQuaternion(bm.Quaternion[boneFrame0]);
                            localTransform.Translation = bm.Position[boneFrame0];
                        }
                        else
                        {
                            Quaternion q = Nlerp(bm.Quaternion[boneFrame0], bm.Quaternion[boneFrame1], boneT);
                            Vector3 p0 = bm.Position[boneFrame0];
                            Vector3 p1 = bm.Position[boneFrame1];

                            localTransform = Matrix.CreateFromQuaternion(q);
                            localTransform.M41 = p0.X + (p1.X - p0.X) * boneT;
                            localTransform.M42 = p0.Y + (p1.Y - p0.Y) * boneT;
                            localTransform.M43 = p0.Z + (p1.Z - p0.Z) * boneT;
                        }

                        if (i == 0 && lockPositions && bm.Position.Length > 0)
                        {
                            var rootPos = bm.Position[0];
                            localTransform.Translation = new Vector3(
                                rootPos.X,
                                rootPos.Y,
                                localTransform.M43 + bodyHeight);
                        }

                        worldTransform = bone.Parent >= 0 && bone.Parent < bones.Length
                            ? localTransform * BoneTransform[bone.Parent]
                            : localTransform;
                    }
                }

                BoneTransform[i] = worldTransform;
                if (previousTransform != worldTransform)
                    anyBoneChanged = true;
            }

            bool forceUpdate = action.NumAnimationKeys <= 1 || !_animationStateValid;

            // Procedural adjustments operate on the same final palette used by rendering,
            // linked equipment and shadows.
            if (PostProcessBoneTransforms(bones, BoneTransform))
                anyBoneChanged = true;

            if (anyBoneChanged || forceUpdate)
            {
                unchecked { _animationPoseVersion++; }
                InvalidateBuffers(MeshDirtyFlags.Animation);
            }

            if (canUseSharedPalette)
                StoreSharedAnimationPalette(sharedPaletteKey, BoneTransform, bones.Length);

            if (shouldCheckCache)
            {
                _lastAnimationState = currentAnimState;
                _animationStateValid = true;
            }
            else if (action.NumAnimationKeys <= 1)
            {
                _animationStateValid = true;
            }
        }

        private bool CanUseSharedAnimationPalette(int actionIdx)
        {
            if (!Constants.ENABLE_SHARED_ANIMATION_PALETTES ||
                this is not MonsterObject ||
                Model == null ||
                _isBlending ||
                LinkParentAnimation ||
                ParentBoneLink >= 0 ||
                ContinuousAnimation ||
                ItemDefinition != null)
            {
                return false;
            }

            if (actionIdx == (int)Client.Main.Models.MonsterActionType.Die)
                return false;

            // Attack and skill one-shots may share an identical quantized pose. Death is
            // rejected above; other special one-shots stay per-instance.
            if (this is WalkerObject walker &&
                walker.IsOneShotPlaying &&
                !walker.IsAttackOrSkillAnimationPlaying())
            {
                return false;
            }

            return true;
        }

        private bool TryApplySharedAnimationPalette(
            SharedAnimationPaletteKey key,
            int boneCount,
            bool shouldCheckCache,
            LocalAnimationState currentAnimState)
        {
            if (!_sharedAnimationPalettes.TryGetValue(key, out var entry) ||
                entry.Bones == null ||
                entry.Bones.Length != boneCount)
            {
                return false;
            }

            bool changed = !ReferenceEquals(_sharedAnimationRenderBones, entry.Bones);
            _sharedAnimationRenderBones = entry.Bones;
            entry.LastFrame = MuGame.FrameIndex;

            if (changed)
            {
                unchecked { _animationPoseVersion++; }
                InvalidateBuffers(MeshDirtyFlags.Animation);
            }

            if (shouldCheckCache)
            {
                _lastAnimationState = currentAnimState;
                _animationStateValid = true;
            }

            RegisterSharedAnimationPaletteHit();
            return true;
        }

        private static void StoreSharedAnimationPalette(SharedAnimationPaletteKey key, Matrix[] bones, int boneCount)
        {
            if (bones == null || boneCount <= 0)
                return;

            // Published pose arrays are immutable because active monsters may hold a
            // direct reference to them. Never overwrite an existing published array.
            if (_sharedAnimationPalettes.TryGetValue(key, out var existing) &&
                existing.Bones != null &&
                existing.Bones.Length == boneCount)
            {
                existing.LastFrame = MuGame.FrameIndex;
                return;
            }

            var snapshot = new Matrix[boneCount];
            Array.Copy(bones, snapshot, boneCount);
            _sharedAnimationPalettes[key] = new SharedAnimationPaletteEntry
            {
                Bones = snapshot,
                LastFrame = MuGame.FrameIndex
            };
        }

        private static void PruneSharedAnimationPaletteCache(int frame)
        {
            if (_sharedAnimationPalettes.Count == 0 ||
                (frame % 120 != 0 && _sharedAnimationPalettes.Count <= MaxSharedAnimationPaletteEntries))
            {
                return;
            }

            var staleKeys = new List<SharedAnimationPaletteKey>(64);
            try
            {
                foreach (var pair in _sharedAnimationPalettes)
                {
                    if (_sharedAnimationPalettes.Count - staleKeys.Count <= MaxSharedAnimationPaletteEntries &&
                        frame - pair.Value.LastFrame <= SharedAnimationPaletteMaxIdleFrames)
                    {
                        continue;
                    }

                    staleKeys.Add(pair.Key);
                }

                for (int i = 0; i < staleKeys.Count; i++)
                    _sharedAnimationPalettes.Remove(staleKeys[i]);
            }
            finally
            {
                staleKeys.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int QuantizeAnimationInterpolation(float t)
        {
            // Five bits are enough for visually smooth interpolation while allowing
            // substantially more monsters to share a pose/instancing batch.
            const float BucketCountMinusOne = 31f;
            return (int)MathHelper.Clamp(MathF.Round(t * BucketCountMinusOne), 0f, BucketCountMinusOne);
        }

        /// <summary>
        /// Allows derived objects to procedurally adjust the computed bone transforms (in-place).
        /// Return true if any bone was modified.
        /// </summary>
        protected virtual bool PostProcessBoneTransforms(BMDTextureBone[] bones, Matrix[] boneTransforms)
        {
            return false;
        }

        private static Quaternion Nlerp(in Quaternion q1, in Quaternion q2, float t)
        {
            var target = q2;
            if (Quaternion.Dot(q1, q2) < 0f)
            {
                target.X = -target.X;
                target.Y = -target.Y;
                target.Z = -target.Z;
                target.W = -target.W;
            }

            var blended = new Quaternion(
                q1.X + (target.X - q1.X) * t,
                q1.Y + (target.Y - q1.Y) * t,
                q1.Z + (target.Z - q1.Z) * t,
                q1.W + (target.W - q1.W) * t);

            return Quaternion.Normalize(blended);
        }

        private void ComputeBoneMatrixTo(int actionIdx, int frame0, int frame1, float t, Matrix[] output)
        {
            if (Model?.Bones == null || output == null)
                return;

            var bones = Model.Bones;
            if (actionIdx < 0 || actionIdx >= Model.Actions.Length)
                actionIdx = 0;

            var action = Model.Actions[actionIdx];

            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];

                if (bone == BMDTextureBone.Dummy || bone.Matrixes == null || actionIdx >= bone.Matrixes.Length)
                    continue;

                var bm = bone.Matrixes[actionIdx];

                int numPosKeys = bm.Position?.Length ?? 0;
                int numQuatKeys = bm.Quaternion?.Length ?? 0;
                if (numPosKeys == 0 || numQuatKeys == 0)
                    continue;

                if (frame0 < 0 || frame1 < 0 || frame0 >= numPosKeys || frame1 >= numPosKeys || frame0 >= numQuatKeys || frame1 >= numQuatKeys)
                {
                    int maxValidIndex = Math.Min(numPosKeys, numQuatKeys) - 1;
                    if (maxValidIndex < 0) maxValidIndex = 0;
                    frame0 = Math.Clamp(frame0, 0, maxValidIndex);
                    frame1 = Math.Clamp(frame1, 0, maxValidIndex);
                    if (frame0 == frame1) t = 0f;
                }

                Quaternion q = Nlerp(bm.Quaternion[frame0], bm.Quaternion[frame1], t);
                Matrix m = Matrix.CreateFromQuaternion(q);

                Vector3 p0 = bm.Position[frame0];
                Vector3 p1 = bm.Position[frame1];

                m.M41 = p0.X + (p1.X - p0.X) * t;
                m.M42 = p0.Y + (p1.Y - p0.Y) * t;
                m.M43 = p0.Z + (p1.Z - p0.Z) * t;

                if (i == 0 && action.LockPositions)
                    m.Translation = new Vector3(bm.Position[0].X, bm.Position[0].Y, m.M43 + BodyHeight);

                Matrix world = bone.Parent != -1 && bone.Parent < output.Length
                    ? m * output[bone.Parent]
                    : m;

                output[i] = world;
            }
        }

        /// <summary>
        /// Allows derived objects to provide modified bone transforms for rendering.
        /// Default returns the input bones unchanged.
        /// Useful for lightweight procedural deformations (e.g., cape flutter).
        /// </summary>
        protected virtual Matrix[] GetRenderBoneTransforms(Matrix[] bones)
        {
            return bones;
        }

        private bool TryApplyPlayerIdlePose(BMDTextureBone[] bones)
        {
            var def = ItemDefinition;
            int group = def?.Group ?? -1;
            bool isArmor = group >= 7 && group <= 11;
            if (!isArmor)
                return false;

            var playerBones = PlayerIdlePoseProvider.GetIdleBoneMatrices();
            if (playerBones == null || playerBones.Length == 0)
                return false;

            EnsureWritableBoneTransforms(bones.Length);

            for (int i = 0; i < bones.Length; i++)
            {
                BoneTransform[i] = (i < playerBones.Length)
                    ? playerBones[i]
                    : BuildBoneFromBmd(bones[i], BoneTransform);
            }

            InvalidateBuffers(MeshDirtyFlags.Animation);
            return true;
        }

        private static Matrix BuildBoneFromBmd(BMDTextureBone bone, Matrix[] parentResults)
        {
            Matrix local = Matrix.Identity;

            if (bone?.Matrixes != null && bone.Matrixes.Length > 0)
            {
                var bm = bone.Matrixes[0];
                if (bm.Position?.Length > 0 && bm.Quaternion?.Length > 0)
                {
                    var q = bm.Quaternion[0];
                    local = Matrix.CreateFromQuaternion(new Quaternion(q.X, q.Y, q.Z, q.W));
                    var p = bm.Position[0];
                    local.Translation = new Vector3(p.X, p.Y, p.Z);
                }
            }

            if (bone != null && bone.Parent >= 0 && bone.Parent < parentResults.Length)
                return local * parentResults[bone.Parent];

            return local;
        }
    }
}
