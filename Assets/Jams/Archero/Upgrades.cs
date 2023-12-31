using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Archero {
  // Base class for runtime data regarding an upgrade.
  [Serializable]
  public class UpgradeData {
    public Upgrade Upgrade;
    public int CurrentLevel = 0;
  }

  [Serializable]
  public struct ExperienceEvent {
    public int Experience;
    public int NextLevelExperience;
    public ExperienceEvent(int experience, int nextLevelExperience) {
      Experience = experience;
      NextLevelExperience = nextLevelExperience;
    }
  }

  public class Upgrades : MonoBehaviour {
    public Upgrade DebugAddUpgrade;

    public List<UpgradeData> Active = new();
    List<UpgradeData> Added = new();
    [Serializable] public class AttributeDictionary : SerializableDictionary<AttributeTag, AttributeModifier> { }
    public AttributeDictionary Modifiers = new ();
    bool Dirty = false;
    public int XP = 0;
    public int CurrentLevel = 1;
    public UnityEvent<ExperienceEvent> OnExperience;
    public UnityEvent<int> OnLevel;
    public int XPToNextLevel => 40 + 10*(CurrentLevel-1);
    public AttributeModifier GetModifier(AttributeTag attrib) => Modifiers.GetValueOrDefault(attrib, null);
    public void AddAttributeModifier(AttributeTag attrib, AttributeModifier modifier) => AttributeModifier.Add(Modifiers, attrib, modifier);
    public void RemoveAttributeModifier(AttributeTag attrib, AttributeModifier modifier) => AttributeModifier.Remove(Modifiers, attrib, modifier);
    public UpgradeData GetUpgradeData(Upgrade upgrade) => Active.Find(ud => ud.Upgrade == upgrade) ?? Added.Find(ud => ud.Upgrade == upgrade);

    void Start() {
      ChangeExperience(XP);
      ChangeLevel(CurrentLevel);
    }

    bool CanBuyUpgrade(Upgrade upgrade) {
      var ud = GetUpgradeData(upgrade);
      return (ud?.CurrentLevel ?? 0) < upgrade.MaxLevel;
    }

    public void BuyUpgrade(Upgrade upgrade) {
      AddUpgrade(upgrade);
    }

    public void AddUpgrade(Upgrade upgrade) {
      Dirty = true;

      if (GetUpgradeData(upgrade) is var data && data != null) {
        data.CurrentLevel++;
      } else {
        data = new() { Upgrade = upgrade, CurrentLevel = 1 };
        Added.Add(data);
      }
      upgrade.OnAdded(this, data.CurrentLevel);
    }

    public void RemoveUpgrade(Upgrade upgrade) {
      Dirty = true;
      if (GetUpgradeData(upgrade) is var data) {
        Added.Remove(data);
        Active.Remove(data);
      }
    }

    public void ChangeExperience(int experience) {
      XP = experience;
      var experienceEvent = new ExperienceEvent(XP, XPToNextLevel);
      OnExperience.Invoke(experienceEvent);
      BroadcastMessage("OnExperience", experienceEvent, SendMessageOptions.DontRequireReceiver);
    }

    public void ChangeLevel(int level) {
      CurrentLevel = level;
      OnLevel.Invoke(level);
      BroadcastMessage("OnLevel", CurrentLevel, SendMessageOptions.DontRequireReceiver);
    }

    public void CollectGold(int gold) {
      if (XP < XPToNextLevel && XP+gold >= XPToNextLevel)
        WorldSpaceMessageManager.Instance.SpawnMessage($"Level Up!", transform.position + 2*Vector3.up, 2f);
      ChangeExperience(XP+gold);
    }

    public void MaybeLevelUp() {
      if (CurrentLevel == 0 || XP >= XPToNextLevel) {
        var newXP = XP - XPToNextLevel;
        ChangeLevel(CurrentLevel+1);
        ChangeExperience(newXP);
        UpgradeUI.Instance.Show(this, $"Level {CurrentLevel} in this adventure!", "Select a new ability",
          PickUpgrades(GameManager.Instance.Upgrades, 3));
      }
    }

    public void ChooseFirstUpgrade() {
      UpgradeUI.Instance.Show(this, $"New game", "Choose your first ability",
        PickUpgrades(GameManager.Instance.Upgrades, 3));
    }

    public IEnumerable<Upgrade> PickUpgrades(List<Upgrade> choices, int n) {
      UnityEngine.Random.InitState((int)DateTime.Now.Ticks);  // Why do I need to call this EXACTLY HERE?
      var availableUpgrades = choices.Where(u => CanBuyUpgrade(u)).ToList();
      availableUpgrades.Shuffle();
      return availableUpgrades.Take(n);
    }

    void FixedUpdate() {
      if (DebugAddUpgrade != null) {
        AddUpgrade(DebugAddUpgrade);
        DebugAddUpgrade = null;
      }
      if (Added.Count > 0 || Dirty) {
        Dirty = false;
        Added.ForEach(e => Active.Add(e));
        Added.Clear();
        Modifiers.Clear();
        Active.ForEach(ud => ud.Upgrade.Apply(this));
        GetComponent<Damageable>().Heal(0);  // Update health bar with potentially new max HP.
      }
    }

    bool TookDamageThisRoom = false;
    void OnDamage(DamageEvent damageEvent) {
      TookDamageThisRoom = true;
    }

    public void OnNewRoom() {
      var challenges = Active.Where(ud => ud.Upgrade is UpgradeChallenge).ToArray();
      if (!TookDamageThisRoom) {
        challenges.ForEach(c => ((UpgradeChallenge)c.Upgrade).OnSuccess(this));
      } else {
        challenges.ForEach(c => RemoveUpgrade(c.Upgrade));
      }
      TookDamageThisRoom = false;
    }
  }
 }