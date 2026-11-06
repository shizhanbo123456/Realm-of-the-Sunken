using System.Collections.Generic;
using UnityEngine;

public class BoidSchool : MonoBehaviour
{
    [Header("鱼")]
    [Tooltip("单条鱼的预制体（带 MeshRenderer 的普通模型）")]
    public GameObject fishPrefab;

    [Tooltip("生成数量")]
    public int fishCount = 30;

    [Header("活动范围（以本物体位置为中心的圆柱）")]
    [Tooltip("圆柱半径（米）")]
    public float radius = 10f;

    [Tooltip("圆柱总高度（米），本物体位置上下各一半")]
    public float height = 6f;

    [Header("游动")]
    [Tooltip("最低巡航速度（米/秒）")]
    public float minSpeed = 0.8f;

    [Tooltip("最高速度（米/秒）")]
    public float maxSpeed = 2f;

    [Tooltip("转向平滑速度，越大转头越快")]
    public float turnSmoothing = 4f;

    [Tooltip("模型正面与运动方向不一致时修正（常用 0 / 90 / 180）")]
    public float yawOffset = 0f;

    [Header("群聚（boids）")]
    [Tooltip("邻居判定半径，超过该距离不参与群聚计算")]
    public float neighborRadius = 4f;

    [Tooltip("排斥半径，比该距离更近的个体会互相推开")]
    public float separationRadius = 1f;

    [Tooltip("向邻居中心聚拢的权重")]
    public float cohesionWeight = 0.9f;

    [Tooltip("向邻居平均速度对齐的权重")]
    public float alignmentWeight = 1.2f;

    [Tooltip("互相排斥的权重")]
    public float separationWeight = 2f;

    [Tooltip("随机游动权重")]
    public float wanderWeight = 0.7f;

    [Tooltip("越出圆柱时的回拉权重")]
    public float boundaryWeight = 2.5f;

    // 惊吓参数：由静态接口 Scare 使用，不进 Inspector
    private const float ScareRadius = 12f;
    private const float ScareDuration = 3f;
    private const float ScareWeight = 8f;

    // 边界在半径/高度的 90% 处开始回拉，留出缓冲带避免贴边
    private const float SoftBoundary = 0.9f;
    private const float BobSpeed = 0.25f;

    private struct Fish
    {
        public Transform t;
        public Vector3 pos;
        public Vector3 vel;
        public float speed;       // 个体巡航速度
        public float wanderAngle; // 随机游动朝向（度）
        public float wanderTurn;  // 随机游动转向速度（度/秒）
        public float bobPhase;
        public float bobRate;
    }

    private Fish[] fishes;
    private static readonly List<BoidSchool> schools = new();

    private Vector3 scarePoint;
    private float scareTimer;

    // 恐吓源：对所有鱼群生效，按圆柱外接球先做距离剔除，静态调用本身不产生逐鱼开销
    public static void Scare(Vector3 source)
    {
        for (int i = 0; i < schools.Count; i++)
        {
            BoidSchool s = schools[i];
            if (s == null)
            {
                schools.RemoveAt(i); // 场景切换后残留的已销毁引用
                i--;
                continue;
            }
            if (!s.isActiveAndEnabled) continue;

            float reach = ScareRadius + Mathf.Sqrt(s.radius * s.radius + s.height * s.height * 0.25f);
            if ((s.transform.position - source).sqrMagnitude > reach * reach) continue;

            s.scarePoint = source;
            s.scareTimer = ScareDuration;
        }
    }

    private void OnEnable()
    {
        if (!schools.Contains(this)) schools.Add(this);
    }

    private void OnDisable()
    {
        schools.Remove(this);
    }

    private void Start()
    {
        if (fishPrefab == null)
        {
            Debug.LogError("[BoidSchool] fishPrefab 未设置，鱼群不会生成", this);
            return;
        }
        Spawn();
    }

    private void Spawn()
    {
        fishes = new Fish[fishCount];
        Vector3 center = transform.position;

        for (int i = 0; i < fishes.Length; i++)
        {
            // 初始位置落在中心 60% 半径内，先聚成一团再由群聚力自然散开
            Vector2 disc = Random.insideUnitCircle * (radius * 0.6f);
            Vector3 pos = center + new Vector3(disc.x, Random.Range(-height * 0.3f, height * 0.3f), disc.y);
            float yaw = Random.Range(0f, 360f);

            GameObject go = Instantiate(fishPrefab, pos, Quaternion.Euler(0f, yaw, 0f), transform);
            fishes[i] = new Fish
            {
                t = go.transform,
                pos = pos,
                vel = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Random.Range(minSpeed, maxSpeed),
                speed = Random.Range(minSpeed, maxSpeed),
                wanderAngle = Random.Range(0f, 360f),
                wanderTurn = Random.Range(12f, 34f) * (Random.value < 0.5f ? -1f : 1f),
                bobPhase = Random.Range(0f, 6.2832f),
                bobRate = Random.Range(0.6f, 1.4f),
            };
        }
    }

    private void Update()
    {
        if (fishes == null || fishes.Length == 0) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 center = transform.position;
        float halfHeight = height * 0.5f;
        float neighborR2 = neighborRadius * neighborRadius;
        float sepR2 = separationRadius * separationRadius;
        bool scared = scareTimer > 0f;
        if (scared) scareTimer -= dt;

        // 先快照全部位置，邻居循环里不再反复访问 Transform
        for (int i = 0; i < fishes.Length; i++)
            fishes[i].pos = fishes[i].t.position;

        for (int i = 0; i < fishes.Length; i++)
        {
            Fish f = fishes[i];
            Vector3 sepSum = Vector3.zero;
            Vector3 aliSum = Vector3.zero;
            Vector3 cohSum = Vector3.zero;
            int count = 0;
            int sepCount = 0;

            for (int j = 0; j < fishes.Length; j++)
            {
                if (j == i) continue;
                Vector3 diff = f.pos - fishes[j].pos;
                diff.y = 0f;
                float d2 = diff.sqrMagnitude;
                if (d2 > neighborR2) continue;

                count++;
                cohSum += fishes[j].pos;
                aliSum += new Vector3(fishes[j].vel.x, 0f, fishes[j].vel.z);
                if (d2 < sepR2 && d2 > 0.0001f)
                {
                    sepSum += diff / d2; // 反比平方：越近斥力越强
                    sepCount++;
                }
            }

            // 三法则：分离 / 对齐 / 聚合，只在水平面计算以保持横向游动
            Vector3 steer = Vector3.zero;
            if (sepCount > 0) steer += sepSum.normalized * separationWeight;
            if (count > 0)
            {
                Vector3 velXZ = new(f.vel.x, 0f, f.vel.z);
                Vector3 coh = cohSum / count - f.pos;
                coh.y = 0f;
                Vector3 ali = aliSum / count - velXZ;
                if (coh.sqrMagnitude > 0.0001f) steer += coh.normalized * cohesionWeight;
                if (ali.sqrMagnitude > 0.0001f) steer += ali.normalized * alignmentWeight;
            }

            // 随机游动：方向绕 Y 轴缓慢转动
            f.wanderAngle += f.wanderTurn * dt;
            float wanderRad = f.wanderAngle * Mathf.Deg2Rad;
            steer += new Vector3(Mathf.Sin(wanderRad), 0f, Mathf.Cos(wanderRad)) * wanderWeight;

            // 圆柱边界：越过 90% 半径就开始回拉，越出越多拉力越大，位置不做硬夹
            float dx = f.pos.x - center.x;
            float dz = f.pos.z - center.z;
            float horizDist = Mathf.Sqrt(dx * dx + dz * dz);
            float overR = horizDist - radius * SoftBoundary;
            if (overR > 0f && horizDist > 0.0001f)
                steer += new Vector3(-dx, 0f, -dz) / horizDist * (overR * boundaryWeight);

            float desiredY = Mathf.Sin(Time.time * f.bobRate + f.bobPhase) * BobSpeed;
            float dy = f.pos.y - center.y;
            float overY = Mathf.Abs(dy) - halfHeight * SoftBoundary;
            if (overY > 0f) desiredY += (dy > 0f ? -1f : 1f) * overY * boundaryWeight;

            // 惊吓：距离越近逃离越急，只要有一群被标记，逐鱼计算也只发生在计时内
            if (scared)
            {
                Vector3 away = f.pos - scarePoint;
                float awaySqr = away.sqrMagnitude;
                if (awaySqr < ScareRadius * ScareRadius && awaySqr > 0.0001f)
                {
                    float dist = Mathf.Sqrt(awaySqr);
                    float falloff = 1f - dist / ScareRadius;
                    steer += new Vector3(away.x, 0f, away.z) / dist * (ScareWeight * falloff);
                    desiredY += away.y / dist * (ScareWeight * falloff * 0.5f);
                }
            }

            // 水平速度积分后限制在 [minSpeed, 目标速度]，惊吓时按最高速逃
            Vector3 horizVel = new Vector3(f.vel.x, 0f, f.vel.z) + steer * dt;
            if (horizVel.sqrMagnitude < 0.0001f)
                horizVel = new Vector3(Mathf.Sin(wanderRad), 0f, Mathf.Cos(wanderRad));
            float targetSpeed = scared ? maxSpeed : f.speed;
            float speed = Mathf.Clamp(horizVel.magnitude, minSpeed, Mathf.Max(targetSpeed, minSpeed));
            horizVel = horizVel.normalized * speed;

            float vy = Mathf.Lerp(f.vel.y, Mathf.Clamp(desiredY, -maxSpeed, maxSpeed), dt * 4f);
            f.vel = new Vector3(horizVel.x, vy, horizVel.z);
            f.pos += f.vel * dt;
            f.t.position = f.pos;

            if (f.vel.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(f.vel) * Quaternion.Euler(0f, yawOffset, 0f);
                f.t.rotation = Quaternion.Slerp(f.t.rotation, target, dt * turnSmoothing);
            }

            fishes[i] = f;
        }
    }

    private void OnDestroy()
    {
        if (fishes == null) return;
        for (int i = 0; i < fishes.Length; i++)
            if (fishes[i].t != null) Destroy(fishes[i].t.gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.7f);
        Vector3 c = transform.position;
        float half = height * 0.5f;
        DrawCircle(c + Vector3.up * half, radius);
        DrawCircle(c - Vector3.up * half, radius);

        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Gizmos.DrawLine(c + offset + Vector3.up * half, c + offset - Vector3.up * half);
        }
    }

    private static void DrawCircle(Vector3 c, float r)
    {
        const int Segments = 32;
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            Vector3 cur = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
#endif
}
