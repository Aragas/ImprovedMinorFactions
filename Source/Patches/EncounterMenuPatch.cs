﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedMinorFactions.Patches
{
    // makes sure correct menus appear when visiting MFHideout
    [HarmonyPatch(typeof(DefaultEncounterGameMenuModel), "GetGenericStateMenu")]
    public class DefaultEncounterGameMenuModelStateMenuPatch
    {
        static void Postfix(ref string __result)
        {
            if (__result != null)
                return;

            MobileParty mainParty = MobileParty.MainParty;
            Settlement currentSettlement = mainParty.CurrentSettlement;
            if (PlayerEncounter.Current?.IsPlayerWaiting == true)
                __result = "mf_hideout_wait";
            else if (mainParty.AttachedTo == null && mainParty.CurrentSettlement != null && Helpers.isMFHideout(currentSettlement))
                __result = "mf_hideout_place";
        }
    }

    [HarmonyPatch(typeof(DefaultEncounterGameMenuModel), "GetEncounterMenu")]
    public class DefaultEncounterGameMenuModelEncounterMenuPatch
    {
        static void Postfix(DefaultEncounterGameMenuModel __instance, ref string __result, PartyBase attackerParty, PartyBase defenderParty)
        {
            if (__result != null)
                return;
            PartyBase encounteredPartyBase = GetEncounteredPartyBaseCopy(attackerParty, defenderParty);
            if (encounteredPartyBase.IsSettlement && Helpers.isMFHideout(encounteredPartyBase.Settlement))
                __result = "mf_hideout_place";
        }

        // copypasta
        static PartyBase GetEncounteredPartyBaseCopy(PartyBase attackerParty, PartyBase defenderParty)
        {
            if (attackerParty == PartyBase.MainParty || defenderParty == PartyBase.MainParty)
            {
                if (attackerParty != PartyBase.MainParty)
                    return attackerParty;
                return defenderParty;
            }
            else
            {
                if (defenderParty.MapEvent == null)
                    return attackerParty;
                return defenderParty;
            }
        }
    }
}
