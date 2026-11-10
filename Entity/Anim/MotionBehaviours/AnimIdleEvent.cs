using UnityEngine;

public class AnimIdleEvent : AnimMotionEvent
{
    public override EntityAnim.WeaponPos ShowWeapon => EntityAnim.WeaponPos.DontSet;
}
