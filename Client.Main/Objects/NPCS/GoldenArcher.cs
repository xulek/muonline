using Client.Main.Content;
using Client.Main.Extensions;
using Client.Main.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    /// <summary>
    /// Golden Archer (Lucky Coin / Rena exchange NPC). A talkable NPC that reuses the player skeleton
    /// with the bow idle pose and the golden monster material, like the original MODEL_PLAYER based NPC.
    /// </summary>
    [NpcInfo(236, "Golden Archer")]
    public class GoldenArcher : NPCObject
    {
        // The skeleton model only needs the idle pose: action 0 = player standing (male).
        private static readonly IReadOnlyDictionary<int, int> ActionMap = new Dictionary<int, int>
        {
            { 0, (int)PlayerAction.PlayerStopMale },
        };

        public GoldenArcher()
        {
            AnimationSpeed = 25f; // Uses Player.bmd animations, needs player animation speed
        }

        public override async Task Load()
        {
            var skeletonModel = await BMDLoader.Instance.Prepare($"Skill/Skeleton02.bmd");
            var playerModel = await BMDLoader.Instance.Prepare("Player/Player.bmd");

            if (skeletonModel != null && playerModel != null)
            {
                skeletonModel.Actions = MonsterObject.BuildActionArray(playerModel, ActionMap.Count, ActionMap);
                skeletonModel.Bones = MonsterObject.BuildBoneArray(playerModel, ActionMap.Count, ActionMap);
            }

            Model = skeletonModel;

            await base.Load();
            CurrentAction = 0;

            this.SetGoldGlow(0.8f);
        }

        protected override void HandleClick()
        {
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendTalkToNpcRequestAsync(NetworkId);
            }
        }
    }
}
