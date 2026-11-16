#pragma warning disable CS0162, CS0164
using System;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
    /// <summary>
    /// ItemValue (ZzzInfomation.cpp:1592-2340), CalcRepairCost (ZzzInventory.cpp:1205) and COMGEM::CalcItemValue (CComGem.cpp).
    /// goldType: 0 = buying price, 1 = selling price, 2 = repair base.
    /// </summary>
    public static class SourceItemValue
    {
        /// <summary>Dark Horse / Dark Raven pet level from the pet info packet (-1 = unknown, PET_TYPE_NONE).</summary>
        public static long ItemValue(SourceItem ip, int goldType, int petLevel = -1)
    {
    if (ip.Type == -1) return 0;

    SourceItemAttribute p = ip.Attribute;

    long Gold = 0;

    if (p.iZen != 0)
    {
        Gold = p.iZen;

        if (goldType != 0)
        {
            Gold = Gold / 3;
        }

        if (Gold >= 1000)
            Gold = Gold / 100 * 100;
        else if (Gold >= 100)
            Gold = Gold / 10 * 10;

        return (int)Gold;
    }

    int Type = ip.Type / MAX_ITEM_INDEX;
    int Level = ip.Level;
    bool Excellent = false;
    for (int i = 0; i < ip.SpecialNum; i++)
    {
        switch (ip.Special[i])
        {
        case AT_IMPROVE_LIFE:
        case AT_IMPROVE_MANA:
        case AT_DECREASE_DAMAGE:
        case AT_REFLECTION_DAMAGE:
        case AT_IMPROVE_BLOCKING_PERCENT:
        case AT_IMPROVE_GAIN_GOLD:
        case AT_EXCELLENT_DAMAGE:
        case AT_IMPROVE_DAMAGE_LEVEL:
        case AT_IMPROVE_DAMAGE_PERCENT:
        case AT_IMPROVE_MAGIC_LEVEL:
        case AT_IMPROVE_MAGIC_PERCENT:
        case AT_IMPROVE_ATTACK_SPEED:
        case AT_IMPROVE_GAIN_LIFE:
        case AT_IMPROVE_GAIN_MANA:
            Excellent = true;
            break;
        }
    }
    int Level2 = p.Level + Level * 3;
    if (Excellent)
        Level2 += 25;

    if (ip.Type == ITEM_BOLT)
    {
        int sellMoney = 0;

        switch (Level)
        {
        case    0:
            sellMoney = 100;
            break;
        case    1:
            sellMoney = 1400;
            break;
        case    2:
            sellMoney = 2200;
            break;
        case    3:
            sellMoney = 3000;
            break;
        }
        if (p.Durability > 0)
            Gold = (sellMoney * ip.Durability / p.Durability);//+(170*(Level*2));
    }
    else if (ip.Type == ITEM_ARROWS)
    {
        int sellMoney = 0;

        switch (Level)
        {
        case    0:
            sellMoney = 70;
            break;
        case    1:
            sellMoney = 1200;
            break;
        case    2:
            sellMoney = 2000;
            break;
        case    3:
            sellMoney = 2800;
            break;
        }
        if (p.Durability > 0)
            Gold = (sellMoney * ip.Durability / p.Durability);//+(80*(Level*2));
    }
    else if (COMGEM.isCompiledGem(ip))
    {
        Gold = CalcCompiledGemValue(ip);
    }
    else if (ip.Type == ITEM_JEWEL_OF_BLESS)
    {
        Gold = 9000000;
    }
    else if (ip.Type == ITEM_JEWEL_OF_SOUL)
    {
        Gold = 6000000;
    }
    else if (ip.Type == ITEM_JEWEL_OF_CHAOS)
    {
        Gold = 810000;
    }
    else if (ip.Type == ITEM_JEWEL_OF_LIFE)
    {
        Gold = 45000000;
    }
    else if (ip.Type == ITEM_JEWEL_OF_CREATION)
    {
        Gold = 36000000;
    }
    else if (ip.Type == ITEM_POTION + 141)
    {
        Gold = 224000 * 3;
    }
    else if (ip.Type == ITEM_POTION + 142)
    {
        Gold = 182000 * 3;
    }
    else if (ip.Type == ITEM_POTION + 143)
    {
        Gold = 157000 * 3;
    }
    else if (ip.Type == ITEM_POTION + 144)
    {
        Gold = 121000 * 3;
    }

    else if (ip.Type == ITEM_LOCHS_FEATHER)
    {
        switch (Level)
        {
        case 0: Gold = 180000;  break;
        case 1: Gold = 7500000; break;
        }
    }
    else if (ip.Type == ITEM_HORN_OF_DINORANT)
    {
        Gold = 960000;
        for (int i = 0; i < ip.SpecialNum; i++)
        {
            switch (ip.Special[i])
            {
            case AT_DAMAGE_ABSORB:
            case AT_IMPROVE_AG_MAX:
            case AT_IMPROVE_ATTACK_SPEED:
                Gold += 300000;
                break;
            }
        }
    }
    else if (ip.Type == ITEM_FRUITS)
    {
        Gold = 33000000;
    }
    else if (ip.Type == ITEM_SCROLL_OF_ARCHANGEL || ip.Type == ITEM_BLOOD_BONE)
    {
        switch (Level)
        {
        case 1: Gold = 10000; break;
        case 2: Gold = 50000; break;
        case 3: Gold = 100000; break;
        case 4: Gold = 300000; break;
        case 5: Gold = 500000; break;
        case 6: Gold = 800000; break;
        case 7: Gold = 1000000; break;
        case 8: Gold = 1200000; break;
        default:
            Gold = 0;
            break;
        }
    }
    else if (ip.Type == ITEM_INVISIBILITY_CLOAK)
    {
        Gold = (long)(200000 + 20000 * (Level - 1));
        if (Level == 1)
        {
            Gold = 50000;
        }
        Gold *= 3;
    }
    else if (ip.Type == ITEM_LOST_MAP)
    {
        Gold = 200000 * 3;
    }
    else if (ip.Type == ITEM_SYMBOL_OF_KUNDUN)
    {
        Gold = (long)(ip.Durability * 10000) * 3;
    }
    else if (ip.Type == ITEM_POTION + 111)
    {
        Gold = 200000 * 3;
    }
    else if (ip.Type == ITEM_POTION + 110)
    {
        Gold = (long)(ip.Durability * 10000) * 3;
    }
    else if (ITEM_GAIONS_ORDER == ip.Type)
    {
        Gold = 10000 * 3;
    }
    else if (ITEM_COMPLETE_SECROMICON == ip.Type)
    {
        Gold = 10000 * 3;
    }
    else if (ITEM_SUSPICIOUS_SCRAP_OF_PAPER == ip.Type || ITEM_FIRST_SECROMICON_FRAGMENT <= ip.Type && ip.Type <= ITEM_SIXTH_SECROMICON_FRAGMENT)
    {
        Gold = (long)((long)ip.Durability * 10000) * 3;
    }
    else if (ip.Type == ITEM_ARMOR_OF_GUARDSMAN)
    {
        Gold = 5000;
    }
    else if (ip.Type == ITEM_POTION + 21)
    {
        if (Level == 0)
            Gold = 9000;
        else if (Level == 1)
            Gold = 9000;
        else if (Level == 2)
            Gold = 3000 * 3;
        else if (Level == 3)
        {
            Gold = (long)ip.Durability * 3900;
        }
    }
    else if (ip.Type == ITEM_DEVILS_EYE)
    {
        int[] iValue = { 30000, 10000, 50000, 100000, 300000, 500000, 800000, 1000000 };
        Gold = iValue[Math.Min(Math.Max(0, Level), 7)];
    }
    else if (ip.Type == ITEM_DEVILS_KEY)
    {
        int[] iValue = { 30000, 15000, 75000, 150000, 450000, 750000, 1200000, 1500000 };
        Gold = iValue[Math.Min(Math.Max(0, Level), 7)];
    }
    else if (ip.Type == ITEM_DEVILS_INVITATION)
    {
        int[] iValue = { 120000, 60000, 84000, 120000, 180000, 240000, 300000, 180000 };
        Gold = iValue[Math.Min(Math.Max(0, Level), 7)];
    }
    else if (ip.Type == ITEM_OLD_SCROLL || ip.Type == ITEM_ILLUSION_SORCERER_COVENANT || ip.Type == ITEM_SCROLL_OF_BLOOD)
    {
        switch (Level)
        {
        case 1: Gold = 500000; break;
        case 2: Gold = 600000; break;
        case 3: Gold = 800000; break;
        case 4: Gold = 1000000; break;
        case 5: Gold = 1200000; break;
        case 6: Gold = 1400000; break;
        default:
            Gold = 3000 * 3;
            break;
        }
    }
    else if (ip.Type == ITEM_SCROLL_OF_EMPEROR_RING_OF_HONOR || ip.Type == ITEM_BROKEN_SWORD_DARK_STONE || ip.Type == ITEM_TEAR_OF_ELF || ip.Type == ITEM_SOUL_SHARD_OF_WIZARD
        || ip.Type == ITEM_FLAME_OF_DEATH_BEAM_KNIGHT || ip.Type == ITEM_HORN_OF_HELL_MAINE || ip.Type == ITEM_FEATHER_OF_DARK_PHOENIX || ip.Type == ITEM_EYE_OF_ABYSSAL
        )
    {
        Gold = 9000;
    }
    else if (ip.Type == ITEM_ALE && Level == 1)
    {
        Gold = 1000;
    }
    else if (ip.Type == ITEM_POTION + 20)
    {
        Gold = 900;
    }
    else if (ip.Type == ITEM_CHRISTMAS_STAR)
    {
        Gold = 200000;
    }
    else if (ip.Type == ITEM_FIRECRACKER)
    {
        Gold = 200000;
    }
    else if (ip.Type == ITEM_GM_GIFT)
    {
        Gold = 33 * 3;
    }
    else if (ip.Type == ITEM_TOWN_PORTAL_SCROLL)
    {
        Gold = 750;
    }
    else if (ip.Type == ITEM_JEWEL_OF_GUARDIAN)
    {
        Gold = 60000000;
    }
    else if (ip.Type == ITEM_SIEGE_POTION)
    {
        switch (Level)
        {
        case 0: Gold = (long)900000 * ip.Durability; break;
        case 1: Gold = (long)450000 * ip.Durability; break;
        }
    }
    else if (ip.Type == ITEM_HELPER + 7)
    {
        switch (Level)
        {
        case 0: Gold = 1500000; break;
        case 1: Gold = 1200000; break;
        }
    }
    else if (ip.Type == ITEM_LIFE_STONE_ITEM)
    {
        switch (Level)
        {
        case 0: Gold = 100000; break;
        case 1: Gold = 2400000; break;
        }
    }
    else if (ip.Type >= ITEM_SMALL_SHIELD_POTION && ip.Type <= ITEM_LARGE_SHIELD_POTION)
    {
        switch (ip.Type)
        {
        case ITEM_SMALL_SHIELD_POTION:
            Gold = 2000;
            break;
        case ITEM_MEDIUM_SHIELD_POTION:
            Gold = 4000;
            break;
        case ITEM_LARGE_SHIELD_POTION:
            Gold = 6000;
            break;
        }
        Gold *= ip.Durability;
    }
    else if (ip.Type >= ITEM_SMALL_COMPLEX_POTION && ip.Type <= ITEM_LARGE_COMPLEX_POTION)
    {
        switch (ip.Type)
        {
        case ITEM_SMALL_COMPLEX_POTION:
            Gold = 2500;
            break;
        case ITEM_MEDIUM_COMPLEX_POTION:
            Gold = 5000;
            break;
        case ITEM_LARGE_COMPLEX_POTION:
            Gold = 7500;
            break;
        }
        Gold *= ip.Durability;
    }
    else if (ip.Type == ITEM_POTION + 100)
    {
        Gold = 100 * 3;
        Gold *= ip.Durability;
    }
    else if (ip.Type == ITEM_GOLDEN_CHERRY_BLOSSOM_BRANCH || ip.Type == ITEM_CHERRY_BLOSSOM_WINE || ip.Type == ITEM_CHERRY_BLOSSOM_RICE_CAKE || ip.Type == ITEM_CHERRY_BLOSSOM_FLOWER_PETAL)
    {
        Gold = 300;
        Gold *= ip.Durability;
    }

    else if (p.Value > 0)
    {
        Gold = p.Value * p.Value * 10 / 12;

        if (ip.Type == ITEM_LARGE_HEALING_POTION || ip.Type == ITEM_LARGE_MANA_POTION)
        {
            Gold = 1500;
        }

        if (ip.Type >= ITEM_POTION && ip.Type <= ITEM_ANTIDOTE)
        {
            if (Level > 0)
                Gold *= (long)Math.Pow(2, Level);

            Gold = Gold / 10 * 10;
            Gold *= (long)ip.Durability;

            if (goldType != 0)
            {
                Gold = Gold / 3;
                Gold = Gold / 10 * 10;
            }
            return (long)Gold;
        }
    }
    else if (ip.Type == ITEM_WIZARDS_RING)
    {
        if (Level == 0)
            Gold = 3000;
        else if (Level == 1)
            Gold = 0;
    }
    else if (ip.Type == ITEM_FLAME_OF_CONDOR || ip.Type == ITEM_FEATHER_OF_CONDOR)
    {
        Gold = 3000000;
    }
    else if (ip.Type == ITEM_SPIRIT)
    {
        switch (Level)
        {
        case 0: Gold = 10000000 * 3; break;
        case 1: Gold = 5000000 * 3;  break;
        }
    }
    else if (((Type == 12 && (ip.Type > ITEM_WINGS_OF_DARKNESS
        && !(ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_WING_OF_DIMENSION)
        && (ip.Type != ITEM_CAPE_OF_OVERRULE)
        )) || Type == 13 || Type == 15))
    {
        Gold = (long)100 + Level2 * Level2 * Level2;

        for (int i = 0; i < ip.SpecialNum; i++)
        {
            switch (ip.Special[i])
            {
            case AT_LIFE_REGENERATION:
                Gold += Gold * ip.SpecialValue[i];
                break;
            }
        }
    }
    else if (ip.Type == ITEM_POTION + 53)
    {
        Gold = 0;
    }
    else
    {
        switch (Level)
        {
        case 5:Level2 += 4; break;
        case 6:Level2 += 10; break;
        case 7:Level2 += 25; break;
        case 8:Level2 += 45; break;
        case 9:Level2 += 65; break;
        case 10:Level2 += 95; break;
        case 11:Level2 += 135; break;
        case 12:Level2 += 185; break;
        case 13:Level2 += 245; break;
        case 14:Level2 += 305; break;
        case 15:Level2 += 365; break;
        }
        if ((Type == 12 && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_CAPE_OF_LORD || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_WING_OF_DIMENSION)
            || (ip.Type == ITEM_CAPE_OF_OVERRULE))
        {
            Gold = (long)(40000000 + ((40 + Level2) * Level2 * Level2 * 11));
        }
        else
        {
            Gold = (long)100 + (40 + Level2) * Level2 * Level2 / 8;
        }
        if (Type >= 0 && Type <= 6)
        {
            if (!p.TwoHand)
                Gold = Gold * 80 / 100;
        }
        for (int i = 0; i < ip.SpecialNum; i++)
        {
            switch (ip.Special[i])
            {
            case AT_SKILL_BLOCKING:
            case AT_SKILL_SWORD1:
            case AT_SKILL_SWORD2:
            case AT_SKILL_SWORD3:
            case AT_SKILL_SWORD4:
            case AT_SKILL_SWORD5:
            case AT_SKILL_MANY_ARROW_UP:
            case AT_SKILL_MANY_ARROW_UP + 1:
            case AT_SKILL_MANY_ARROW_UP + 2:
            case AT_SKILL_MANY_ARROW_UP + 3:
            case AT_SKILL_MANY_ARROW_UP + 4:
            case AT_SKILL_CROSSBOW:
            case AT_SKILL_RECOVER:
            case AT_SKILL_GAOTIC:
            case AT_SKILL_MULTI_SHOT:
            case AT_SKILL_POWER_SLASH_UP:
            case AT_SKILL_POWER_SLASH_UP + 1:
            case AT_SKILL_POWER_SLASH_UP + 2:
            case AT_SKILL_POWER_SLASH_UP + 3:
            case AT_SKILL_POWER_SLASH_UP + 4:
            case AT_SKILL_ICE_BLADE:
            case AT_SKILL_THRUST:
            case AT_SKILL_STAMP:
                Gold += (long)((double)Gold * 1.5f);
                break;
            case AT_IMPROVE_DAMAGE:
            case AT_IMPROVE_MAGIC:
            case AT_IMPROVE_CURSE:
            case AT_IMPROVE_DEFENSE:
            case AT_LIFE_REGENERATION:
                if ((Type == 12 && ip.Type <= ITEM_WINGS_OF_DARKNESS)

                    || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_WING_OF_DIMENSION)
                    || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE)
                    )    //  날개.
                {
                    int iOption = ip.SpecialValue[i];
                    if (AT_LIFE_REGENERATION == ip.Special[i])
                    {
                        iOption *= 4;
                    }
                    switch (iOption)
                    {
                    case 4:Gold += (long)((double)Gold * 6 / 10); break;
                    case 8:Gold += (long)((double)Gold * 14 / 10); break;
                    case 12:Gold += (long)((double)Gold * 28 / 10); break;
                    case 16:Gold += (long)((double)Gold * 56 / 10); break;
                    }
                }
                else
                {
                    switch (ip.SpecialValue[i])
                    {
                    case 4:Gold += (long)((double)Gold * 6 / 10); break;
                    case 8:Gold += (long)((double)Gold * 14 / 10); break;
                    case 12:Gold += (long)((double)Gold * 28 / 10); break;
                    case 16:Gold += (long)((double)Gold * 56 / 10); break;
                    }
                }
                break;
            case AT_IMPROVE_BLOCKING:
                switch (ip.SpecialValue[i])
                {
                case 5:Gold += (long)((double)Gold * 6 / 10); break;
                case 10:Gold += (long)((double)Gold * 14 / 10); break;
                case 15:Gold += (long)((double)Gold * 28 / 10); break;
                case 20:Gold += (long)((double)Gold * 56 / 10); break;
                }
                break;
            case AT_LUCK:
                Gold += (long)((double)Gold * 25 / 100);
                break;
            case AT_IMPROVE_LIFE:
            case AT_IMPROVE_MANA:
            case AT_DECREASE_DAMAGE:
            case AT_REFLECTION_DAMAGE:
            case AT_IMPROVE_BLOCKING_PERCENT:
            case AT_IMPROVE_GAIN_GOLD:
            case AT_EXCELLENT_DAMAGE:
            case AT_IMPROVE_DAMAGE_LEVEL:
            case AT_IMPROVE_DAMAGE_PERCENT:
            case AT_IMPROVE_MAGIC_LEVEL:
            case AT_IMPROVE_MAGIC_PERCENT:
            case AT_IMPROVE_GAIN_LIFE:
            case AT_IMPROVE_GAIN_MANA:
            case AT_IMPROVE_ATTACK_SPEED:
                Gold += Gold;
                break;
            case AT_IMPROVE_EVADE:
                Gold += Gold;
                break;
            case AT_IMPROVE_HP_MAX:
            case AT_IMPROVE_MP_MAX:
            case AT_ONE_PERCENT_DAMAGE:
            case AT_IMPROVE_AG_MAX:
            case AT_DAMAGE_ABSORB:
            case AT_DAMAGE_REFLECTION:
            case AT_RECOVER_FULL_LIFE:
            case AT_RECOVER_FULL_MANA:
                Gold += (long)((double)Gold * 25 / 100);
                break;
            }
        }
        Gold += SourceSocketItems.CalcSocketBonusItemValue(ip, Gold);
    }
    Gold = Math.Min(Gold, 3000000000L);

    if (goldType == 2)
    {
        if (Gold >= 1000)
        {
            Gold = (Gold / 100) * 100;
        }
        else if (Gold >= 100)
        {
            Gold = (Gold / 10) * 10;
        }
    }

    if (goldType != 0)
    {
        Gold = Gold / 3;
    }

    if (ip.Type == ITEM_SPLINTER_OF_ARMOR)
        Gold = (long)ip.Durability * 50;
    else if (ip.Type == ITEM_BLESS_OF_GUARDIAN)
        Gold = (long)ip.Durability * 100;
    else if (ip.Type == ITEM_CLAW_OF_BEAST)
        Gold = (long)ip.Durability * 1000;
    else if (ip.Type == ITEM_FRAGMENT_OF_HORN)
        Gold = (long)ip.Durability * 10000;
    else if (ip.Type == ITEM_BROKEN_HORN)
        Gold = 30000;
    else if (ip.Type == ITEM_HORN_OF_FENRIR)
        Gold = 50000;

    if (ip.Type >= ITEM_PUMPKIN_OF_LUCK && ip.Type <= ITEM_JACK_OLANTERN_DRINK)
    {
        Gold = (long)ip.Durability * 50;
    }

    if (ip.Type == ITEM_HELPER + 71 || ip.Type == ITEM_HELPER + 72 || ip.Type == ITEM_HELPER + 73 || ip.Type == ITEM_HELPER + 74 || ip.Type == ITEM_HELPER + 75)
    {
        Gold = 2000000;
    }

    if ((ip.Type == ITEM_DARK_HORSE_ITEM) || (ip.Type == ITEM_DARK_RAVEN_ITEM))
    {
        if (petLevel < 0) return -1; // giPetManager::GetPetInfo: PET_TYPE_NONE




        Gold = ip.Type == ITEM_DARK_HORSE_ITEM ? petLevel * 2000000L : petLevel * 1000000L; // giPetManager::GetPetItemValue
    }

    switch (ip.Type)
    {
    case ITEM_POTION + 112:
    case ITEM_POTION + 113:
    case ITEM_POTION + 121:
    case ITEM_POTION + 122:
    case ITEM_POTION + 123:
    case ITEM_POTION + 124:
    case ITEM_PET_PANDA:
    case ITEM_PANDA_TRANSFORMATION_RING:
    case ITEM_DEMON:
    case ITEM_SPIRIT_OF_GUARDIAN:
    case ITEM_HELPER + 109:
    case ITEM_HELPER + 110:
    case ITEM_HELPER + 111:
    case ITEM_HELPER + 112:
    case ITEM_HELPER + 113:
    case ITEM_HELPER + 114:
    case ITEM_HELPER + 115:
        Gold = 1000;
        break;
    case ITEM_SKELETON_TRANSFORMATION_RING:
    case ITEM_PET_SKELETON:
        Gold = 2000;
        break;
    case ITEM_WING + 130:
    case ITEM_WING + 131:
    case ITEM_WING + 132:
    case ITEM_WING + 133:
    case ITEM_WING + 134:
    case ITEM_WING + 135:
        Gold = 80;
        break;
    }

    if (ip.Type == ITEM_TRANSFORMATION_RING || ip.Type == ITEM_WIZARDS_RING || ip.Type == ITEM_ARMOR_OF_GUARDSMAN)
        goto EXIT_CALCULATE;
    if (ip.Type == ITEM_BOLT || ip.Type == ITEM_ARROWS || ip.Type >= ITEM_POTION)
        goto EXIT_CALCULATE;
    if (ip.Type >= ITEM_ORB_OF_TWISTING_SLASH && ip.Type <= ITEM_ORB_OF_DEATH_STAB)
        goto EXIT_CALCULATE;
    if ((ip.Type >= ITEM_LOCHS_FEATHER && ip.Type <= ITEM_WEAPON_OF_ARCHANGEL) || ip.Type == ITEM_POTION + 21)
        goto EXIT_CALCULATE;
    if (ip.Type == ITEM_SIEGE_POTION || ip.Type == ITEM_HELPER + 7 || ip.Type == ITEM_LIFE_STONE_ITEM)
        goto EXIT_CALCULATE;
    if ((ip.Type >= ITEM_OLD_SCROLL) && (ip.Type <= ITEM_SCROLL_OF_BLOOD))
        goto EXIT_CALCULATE;

    switch (ip.Type)
    {
    case ITEM_POTION + 112:	goto EXIT_CALCULATE;
        // MODEL_POTION+113
    case ITEM_POTION + 113:	goto EXIT_CALCULATE;
    case ITEM_POTION + 121:	goto EXIT_CALCULATE;
    case ITEM_POTION + 122:	goto EXIT_CALCULATE;
    case ITEM_POTION + 123:	goto EXIT_CALCULATE;
    case ITEM_POTION + 124:	goto EXIT_CALCULATE;
    case ITEM_WING + 130:
    case ITEM_WING + 131:
    case ITEM_WING + 132:
    case ITEM_WING + 133:
    case ITEM_WING + 134:
    case ITEM_WING + 135:
        goto EXIT_CALCULATE;
    case ITEM_SKELETON_TRANSFORMATION_RING:
        goto EXIT_CALCULATE;
    case ITEM_PET_SKELETON:
        goto EXIT_CALCULATE;
    case ITEM_PET_PANDA:
        goto EXIT_CALCULATE;
    case ITEM_PANDA_TRANSFORMATION_RING:
        goto EXIT_CALCULATE;
    case ITEM_DEMON:
    case ITEM_SPIRIT_OF_GUARDIAN:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 109:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 110:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 111:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 112:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 113:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 114:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 115:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 128:
    case ITEM_HELPER + 129:
    case ITEM_HELPER + 134:
        goto EXIT_CALCULATE;
    case ITEM_HELPER + 130:
    case ITEM_HELPER + 131:
    case ITEM_HELPER + 132:
    case ITEM_HELPER + 133:
        goto EXIT_CALCULATE;
    }

    if (goldType == 1 && !(ip.Type >= ITEM_SPLINTER_OF_ARMOR && ip.Type <= ITEM_HORN_OF_FENRIR))
    {
        //        wchar_t Text[100];
                //int repairGold = ConvertRepairGold(Gold,ip.Durability, p.Durability, ip.Type, Text);
        long maxDurability = SourceItemAttributes.CalcMaxDurability(ip, p, Level);
        float persent = 1f - ((float)ip.Durability / (float)maxDurability);
        var repairGold = (long)(Gold * 0.6f * persent);

        if (ip.Type == ITEM_SPIRIT)
            repairGold = 0;

        Gold -= repairGold;
    }
EXIT_CALCULATE:

    if (Gold >= 1000)
    {
        Gold = (Gold / 100) * 100;
    }
    else if (Gold >= 100)
    {
        Gold = (Gold / 10) * 10;
    }
    if (SourceItemAttributes.Check_LuckyItem(ip.Type))	Gold = 0;

    return (long)Gold;
        }

        // COMGEM::CalcItemValue (CComGem.cpp)
        public static int CalcCompiledGemValue(SourceItem p)
        {
            int Level = p.Level;
            switch (COMGEM.CheckOneItem(p))
            {
                case COMGEM.eBLESS_C: return 9000000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.eSOUL_C: return 6000000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.eLIFE_C: return 45000000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.eCREATE_C: return 36000000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.ePROTECT_C: return 60000000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.eCHAOS_C: return 810000 * (Level + 1) * COMGEM.FIRST;
                case COMGEM.eGEMSTONE_C:
                case COMGEM.eHARMONY_C:
                case COMGEM.eLOW_C:
                case COMGEM.eUPPER_C:
                    return 18600 * (Level + 1) * COMGEM.FIRST;
            }
            return 0;
        }

        /// <summary>CalcRepairCost (ZzzInventory.cpp:1205).</summary>
        public static long CalcRepairCost(long ItemValue, int Durability, int MaxDurability, int Type, bool SelfRepair)
        {
            long repairGold = Math.Min(ItemValue, 400000000L);
            float percent = MaxDurability == 0 ? 0f : 1f - (float)Durability / MaxDurability;
            if (percent > 0f)
            {
                double fRoot = Math.Sqrt(repairGold);
                double fRootRoot = Math.Sqrt(fRoot);
                repairGold = (long)(3.5 * fRoot * fRootRoot * percent) + 1;
                if (Durability <= 0)
                {
                    if (Type == ITEM_DARK_HORSE_ITEM || Type == ITEM_DARK_RAVEN_ITEM)
                        repairGold *= 2;
                    else
                        repairGold = (long)(repairGold * 1.4);
                }
            }
            else
            {
                repairGold = 0;
            }

            if (SelfRepair)
                repairGold = (long)(repairGold * 2.5);

            if (repairGold >= 1000) repairGold = repairGold / 100 * 100;
            else if (repairGold >= 100) repairGold = repairGold / 10 * 10;
            return repairGold;
        }
    }
}
