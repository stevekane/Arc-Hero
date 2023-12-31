using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Archero {
  public delegate void OnEffectComplete(Status status);

  public abstract class StatusEffect : IDisposable {
    internal Status Status; // non-null while Added to this Status
    public OnEffectComplete OnComplete;
    public abstract bool Merge(StatusEffect e);
    public abstract void Apply(Status status);
    public virtual void OnRemoved(Status status) { }
    public void Dispose() => Status?.Remove(this);
  }

  public class TimedEffect : StatusEffect {
    protected int Ticks = 0;
    protected int TotalTicks;
    public TimedEffect(int ticks) => TotalTicks = ticks;
    public override sealed void Apply(Status status) {
      if (Ticks++ < TotalTicks) {
        ApplyTimed(status);
      } else {
        status.Remove(this);
      }
    }
    public virtual void ApplyTimed(Status status) { }
    public override bool Merge(StatusEffect e) {
      var other = (TimedEffect)e;
      TotalTicks = Mathf.Max(TotalTicks - Ticks, other.TotalTicks);
      Ticks = 0;
      return true;
    }
  }

  public class InlineEffect : StatusEffect {
    Action<Status> ApplyFunc;
    string Name;
    public InlineEffect(Action<Status> apply, string name = "InlineEffect") => (ApplyFunc, Name) = (apply, name);
    public override bool Merge(StatusEffect e) => false;
    public override void Apply(Status status) => ApplyFunc(status);
    public override string ToString() => Name;
  }

  public class HurtStunEffect : TimedEffect {
    public HurtStunEffect(int ticks) : base(ticks) { }
    public override void ApplyTimed(Status status) {
      status.CanMove = false;
      status.CanRotate = false;
      status.CanAttack = false;
      status.IsHurt = true;
    }
  }

  // +25% attack speed after killing mob.
  public class InspireEffect : TimedEffect {
    public AttributeModifier Modifier = new() { Mult = .25f };
    public InspireEffect() : base(Timeval.FromSeconds(2f).Ticks) { }
    public override void ApplyTimed(Status status) {
      status.AddAttributeModifier(AttributeTag.AttackSpeed, Modifier);
    }
  }

  public class FlameEffect : TimedEffect {
    float Damage = 1f;
    static int DelayTicks = 60/4;  // .25s
    int TicksRemaining;
    public FlameEffect(float baseDamage) : base(Timeval.FromSeconds(2f).Ticks) {
      Damage = baseDamage * .18f;
      TicksRemaining = DelayTicks;
    }
    public override void ApplyTimed(Status status) {
      if (--TicksRemaining < 0) {
        status.Damage.TakeDamage((int)Damage, false);
        TicksRemaining = DelayTicks;
      }
    }
  }

  public class FreezeEffect : TimedEffect {
    public FreezeEffect() : base(Timeval.FromSeconds(2f).Ticks) { }
    public override void ApplyTimed(Status status) {
      status.CanMove = false;
      status.CanRotate = false;
      status.CanAttack = false;
      status.AddAttributeModifier(AttributeTag.LocalTimeScale, AttributeModifier.TimesZero);
    }
  }

  public class PoisonEffect : StatusEffect {
    float Damage;
    static int DelayTicks = 60;  // 1s
    int TicksRemaining;
    public PoisonEffect(float baseDamage) {
      Damage = baseDamage * .35f;
      TicksRemaining = DelayTicks;
    }
    public override bool Merge(StatusEffect e) => false;
    public override void Apply(Status status) {
      if (--TicksRemaining < 0) {
        status.Damage.TakeDamage((int)Damage, false);
        TicksRemaining = DelayTicks;
      }
    }
  }

  // The flying state that happens to a defender when they get hit by a strong attack.
  public class KnockbackEffect : StatusEffect {
    const float DONE_SPEED = 1f;

    AI AI;
    public float Drag;
    public Vector3 Velocity;
    public KnockbackEffect(AI ai, Vector3 velocity, float drag = 5f) {
      AI = ai;
      Velocity = velocity;
      Drag = drag;
    }
    public override bool Merge(StatusEffect e) {
      Velocity = ((KnockbackEffect)e).Velocity;
      return true;
    }
    public override void Apply(Status status) {
      Velocity = Velocity * Mathf.Exp(-Time.fixedDeltaTime * Drag);
      AI.ScriptedVelocity = Velocity;
      if (Velocity.sqrMagnitude < DONE_SPEED.Sqr()) {
        status.Remove(this);
      }
    }
  }

  public class Status : MonoBehaviour {
    public List<StatusEffect> Active = new();
    internal Attributes Attributes;
    internal Upgrades Upgrades;
    internal CharacterController CharacterController;
    internal Damageable Damage;
    Dictionary<AttributeTag, AttributeModifier> Modifiers = new();

    public bool IsGrounded { get; private set; }
    public bool JustGrounded { get; private set; }
    public bool JustTookOff { get; private set; }
    public bool IsWallSliding { get; private set; }
    public bool IsFallen { get; set; }
    public bool IsHurt { get; set; }
    public bool CanMove { get => GetBoolean(AttributeTag.MoveSpeed); set => SetBoolean(AttributeTag.MoveSpeed, value); }
    public bool CanRotate { get => GetBoolean(AttributeTag.TurnSpeed); set => SetBoolean(AttributeTag.TurnSpeed, value); }
    public bool HasGravity { get => GetBoolean(AttributeTag.HasGravity); set => SetBoolean(AttributeTag.HasGravity, value); }
    public bool CanAttack { get => GetBoolean(AttributeTag.CanAttack); set => SetBoolean(AttributeTag.CanAttack, value); }
    public bool IsInterruptible { get => GetBoolean(AttributeTag.IsInterruptible); set => SetBoolean(AttributeTag.IsInterruptible, value); }
    public bool IsHittable { get => GetBoolean(AttributeTag.IsHittable); set => SetBoolean(AttributeTag.IsHittable, value); }
    public bool IsDamageable { get => GetBoolean(AttributeTag.IsDamageable); set => SetBoolean(AttributeTag.IsDamageable, value); }
    public AbilityTag Tags = 0;

    // All booleans default to true. Set to false after Modifiers.Clear() if you want otherwise.
    bool GetBoolean(AttributeTag attrib) => Attributes.GetValue(attrib, 1f) > 0f;
    void SetBoolean(AttributeTag attrib, bool value) {
      if (value) {
        Modifiers.Remove(attrib); // reset it to default
      } else {
        AttributeModifier.Add(Modifiers, attrib, AttributeModifier.TimesZero);
      }
    }

    List<StatusEffect> Added = new();
    public StatusEffect Add(StatusEffect effect, OnEffectComplete onComplete = null) {
      Debug.Assert(!Active.Contains(effect), $"Effect {effect} is getting reused");
      Debug.Assert(!Added.Contains(effect), $"Effect {effect} is getting reused");
      effect.Status = this;
      var count = Active.Count;
      var existing = Active.FirstOrDefault((e) => e.GetType() == effect.GetType()) ?? Added.FirstOrDefault((e) => e.GetType() == effect.GetType());
      if (existing != null && existing.Merge(effect))
        return existing;
      effect.OnComplete = onComplete;
      Added.Add(effect);
      return effect;
      // TODO: merge onComplete with existing.OnComplete?
    }

    // TODO HACK: Use double-buffering instead.
    List<Action<Status>> NextTick = new();
    public void AddNextTick(Action<Status> func) => NextTick.Add(func);

    List<StatusEffect> Removed = new();
    public void Remove(StatusEffect effect) {
      if (effect != null)
        Removed.Add(effect);
    }

    public void Remove(Predicate<StatusEffect> predicate) {
      foreach (var effect in Active)
        if (predicate(effect))
          Removed.Add(effect);
      foreach (var effect in Added)
        if (predicate(effect))
          Removed.Add(effect);
    }

    public T Get<T>() where T : StatusEffect {
      return Active.FirstOrDefault(e => e is T) as T;
    }

    public AttributeModifier GetModifier(AttributeTag attrib) => Modifiers.GetValueOrDefault(attrib, null);
    public void AddAttributeModifier(AttributeTag attrib, AttributeModifier modifier) => AttributeModifier.Add(Modifiers, attrib, modifier);
    public void RemoveAttributeModifier(AttributeTag attrib, AttributeModifier modifier) => AttributeModifier.Remove(Modifiers, attrib, modifier);

    private void Awake() {
      Attributes = this.GetOrCreateComponent<Attributes>();
      Upgrades = this.GetOrCreateComponent<Upgrades>();
      CharacterController = GetComponent<CharacterController>();
      this.InitComponent(out Damage);
    }

    private void FixedUpdate() {
      Modifiers.Clear();

      IsHurt = false;
      IsFallen = false;

      //Tags = Upgrades.AbilityTags;

      // TODO: differentiate between cancelled and completed?
      Removed.ForEach(e => {
        e.OnComplete?.Invoke(this);
        e.OnRemoved(this);
        e.Status = null;
        Active.Remove(e);
        Added.Remove(e);
      });
      Removed.Clear();
      Added.ForEach(e => Active.Add(e));
      Added.Clear();
      Active.ForEach(e => e.Apply(this));
      NextTick.ForEach(f => f(this));
      NextTick.Clear();

#if UNITY_EDITOR
      DebugEffects.Clear();
      Active.ForEach(e => DebugEffects.Add($"{e}"));
#endif
    }

    void OnHurt(HitParams hitParams) {
      Remove(Get<FreezeEffect>());
      if (hitParams.AttackerAttributes.GetValue(AttributeTag.Blaze, 0) > 0) {
        Add(new FlameEffect(hitParams.ElemDamage));
      }
      if (hitParams.AttackerAttributes.GetValue(AttributeTag.Freeze, 0) > 0) {
        Add(new FreezeEffect());
      }
      if (hitParams.AttackerAttributes.GetValue(AttributeTag.Poison, 0) > 0) {
        Add(new PoisonEffect(hitParams.ElemDamage));
      }
    }

#if UNITY_EDITOR
    public List<string> DebugEffects = new();
#endif
  }
}