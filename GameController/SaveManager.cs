using UnityEngine;

public class SaveManager : MonoBehaviour
{
    // 局外只存身份与选角：等级/经验随局外养成一并删除，旧格式不再兼容
    public const string SaveKey = "GameSaveDataV6";

    private void Awake()
    {
        Tool.SaveManager = this;
        LoadOrCreate();
        ClientSelection.playerName = playerName;
    }

    // 唯一的常规落盘时机：进程退出时统一写一次（改名/选角都只改内存）
    private void OnApplicationQuit()
    {
        Save();
    }

    #region 数据
    public string playerName = "";

    // 玩家令牌：身份凭据，跨断线/跨进程稳定；只用正数段，AI 占另一段（见 BattleManager.ROBOT_TOKEN_BASE）
    public int token;

    public const int TokenMinInclusive = 1;
    public const int TokenMaxExclusive = 1_000_000_000;
    #endregion

    #region 序列化（PlayerPrefs，| 分隔）
    private void LoadOrCreate()
    {
        string data = PlayerPrefs.GetString(SaveKey, "");
        if (string.IsNullOrEmpty(data))
        {
            CreateNewSave();
            return;
        }
        int attackIndex;
        int defenseIndex;
        try
        {
            var parts = data.Split('|');
            // 字段数必须与当前格式完全一致：长度不符即判为无效存档
            int expected = 4; // 名称 / 令牌 / 攻方下标 / 守方下标
            if (parts.Length != expected)
                throw new System.FormatException($"字段数 {parts.Length} 与当前存档格式 {expected} 不符");

            int idx = 0;
            playerName = parts[idx++];
            token = int.Parse(parts[idx++]);
            attackIndex = int.Parse(parts[idx++]);
            defenseIndex = int.Parse(parts[idx++]);
            if (!IsTokenValid(token))
                throw new System.FormatException($"令牌 {token} 越界");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"存档解析失败，已重建：{e.Message}");
            CreateNewSave();
            return;
        }

        // 名字为空 → 视为无效存档 → 新建存档（随机六位数名字）
        if (string.IsNullOrEmpty(playerName))
        {
            Debug.LogWarning("存档玩家名为空，视为无效，新建存档");
            CreateNewSave();
            return;
        }

        ClientSelection.selectedAttackIndex = InRange(attackIndex, GlobalConfig.attack_character_count);
        ClientSelection.selectedDefenseIndex = InRange(defenseIndex, GlobalConfig.defense_character_count);
    }

    // 存档里的选中下标超出当前角色数（配表改过）时退回 0
    private static int InRange(int index, int count) => index >= 0 && index < count ? index : 0;

    private static bool IsTokenValid(int value) => value >= TokenMinInclusive && value < TokenMaxExclusive;

    private static int GenerateToken() => UnityEngine.Random.Range(TokenMinInclusive, TokenMaxExclusive);

    public void Save()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((playerName ?? "").Replace("|", "")).Append('|');
        sb.Append(token).Append('|');
        sb.Append(ClientSelection.selectedAttackIndex).Append('|');
        sb.Append(ClientSelection.selectedDefenseIndex);
        PlayerPrefs.SetString(SaveKey, sb.ToString());
        PlayerPrefs.Save();
    }

    private string GenerateRandomName()
    {
        return UnityEngine.Random.Range(100000, 1000000).ToString();
    }

    private void CreateNewSave()
    {
        if (!Application.isPlaying) return; // 编辑模式不建存档，清档只删盘上数据
        playerName = GenerateRandomName();
        token = GenerateToken();
        ClientSelection.selectedAttackIndex = 0;
        ClientSelection.selectedDefenseIndex = 0;
        Save();
    }

    [ContextMenu("ClearSave")]
    private void ClearSave()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        CreateNewSave();
        Debug.Log("存档已清除");
    }
    #endregion
}
