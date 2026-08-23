using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;

namespace Client.Main.Objects.Vehicle;

public class VehicleObject : ModelObject
{
    // Default vehicle animation indices (some vehicles override via VehicleDefinition)
    public const int DefaultAnimationIdle = 0;
    public const int DefaultAnimationRun = 2;
    public const int DefaultAnimationSkill = 4;

    // Vehicle IDs that have root motion in their run animation and need position locking
    private static readonly HashSet<int> VehiclesWithRootMotion = new()
    {
        7,  // Rider 01 (Uniria/Dinorant)
        8,  // Rider 02
        27, // Pon Up Ride
        28, // Pon Ride
        22, // Griffs Up Ride
        23, // Griffs Ride
        30, // Rippen Up Ride
        31, // Rippen Ride
    };

    // Icarus internal world index (original WD_10HEAVEN) — flight map with its own gallop effect.
    private const int IcarusWorldIndex = 11;

    private short itemIndex = -1;
    public short ItemIndex
    {
        get => itemIndex;
        set
        {
            if (itemIndex == value) return;
            itemIndex = value;
            int changeVersion = Interlocked.Increment(ref _changeVersion);
            _ = OnChangeIndex(itemIndex, changeVersion);
        }
    }

    // Dark Horse (vehicle index 0) hoof-dust emitter — see DarkHorseHoofDustEffect.
    private readonly DarkHorseHoofDustEffect _hoofDust;

    // Icarus-specific Dark Horse gallop emitter (shock-wave rings + smoke) — see DarkHorseIcarusGallopEffect.
    private readonly DarkHorseIcarusGallopEffect _icarGallop;
    private bool _isMoving;

    /// <summary>The run animation index used by this vehicle.</summary>
    public int RunActionIndex => runActionIndex;

    /// <summary>
    /// The vertical offset to apply to the rider when mounted on this vehicle.
    /// Retrieved from VehicleDefinition when the vehicle is loaded.
    /// </summary>
    public float RiderHeightOffset { get; private set; } = 0f;

    /// <summary>
    /// The animation speed multiplier for this vehicle.
    /// Retrieved from VehicleDefinition when the vehicle is loaded.
    /// </summary>
    public float AnimationSpeedMultiplier { get; private set; } = 1.0f;
    private float idleAnimationSpeedMultiplier = 1.0f;
    private float runAnimationSpeedMultiplier = 1.0f;
    private float skillAnimationSpeedMultiplier = 1.0f;
    private int idleActionIndex = DefaultAnimationIdle;
    private int runActionIndex = DefaultAnimationRun;
    private int skillActionIndex = DefaultAnimationSkill;
    private Dictionary<int, float> actionPlaySpeedOverrides;
    private int _changeVersion;

    public VehicleObject()
    {
        RenderShadow = true;
        IsTransparent = true;
        AffectedByTransparency = true;
        BlendState = BlendState.AlphaBlend;
        BlendMesh = -1;
        BlendMeshState = BlendState.Additive;
        Alpha = 1f;
        LinkParentAnimation = false;
        AnimationSpeed = 25f;

        _hoofDust = new DarkHorseHoofDustEffect();
        Children.Add(_hoofDust);

        _icarGallop = new DarkHorseIcarusGallopEffect(this);
        Children.Add(_icarGallop);
    }

    private bool IsCurrentChangeVersion(int changeVersion)
    {
        return Volatile.Read(ref _changeVersion) == changeVersion;
    }

    private void UpdateStatusAfterAsyncResolve(bool modelResolved)
    {
        if (Status is not (GameControlStatus.Ready or GameControlStatus.Error))
            return;

        Status = modelResolved ? GameControlStatus.Ready : GameControlStatus.Error;
    }

    private void ResetVehicleConfiguration()
    {
        RiderHeightOffset = 0f;
        AnimationSpeedMultiplier = 1.0f;
        idleAnimationSpeedMultiplier = 1.0f;
        runAnimationSpeedMultiplier = 1.0f;
        skillAnimationSpeedMultiplier = 1.0f;
        idleActionIndex = DefaultAnimationIdle;
        runActionIndex = DefaultAnimationRun;
        skillActionIndex = DefaultAnimationSkill;
        actionPlaySpeedOverrides = null;
    }

    private async Task OnChangeIndex(short requestedItemIndex, int changeVersion)
    {
        if (!IsCurrentChangeVersion(changeVersion))
            return;

        if (requestedItemIndex < 0)
        {
            Model = null;
            ResetVehicleConfiguration();
            return;
        }

        VehicleDefinition riderDefinition = VehicleDatabase.GetVehicleDefinition(requestedItemIndex);
        if (riderDefinition == null)
        {
            Model = null;
            ResetVehicleConfiguration();
            UpdateStatusAfterAsyncResolve(false);
            return;
        }

        string modelPath = riderDefinition.TexturePath;
        var resolvedModel = await BMDLoader.Instance.Prepare(Path.Combine("Skill", modelPath));

        if (!IsCurrentChangeVersion(changeVersion))
            return;

        // Apply configuration from the winning request only.
        RiderHeightOffset = riderDefinition.RiderHeightOffset;
        AnimationSpeedMultiplier = riderDefinition.AnimationSpeedMultiplier;
        AnimationSpeed = riderDefinition.AnimationSpeed;
        idleAnimationSpeedMultiplier = riderDefinition.IdleAnimationSpeedMultiplier;
        runAnimationSpeedMultiplier = riderDefinition.RunAnimationSpeedMultiplier;
        skillAnimationSpeedMultiplier = riderDefinition.SkillAnimationSpeedMultiplier;
        idleActionIndex = riderDefinition.IdleActionIndex;
        runActionIndex = riderDefinition.RunActionIndex;
        skillActionIndex = riderDefinition.SkillActionIndex;
        actionPlaySpeedOverrides = riderDefinition.ActionPlaySpeedOverrides;

        Model = resolvedModel;
        if (resolvedModel == null)
        {
            UpdateStatusAfterAsyncResolve(false);
            return;
        }

        UpdateStatusAfterAsyncResolve(true);

        // Apply animation speed multiplier to all actions
        ApplyAnimationSpeedMultiplier();

        // For vehicles with root motion in their animations, lock positions to prevent drifting
        if (VehiclesWithRootMotion.Contains(requestedItemIndex))
        {
            ApplyPositionLockToAnimations();
        }
    }

    /// <summary>
    /// Applies the AnimationSpeedMultiplier to all animations for this vehicle.
    /// </summary>
    private void ApplyAnimationSpeedMultiplier()
    {
        if (Model?.Actions == null)
            return;

        if (actionPlaySpeedOverrides != null && actionPlaySpeedOverrides.Count > 0)
        {
            foreach (var kvp in actionPlaySpeedOverrides)
            {
                int index = kvp.Key;
                if (index < 0 || index >= Model.Actions.Length)
                {
                    continue;
                }

                var action = Model.Actions[index];
                if (action == null)
                {
                    continue;
                }

                action.PlaySpeed = kvp.Value;
            }
            return;
        }

        bool hasCustomMultiplier = AnimationSpeedMultiplier != 1.0f
            || idleAnimationSpeedMultiplier != 1.0f
            || runAnimationSpeedMultiplier != 1.0f
            || skillAnimationSpeedMultiplier != 1.0f;
        if (!hasCustomMultiplier)
            return;

        for (int i = 0; i < Model.Actions.Length; i++)
        {
            var action = Model.Actions[i];
            if (action == null)
            {
                continue;
            }

            float multiplier = AnimationSpeedMultiplier;
            if (i == idleActionIndex)
            {
                multiplier *= idleAnimationSpeedMultiplier;
            }
            else if (i == runActionIndex)
            {
                multiplier *= runAnimationSpeedMultiplier;
            }
            else if (i == skillActionIndex)
            {
                multiplier *= skillAnimationSpeedMultiplier;
            }

            if (multiplier != 1.0f)
            {
                action.PlaySpeed *= multiplier;
            }
        }
    }

    /// <summary>
    /// Forces LockPositions on all animations for vehicles that have root motion baked in.
    /// This prevents the vehicle from drifting ahead during run animations.
    /// </summary>
    private void ApplyPositionLockToAnimations()
    {
        if (Model?.Actions == null)
            return;

        foreach (var action in Model.Actions)
        {
            if (action != null && !action.LockPositions)
            {
                action.LockPositions = true;
            }
        }
    }

    public override async Task Load()
    {
        await base.Load();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        // Dark Horse (vehicle index 0): on Icarus (worldIndex 11, the original WD_10HEAVEN)
        // the gallop produces ground shock-wave rings + smoke instead of hoof dust
        // (GOBoid.cpp MODEL_DARK_HORSE / PLAYER_RUN_RIDE_HORSE). Toggled every frame so it
        // also stops when the mount is hidden/dismounted even if SetRiderAnimation is
        // no longer invoked.
        bool isVehicleRunning = !Hidden && Model != null && _isMoving;
        bool isDarkHorseRunning = isVehicleRunning && ItemIndex == 0;
        bool onIcarus = World?.WorldIndex == IcarusWorldIndex;

        if (_hoofDust != null)
            _hoofDust.Emitting = isVehicleRunning && !onIcarus;
        if (_icarGallop != null)
            _icarGallop.Emitting = isDarkHorseRunning && onIcarus;
    }

    /// <summary>
    /// Sets the vehicle animation based on rider state.
    /// </summary>
    public void SetRiderAnimation(bool isMoving, bool isUsingSkill = false)
    {
        _isMoving = isMoving;

        if (Model == null || Hidden)
            return;

        int targetAnim;
        bool isFlightMap = World != null && (World.WorldIndex == 8 || World.WorldIndex == 9 || World.WorldIndex == 10 || World.WorldIndex == 11);
        bool isDinorant = ItemIndex == 8;

        if (isUsingSkill)
        {
            targetAnim = (isDinorant && isFlightMap) ? 7 : skillActionIndex;
            // Skill animation should play once and hold on last frame
            HoldOnLastFrame = true;
        }
        else
        {
            if (isMoving)
                targetAnim = (isDinorant && isFlightMap) ? 3 : runActionIndex;
            else
                targetAnim = (isDinorant && isFlightMap) ? 1 : idleActionIndex;

            // Normal animations should loop
            HoldOnLastFrame = false;
        }

        if (CurrentAction != targetAnim)
        {
            CurrentAction = targetAnim;
            // Reset animation time when changing actions
            _animTime = 0.0;
        }
    }

    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        foreach (var child in Children)
        {
            child.Draw(gameTime);
        }
    }

    public override void DrawAfter(GameTime gameTime)
    {
        base.DrawAfter(gameTime);
        foreach (var child in Children)
        {
            child.DrawAfter(gameTime);
        }
    }
}
