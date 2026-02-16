public interface IHealthProvider
{
    float CurrentHealth { get; }
    float MaxHealth { get; }

    void TakeDamage(float amount);
    void Heal(float amount);
    bool IsDead { get; }
}
