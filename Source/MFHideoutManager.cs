﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace ImprovedMinorFactions
{
    internal class MFHideoutManager
    {
        public MFHideoutManager() 
        {
            _LoadedMFHideouts = new Dictionary<string, MinorFactionHideout>();
            _factionsWaitingForWar = new HashSet<Clan>();
        }

        private static void InitManager()
        {
            MFHideoutManager.Current = new MFHideoutManager();
            Current._factionHideoutsInitialized = Current.TryInitMFHideoutsLists();
        }

        public static void InitManagerIfNone()
        {
            if (MFHideoutManager.Current == null)
                InitManager();

        }
        public static void ClearManager()
        {
            MFHideoutManager.Current = null;
        }

        public void AddLoadedMFHideout(MinorFactionHideout mfh)
        {
            if (_LoadedMFHideouts.ContainsKey(mfh.StringId))
                throw new Exception($"{mfh.StringId} duplicate hideout saves, likely a save definer issue!");
                
            _LoadedMFHideouts.Add(mfh.StringId, mfh);
        }

        public MinorFactionHideout GetLoadedMFHideout(string stringId)
        {
            return _LoadedMFHideouts[stringId];
        }

        // should only be done when all Settlements are loaded in
        public bool TryInitMFHideoutsLists()
        {
            if (Campaign.Current == null)
                return false;
            if (_factionHideoutsInitialized)
                return true;
            _factionHideouts = new Dictionary<Clan, List<MinorFactionHideout>>();
            foreach (Settlement settlement in Campaign.Current.Settlements)
            {
                if (settlement.OwnerClan?.IsMinorFaction ?? Helpers.isMFHideout(settlement))
                {
                    var key = settlement.OwnerClan;
                    List<MinorFactionHideout> list = null;
                    if (!_factionHideouts.ContainsKey(key))
                        _factionHideouts[key] = new List<MinorFactionHideout>();
                    _factionHideouts[key].Add(Helpers.GetMFHideout(settlement));
                }
            }
            _factionHideoutsInitialized = true;
            this._hideouts =
                (from x in Campaign.Current.Settlements
                where Helpers.isMFHideout(x)
                select Helpers.GetMFHideout(x)).ToMBList();
            return true;
        }

        public void ActivateAllFactionHideouts()
        {
            if (!_factionHideoutsInitialized)
                throw new Exception("Trying to activate faction hideouts early :(");
            foreach(var (faction, hideouts) in _factionHideouts.Select(x => (x.Key, x.Value)))
            {
                int activateIndex = MBRandom.RandomInt(hideouts.Count);
                hideouts[activateIndex].ActivateHideoutFirstTime();
            }
        }

        public void ClearHideout(MinorFactionHideout oldHideout)
        {
            if (!TryInitMFHideoutsLists())
                throw new Exception("can't switch Hideout due to uninitialized Hideout Manager");

            var oldSettlement = oldHideout.Settlement;
            if (oldSettlement.Parties.Count > 0)
            {
                foreach (MobileParty mobileParty in new List<MobileParty>(oldSettlement.Parties))
                {
                    LeaveSettlementAction.ApplyForParty(mobileParty);
                    mobileParty.Ai.SetDoNotAttackMainParty(3);
                }
            }
            oldHideout.IsSpotted = false;
            oldSettlement.IsVisible = false;

            var hideouts = _factionHideouts[oldHideout.OwnerClan];
            int activateIndex = MBRandom.RandomInt(hideouts.Count);
            while (hideouts[activateIndex].Settlement.Equals(oldHideout.Settlement))
                activateIndex = MBRandom.RandomInt(hideouts.Count);
            var newHideout = hideouts[activateIndex];
            oldHideout.MoveHideouts(newHideout);
        }

        public MinorFactionHideout GetHideoutOfClan(Clan minorFaction)
        {
            if (!minorFaction.IsMinorFaction || !this.HasFaction(minorFaction))
                return null;
            foreach(var mfHideout in _factionHideouts[minorFaction])
            {
                if (mfHideout.IsActive)
                    return mfHideout;
            }
            return null;
        }

        public void ValidateMaxOneActiveHideoutPerClan()
        {
            foreach (var kvp in this._factionHideouts)
            {
                int count = 0;
                foreach (var mfHideout in kvp.Value)
                {
                    if (mfHideout.IsActive)
                        count++;
                }
                if (count > 1)
                {
                    if (Helpers.IsDebugMode)
                        throw new Exception($"{kvp.Key} has multiple active hideouts");
                    else
                        FixHideoutInconsistencies(kvp.Value);
                }
                    

            }
        }

        private void FixHideoutInconsistencies(List<MinorFactionHideout> mfhList)
        {
            MinorFactionHideout mostRationalHideout = null;
            foreach (var mfHideout in mfhList) { 
                if (mfHideout.IsActive && mfHideout.Settlement.Notables.Count == 2)
                {
                    mostRationalHideout = mfHideout;
                    break;
                }
            }
            // if most rational is null then they're all getting destroyed
            foreach (var mfHideout in mfhList)
            {
                if (mfHideout != mostRationalHideout)
                {
                    mfHideout.DeactivateHideout(false);
                }
            }
            if (mostRationalHideout == null)
            {
                MFHideoutManager.Current.RemoveClan(mfhList[0].OwnerClan);
            }
        }

        public bool HasFaction(Clan minorFaction)
        {
            if (!TryInitMFHideoutsLists())
                throw new Exception("can't initialize hideouts list in Hideout Manager");
            return _factionHideouts.ContainsKey(minorFaction);
        }

        internal void RemoveClan(Clan destroyedClan)
        {
            if (!this.HasFaction(destroyedClan))
                return;
            foreach (var mfHideout in _factionHideouts[destroyedClan])
            {
                if (mfHideout.IsActive)
                {
                    // need to do this because can't kill notables and iterate over Notables list simultaneously.
                    var notablesToKill = new List<Hero>();
                    notablesToKill.AddRange(mfHideout.Settlement.Notables);
                    foreach (Hero notable in notablesToKill)
                    {
                        KillCharacterAction.ApplyByRemove(notable, true, true);
                    }
                    mfHideout.DeactivateHideout(false);
                }
            }
                
            _factionHideouts.Remove(destroyedClan);
            _factionsWaitingForWar.Remove(destroyedClan);
        }

        public void RegisterClanForPlayerWarOnEndingMercenaryContract(Clan minorFaction)
        {
            if (!minorFaction.IsMinorFaction)
                throw new MBIllegalValueException($"{minorFaction} is not a minor faction clan, you cannot register it for a later war with Player.");
            _factionsWaitingForWar.Add(minorFaction);
        }

        public void DeclareWarOnPlayerIfNeeded(Clan minorFaction)
        {
            if (_factionsWaitingForWar.Contains(minorFaction))
            {
                DeclareWarAction.ApplyByPlayerHostility(minorFaction.MapFaction, Clan.PlayerClan.MapFaction);
                _factionsWaitingForWar.Remove(minorFaction);
            }
        }

        internal MBReadOnlyList<MinorFactionHideout> AllMFHideouts
        {
            get => this._hideouts;
        }

        public static MFHideoutManager Current { get; private set; }

        private Dictionary<string, MinorFactionHideout> _LoadedMFHideouts;

        private Dictionary<Clan, List<MinorFactionHideout>> _factionHideouts;

        private HashSet<Clan> _factionsWaitingForWar;

        private bool _factionHideoutsInitialized;

        private MBList<MinorFactionHideout> _hideouts;

        public IEnumerable<Tuple<Settlement, GameEntity>> _allMFHideouts;
    }
}
