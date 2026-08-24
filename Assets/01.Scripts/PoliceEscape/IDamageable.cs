namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// Anything a blast (or any other damage source) can hurt, found on the
    /// same GameObject as the thing's Rigidbody. Damage is NORMALIZED — 1 is
    /// a full health bar — so sources don't need to know what they're
    /// hitting: a car bleeds it off its health, a barrel treats any amount as
    /// a detonator, the player's receiver scales it by its plating before
    /// feeding the corruption meter. Implementations must tolerate damage
    /// while already dead/spent (ignore it), because blasts overlap.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>Take damage. 1 = a full health bar; amounts ≤ 0 are ignored.</summary>
        void ApplyDamage(float amount);
    }
}
