using System;
using UnityEngine;

[Serializable]
public enum KnockbackType {
  Delta,
  Forward,
}

public static class KnockbackTypeExtensions {
  /*
  KnockbackVector determined from choosing an attack axis and then a vector relative
  to that attack axis.

  For example, if you declare Attacker then <0,1,0> the resulting vector will be straight up
  along the attacker's forward direction.

  If you want to encode an AOE knock-away attack, you might chooise Delta then <0,0,1>
  which will knock all targets away from the attacker along the floor (z is forward)
  */
  public static Vector3 KnockbackVector(
  this KnockbackType type,
  float pitchAngle,
  Transform attacker,
  Transform target) {
    var direction = type switch {
      KnockbackType.Delta => attacker.position.TryGetDirection(target.position) ?? attacker.forward.XZ(),
      KnockbackType.Forward => attacker.forward.XZ(),
      _ => attacker.forward.XZ(),
    };
    return Vector3.RotateTowards(direction, Vector3.up, pitchAngle * Mathf.Deg2Rad, 0f);
  }
}