public static class Landscape
{
    // 区块边长、每边区块数、地图范围全部取自 global_variables.csv
    public static float chunkSize => ConfigManager.GlobalVariableConfig.terrain_chunk_size;

    public static int chunkCount => ConfigManager.GlobalVariableConfig.terrain_chunk_size_count;

    public static float MapMin => ConfigManager.GlobalVariableConfig.terrain_size_min;

    public static float MapSize => ConfigManager.GlobalVariableConfig.terrain_size;

    public static float MapMax => MapMin + MapSize;

    public static UnityEngine.Vector3 MapCenter =>
        new UnityEngine.Vector3(MapMin + MapSize * 0.5f, 0f, MapMin + MapSize * 0.5f);

    public static bool InMap(UnityEngine.Vector3 pos)
    {
        return pos.x >= MapMin && pos.z >= MapMin && pos.x < MapMax && pos.z < MapMax;
    }
}
