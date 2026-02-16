using UnityEngine;

public class ZombiePerception : MonoBehaviour
{
    [Header("Horde System")]
    [SerializeField] private bool disableHordeAISystem;

    private enum TargetFilter
    {
        EnemyOrPlayer,
        Zombie
    }

    public float viewRadius = 10f;
    [Range(1f, 360f)] public float viewAngle = 120f;
    public LayerMask obstacleMask;

    [Header("Optimization")]
    [Min(0.02f)] public float perceptionInterval = 0.18f;
    [Min(1)] public int maxQueriesPerFrame = 1;

    public Transform VisibleEnemy { get; private set; }
    public Transform VisibleZombie { get; private set; }

    private float nextScanTime;
    private bool registeredToHorde;

    private void Awake()
    {
        float delay = Mathf.Max(0.02f, perceptionInterval);
        nextScanTime = Time.time + Random.Range(0f, delay);
        EnemyQueryService.ConfigureOwnerBudget(this, Mathf.Max(1, maxQueriesPerFrame));
    }

    private void OnEnable()
    {
        registeredToHorde = false;

        if (!disableHordeAISystem)
            registeredToHorde = HordeAISystem.TryRegister(this);
    }

    private void OnDisable()
    {
        if (registeredToHorde)
            HordeAISystem.Unregister(this);

        registeredToHorde = false;
    }

    private void Update()
    {
        if (!disableHordeAISystem && registeredToHorde)
            return;

        TickFromHordeSystem();
    }

    internal bool TickFromHordeSystem()
    {
        if (!isActiveAndEnabled)
            return false;

        if (Time.time < nextScanTime)
            return false;

        nextScanTime = Time.time + Mathf.Max(0.02f, perceptionInterval);

        FindVisibleTargets(out Transform visibleEnemy, out Transform visibleZombie);
        VisibleEnemy = visibleEnemy;
        VisibleZombie = visibleZombie;
        return true;
    }

    internal void SetVisibleTargetsFromSystem(Transform visibleEnemy, Transform visibleZombie)
    {
        VisibleEnemy = visibleEnemy;
        VisibleZombie = visibleZombie;
    }

    private void FindVisibleTargets(out Transform visibleEnemy, out Transform visibleZombie)
    {
        visibleEnemy = null;
        visibleZombie = null;

        Collider[] hits = EnemyQueryService.OverlapSphere(
            transform.position,
            viewRadius,
            ~0,
            QueryTriggerInteraction.Ignore,
            this,
            maxQueriesPerFrame: Mathf.Max(1, maxQueriesPerFrame)
        );
        int hitCount = EnemyQueryService.GetLastHitCount(this);

        float bestEnemyDistSqr = float.MaxValue;
        float bestZombieDistSqr = float.MaxValue;
        float minDot = Mathf.Cos(viewAngle * 0.5f * Mathf.Deg2Rad);
        Transform selfRoot = transform.root;
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            EvaluateCandidate(
                hit.transform,
                TargetFilter.EnemyOrPlayer,
                excludeSelf: false,
                origin,
                minDot,
                selfRoot,
                ref bestEnemyDistSqr,
                ref visibleEnemy
            );
            EvaluateCandidate(
                hit.transform,
                TargetFilter.Zombie,
                excludeSelf: true,
                origin,
                minDot,
                selfRoot,
                ref bestZombieDistSqr,
                ref visibleZombie
            );
        }
    }

    private void EvaluateCandidate(
        Transform rawCandidate,
        TargetFilter filter,
        bool excludeSelf,
        Vector3 origin,
        float minDot,
        Transform selfRoot,
        ref float bestDistSqr,
        ref Transform bestTarget
    )
    {
        Transform taggedTarget = ResolveTaggedTarget(rawCandidate, filter);
        if (taggedTarget == null)
            return;

        if (excludeSelf && taggedTarget.root == selfRoot)
            return;

        Vector3 dir = taggedTarget.position - transform.position;
        float sqrDistance = dir.sqrMagnitude;
        if (sqrDistance < 0.0001f || sqrDistance >= bestDistSqr)
            return;

        float invDistance = 1f / Mathf.Sqrt(sqrDistance);
        Vector3 dirNormalized = dir * invDistance;

        if (Vector3.Dot(transform.forward, dirNormalized) < minDot)
            return;

        float distance = sqrDistance * invDistance;
        if (Physics.Raycast(origin, dirNormalized, distance, obstacleMask, QueryTriggerInteraction.Ignore))
            return;

        bestDistSqr = sqrDistance;
        bestTarget = taggedTarget;
    }

    private static Transform ResolveTaggedTarget(Transform candidate, TargetFilter filter)
    {
        if (candidate == null)
            return null;

        if (MatchesFilter(candidate, filter))
            return candidate;

        Transform root = candidate.root;
        if (root != null && root != candidate && MatchesFilter(root, filter))
            return root;

        return null;
    }

    private static bool MatchesFilter(Transform candidate, TargetFilter filter)
    {
        if (candidate == null)
            return false;

        return filter == TargetFilter.EnemyOrPlayer
            ? candidate.CompareTag("Player") || candidate.CompareTag("Enemy")
            : candidate.CompareTag("Zombie");
    }
}
