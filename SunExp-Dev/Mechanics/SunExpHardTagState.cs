using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Data.Save;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SunExpHardTagState
{
    public static bool Active(string id)
    {
        return Level(id) > 0;
    }

    public static int Level(string id)
    {
        var runtimeLevel = RuntimeLevel(id);
        if (runtimeLevel > 0)
        {
            return runtimeLevel;
        }

        var level = 0;
        foreach (var tag in CurrentHardTags())
        {
            var tagId = DictionaryUtil.Get(tag?.data, "Id");
            if (SunExpHardTagIds.Same(tagId, id))
            {
                level++;
            }
        }

        return level;
    }

    private static int RuntimeLevel(string id)
    {
        try
        {
            var entries = Singleton<GameRuntimeData>.Instance?.HardTags;
            if (entries == null)
            {
                return 0;
            }

            var level = 0;
            foreach (var entry in entries)
            {
                var tagId = DictionaryUtil.Get(entry?.Data, "Id");
                if (SunExpHardTagIds.Same(tagId, id))
                {
                    level += Math.Max(0, entry?.DynamicValue ?? 0);
                }
            }

            return level;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime hard tag read skipped: " + ex.Message);
            return 0;
        }
    }

    private static IEnumerable<DataConfig?> CurrentHardTags()
    {
        List<DataConfig>? tags = null;
        try
        {
            tags = GameSaveManager.GetHardTags();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Hard tag read skipped: " + ex.Message);
        }

        if (tags == null)
        {
            yield break;
        }

        foreach (var tag in tags)
        {
            yield return tag;
        }
    }
}
