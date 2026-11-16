using System;
using System.Collections.Generic;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
public sealed partial class SourceItemTooltip
{
    /// <summary>Port of the CSItemOption tooltip parts (CSItemOption.cpp).</summary>
    private sealed class ItemSetOptionRenderer
    {
        private const int MAX_EQUIPMENT_INDEX = 12, EQUIPMENT_WEAPON_RIGHT = 0, EQUIPMENT_WEAPON_LEFT = 1, EQUIPMENT_WING = 7,
            EQUIPMENT_HELPER = 8, EQUIPMENT_RING_RIGHT = 10, EQUIPMENT_RING_LEFT = 11;

        private readonly SourceItemTooltip _t;
        private readonly ItemSetOptionData _data = ItemSetOptionData.Instance;
        private readonly List<SetSearchResult> m_SetSearchResult = new();

        public ItemSetOptionRenderer(SourceItemTooltip owner) => _t = owner;

        public bool GetSetItemName(ref string strName, int iType, int setType) => _data.GetSetItemName(ref strName, iType, setType);

        // CSItemOption::GetDefaultOptionText (CSItemOption.cpp:659)
        private bool GetDefaultOptionText(SourceItem ip, out string Text)
        {
            Text = string.Empty;
            if (ip.Type > MAX_ITEM || ip.AncientBonusOption <= 0) return false;
            switch (ip.Attribute.AttType)
            {
                case SET_OPTION_STRENGTH: Text = Fmt(GlobalText[950], ip.AncientBonusOption * 5); break;
                case SET_OPTION_DEXTERITY: Text = Fmt(GlobalText[951], ip.AncientBonusOption * 5); break;
                case SET_OPTION_ENERGY: Text = Fmt(GlobalText[952], ip.AncientBonusOption * 5); break;
                case SET_OPTION_VITALITY: Text = Fmt(GlobalText[953], ip.AncientBonusOption * 5); break;
                default: return false;
            }
            return true;
        }

        // CSItemOption::RenderDefaultOptionText (CSItemOption.cpp:726)
        public int RenderDefaultOptionText(SourceItem ip, int TextNum)
        {
            int TNum = TextNum;
            if (GetDefaultOptionText(ip, out var text))
            {
                _t.TextList[TNum] = text;
                _t.TextListColor[TNum] = TEXT_COLOR_BLUE;
                TNum++;
                if ((ip.Type >= ITEM_RING_OF_ICE && ip.Type <= ITEM_RING_OF_POISON) || (ip.Type >= ITEM_PENDANT_OF_LIGHTING && ip.Type <= ITEM_PENDANT_OF_FIRE) || (ip.Type >= ITEM_RING_OF_FIRE && ip.Type <= ITEM_PENDANT_OF_WATER))
                {
                    _t.swprintf(TNum, GlobalText[1165]);
                    _t.TextListColor[TNum] = TEXT_COLOR_BLUE;
                    TNum++;
                }
            }
            return TNum;
        }

        // IsRequireEquipItem (ZzzInfomation.cpp:2342)
        private bool IsRequireEquipItem(SourceItem pItem)
        {
            var hero = _t.Hero;
            var attr = pItem.Attribute;
            int firstClass = ItemTooltipHero.GetBaseClass(hero.Class);
            int stepClass = ItemTooltipHero.GetStepClass(hero.Class);
            bool bEquipable = false;
            if (attr.RequireClass[firstClass] != 0) bEquipable = true;
            else if (firstClass == CLASS_DARK && attr.RequireClass[CLASS_WIZARD] != 0 && attr.RequireClass[CLASS_KNIGHT] != 0) bEquipable = true;
            if (attr.RequireClass[firstClass] > stepClass) return false;
            if (!bEquipable) return false;

            int iDecNeedStrength = 0, iDecNeedDex = 0;
            if (pItem.Level >= pItem.Jewel_Of_Harmony_OptionLevel)
            {
                var SC = new StrengthenCapability();
                JewelHarmonyInfo.Instance.GetStrengthenCapability(SC, pItem, 0);
                if (SC.SI_isNB) { iDecNeedStrength = SC.SI_NB.SI_force; iDecNeedDex = SC.SI_NB.SI_activity; }
            }
            if (pItem.RequireStrength - iDecNeedStrength > hero.Strength + hero.AddStrength) return false;
            if (pItem.RequireDexterity - iDecNeedDex > hero.Dexterity + hero.AddDexterity) return false;
            if (pItem.RequireEnergy > hero.Energy + hero.AddEnergy) return false;
            if (pItem.RequireVitality > hero.Vitality + hero.AddVitality) return false;
            if (pItem.RequireCharisma > hero.Charisma + hero.AddCharisma) return false;
            if (pItem.RequireLevel > hero.Level) return false;
            return bEquipable;
        }

        // CSItemOption::checkItemType (CSItemOption.cpp:334)
        private void checkItemType(List<SetSearchResult> optionList, int iType, int ancientDiscriminator)
        {
            if (ancientDiscriminator <= 0 || iType < 0 || iType >= MAX_ITEM) return;
            int setTypeIndex = (ancientDiscriminator - 1) & 0xFF;
            if (setTypeIndex >= ItemSetOptionData.MAX_ITEM_SETS_PER_ITEM) return;
            int itemSetNumber = _data.m_ItemSetType[iType].byOption[setTypeIndex];
            if (itemSetNumber == 255 || itemSetNumber == 0 || itemSetNumber >= ItemSetOptionData.MAX_SET_OPTION) return;
            foreach (var current in optionList)
            {
                if (current.SetNumber == itemSetNumber)
                {
                    current.ItemCount++;
                    current.SetTypeIndex = setTypeIndex;
                    return;
                }
            }
            optionList.Add(new SetSearchResult
            {
                SetNumber = itemSetNumber,
                CompleteSetItemCount = _data.m_ItemSetOption[itemSetNumber].bySetItemCount,
                ItemCount = 1,
                SetTypeIndex = setTypeIndex,
                SetName = _data.m_ItemSetOption[itemSetNumber].strSetName,
            });
        }

        // CSItemOption::isClassRequirementFulfilled (CSItemOption.cpp:374)
        private static bool isClassRequirementFulfilled(ItemSetOptionData.SetOption setOptions, int firstClass, int secondClass)
        {
            bool RequireClass = false;
            for (int i = CLASS_WIZARD; i <= CLASS_RAGEFIGHTER; i++)
            {
                if (setOptions.byRequireClass[i] == 1 && firstClass == i) RequireClass = true;
                if (setOptions.byRequireClass[i] == 2 && firstClass == i && secondClass != 0) RequireClass = true;
            }
            return RequireClass;
        }

        // CSItemOption::TryAddSetOption (CSItemOption.cpp:393)
        private static void TryAddSetOption(int option, int value, int optionIndex, SetSearchResult set, ItemSetOptionData.SetOption setOptions,
            bool isThisSetComplete, bool isFullOption, bool isExtOption, bool fulfillsClassRequirement, int firstClass, int secondClass)
        {
            if (option == ItemSetOptionData.EMPTY_OPTION || value == 0
                || (option >= ItemSetOptionData.MASTERY_OPTION && (setOptions.byRequireClass[firstClass] != 0 && secondClass >= setOptions.byRequireClass[firstClass] - 1)))
                return;
            set.SetOption.Add(new SetOptionEntry
            {
                OptionNumber = option,
                FulfillsClassRequirement = fulfillsClassRequirement,
                Value = value,
                IsActive = isThisSetComplete || optionIndex < set.ItemCount - 1,
                IsFullOption = isFullOption,
                IsExtOption = isExtOption,
            });
        }

        /// <summary>
        /// CSItemOption::CheckItemSetOptions (CSItemOption.cpp:870) for the worn items. The original first pass only feeds
        /// getAllAddStateOnlyAddValue; here the server-provided stat totals are used for the requirement pass.
        /// </summary>
        private void CheckItemSetOptions()
        {
            m_SetSearchResult.Clear();
            var optionList = new List<SetSearchResult>();
            var equipment = _t._equipment;
            SourceItem itemRight = null;
            for (int i = 0; i < MAX_EQUIPMENT_INDEX && i < equipment.Count; ++i)
            {
                if (i == EQUIPMENT_WING || i == EQUIPMENT_HELPER) continue;
                var ip = equipment[i];
                if (ip == null || ip.Type < 0) continue;
                if (!IsRequireEquipItem(ip) || ip.Durability <= 0) continue;
                if ((i == EQUIPMENT_WEAPON_LEFT || i == EQUIPMENT_RING_LEFT) && itemRight != null && itemRight.Type == ip.Type && itemRight.AncientDiscriminator == ip.AncientDiscriminator)
                    continue;
                checkItemType(optionList, ip.Type, ip.AncientDiscriminator);
                if (i == EQUIPMENT_WEAPON_RIGHT || i == EQUIPMENT_RING_RIGHT) itemRight = ip;
            }

            int firstClass = ItemTooltipHero.GetBaseClass(_t.Hero.Class);
            int secondClass = ItemTooltipHero.IsSecondClass(_t.Hero.Class) ? 1 : 0;
            foreach (var set in optionList)
            {
                if (set.ItemCount < 2) continue;
                m_SetSearchResult.Add(set);
                var setOptions = _data.m_ItemSetOption[set.SetNumber];
                bool isThisSetComplete = set.CompleteSetItemCount <= set.ItemCount;
                bool requireClass = isClassRequirementFulfilled(setOptions, firstClass, secondClass);
                int standardOptionCount = Math.Min(set.CompleteSetItemCount - 1, ItemSetOptionData.MAX_ITEM_SET_STANDARD_OPTION_COUNT);
                for (int o = 0; o < standardOptionCount; ++o)
                    for (int n = 0; n < ItemSetOptionData.MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT; ++n)
                        TryAddSetOption(setOptions.byStandardOption[o, n], setOptions.byStandardOptionValue[o, n], o, set, setOptions, isThisSetComplete, false, false, requireClass, firstClass, secondClass);
                for (int o = 0; o < ItemSetOptionData.MAX_ITEM_SET_EXT_OPTION_COUNT; ++o)
                    TryAddSetOption(setOptions.byExtOption[o], setOptions.byExtOptionValue[o], 0, set, setOptions, isThisSetComplete, false, true, requireClass, firstClass, secondClass);
                for (int o = 0; o < ItemSetOptionData.MAX_ITEM_SET_FULL_OPTION_COUNT; ++o)
                    TryAddSetOption(setOptions.byFullOption[o], setOptions.byFullOptionValue[o], 255, set, setOptions, isThisSetComplete, true, false, requireClass, firstClass, secondClass);
            }
        }

        // CSItemOption::getExplainText (CSItemOption.cpp:476)
        private static bool getExplainText(out string text, int option, int value)
        {
            text = string.Empty;
            if (option == ItemSetOptionData.EMPTY_OPTION) return false;
            switch (option + AT_SET_OPTION_IMPROVE_STRENGTH)
            {
                case AT_SET_OPTION_IMPROVE_MAGIC_POWER:
                    text = Fmt(GlobalText[632], value); return true;
                case AT_SET_OPTION_IMPROVE_STRENGTH:
                case AT_SET_OPTION_IMPROVE_DEXTERITY:
                case AT_SET_OPTION_IMPROVE_ENERGY:
                case AT_SET_OPTION_IMPROVE_VITALITY:
                case AT_SET_OPTION_IMPROVE_CHARISMA:
                case AT_SET_OPTION_IMPROVE_ATTACK_MIN:
                case AT_SET_OPTION_IMPROVE_ATTACK_MAX:
                    text = Fmt(GlobalText[950 + option], value); return true;
                case AT_SET_OPTION_IMPROVE_DAMAGE:
                case AT_SET_OPTION_IMPROVE_ATTACKING_PERCENT:
                case AT_SET_OPTION_IMPROVE_DEFENCE:
                case AT_SET_OPTION_IMPROVE_MAX_LIFE:
                case AT_SET_OPTION_IMPROVE_MAX_MANA:
                case AT_SET_OPTION_IMPROVE_MAX_AG:
                case AT_SET_OPTION_IMPROVE_ADD_AG:
                case AT_SET_OPTION_IMPROVE_CRITICAL_DAMAGE_PERCENT:
                case AT_SET_OPTION_IMPROVE_CRITICAL_DAMAGE:
                case AT_SET_OPTION_IMPROVE_EXCELLENT_DAMAGE_PERCENT:
                case AT_SET_OPTION_IMPROVE_EXCELLENT_DAMAGE:
                case AT_SET_OPTION_IMPROVE_SKILL_ATTACK:
                case AT_SET_OPTION_DOUBLE_DAMAGE:
                    text = Fmt(GlobalText[949 + option], value); return true;
                case AT_SET_OPTION_DISABLE_DEFENCE:
                    text = Fmt(GlobalText[970], value); return true;
                case AT_SET_OPTION_TWO_HAND_SWORD_IMPROVE_DAMAGE:
                    text = Fmt(GlobalText[983], value); return true;
                case AT_SET_OPTION_IMPROVE_SHIELD_DEFENCE:
                    text = Fmt(GlobalText[984], value); return true;
                case AT_SET_OPTION_IMPROVE_ATTACK_1:
                case AT_SET_OPTION_IMPROVE_ATTACK_2:
                case AT_SET_OPTION_IMPROVE_MAGIC:
                case AT_SET_OPTION_IMPROVE_DEFENCE_3:
                case AT_SET_OPTION_IMPROVE_DEFENCE_4:
                case AT_SET_OPTION_FIRE_MASTERY:
                case AT_SET_OPTION_ICE_MASTERY:
                case AT_SET_OPTION_THUNDER_MASTERY:
                case AT_SET_OPTION_POSION_MASTERY:
                case AT_SET_OPTION_WATER_MASTERY:
                case AT_SET_OPTION_WIND_MASTERY:
                case AT_SET_OPTION_EARTH_MASTERY:
                    text = Fmt(GlobalText[971 + (option + AT_SET_OPTION_IMPROVE_STRENGTH - AT_SET_OPTION_IMPROVE_ATTACK_2)], value); return true;
            }
            return false;
        }

        // CSItemOption::RenderSetOptionList (CSItemOption.cpp:1181)
        private int RenderSetOptionList(SetSearchResult set, int textIndex, bool bIsEquippedItem, bool bShowInactive)
        {
            foreach (var option in set.SetOption)
            {
                if (!bShowInactive && !option.IsActive) break;
                if (getExplainText(out var text, option.OptionNumber, option.Value))
                {
                    _t.TextList[textIndex] = text;
                    if (!bIsEquippedItem || !option.IsActive) _t.TextListColor[textIndex] = TEXT_COLOR_GRAY;
                    else if (option.OptionNumber >= AT_SET_OPTION_IMPROVE_ATTACK_1 && !option.FulfillsClassRequirement) _t.TextListColor[textIndex] = TEXT_COLOR_RED;
                    else _t.TextListColor[textIndex] = option.IsFullOption ? TEXT_COLOR_YELLOW : option.IsExtOption ? TEXT_COLOR_GREEN : TEXT_COLOR_BLUE;
                    _t.TextBold[textIndex] = false;
                    textIndex++;
                }
            }
            _t.swprintf(textIndex, "\n");
            textIndex++;
            return textIndex;
        }

        // CSItemOption::RenderSetOptionListInItem (CSItemOption.cpp:1137)
        public int RenderSetOptionListInItem(SourceItem ip, int TextNum, bool bIsEquippedItem)
        {
            if (ip.AncientDiscriminator <= 0 || ip.Type < 0 || ip.Type >= MAX_ITEM) return TextNum;
            int index = ip.AncientDiscriminator - 1;
            if (index >= ItemSetOptionData.MAX_ITEM_SETS_PER_ITEM) return TextNum;
            int selected = _data.m_ItemSetType[ip.Type].byOption[index];
            if (selected <= 0 || selected == 255 || selected >= ItemSetOptionData.MAX_SET_OPTION) return TextNum;

            int TNum = TextNum;
            var setOption = _data.m_ItemSetOption[selected];
            if (setOption.byOptionCount >= 255) return TNum;

            CheckItemSetOptions();

            _t.swprintf(TNum, "\n"); TNum++;
            _t.swprintf(TNum, "%s %s", GlobalText[1089], GlobalText[159]);
            _t.TextListColor[TNum] = TEXT_COLOR_YELLOW;
            TNum++;
            _t.swprintf(TNum, "\n"); TNum++;
            _t.swprintf(TNum, "\n"); TNum++;

            foreach (var set in m_SetSearchResult)
            {
                if (set.SetName == setOption.strSetName)
                {
                    TNum = RenderSetOptionList(set, TNum, bIsEquippedItem, true);
                    break;
                }
            }

            _t.swprintf(TNum, "\n"); TNum++;
            _t.swprintf(TNum, "\n"); TNum++;
            return TNum;
        }
    }
}
}
