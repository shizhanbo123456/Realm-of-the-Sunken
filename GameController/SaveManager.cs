using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public const string SaveKey = "GameSaveDataV3";

    private void Awake()
    {
        Tool.SaveManager = this;
        LoadOrCreate();
        ClientSelection.playerName = playerName;
    }

    // 唯一的常规落盘时机：进程退出时统一写一次（改名/选角/结算入账都只改内存）
    private void OnApplicationQuit()
    {
        Save();
    }

    #region 数据
    public int playerLevel = 1;
    public int playerExp;

    public string playerName = "";

    // 玩家令牌：身份凭据，跨断线/跨进程稳定；只用正数段，AI 占另一段（见 BattleManager.ROBOT_TOKEN_BASE）
    public int token;

    public const int TokenMinInclusive = 1;
    public const int TokenMaxExclusive = 1_000_000_000;

    public List<int> characterLevels = new();
    public List<int> characterExp = new();
    public List<bool> characterUnlocked = new();

    public static int CharacterTotalCount => Config.attack_character_count + Config.defense_character_count;
    #endregion

    #region 查询
    public int GetCharacterLevel(int index)
    {
        if (index < 0 || index >= characterLevels.Count) return 1;
        return characterLevels[index];
    }

    public int GetCharacterExp(int index)
    {
        if (index < 0 || index >= characterExp.Count) return 0;
        return characterExp[index];
    }

    public bool IsCharacterUnlocked(int index)
    {
        if (index < 0 || index >= characterUnlocked.Count) return IsUnlockedByPlayerLevel(index);
        return characterUnlocked[index] || IsUnlockedByPlayerLevel(index);
    }

    public bool IsUnlockedByPlayerLevel(int characterIndex)
    {
        if (Tool.InfoManager == null) return true;
        return playerLevel >= Tool.InfoManager.GetPlayerCharacterInfo(characterIndex).unlockLevel;
    }
    #endregion

    #region 修改
    public void AddCharacterExp(int index, int exp)
    {
        EnsureListSize(index);
        characterExp[index] += exp;
        int level = characterLevels[index];
        while (level < Config.save_max_level)
        {
            int need = ConfigManager.ExpCharacterConfig.Get(level + 1).exp;
            if (characterExp[index] < need) break;
            characterExp[index] -= need;
            level++;
        }
        characterLevels[index] = level;
    }

    public void AddPlayerExp(int exp)
    {
        playerExp += exp;
        while (playerLevel < Config.player_max_level && playerExp >= Config.GetPlayerLevelUpExp(playerLevel))
        {
            playerExp -= Config.GetPlayerLevelUpExp(playerLevel);
            playerLevel++;
        }
    }
    #endregion

    #region 序列化（PlayerPrefs，| 分隔）
    private void EnsureListSize(int index)
    {
        while (characterLevels.Count < CharacterTotalCount) characterLevels.Add(1);
        while (characterExp.Count < CharacterTotalCount) characterExp.Add(0);
        while (characterUnlocked.Count < CharacterTotalCount)
        {
            characterUnlocked.Add(false); // 解锁由配表 entity.csv 的解锁等级判定，存档标记默认全 false
        }
    }

    private void LoadOrCreate()
    {
        EnsureListSize(0);
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
            // 字段数必须与当前格式完全一致：不兼容旧格式，长度不符即判为无效存档
            int expected = 3 + CharacterTotalCount * 3 + 4; // 头 3 项 + 等级/经验/解锁各一列 + 名称/令牌/攻方下标/守方下标
            if (parts.Length != expected)
                throw new System.FormatException($"字段数 {parts.Length} 与当前存档格式 {expected} 不符");

            int idx = 0;
            playerLevel = int.Parse(parts[idx++]);
            playerExp = int.Parse(parts[idx++]);
            int count = int.Parse(parts[idx++]);
            if (count != CharacterTotalCount)
                throw new System.FormatException($"角色数 {count} 与配置 {CharacterTotalCount} 不符");
            for (int i = 0; i < count; i++) characterLevels[i] = int.Parse(parts[idx++]);
            for (int i = 0; i < count; i++) characterExp[i] = int.Parse(parts[idx++]);
            for (int i = 0; i < count; i++) characterUnlocked[i] = parts[idx++] == "1";
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

        ClientSelection.selectedAttackIndex = InRange(attackIndex, Config.attack_character_count);
        ClientSelection.selectedDefenseIndex = InRange(defenseIndex, Config.defense_character_count);
    }

    // 存档里的选中下标超出当前角色数（配表改过）时退回 0
    private static int InRange(int index, int count) => index >= 0 && index < count ? index : 0;

    private static bool IsTokenValid(int value) => value >= TokenMinInclusive && value < TokenMaxExclusive;

    private static int GenerateToken() => UnityEngine.Random.Range(TokenMinInclusive, TokenMaxExclusive);

    public void Save()
    {
        EnsureListSize(0);
        var sb = new System.Text.StringBuilder();
        sb.Append(playerLevel).Append('|').Append(playerExp).Append('|');
        sb.Append(CharacterTotalCount).Append('|');
        for (int i = 0; i < CharacterTotalCount; i++) sb.Append(characterLevels[i]).Append('|');
        for (int i = 0; i < CharacterTotalCount; i++) sb.Append(characterExp[i]).Append('|');
        for (int i = 0; i < CharacterTotalCount; i++) sb.Append(characterUnlocked[i] ? "1" : "0").Append('|');
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
        playerLevel = 1;
        playerExp = 0;
        characterLevels.Clear();
        characterExp.Clear();
        characterUnlocked.Clear();
        EnsureListSize(0);
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
